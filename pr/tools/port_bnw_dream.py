"""Cyan's dream as Brave New World has it (bnw-dream, 2026-11-20): PowerPanda's "Cyan's Uneventful
Dream" (BNW's source: bnw 3.0/asm/cyans_uneventful_dream.asm; its event code read with
tools/snes_events.py, SNES map 317 and the Doma bed CB/82B1).

Vanilla's dream splits the party: Cyan goes to the reserve, two of the other three wait somewhere in the
Dreamscape (lying at two spots) and the third looks for them; the door to the Dream Stooges pushes the
party back until both have rejoined. BNW keeps the three together and has them find three pieces of
Cyan's psyche instead: three Cyans lying in the Dreamscape, each getting up, shaking his head and
vanishing in a flash (blue, red, green) when the party talks to him; the door pushes back until all
three are gone. The Stooges meet the party three times on the way (BNW adds a first one beside the first
Cyan and moves the third's spot), each "Back off!" and a flash, then the fight at the door as vanilla's.

The PR (its own redraw of the dream):
  * Doma's bed (sc_e_0895, Map_20451_4): Cyan to the reserve (控えに移動：カイエン) and the split
    (夢のダンジョン：分離 = SeparateCorpsAtDreamscape, tools/decomp/dream), into the Dreamscape (map 520).
  * The Dreamscape's first part (Map_30821_1, map 520 = SNES 317's west half, PR = SNES + (2, 1) in
    the maps' object coordinates): the Stooges (ev_e_0896 / ev_e_0898: a boy NPC and two touch
    triggers each, ScenarioFlag2 180 / 182), the two companions (ev_e_0897 / ev_e_0899: a touch object
    at the spot, the slot's leader shown there by the map script's puppet swap; flags 181 / 183).
  * The door (Map_30821_2, map 521, SNES 317's east part): ev_e_0900 pushes back until 181 and 183,
    ev_e_0901 the Stooges' fight (sc_e_0901, party 461; 184 after), ev_e_0902 on to the dream's train.
    Its map script swaps the slot 2/3 leaders' puppets in on every entry: with no split, slots 2/3 are
    empty and GetLeaderResidentEntityId gives entity 0 (decompiled: tools/decomp/dream_puppets), so
    BNW's flow doesn't run it.

Ported (each a rewrite of the PR file, so a re-port keeps it; nothing here runs the game):
  * the bed: Cyan to the reserve as vanilla (BNW's bed also takes him out: 3F 02 00), no split; the three
    Cyans set present (BNW's CB/847B sets their NPC bits 53A-53C), 181/183 set (no companions to find);
  * map 520: Cyan #1 (SNES 13,49) and Stooge #1 (14,49, triggers 13/14,50) new layers; Cyan #2/#3 on the
    companions' spots (SNES 17,46 / 27,52: their touch objects run the Cyans' scripts now, a Cyan NPC
    lies there); Stooge #3's boy moved to BNW's spot (SNES 26,51); the Cyans lie down on entry ('Fall',
    as the PR lays its companions);
  * map 521: the push-back while any Cyan is still there; no puppet swap; the fight and the way on as
    the PR's.
Flags: BNW's bits through the BNW allocation (data/snes_event_alloc_bnw.json, ScenarioFlag4): 0x53A-0x53C
(Cyan #n still there: set by the bed, cleared when he vanishes, BNW's own polarity) and 0x188 (Stooge #1
met). Script ids 9971-9972 (port_boss_events: 9961-9963).

    port(out_dir, log, tables) -> set of script-table ids changed
    .venv/Scripts/python tools/port_bnw_dream.py <out_dir>     (a dry run into a scratch folder)
    .venv/Scripts/python tools/port_bnw_dream.py --inplace     (onto pr/mods/BraveNewWorld as ported)
Headless checks: BnwPlugin/DreamTest.cs (harness dreamparty, dreamstate).
"""
import copy
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import port_chests as C  # noqa: E402
import pr_script as S  # noqa: E402
import prdata as P  # noqa: E402

