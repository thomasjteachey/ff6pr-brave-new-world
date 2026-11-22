"""BNW's changed scenes in the PR's own map scripts (bnw-text-ui, 2026-11-20; what BNW changed beyond
its text: PORTING.md "BNW's changed scenes beyond its text", from tools/event_diff.py).

  * the Narshe blacksmith (WoR, SNES map 24, CC/0B1E; the PR's sc_e_0873): vanilla's choice was the
    magicite Ragnarok or the sword Ragnarok; BNW's "Choose your destiny" forges one of his two stones:
    Apocalypse (SNES item 0x1B, the PR's Ragnarok sword row) or Illumina (0x1A), or "Wait" (nothing
    taken, his flag not set: he asks again). The options' text is BNW's (bnw_dialog EXTRA_OPTIONS; the
    third under its own key, added by DialogText).
  * lines BNW removed from a scene and shows nowhere else (the audit's "lines removed" and "scene cut"
    rows whose BNW side the decoder reads whole): each Msg of those PR lines doesn't run (a Jump past
    it, every copy of its script). NPC chatter BNW dropped (Narshe, Jidoor's auction house, Mobliz) and
    the cabin's Empire exchange before Doma (BNW's Sabin hears only of the landslide).

Called by profile_bnw.extra_data (the porter's mod folder is new each port). Nothing here runs the
game; tests go through the PC test runner.
"""
import json
import os

import port_chests as C
import pr_script as S
import prdata as P

# (PR line key, why): lines BNW cut (SNES dialogue: the scene's unit in the audit)
CUT_LINES = [
    ("N002_C00_003_09_02", "Narshe townsperson (SNES map 20, CC/D207, dialogue 0x39E)"),
    ("N002_C00_003_10_02", "Narshe townsperson (SNES map 20, CC/D23F, dialogue 0x3A2)"),
    ("N048_C00_282_07_01", "Jidoor (SNES map 198, CB/453F: BNW has no auction house)"),
    ("N002_C00_105_05_01", "Jidoor (SNES map 198, CB/453F: \"This is the Jidoor Auction House\")"),
    ("N003_C00_282_10_01", "Jidoor (SNES map 198, CB/4592)"),
    ("E0951_00_305_a_01", "Mobliz, WoR (SNES map 340, CB/73FE, dialogue 0xB5D)"),
    ("E0097_00_073_a_02", "the cabin (SNES map 115, CB/0A5F): dialogue 0x1C5, the Empire's camp"),
    ("E0097_00_073_a_03", "the cabin: 0x1C5"),
    ("E0097_00_073_a_04", "the cabin: 0x1C5"),
    ("E0097_00_073_a_05", "the cabin: 0x1C5"),
    # Owzer's after Chadarnook (SNES map 208, CB/4DC6): BNW gives no esper there and shows none of
    # these (dialogues 0xAA3, 0xAAD, 0xAC2 unused; bnw-enemy-ai removes the gift in the same script)
    ("E0775_00_446_a_01", "Owzer's: \"This is magicite...\" (0xAA3)"),
    ("E0775_00_446_a_02", "Owzer's: 0xAA3"),
    ("E0775_00_446_a_03", "Owzer's: \"Obtained Lakshmi magicite!\" (0xAAD)"),
    ("E0775_00_446_a_04", "Owzer's: 0xAC2"),
    ("E0775_00_446_a_05", "Owzer's: 0xAC2"),
]

APOCALYPSE, ILLUMINA = 121, 120     # the PR's content ids of SNES items 0x1B / 0x1A (idmap.item)
WAIT_KEY = "BNW_E0873_00_010_c_03"   # the blacksmith's third option (bnw_dialog EXTRA_OPTIONS)


def _load(out_dir, f):
    """The port's copy of a vanilla file if an earlier step wrote one, else vanilla's."""
    mine = os.path.join(out_dir, os.path.relpath(f, P.VANILLA))
    return json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))


def _key(akeys, keys, f):
    rel = os.path.relpath(f, P.VANILLA).replace("\\", "/")
    group, value = rel.split("/", 1)
    value = os.path.splitext(value)[0]
    k = akeys.get((group, value))
    assert k, f"no asset key for {rel}"
    keys.setdefault(group, {})[k] = value


def _sv(m):
    return [x for x in (m.get("operands") or {}).get("sValues") or [] if x]


def blacksmith(out_dir, akeys, keys, log):
    f = S.path("sc_e_0873")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    sel = next(n for n, m in enumerate(ms) if m["mnemonic"] == "Select" and _sv(m) == ["E0873_00_010_c_01", "E0873_00_010_c_02"])
    assert ms[sel + 1]["mnemonic"] == "Branch", "the blacksmith's choice changed"
    gets = [n for n, m in enumerate(ms) if m["mnemonic"] == "GetItem"]
    assert [ms[n]["operands"]["iValues"][0] for n in gets] == [86, APOCALYPSE], "the blacksmith's gifts changed"
    ms[gets[0]]["operands"]["iValues"][0] = APOCALYPSE     # option 1: Forge Apocalypse
    ms[gets[1]]["operands"]["iValues"][0] = ILLUMINA       # option 2: Forge Illumina
    ms[sel]["operands"]["sValues"][2] = WAIT_KEY           # option 3: Wait
    # option 3 (MessageSelection 2): nothing taken, his flag not set
    stones = next(s["EntryPoint"] for s in sc["Segments"] if s["Label"] == "delete_stone")
    S.hook(sc, sel + 1, "bnw_wait", [
        S.op("Branch", i=[0, 2, "@wait"], s=["MessageSelection", "＝", "imm"]),
        S.op("Jump", i=["@vanilla"]),
        S.label("wait"),
        S.op("Call", i=[stones]),                        # his stones put away, as after a forge
        S.op("Exit"),
    ])
    S.save_path(sc, out_dir, f)
    _key(akeys, keys, f)
    log("bnw scenes: the Narshe blacksmith forges Apocalypse or Illumina (or waits)")


