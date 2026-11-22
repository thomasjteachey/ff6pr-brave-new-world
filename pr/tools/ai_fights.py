"""Play BNW's monster-AI fights headless, many in one game run (the enemy-AI session, 2026-11-20: "make
slow tests fast"): one start, one save loaded with a real party, then each formation in turn with the
kit harness's "battle <party>" (EncountBoss), the party on auto and kept alive (BNW's "aikeep"), optional
knockouts ("ko": a BNW boss is held at 0 HP for its script), the victory screens pressed through, and a
fight that doesn't end by itself finished with "endfight". Each fight's enemy-AI log lines (turns,
counters, deaths, entries, battle lines: BnwEnemyAi's verbose host in a probe run) go to
build/ai_fights/<party>.log, and a summary line per fight to stdout.

Start the run first (in the background; it ends on "quit" or after -Minutes):

    powershell -NoProfile -ExecutionPolicy Bypass -File pr\\tools\\probe_headless.ps1 -Minutes 40 `
        -Extra "harness=1" -Until "\\[probe\\] done"
    .venv/Scripts/python tools/ai_fights.py --slot 11 --seconds 60 --ko 12 443 447 460

--slot: the save to load (its party fights; the run's saves are blocked); --seconds: each fight's length
before endfight; --ko N: a knockout every N seconds (0: the party's own attacks only); --keep-going:
don't quit the game at the end.
"""
import argparse
import os
import re
import socket
import sys
import time

GAME_LOG = r"D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR\BepInEx\LogOutput.log"
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "build", "ai_fights")
PORT = 47811


class Game:
    def __init__(self, connect=180):
        end = time.time() + connect
        while True:
            try:
                self.sock = socket.create_connection(("127.0.0.1", PORT), timeout=5)
                break
            except OSError:
                if time.time() > end:
                    raise
                time.sleep(2)
        self.sock.settimeout(140)
        self.f = self.sock.makefile("r", encoding="utf-8", newline="\n")

    def ask(self, line):
        self.sock.sendall((line + "\n").encode("utf-8"))
        r = self.f.readline()
        if not r:
            raise ConnectionError("the game closed the connection")
        return r.rstrip("\n")

    def until(self, cmd, pattern, timeout, every=1.0, between=None):
        end = time.time() + timeout
        r = ""
        while time.time() < end:
            r = self.ask(cmd)
            if re.search(pattern, r):
                return r
            if between:
                self.ask(between)
            time.sleep(every)
        return None


def log_size():
    try:
        return os.path.getsize(GAME_LOG)
    except OSError:
        return 0


def log_since(pos):
    try:
        with open(GAME_LOG, "rb") as fh:
            fh.seek(pos)
            return fh.read().decode("utf-8", "replace").splitlines()
    except OSError:
        return []


def fight(g, party, seconds, ko_every, log):
    pos = log_size()
    r = g.until(f"battle {party}", r"started", 60, every=2)
    if not r:
        return f"party {party}: couldn't start ({g.ask('state')[:200]})"
    if not g.until("aistat", r"battle True", 60, every=1):
        return f"party {party}: no battle came"
    start, last_ko, ended, kos = time.time(), time.time(), False, 0
    stat = ""
    while time.time() - start < seconds:
        time.sleep(2)
        stat = g.ask("aistat")
        if "battle False" in stat:
            ended = True
            break
        if re.search(r"phase \w*(Result|Lose|Win)", stat):
            g.ask("press action")
        if ko_every and time.time() - last_ko >= ko_every:
            last_ko = time.time()
            k = g.ask("ko")
            if "ko monster" in k:
                kos += 1
                log.append(f"[driver] {k}")
    how = "ended by itself" if ended else "still going"
    if not ended:
        log.append("[driver] " + g.ask("endfight"))
    # the victory screen (and any after-battle message) waits for a press
    if not g.until("state", r"scene field", 90, every=1.5, between="press action"):
        how += "; NOT back on the field"
    time.sleep(2)
    lines = [l for l in log_since(pos) if "enemy AI" in l or "[driver]" in l or "Exception" in l or "[harness]" in l and "act" not in l]
    log.extend(lines)
    turns = sum(" turn: " in l for l in lines)
    counters = sum(" counter: " in l for l in lines)
    deaths = sum(" death: " in l for l in lines)
    entries = sum(" entry/exit: " in l for l in lines)
    errors = sum("Exception" in l for l in lines)
    return (f"party {party}: {how} after {int(time.time() - start)} s, {kos} knockouts; log: {turns} turns, {counters} counters, "
            f"{deaths} deaths, {entries} entries, {errors} exceptions; {stat[:300]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("parties", nargs="+", type=int)
    ap.add_argument("--slot", default="")
    ap.add_argument("--seconds", type=float, default=60)
    ap.add_argument("--ko", type=float, default=0)
    ap.add_argument("--keep-going", action="store_true")
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    g = Game()
    print(g.ask("state")[:200], flush=True)
    g.ask("skipmsg on")
    if not re.search(r"scene field", g.ask("state")) or re.search(r"map 609\b", g.ask("state")):
        # at FF6's title the in-game load does nothing headless and the title's idle demo (map 609) looks
        # like a field after ~74 s (the PC test runner, 2026-11-20): the title's own load path, and not 609
        g.until(f"load {a.slot} title" if a.slot else "load", r"loading", 120, every=3)   # the title path needs a slot
        if not g.until("state", r"scene field(?!.*map 609\b)", 240, every=3):
            print("never reached the field: " + g.ask("state")[:300])
            return 1
    time.sleep(5)
    g.ask("aikeep on")
    for p in a.parties:
        log = []
        try:
            line = fight(g, p, a.seconds, a.ko, log)
        except Exception as e:   # one fight's trouble shouldn't lose the others' results
            line = f"party {p}: driver error {e!r}"
        with open(os.path.join(OUT, f"{p}.log"), "w", encoding="utf-8") as fh:
            fh.write("\n".join(log) + "\n")
        print(line, flush=True)
    if not a.keep_going:
        try:
            g.ask("quit")
        except Exception:
            pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