GROUP, FOLDER = "map_30821", "Map_30821"
WEST, EAST = "Map_30821_1", "Map_30821_2"       # PR maps 520 / 521
BED_GROUP, BED_ROOM = "map_20451", "Map_20451_4"
ALLOC = os.path.join(P.ROOT, "data", "snes_event_alloc_bnw.json")
EQ = "＝"
SID_CYAN1, SID_STOOGE1 = 9971, 9972
SPLIT_FLAGS = (179, 180, 181, 182, 183)        # ScenarioFlag2: the dream's split progress, read by CurlaxsBattle
SPLIT_FLAGS_AT_DOOR = (179, 180, 181, 183)     # set when BNW's dream reaches the Stooges' door (QA dreamstate)
STOOGES_PARTY = 8346     # port_ai.new_fights: 8000 + BNW formation 346 (Curly, Larry, Moe, Shemp) on party 461's look
CYAN_ASSET = 122         # the PR's Cyan NPC (asset 122 on every map that shows him as an NPC)
SE_POOF = 106            # SNES sound 0x45 (vanilla's Stooge routine CB/8B9D <-> sc_e_0896)
FLASH = {"blue": [100, 100, 255, 150], "red": [255, 100, 100, 150], "green": [100, 255, 100, 150]}
SPLIT = "夢のダンジョン：分離"
RESERVE = "控えに移動：カイエン"

# BNW's bits (snes_events: CB/847B, CB/8AE3/8B0A/8B31, CB/8B58)
BIT_CYAN = (0x53A, 0x53B, 0x53C)
BIT_STOOGE1 = 0x188
# PR object coordinates (tile objects: x, bottom y in tiles) on map 520 = SNES + (2, 1)
DX, DY = 2, 1
CYAN1_AT, STOOGE1_AT, STOOGE1_TRIGGERS = (13, 49), (14, 49), ((13, 50), (14, 50))
STOOGE3_AT = (26, 51)
# new entity ids on map 520 (the PR's go up to 62)
E_CYAN = (63, 64, 65)     # the three Cyan NPCs
E_CYAN1_TOUCH, E_STOOGE1, E_STOOGE1_T = 66, 67, (68, 69)
E_TOUCH = (E_CYAN1_TOUCH, 55, 51)   # each Cyan's touch object (#2/#3: the companions' own)
OBJ = 100                 # new Tiled object ids from 101 (map 520's files use up to 77)


def _path(group, folder, sub, name, root=None):
    return os.path.join(root or P.VANILLA, group, "Assets", "GameAssets", "Serial", "Res", "Map", folder, sub, name + ".json")


def _load(out_dir, f):
    """The port's copy of a vanilla file if an earlier step wrote one, else vanilla's."""
    mine = os.path.join(out_dir, os.path.relpath(f, P.VANILLA))
    return json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))


def _write(out_dir, group, folder, sub, name, doc):
    dest = _path(group, folder, sub, name, out_dir)
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    with open(dest, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))


def _key(akeys, keys, group, folder, sub, name):
    value = f"Assets/GameAssets/Serial/Res/Map/{folder}/{sub}/{name}"
    keys.setdefault(group, {})[akeys.get((group, value)) or f"{sub}/{name}"] = value


def _flag(f):
    return f[0], f[1]


def _set(f):
    return S.op("SetFlag", i=[f[1]], s=[f[0]])


def _reset(f):
    return S.op("ResetFlag", i=[f[1]], s=[f[0]])


def _branch(f, value, target):
    """if flag f == value: go on after `target`"""
    return S.op("Branch", i=[f[1], value, target], s=[f[0], EQ, "imm"])


def _script(name, blocks, like=None):
    """A script from labelled blocks [(label, kind, ops)] ("@label" targets resolved). Each block is a
    segment, except a puppet's own entries ("<head>_NN"), which belong to their head's (as the PR's)."""
    ops, segs = [], []
    for lab, kind, body in blocks:
        if segs and lab.startswith(segs[-1]["Label"] + "_"):
            segs[-1]["Count"] += len(body) + 1
        else:
            segs.append({"Label": lab, "EntryPoint": len(ops), "Count": len(body) + 1})
        ops.append(S.label(lab, kind=kind))
        for o in body:
            o["type"] = kind
        ops += body
    S._resolve(ops, 0)
    base = copy.deepcopy(like) if like else {
        "SystemFlag": {"SkipInitialize": 0, "KeepPlayerPuppet": 0, "KeepBadStatusVisual": 0, "RidingVehicle": 0},
        "Animations": [], "Title": {"main": "汎用スクリプト", "sub": ""}}
    base.update({"Segments": segs, "Mnemonics": ops, "Name": name,
                 "ScriptLocal": ["なし"], "ScriptLocalValue": [0]})   # PlaySE keeps its handle in one
    return base


