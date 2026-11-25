"""Capture BNW gameplay without opening or focusing a game window.

The kit's graphics harness runs FF6 PR hidden in batch mode, renders the game's own cameras,
and writes PNGs through its ``shot`` command. A plan can load a numbered save or start
a disposable New Game and stage scenes in memory. Test runs block writes to game saves.

Plan format::

    {"captures": [
      {"name": "esper", "setup": ["bnwel"],
       "actions": ["press menu", "wait 20", "press action"]}
    ]}

Usage::

    .venv/Scripts/python tools/bnw_capture.py plan.json --dry-run
    .venv/Scripts/python tools/bnw_capture.py plan.json --out build/shots/bnw_capture
"""
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys

PR = Path(__file__).resolve().parent.parent
GAME = Path(r"D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR")
PLUGINS = GAME / "BepInEx" / "plugins"
KEYS = "action cancel menu up down left right pageup pagedown switchleft switchright start select shortcut"
KEY_SET = set(KEYS.split())
NAME = re.compile(r"[a-z][a-z0-9_-]{0,39}\Z")
STATE = r"scene field; map [1-9]\d*;(?!.*title window)"


def valid_action(action):
    """Only the same inputs a player could make, plus read-only observations."""
    words = action.split()
    if not words:
        return False
    if words[0] == "press":
        return len(words) in (2, 3) and words[1] in KEY_SET and (
            len(words) == 2 or words[2].isdigit() and 1 <= int(words[2]) <= 300)
    if words[0] == "wait":
        return len(words) == 2 and words[1].isdigit() and 1 <= int(words[1]) <= 600
    if words[0] == "sleep":
        return len(words) == 2 and words[1].isdigit() and 1 <= int(words[1]) <= 30
    return action in ("state", "party", "slots", "shotinfo")


def valid_setup(step):
    """A single harness/batch step; this plan owns the screenshot and process lifetime."""
    if not isinstance(step, str) or not step or step != step.strip() or "\n" in step or "\r" in step:
        return False
    head = step.split()[0]
    return head not in ("quit", "shot", "shell", "cmd", "powershell")


def make_batch(plan, out):
    slot = plan.get("slot")
    if slot is not None and (type(slot) is not int or not 1 <= slot <= 100):
        raise ValueError("save slot must be numbered 1..100")
    captures = plan.get("captures")
    if not isinstance(captures, list) or not captures:
        raise ValueError("captures must be a nonempty list")
    out = Path(out).resolve()
    if " " in str(out):
        raise ValueError("the harness screenshot path cannot contain spaces")
    runs = []
    used = set()
    for capture in captures:
        name = capture.get("name") if isinstance(capture, dict) else None
        if not isinstance(name, str) or not NAME.fullmatch(name) or name in used:
            raise ValueError(f"capture name must be unique and filename-safe: {name!r}")
        used.add(name)
        actions = capture.get("actions", [])
        if not isinstance(actions, list) or not all(isinstance(a, str) and valid_action(a) for a in actions):
            raise ValueError(f"{name}: actions must be normal inputs or read-only observations")
        setup = capture.get("setup", [])
        if not isinstance(setup, list) or not all(valid_setup(s) for s in setup):
            raise ValueError(f"{name}: setup must be one-line harness steps (no shot or quit)")
        expect = capture.get("expect", [])
        if not isinstance(expect, list) or not all(isinstance(x, str) and x for x in expect):
            raise ValueError(f"{name}: expect must be a list of nonempty regexes")
        shot = out / f"{name}.png"
        start = (["titlesettle", "slots", f"load {slot}", rf"until {STATE} 90", "freefield"]
                 if slot is not None else ["tofield"])
        steps = [*start, "skipmsg off", *setup, *actions,
                 "state", f"shot {shot.as_posix()} 1280"]
        runs.append({"name": name, "kind": "harness", "game": "ff6", "graphics": True, "minutes": 15,
                     "requests": [{"id": name, "from": "bnw-capture", "steps": steps,
                                   "pass": ([f"loading save {slot}"] if slot is not None else []) + expect + [f"shot .*{name}\\.png"],
                                   "fail": r"Exception|\[Error|error:"}]})
    return {"name": "bnw-background-capture", "mod": "bnw", "runs": runs}


def verify_shots(batch, out):
    from PIL import Image, ImageStat
    for run in batch["runs"]:
        path = out / f"{run['name']}.png"
        if not path.is_file():
            raise RuntimeError(f"missing screenshot: {path}")
        with Image.open(path) as im:
            im.verify()
        with Image.open(path) as im:
            if im.width < 640 or im.height < 360:
                raise RuntimeError(f"screenshot too small: {path} ({im.size})")
            sample = im.convert("RGB").resize((64, 36))
            variance = max(ImageStat.Stat(sample).var)
            visible = sum(max(sample.getpixel((x, y))) >= 28
                          for y in range(36) for x in range(64)) / (64 * 36)
            if visible < 0.05:
                raise RuntimeError(f"mostly black screenshot: {path} ({visible:.1%} visible)")
            if variance < 20:
                raise RuntimeError(f"blank or faded screenshot: {path} (variance {variance:.1f})")
        print(f"[capture] {path}")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("plan", type=Path)
    ap.add_argument("--out", type=Path, default=PR / "build" / "shots" / "bnw_capture")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args(argv)
    plan = json.loads(a.plan.read_text(encoding="utf-8"))
    batch = make_batch(plan, a.out)
    if a.dry_run:
        print(json.dumps(batch, indent=2))
        return 0
    if not (PLUGINS / "BraveNewWorld").is_dir() or any(
        (PLUGINS / mod).exists() for mod in ("BeyondChaos", "WorldsCollide", "TEdition")):
        print("[capture] Brave New World must already be the active mod; no install was changed", file=sys.stderr)
        return 2
    out = a.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    batch_path = out / "capture_batch.json"
    batch_path.write_text(json.dumps(batch, indent=2), encoding="utf-8")
    python = PR / ".venv" / "Scripts" / "python.exe"
    cmd = [str(python if python.exists() else sys.executable), str(PR / "tools" / "test_batch.py"),
           str(batch_path), "--no-build", "--no-install", "--no-warm", "--no-preflight"]
    result = subprocess.run(cmd, cwd=PR, check=False)
    if result.returncode:
        return result.returncode
    verify_shots(batch, out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