def mog_tips(out_dir, akeys, keys, log):
    """Mog joining in the World of Ruin (sc_e_0778): BNW has him explain its Dances right after
    (SNES dialogues 0x279, 0x27B: BNW_E0778_01/02, bnw_dialog ADDED_LINES)."""
    f = S.path("sc_e_0778")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    join = next(n for n, m in enumerate(ms) if m["mnemonic"] == "SysCall" and _sv(m) == ["メンバー加入：モグ"])
    assert ms[join + 1]["mnemonic"] == "RemoveCondition", "Mog's join scene changed"
    S.hook(sc, join + 1, "bnw_mog_tips", [
        S.op("Msg", s=["BNW_E0778_01"]),
        S.op("Msg", s=["BNW_E0778_02"]),
        S.op("Jump", i=["@vanilla"]),
    ])
    S.save_path(sc, out_dir, f)
    _key(akeys, keys, f)
    log("bnw scenes: Mog explains BNW's Dances when he joins in the World of Ruin")


def briefing(out_dir, akeys, keys, log):
    """The Returners' briefing ends (BNW CB/034D, both copies: sc_e_0062_1/_2): after Locke's "wait for
    me", BNW has Edgar "Uh, Locke...", Locke "Right.", Sabin "What do you see in that guy?" (dialogues
    0x16B-0x16D) where vanilla had Sabin's "Guy's got a point" (0x14A). The PR's Edgar line (a_39) and
    Sabin line (a_40) take BNW's, with Locke's between them."""
    for f in S.all_paths("sc_e_0062_1") + S.all_paths("sc_e_0062_2"):
        sc = _load(out_dir, f)
        ms = sc["Mnemonics"]
        at = {_sv(m)[0]: n for n, m in enumerate(ms) if m["mnemonic"] == "Msg" and _sv(m)}
        if "E0062_01_067_a_39" not in at or "E0062_01_067_a_40" not in at:
            continue
        ms[at["E0062_01_067_a_39"]]["operands"]["sValues"][0] = "BNW_D016B"
        sabin = at["E0062_01_067_a_40"]
        ms[sabin]["operands"]["sValues"][0] = "BNW_D016D"
        S.hook(sc, sabin, "bnw_briefing", [S.op("Msg", s=["BNW_D016C"]), S.op("Jump", i=["@vanilla"])])
        S.save_path(sc, out_dir, f)
        _key(akeys, keys, f)
    log("bnw scenes: the Returners' briefing ends with BNW's Edgar / Locke / Sabin lines")


def cut_lines(out_dir, akeys, keys, log):
    want = {k for k, _ in CUT_LINES}
    found, files = set(), 0
    import glob
    for f in glob.glob(os.path.join(P.VANILLA, "map_*", "**", "sc_*.json"), recursive=True):
        try:
            raw = open(f, encoding="utf-8").read()
        except OSError:
            continue
        if not any(k in raw for k in want):
            continue
        sc = _load(out_dir, f)
        ms = sc["Mnemonics"]
        ats = [n for n, m in enumerate(ms) if m["mnemonic"].startswith("Msg") and _sv(m) and _sv(m)[0] in want]
        if not ats:
            continue
        for at in ats:
            found.add(_sv(ms[at])[0])
            ms[at] = S.op("Jump", i=[at], label=ms[at]["label"], kind=ms[at].get("type", 1))   # goes on at at + 1
        S.save_path(sc, out_dir, f)
        _key(akeys, keys, f)
        files += 1
    missing = sorted(want - found)   # an NPC that says its line from its map entity, not a script
    log(f"bnw scenes: {len(found)} lines BNW cut don't show ({files} scripts)" +
        (f"; not in a script (kept): {', '.join(missing)}" if missing else ""))


def port(out_dir, log=print):
    akeys, keys = C.asset_keys(), {}
    blacksmith(out_dir, akeys, keys, log)
    mog_tips(out_dir, akeys, keys, log)
    briefing(out_dir, akeys, keys, log)
    cut_lines(out_dir, akeys, keys, log)
    for group, kv in keys.items():
        kdir = os.path.join(out_dir, group, "keys")
        os.makedirs(kdir, exist_ok=True)
        with open(os.path.join(kdir, "bnw_scenes.json"), "w", encoding="utf-8") as fh:
            json.dump({"keys": list(kv), "values": list(kv.values())}, fh, indent=1)
    return sorted(keys)