def _puppets(name, slots):
    """A puppet block: [(entity, ops)] each run on its own slot (SetPuppet, ExecPuppet, Return)."""
    head = [S.op("SetPuppet", i=[k, ent, f"@{name}_{k:02d}"]) for k, (ent, _) in enumerate(slots)]
    blocks = [(name, 2, head + [S.op("ExecPuppet"), S.op("Return")])]
    for k, (_, body) in enumerate(slots):
        blocks.append((f"{name}_{k:02d}", 2, body + [S.op("Exit")]))
    return blocks


def _obj_props(o):
    return {p["name"]: p for p in o.get("properties", [])}


def _layer_doc(like_doc, name, objects, layer_id):
    """An entity set (ev_*.json) like like_doc's with these objects; group/layer ids past the map's own."""
    doc = copy.deepcopy(like_doc)
    doc["name"], doc["id"] = name, layer_id
    doc["layers"] = [doc["layers"][0]]
    doc["layers"][0]["objects"] = objects
    doc["layers"][0]["id"] = layer_id + 1
    return doc


def _clone(o, oid, entity, at, **props):
    """A copy of tile object o with Tiled id oid, entity id `entity`, at SNES cell `at` (map 520's offset)."""
    n = copy.deepcopy(o)
    n["id"] = oid
    n["x"], n["y"] = 16 * (at[0] + DX), 16 * (at[1] + DY)
    pr = _obj_props(n)
    pr["entity_id"]["value"] = entity
    for k, v in props.items():
        pr[k]["value"] = v
    return n


def _cyan_npc(boy, oid, entity, at):
    """A Cyan NPC (the Stooge's boy NPC with Cyan's look) that only shows: no talk, no turning."""
    n = _clone(boy, oid, entity, at, asset_id=CYAN_ASSET, action_id=0, eye_contact=False, turn_around=False,
               idling=False, pushed=False, direction=4)
    n["name"] = "BNW カイエン（心のかけら）"
    return n


# --- the scripts ---------------------------------------------------------------------------------------
def _cyan_script(name, n, flags, like):
    """Talking to Cyan #n: (#2/#3: the leader steps beside him as the PR's companions have it) he gets up,
    shakes his head, faces down and vanishes in his flash (BNW CB/8AE3, CB/8B0A, CB/8B31)."""
    color = ("blue", "red", "green")[n]
    me, touch = E_CYAN[n], E_TOUCH[n]
    main = [_branch(flags[n], 0, "@done")]
    if like is not None:
        # the PR's steps for its companion on the same spot (sc_e_0897/0899 0-11: by the leader's facing)
        steps = like["Mnemonics"]
        assert steps[1]["mnemonic"] == "BranchByPlayerDirection" and steps[11]["label"] == "スタート", f"{name}: the steps moved"
        main += [S.op("BranchByPlayerDirection", i=["@up", "@right", "@down", "@start"])]
    blocks = [("Main", 1, main + [S.op("Jump", i=["@start"])])]
    if like is not None:
        puppet = {"up": 19, "right": 33, "down": 49}
        for lab, at in puppet.items():
            blocks.append((lab, 1, [S.op("Call", i=[f"@steps_{lab}"]), S.op("Jump", i=["@start"])]))
    blocks.append(("start", 1, [
        S.op("Wait", r=[0.35]),
        S.op("Call", i=["@get_up"]),
        S.op("Wait", r=[0.5]),
        S.op("Flash", i=FLASH[color], r=[0.1]),
        S.op("PlaySE", i=[SE_POOF, 1]),
        S.op("Call", i=["@vanish"]),
        _reset(flags[n]),
        S.op("Exit")]))
    blocks.append(("done", 1, [S.op("Exit")]))
    blocks += _puppets("get_up", [(me, [
        S.op("LookAt", i=[2]),
        S.op("Wait", r=[0.35]),
        S.op("PlayAnim", i=[0, 4], r=[2], s=["shake"]),
        S.op("LookAt", i=[2]),
        S.op("Wait", r=[0.35])])])
    blocks += _puppets("vanish", [(me, [S.op("Hide")]), (touch, [S.op("Hide")])])
    if like is not None:
        lm = like["Mnemonics"]
        for lab, at in {"up": 19, "right": 33, "down": 49}.items():
            # the PR's block: SetPuppet [slot, entity, entry]..., ExecPuppet, Return; each entry runs to its Exit
            slots, k = [], at + 1
            while lm[k]["mnemonic"] == "SetPuppet":
                ent, entry = lm[k]["operands"]["iValues"][1:3]
                end = next(j for j in range(entry + 1, len(lm)) if lm[j]["mnemonic"] == "Exit")
                slots.append((ent, S.copy_ops(like, entry + 1, end)))
                k += 1
            assert slots and lm[k]["mnemonic"] == "ExecPuppet", f"{name}: the steps at {at} changed"
            blocks += _puppets(f"steps_{lab}", slots)
    sc = _script(name, blocks, like=like if like is not None else S.load("sc_e_0897"))
    sc["Name"] = name
    return sc


