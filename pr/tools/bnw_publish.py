"""The public Brave New World repository (ff6pr-brave-new-world): this project's BNW parts, copied
into their own git tree, with the modding kit as its kit/ submodule.

    .venv/Scripts/python tools/bnw_publish.py [OUT]     (default: ../../ff6pr-brave-new-world)

Copies the BNW plugin and its rules library, BNW's tools (its porter profile, plugin data, formula
probe) and its port checks into OUT with the project's layout (pr/...), writes its README,
.gitignore, requirements and manifest, and records the kit (ff6pr-kit, kit/) at the kit
repository's current commit: publish and commit the kit first (tools/kit_publish.py). Nothing of
Worlds Collide's. No BNW files either (its ROM, patch or source): a build uses the player's own.
OUT's .git is left alone: commit and push from there. The checks are tools/repo_publish.py's.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kitpath  # noqa: E402,F401  (the modding kit's tools, when it's this repository's kit/ submodule)
import repo_publish as R  # noqa: E402

DEFAULT_OUT = os.path.join(os.path.dirname(R.PROJECT), "ff6pr-brave-new-world")

PARTS = [
    ("pr/plugin/BnwPlugin", ["**"]),
    ("pr/plugin/Rules", ["**"]),
    ("pr/plugin/Rules.Tests", ["**"]),
    ("pr/tools", R.BNW_TOOLS),
    ("pr/tests", ["test_mod.py"]),
]

README = """# Brave New World for FINAL FANTASY VI Pixel Remaster

Brave New World 3.0, the FF6 rebalance and
redesign by Synchysi and its contributors, ported to the Steam Pixel Remaster. **In development:
preview builds are on the [Releases](https://github.com/thomasjteachey/ff6pr-brave-new-world/releases)
page** (about 87% done: data, espers, the battle engine, BNW's enemy AI and its dialogue are in;
BNW's monster art is not, and it hasn't had a full playthrough yet).

## Playing it

Download `BraveNewWorld-PR.zip` from the latest release and extract it into the game folder (next
to `FINAL FANTASY VI.exe`). Needs BepInEx 6 (IL2CPP x64, bleeding edge, made with 6.0.0-be.697)
and Magicite installed first; the zip's README has the details. Don't install it beside Worlds
Collide, and start a new game.

## How it works

- `kit/`: the [modding kit](https://github.com/thomasjteachey/ff6pr-kit) (a submodule). Its
  porter turns BNW's ROM into Pixel Remaster data: characters, equipment, spells, espers,
  monsters and their AI, formations, encounters, shops, chests, names and descriptions.
  `kit/pr/PORTING.md` is the project's full development log (BNW's status table and notes).
- `pr/tools/profile_bnw.py`: what BNW does differently that the porter can't read from the
  ROM's tables. `pr/tools/bnw_plugin_data.py`: BNW's esper system, relic and weapon rules,
  exported for the plugin. `pr/tools/port_bnw.py` runs the port.
- `pr/plugin/BnwPlugin`: the BepInEx plugin for BNW's mechanics (esper levels and the SP bank,
  restrictions and bonuses, weapon procs and relics, the Wait Gauge, BNW's battle formulas,
  Slot, Morph, Umaro, Cover...). `pr/plugin/Rules`: its game-free rules, with offline tests.
- `pr/tools/probe_check.py`: checks the in-game formula probe against FF6's and BNW's formulas.

## Build

Needs Windows, FF6 PR with BepInEx 6 and Magicite installed, .NET 6 SDK, Python 3.12, the US v1.0
SNES ROM (`rom/vanilla/Final Fantasy III (USA).sfc`), and Brave New World 3.0 from its own
distribution: its source in `mods/brave-new-world/source` and the patched ROM at
`build/brave-new-world/Brave New World 3.0 FRC.sfc`. None of these are in this repository.

    git clone --recurse-submodules <this repo>
    cd pr && python -m venv .venv && .venv/Scripts/pip install -r requirements.txt
    .venv/Scripts/python ../kit/pr/tools/extract.py --game "<your FF6 PR folder>"   # pr/vanilla
    .venv/Scripts/python tools/port_bnw.py                                         # pr/mods/BraveNewWorld
    bash ../kit/pr/tools/deploy.sh --bnw --pc                                      # build + install
    PR_MOD=BraveNewWorld .venv/Scripts/python -m unittest tests.test_mod           # port checks

## Credits

Brave New World by Synchysi and its contributors (see BNW's readme). BepInEx, Il2CppInterop,
HarmonyX, Magicite by Silvris. FINAL FANTASY VI is Square Enix's; this is an unofficial fan mod.
"""


def main():
    out = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT)
    entries = list(R.collect(PARTS))
    texts = {"README.md": README, ".gitignore": R.GITIGNORE_COMMON + "mods/\n",
             "pr/requirements.txt": R.REQUIREMENTS, "pr/modkit.json": R.manifest(keep=("bnw",))}
    done = R.write(out, entries, texts, submodules=[("kit", R.KIT_URL, R.head(R.KIT_REPO))])
    print(f"BNW publish: {done} -> {out}")


if __name__ == "__main__":
    main()