def _stooge1_script(flag):
    """Stooge #1 (BNW CB/8B58): once; the boy hops, "Back off!", the flashes, he's gone: the PR's own
    Stooge scene (sc_e_0896) on the new boy and triggers."""
    like = S.load("sc_e_0896")
    sc = copy.deepcopy(like)
    sc["Name"] = "sc_bnw_dream_stooge1"
    remap = {58: E_STOOGE1, 56: E_STOOGE1_T[0], 57: E_STOOGE1_T[1]}
    for m in sc["Mnemonics"]:
        iv = m["operands"]["iValues"]
        if m["mnemonic"] == "SetPuppet" and iv[1] in remap:
            iv[1] = remap[iv[1]]
        if m["mnemonic"] == "SetFlag":
            assert iv[0] == 180, "the Stooge's flag moved"
            iv[0] = flag[1]
            m["operands"]["sValues"][0] = flag[0]
    ms = sc["Mnemonics"]
    assert ms[1]["mnemonic"] == "Call" and ms[2]["mnemonic"] == "Msg", "the Stooge scene changed"
    S.hook(sc, 1, "bnw_once", [_branch(flag, 1, "@met"), S.op("Jump", i=["@vanilla"]), S.label("met"), S.op("Exit")])
    return sc


def _west_map_script(flags, stooge1):
    """Map 520's script: the Stooges not met yet, the Cyans still there (lying down)."""
    s1, s3 = ("ScenarioFlag2", 180), ("ScenarioFlag2", 182)
    main = [
        _branch(s1, 1, "@stooge1"), S.op("SetEntities", s=["ev_e_0896"]),
        S.label("stooge1"), _branch(stooge1, 1, "@stooge3"), S.op("SetEntities", s=["ev_bnw_dream_stooge1"]),
        S.label("stooge3"), _branch(s3, 1, "@cyan1"), S.op("SetEntities", s=["ev_e_0898"]),
        S.label("cyan1"), _branch(flags[0], 0, "@cyan2"), S.op("SetEntities", s=["ev_bnw_dream_cyan1"]),
        S.op("Call", i=["@fall1"]),
        S.label("cyan2"), _branch(flags[1], 0, "@cyan3"), S.op("SetEntities", s=["ev_e_0897"]),
        S.op("Call", i=["@fall2"]),
        S.label("cyan3"), _branch(flags[2], 0, "@end"), S.op("SetEntities", s=["ev_e_0899"]),
        S.op("Call", i=["@fall3"]),
        S.label("end"), S.op("Exit")]
    blocks = [("Main", 1, main)]
    for n in range(3):   # each Cyan lies on his spot as the PR lays its companions (position1/2: 'Fall')
        blocks += _puppets(f"fall{n + 1}", [(E_CYAN[n], [S.op("SetPos", i=[0, 0, 0, 1, E_TOUCH[n]], s=["Fall"])])])
    sc = _script("sc_map_30821_1", blocks, like=S.load("sc_map_30821_1"))
    sc["Name"] = S.load("sc_map_30821_1")["Name"]
    return sc


def _east_map_script(flags):
    """Map 521's script: the push-back while a Cyan is still there; the fight and the way on as the PR's
    (sc_map_30821_2 11-19); no puppet swap (no split)."""
    van = S.load("sc_map_30821_2")
    ms = van["Mnemonics"]
    assert ms[11]["label"] == "ev_e_0901" and ms[13]["operands"]["sValues"][0] == "ev_e_0901" and \
        ms[17]["operands"]["sValues"][0] == "ev_e_0902", "the door's map script changed"
    main = [_branch(f, 1, "@barrier") for f in flags] + [S.op("Jump", i=["@fight"]),
            S.label("barrier"), S.op("SetEntities", s=["ev_e_0900"]),
            S.label("fight"),
            S.op("Branch", i=[184, 1, "@train"], s=["ScenarioFlag2", EQ, "imm"]),
            S.op("SetEntities", s=["ev_e_0901"]),
            S.label("train"),
            S.op("Branch", i=[184, 0, "@end"], s=["ScenarioFlag2", EQ, "imm"]),
            S.op("Branch", i=[185, 1, "@end"], s=["ScenarioFlag2", EQ, "imm"]),
            S.op("SetEntities", s=["ev_e_0902"]),
            S.label("end"), S.op("Exit")]
    sc = _script(van["Name"], [("Main", 1, main)], like=van)
    return sc


def _bed(out_dir, flags, akeys, keys):
    """Doma's bed (sc_e_0895): Cyan to the reserve, no split; the Cyans set present, no companions."""
    f = _path(BED_GROUP, "Map_20451", BED_ROOM, "sc_e_0895")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    if any(s["Label"] == "bnw_no_split" for s in sc["Segments"]):
        _key(akeys, keys, BED_GROUP, "Map_20451", BED_ROOM, "sc_e_0895")
        return   # a re-run on the port's own copy
    at = next(n for n, m in enumerate(ms) if m["mnemonic"] == "SysCall" and m["operands"]["sValues"][0] == SPLIT)
    assert ms[at - 1]["operands"]["sValues"][0] == RESERVE and ms[at + 1]["mnemonic"] == "ChangeMap", "Doma's bed changed"
    blk = S.append(sc, "bnw_no_split", [_set(fl) for fl in flags] + [
        S.op("SetFlag", i=[181], s=["ScenarioFlag2"]),
        S.op("SetFlag", i=[183], s=["ScenarioFlag2"]),
        S.op("Jump", i=[at])])                      # goes on at the ChangeMap
    ms[at] = S.op("Jump", i=[blk], kind=ms[at].get("type", 1))
    S.save_path(sc, out_dir, f)
    _key(akeys, keys, BED_GROUP, "Map_20451", BED_ROOM, "sc_e_0895")


def _has_party(out_dir, tables, pid):
    """Whether the port has monster party pid: in its tables (a full port: port_ai.new_fights ran before
    port_boss_events calls us), else in the mod folder's partial master CSV (--inplace)."""
    mp = (tables or {}).get("monster_party")
    if mp is not None:
        return pid in mp.rows
    f = os.path.join(out_dir, "master", "Assets", "GameAssets", "Serial", "Data", "Master", "monster_party.csv")
    return os.path.exists(f) and pid in P.Table("monster_party", text=open(f, encoding="utf-8").read()).rows


# sc_e_0901's puppets for the split's other groups (the slot 2/3 leaders vanilla's map script swaps in,
# entity 0, and the three groups' stand-ins 9-11): BNW's dream has no split, so they don't exist, and the
# scene waited on them for ever (QA bnw-11 dream-1, 2026-11-20: no battle after `script sc_e_0901`)
SPLIT_PUPPETS = {0, 9, 10, 11}


def _pushback_script():
    """sc_e_0900 (the door pushes back while a Cyan is still out there) without the split's puppets: vanilla
    moves the player and the other group's leader (entity 0, SPLIT_PUPPETS) back; with no split that leader
    doesn't exist and the scene never ended (QA, laptop dream-1, 2026-11-20). The player's push-back stays."""
    sc = S.load("sc_e_0900")
    nop = {"iValues": [], "rValues": [], "sValues": []}
    for m in sc["Mnemonics"]:
        if m["mnemonic"] == "SetPuppet" and m["operands"]["iValues"][1] in SPLIT_PUPPETS:
            m["mnemonic"], m["operands"] = "Nop", dict(nop)
    assert any(m["mnemonic"] == "SetPuppet" and m["operands"]["iValues"][1] == -1001 for m in sc["Mnemonics"]), \
        "the push-back lost the player"
    return sc


def _fight_script():
    """sc_e_0901 (the Stooges at the door) with BNW's fight: its EncountBoss on STOOGES_PARTY, and
    without the split's puppets (SPLIT_PUPPETS): the player's knock-back, the lines and the fight stay."""
    sc = S.load("sc_e_0901")
    ms = sc["Mnemonics"]
    enc = [m for m in ms if m["mnemonic"] == "EncountBoss"]
    assert len(enc) == 1 and enc[0]["operands"]["iValues"][0] == 461, "the Stooges' fight changed"
    enc[0]["operands"]["iValues"][0] = STOOGES_PARTY
    nop = {"iValues": [], "rValues": [], "sValues": []}
    for m in ms:
        if m["mnemonic"] == "SetPuppet" and m["operands"]["iValues"][1] in SPLIT_PUPPETS:
            m["mnemonic"], m["operands"] = "Nop", dict(nop)
    # a puppet block left with no puppet: its Call goes too (an ExecPuppet of none isn't relied on)
    for m in ms:
        if m["mnemonic"] != "Call":
            continue
        at = m["operands"]["iValues"][0]
        k, puppets = at + 1, 0
        while k < len(ms) and ms[k]["mnemonic"] in ("SetPuppet", "Nop"):
            puppets += ms[k]["mnemonic"] == "SetPuppet"
            k += 1
        if ms[k]["mnemonic"] == "ExecPuppet" and puppets == 0:
            m["mnemonic"], m["operands"] = "Nop", dict(nop)
    assert not any(m["mnemonic"] == "SetPuppet" and m["operands"]["iValues"][1] in SPLIT_PUPPETS for m in ms)
    # the door's animation cue (entity 2) and the player's knock-back stay (vanilla's): they were taken out after
    # the 18:49 stall (QA bnw-11 rest), but that stall was the dream's flags (below); without them the player wasn't
    # where the scene leaves him for EncountBoss, and the battle's start reloaded map 521, whose map script then
    # replaced the battle script in the battle's opening (QA dream-1, 02:44: sc_b_2028 -> sc_map_30821_2 at CreateObj)
    # the fight's battle routine (sc_b_2028's SysCall "夢の三兄弟バトル" = SystemCall.Current.CurlaxsBattle) reads the
    # dream's split flags ScenarioFlag2 179-183: with BNW's state at the door (179, 180, 181, 183 set, 182 not) it held
    # the battle in its opening for good; all five set, no enemies loaded; none set (or 182 alone), the fight plays
    # with all three Stooges (QA dream-diag2/3, Deck, 2026-11-21). So they're cleared just before the fight, and the
    # after-battle script (_after_fight_script) sets the four back for the map scripts that read them.
    S.hook(sc, ms.index(enc[0]), "bnw_brothers_clear", [S.op("ResetFlag", i=[f], s=["ScenarioFlag2"]) for f in SPLIT_FLAGS])
    return sc


def _after_fight_script():
    """sc_e_0901_1 (after the Stooges' fight): the split flags _fight_script cleared, set back to the dream's state
    at the door before vanilla's "fought" (ScenarioFlag2 184)."""
    sc = S.load("sc_e_0901_1")
    ms = sc["Mnemonics"]
    at = next(n for n, m in enumerate(ms) if m["mnemonic"] == "SetFlag" and m["operands"]["iValues"][0] == 184)
    S.hook(sc, at, "bnw_brothers_restore", [S.op("SetFlag", i=[f], s=["ScenarioFlag2"]) for f in SPLIT_FLAGS_AT_DOOR])
    return sc


def _package(out_dir, sub, scripts=(), entities=()):
    tail = (GROUP, "Assets", "GameAssets", "Serial", "Res", "Map", FOLDER, "package.json")
    out = os.path.join(out_dir, *tail)
    doc = json.load(open(out if os.path.exists(out) else os.path.join(P.VANILLA, *tail), encoding="utf-8"))
    entry = next(m for m in doc["map"] if m["name"] == sub)
    for kind, names in (("script", scripts), ("entity", entities)):
        for name in names:
            asset = f"{sub}/{name}"
            if not any(x["asset"] == asset for x in entry.setdefault(kind, [])):
                entry[kind].append({"name": name, "asset": asset})
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))


def port(out_dir, log=print, tables=None):
    import event_translate as T
    alloc = T.FlagAlloc(ALLOC)
    flags = [alloc.get(b) for b in BIT_CYAN]
    stooge1 = alloc.get(BIT_STOOGE1)
    alloc.save()
    akeys, keys = C.asset_keys(), {}
    changed = set()
    if tables is not None:
        scripts = tables.setdefault("script", P.Table("script"))
        for sid, name in ((SID_CYAN1, "sc_bnw_dream_cyan1"), (SID_STOOGE1, "sc_bnw_dream_stooge1")):
            assert sid not in scripts.rows or scripts.rows[sid]["script_name"] == name, f"script id {sid} taken"
            scripts.rows[sid] = {"id": str(sid), "script_name": name}
            changed.add(sid)

    _bed(out_dir, flags, akeys, keys)

    # map 520's layers
    vdir = os.path.dirname(_path(GROUP, FOLDER, WEST, "x"))
    ev96 = json.load(open(os.path.join(vdir, "ev_e_0896.json"), encoding="utf-8"))
    boy = next(o for o in ev96["layers"][0]["objects"] if _obj_props(o)["object_type"]["value"] == 6)
    trig = next(o for o in ev96["layers"][0]["objects"] if _obj_props(o)["object_type"]["value"] == 2)
    ev97 = _load(out_dir, os.path.join(vdir, "ev_e_0897.json"))
    touch = next(o for o in ev97["layers"][0]["objects"] if _obj_props(o)["entity_id"]["value"] == 55)
    stooge1_layer = _layer_doc(ev96, "ev_bnw_dream_stooge1", [
        _clone(boy, OBJ + 1, E_STOOGE1, STOOGE1_AT),
        _clone(trig, OBJ + 2, E_STOOGE1_T[0], STOOGE1_TRIGGERS[0], script_id=SID_STOOGE1),
        _clone(trig, OBJ + 3, E_STOOGE1_T[1], STOOGE1_TRIGGERS[1], script_id=SID_STOOGE1)], 90)
    stooge1_layer["layers"][0]["objects"][0]["name"] = "BNW 3兄弟の1人（1人目）"
    cyan1_layer = _layer_doc(ev97, "ev_bnw_dream_cyan1", [
        _cyan_npc(boy, OBJ + 4, E_CYAN[0], CYAN1_AT),
        _clone(touch, OBJ + 5, E_CYAN1_TOUCH, CYAN1_AT, script_id=SID_CYAN1)], 92)
    for name, doc in (("ev_bnw_dream_stooge1", stooge1_layer), ("ev_bnw_dream_cyan1", cyan1_layer)):
        _write(out_dir, GROUP, FOLDER, WEST, name, doc)
        _key(akeys, keys, GROUP, FOLDER, WEST, name)
    # Cyan #2 / #3 lie on the companions' spots (their touch objects 55 / 51 run the Cyans' scripts now)
    for n, name in ((1, "ev_e_0897"), (2, "ev_e_0899")):
        doc = _load(out_dir, os.path.join(vdir, name + ".json"))
        objs = doc["layers"][0]["objects"]
        t = next(o for o in objs if _obj_props(o)["entity_id"]["value"] == E_TOUCH[n])
        if not any(_obj_props(o)["entity_id"]["value"] == E_CYAN[n] for o in objs):
            at = (t["x"] // 16 - DX, t["y"] // 16 - DY)
            objs.append(_cyan_npc(boy, OBJ + 5 + n, E_CYAN[n], at))
        _write(out_dir, GROUP, FOLDER, WEST, name, doc)
        _key(akeys, keys, GROUP, FOLDER, WEST, name)
    # Stooge #3's boy where BNW stands him (SNES 26,51; vanilla 28,52, now Cyan #3's neighbour)
    doc = _load(out_dir, os.path.join(vdir, "ev_e_0898.json"))
    b = next(o for o in doc["layers"][0]["objects"] if _obj_props(o)["object_type"]["value"] == 6)
    b["x"], b["y"] = 16 * (STOOGE3_AT[0] + DX), 16 * (STOOGE3_AT[1] + DY)
    _write(out_dir, GROUP, FOLDER, WEST, "ev_e_0898", doc)
    _key(akeys, keys, GROUP, FOLDER, WEST, "ev_e_0898")

    # scripts
    out = [
        (WEST, "sc_bnw_dream_cyan1", _cyan_script("sc_bnw_dream_cyan1", 0, flags, None)),
        (WEST, "sc_e_0897", _cyan_script("sc_e_0897", 1, flags, S.load("sc_e_0897"))),
        (WEST, "sc_e_0899", _cyan_script("sc_e_0899", 2, flags, S.load("sc_e_0899"))),
        (WEST, "sc_bnw_dream_stooge1", _stooge1_script(stooge1)),
        (WEST, "sc_map_30821_1", _west_map_script(flags, stooge1)),
        (EAST, "sc_map_30821_2", _east_map_script(flags)),
        (EAST, "sc_e_0900", _pushback_script()),
    ]
    # the Stooges' fight: BNW's line-up (formation 346, Shemp hidden till their counters bring him in) once
    # the port has its party (bnw-enemy-ai's snes_ai_new_fights {346: 461} -> 8346); else the PR's 461
    if _has_party(out_dir, tables, STOOGES_PARTY):
        out.append((EAST, "sc_e_0901", _fight_script()))
        out.append((EAST, "sc_e_0901_1", _after_fight_script()))
        fight = STOOGES_PARTY
    else:
        fight = 461
    for sub, name, sc in out:
        _write(out_dir, GROUP, FOLDER, sub, name, sc)
        _key(akeys, keys, GROUP, FOLDER, sub, name)
    _package(out_dir, WEST, scripts=["sc_bnw_dream_cyan1", "sc_bnw_dream_stooge1"],
             entities=["ev_bnw_dream_cyan1", "ev_bnw_dream_stooge1"])
    keys.setdefault(GROUP, {})[akeys.get((GROUP, f"Assets/GameAssets/Serial/Res/Map/{FOLDER}/package")) or "package"] = \
        f"Assets/GameAssets/Serial/Res/Map/{FOLDER}/package"

    for group, kv in keys.items():
        kdir = os.path.join(out_dir, group, "keys")
        os.makedirs(kdir, exist_ok=True)
        with open(os.path.join(kdir, "bnw_dream.json"), "w", encoding="utf-8") as fh:
            json.dump({"keys": list(kv), "values": list(kv.values())}, fh, indent=1)
    log(f"  Cyan's dream as BNW's: no split, three Cyans (flags {flags}), Stooge #1 ({stooge1}), the Stooges' "
        f"fight party {fight}; {sum(len(v) for v in keys.values())} files")
    return changed


def apply_in_place(mod_dir=None, log=print):
    """Onto an already ported mod folder (no re-port): its files plus its script-table rows (the mod's
    partial master script.csv)."""
    mod_dir = mod_dir or os.path.join(P.ROOT, "mods", "BraveNewWorld")
    csv_path = os.path.join(mod_dir, "master", "Assets", "GameAssets", "Serial", "Data", "Master", "script.csv")
    t = P.Table("script", text=open(csv_path, encoding="utf-8").read())
    ids = port(mod_dir, log, {"script": t})
    with open(csv_path, "w", encoding="utf-8", newline="") as f:
        f.write(t.to_csv(set(t.rows)))
    return ids


if __name__ == "__main__":
    if sys.argv[1:] == ["--inplace"]:
        print(sorted(apply_in_place()))     # pr/mods/BraveNewWorld as it is
    else:
        print(sorted(port(sys.argv[1])))    # a dry run into a scratch folder
