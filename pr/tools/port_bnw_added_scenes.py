"""BNW's added and extended story scenes in the PR's own map scripts (bnw-scenes, 2026-11-20; the event
audit's "Scenes added or extended" row, PORTING.md "BNW's changed scenes beyond its text"). Read with
tools/snes_events.py on BNW's ROM (its bank-ED code: event_diff.rom_for(path, [(0x230000, 0x260000)])).

  * Kefka's Tower's barrier (the Falcon, SNES map 17, CA/01A2 -> EE/AF50): BNW's party can't fly into
    the tower until it has Terra's pendant back. Story bit 0x2DB (set after the Cultists' Tower top's
    battle, CC/557F: the PR's ScenarioFlag2 168, sc_e_0881_1) opens it. Without it Setzer stops them
    ("Not so fast there, champ."): the forcefield, the Warring Triad, Terra's pendant lost when the
    Blackjack fell apart (0x5FC-0x6A0), bit 0x0E0 set (a flag of ours), back to the Falcon; on later
    tries the short version (0xABE / 0x6A2 / 0x6A1). With it, after a refusal: "The pendant is
    glowing!", the flash, "We're through!" (0xABF / 0x5FF / 0xAC0), then the PR's own scene (the
    Warring Triad talk, the party split, the drop). The PR's scene is sc_e_0966_3 (map 268, the
    Falcon's deck at the tower): BNW's check goes right after its first line (E0966_00_268_a_02, BNW's
    0xBA0 "It's time to take the fight to Kefka!"), on the first visit only (the PR's ScenarioFlag2 217
    skips it all once the tower was entered, as BNW's bit 0x029 does). A refusal ends as boarding the
    Falcon does (sc_e_FAinboard_1: on the deck, the Falcon where sc_e_0966_2 left it, by the tower).
  * Leo's Crest (BNW's relic: SNES item 0xB0, the PR's content 275, MSG_ARMOR_NAME_74), two steps:
      - World of Ruin South Figaro (SNES map 78, NPC 6 at (75, 39), CB/51FC -> EE/B0B0): the cider
        deliveryman, once the world has ended (bit 0x0A4): "People are drinking more of the hard stuff
        these days...", and with Celes in the party (or the cider taken in Locke's scenario, bit 0x1D0:
        ScenarioFlag1 55) the trade, cider for "Leo's Spirits" (0xF7; bit 0x1E1, a flag of ours). The
        PR's World of Ruin town (map_20361) has no deliveryman: he's a new NPC in its room there
        (Map_20361_5, empty in the PR), the PR's World of Balance deliveryman's look (Map_20071_5).
        BNW also clears the cider bit; the PR's flag 55 is its Locke-scenario story flag, left alone.
      - Thamasa, World of Ruin (SNES maps 340/344, the grave's tile (54, 18), CC/0977 -> EE/B0CD): with
        Celes in the party and the Spirits, her farewell (0xB60), "Obtained Leo's Crest!" and the relic,
        the Spirits used up; with Celes, "Leo... I will avenge you." (0x529); without her the grave's
        own "General Leo..." (the PR's N999_C26_305_06_01). The PR's grave there (Map_20460, entity
        44) only showed that line: it runs a script now. BNW lets the trade repeat (Celes always
        qualifies), so each trade and visit gives another crest; ported as BNW has it.

  * Thamasa's World of Ruin traveller (SNES 340, CB/756E): only his line about the Zone Eater's island;
    the story offer and the airship trip are gone (their dialogue numbers are the grave scene's now).
  * Shadow in Kohlingen (SNES 191, CC/6FEB -> ED/8DC6): BNW asks a full party too ("Choose 3
    characters." and the party menu once paid; still four: the fee back, "Go away."). The PR's
    sc_e_0285 turned a full party away before asking.
  * South Figaro (SNES 81's entrance, CB/51A5): the way to the rich man's basement stairs is wall until
    the Lete River raft sets off (the PR's Map_20071_8: an invisible blocking object).
  * Handed over from bnw-enemy-ai's review rows (scene changes with lines of their own): the Coliseum
    without Shadow (413, CB/78C3: sc_e_0750 restores and fades in only); Kefka's Tower's drop outside
    sealed until AtmaWeapon falls (331 = Map_30871_12, CB/5252: sc_e_0978); the Falcon's wheel with
    "Find Doom Gaze" (CB/5230, 0x6A5: sc_e_FAoutboard, a new world-map script sc_bnw_find_deathgaze);
    the Zone Eater's three conductors leave after knocking the party off once (276, new_events.asm:
    Map_30791_2, sc_bnw_conductor_36..38, its map script hides the ones met).

Not ported here (PORTING.md says why): the banquet (Imperial Palace 250, CC/85E3 / CC/860D: BNW's only
change is the door's "Are you done talking to the soldiers?", which the PR already asks, sc_e_0471 /
sc_e_0472), the blacksmith and Shadow's fee (bnw-text-ui: tools/port_bnw_scenes.py, profile
table_overrides).

Text: BNW's own lines under keys of tools/bnw_dialog.py (BNW_D<dialogue> and, for a dialogue whose
speaker changes, BNW_D<dialogue>_T<nn> per speaker turn: bnw_dialog ADDED_DIALOGUES / ADDED_TURN_DIALOGUES),
which BnwPlugin/DialogText.cs adds to the game's text with their speakers. New flags: ScenarioFlag4 from
data/snes_event_alloc_bnw.json (event_translate.FlagAlloc, shared with port_bnw_mechanics). New script
ids 9981-9985 (port_boss_events 9961-9963, port_bnw_dream 9971-9972).

    port(out_dir, log, tables) -> {"script": ids}  (from port_boss_events.port, profile bnw_added_scenes)
    .venv/Scripts/python tools/port_bnw_added_scenes.py <scratch out_dir>    (a dry run, prints the scripts)
"""
import copy
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kitpath  # noqa: E402,F401
import prdata as P  # noqa: E402
import pr_script as S  # noqa: E402

EQ = "＝"
ALLOC = os.path.join(P.ROOT, "data", "snes_event_alloc_bnw.json")
SID_GRAVE, SID_SPIRITS = 9981, 9982

# --- the PR's ids ----------------------------------------------------------------------------------
PENDANT = ("ScenarioFlag2", 168)        # SNES 0x2DB: the Cultists' Tower top cleared (sc_e_0881_1)
TOWER_ENTERED = ("ScenarioFlag2", 217)  # sc_e_0966_1/_3: the tower's first visit done (BNW's 0x029)
CIDER = ("ScenarioFlag1", 55)           # SNES 0x1D0: the old man's cider taken (snes_event_maps.json)
CELES = 7                               # BranchByCharacter's id (sc_e_0966_3: 6 Mash, 8 Strago, 9 Relm)
LEO_CREST, LEO_CREST_NAME = 275, "MSG_ARMOR_NAME_74"   # SNES item 0xB0 (idmap.item_content)
FALCON_MAP = 268                        # the Falcon's deck (Map_20390)
SE_BARRIER, SE_POUR, SE_GOT = 138, 934, 105   # SNES sounds 0x50, 0xE9, 0x8D (snes_event_maps.json "se")
GRAVE_LINE = "N999_C26_305_06_01"       # "General Leo..."
OBTAINED = "T0001_01_01"                # "<CT>Obtained (ITEM_N)!"


def _op(*a, **k):
    return S.op(*a, **k)


def lines(d):
    """The message keys of one BNW dialogue, in order (tools/bnw_dialog.py makes them)."""
    import bnw_dialog as B
    if d in B.ADDED_TURN_DIALOGUES:
        keys = B.turn_keys(d)
        assert keys, f"no BNW text for dialogue {d:#x}"
        return keys
    assert d in B.ADDED_DIALOGUES, f"dialogue {d:#x} has no key (bnw_dialog ADDED_DIALOGUES / ADDED_TURN_DIALOGUES)"
    return [f"BNW_D{d:04X}"]


def _say(*dialogues):
    return [_op("Msg", s=[k]) for d in dialogues for k in lines(d)]


def _load(out_dir, f):
    """The port's copy of a vanilla file if an earlier step wrote one, else vanilla's."""
    mine = os.path.join(out_dir, os.path.relpath(f, P.VANILLA))
    return json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))


def _save(doc, out_dir, f):
    S.save_path(doc, out_dir, f)


def _key(akeys, keys, f):
    """The asset key of a vanilla file the port rewrites (its map group's keys file)."""
    rel = os.path.relpath(f, P.VANILLA).replace("\\", "/")
    group, value = rel.split("/", 1)
    value = os.path.splitext(value)[0]
    k = akeys.get((group, value))
    assert k, f"no asset key for {rel}"
    keys.setdefault(group, {})[k] = value


def _vpath(group, folder, sub, name):
    return os.path.join(P.VANILLA, group, "Assets", "GameAssets", "Serial", "Res", "Map", folder, sub, name)


def _script(name, blocks):
    """A new script from labelled blocks [(label or None, ops)] ("@label" targets resolved)."""
    ops = []
    for lab, body in blocks:
        if lab:
            ops.append(S.label(lab))
        ops += body
    S._resolve(ops, 0)
    ops[0]["label"] = ops[0]["label"] or "Main"
    return {"SystemFlag": {"SkipInitialize": 0, "KeepPlayerPuppet": 0, "KeepBadStatusVisual": 0, "RidingVehicle": 0},
            "Segments": [{"Label": ops[0]["label"], "EntryPoint": 0, "Count": len(ops)}],
            "ScriptLocal": ["なし"], "ScriptLocalValue": [0], "Mnemonics": ops, "Animations": [],
            "Name": name + ".json", "Title": {"main": "", "sub": ""}}


def _new_script(out_dir, keys, group, folder, sub, name, doc):
    """Write a new script into its map's folder and list it in the map's package."""
    room = os.path.join(out_dir, group, "Assets", "GameAssets", "Serial", "Res", "Map", folder, sub)
    os.makedirs(room, exist_ok=True)
    with open(os.path.join(room, name + ".json"), "w", encoding="utf-8") as fh:
        fh.write(json.dumps(doc, ensure_ascii=False, separators=(",", ":")))
    pkg = S.list_in_map(out_dir, group, folder, sub, [name])
    kv = keys.setdefault(group, {})
    kv[f"{sub}/{name}"] = f"Assets/GameAssets/Serial/Res/Map/{folder}/{sub}/{name}"
    kv["package"] = pkg


def _props(o):
    return {p["name"]: p for p in o.get("properties", [])}


# --- Kefka's Tower's barrier -------------------------------------------------------------------------
def falcon_barrier(out_dir, akeys, keys, log, alloc):
    seen = alloc.get(0x0E0)   # BNW's "the barrier stopped us once"
    f = S.path("sc_e_0966_3")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    at = next(n for n, m in enumerate(ms) if m["mnemonic"] == "Msg" and m["operands"]["sValues"][0] == "E0966_00_268_a_02")
    assert ms[at + 1]["mnemonic"] == "Call" and not ms[at + 1]["label"], "sc_e_0966_3 changed after its first line"
    assert any(m["mnemonic"] == "Branch" and m["operands"]["iValues"][0] == TOWER_ENTERED[1] for m in ms[:at]), \
        "sc_e_0966_3: the first-visit branch moved"
    env = next(m for m in ms if m["mnemonic"] == "StopEnvSe")
    fade = next(m for m in ms if m["mnemonic"] == "FadeOut")
    ops = [
        _op("Branch", i=[PENDANT[1], 1, "@pendant"], s=[PENDANT[0], EQ, "imm"]),
        _op("Branch", i=[seen[1], 1, "@again"], s=[seen[0], EQ, "imm"]),
        # first time: Setzer stops them
        _op("Wait", r=[0.5]),
        *_say(0x5FC, 0x5FD, 0xABD, 0x69F, 0x6A0),
        _op("SetFlag", i=[seen[1]], s=[seen[0]]),
        _op("Jump", i=["@leave"]),
        S.label("again"),
        *_say(0xABE, 0x6A2, 0x6A1),
        S.label("leave"),
        _op("Wait", r=[1.0]),
        copy.deepcopy(env), copy.deepcopy(fade),
        _op("ChangeMap", i=[FALCON_MAP, 1, 0, 0, 1], s=["sc_e_FAinboard_1"]),
        _op("Exit"),
        S.label("pendant"),
        _op("Branch", i=[seen[1], 0, "@vanilla"], s=[seen[0], EQ, "imm"]),
        # the pendant breaks the barrier they met before
        *_say(0xABF, 0x5FF),
        _op("Wait", r=[0.5]),
        _op("PlaySE", i=[SE_BARRIER, 1]),
        _op("Flash", i=[255, 255, 255, 255], r=[0.35]),
        _op("Wait", r=[0.6]),
        _op("Flash", i=[255, 255, 255, 255], r=[0.35]),
        _op("Wait", r=[1.0]),
        *_say(0xAC0),
        _op("Jump", i=["@vanilla"]),
    ]
    for o in ops:
        if o["mnemonic"] == "Msg":
            o["comment"] = "BNW"
    S.hook(sc, at + 1, "bnw_barrier", ops)
    _save(sc, out_dir, f)
    _key(akeys, keys, f)
    log(f"  bnw scenes: Kefka's Tower's barrier (sc_e_0966_3): Terra's pendant ({PENDANT[0]} {PENDANT[1]}, the "
        f"Cultists' Tower) needed to fly in; refusals set {seen[0]} {seen[1]}")


# --- Leo's Crest: the trade in World of Ruin South Figaro, the grave in Thamasa -------------------------
SPIRITS_ROOM = ("map_20361", "Map_20361", "Map_20361_5")
SPIRITS_AT = (75, 39)                   # the SNES spot: this World of Ruin room is the whole town's size
WOB_MAN = ("map_20071", "Map_20071", "Map_20071_5", "ev_e_0042.json", "sc_e_0042:商人")
GRAVE = ("map_20460", "Map_20460", "Map_20460")


def spirits_trade(out_dir, akeys, keys, log, alloc):
    spirits = alloc.get(0x1E1)
    doc = _script("sc_bnw_spirits", [
        ("Main", _say(0xF5) + [
            _op("BranchByCharacter", i=[CELES, 1, 1, "@no_celes"]),
            _op("Jump", i=["@trade"])]),
        ("no_celes", [_op("Branch", i=[CIDER[1], 1, "@trade"], s=[CIDER[0], EQ, "imm"]), _op("Exit")]),
        ("trade", _say(0xF7) + [_op("PlaySE", i=[SE_GOT, 1]),
                                _op("SetFlag", i=[spirits[1]], s=[spirits[0]]),
                                _op("Exit")])])
    group, folder, sub = SPIRITS_ROOM
    _new_script(out_dir, keys, group, folder, sub, "sc_bnw_spirits", doc)
    # the deliveryman: the PR's World of Balance one, standing where BNW's does
    g, fo, su, evf, name = WOB_MAN
    ev = json.load(open(_vpath(g, fo, su, evf), encoding="utf-8"))
    man = next(o for lay in ev["layers"] for o in lay.get("objects") or [] if o.get("name") == name)
    f = _vpath(group, folder, sub, "entity_default.json")
    ent = _load(out_dir, f)
    upper = next(lay for lay in ent["layers"] if lay.get("name") == "UpperObject")
    if not any(o.get("name") == "BNW Leo's Spirits" for o in upper["objects"]):
        oid, eid = S.room_ids(out_dir, os.path.dirname(f), [ent])   # the room's event layers too
        npc = copy.deepcopy(man)
        npc["id"] = 1 + oid
        npc["name"] = "BNW Leo's Spirits"
        npc["x"], npc["y"] = 16 * SPIRITS_AT[0], 16 * (SPIRITS_AT[1] + 1)
        pr = _props(npc)
        pr["script_id"]["value"] = SID_SPIRITS
        pr["entity_id"]["value"] = 1 + eid
        upper["objects"].append(npc)
    _save(ent, out_dir, f)
    _key(akeys, keys, f)
    log(f"  bnw scenes: Leo's Spirits (the World of Ruin's South Figaro, {sub}: the cider deliveryman, a new NPC; "
        f"sets {spirits[0]} {spirits[1]})")


def leo_grave(out_dir, akeys, keys, log, alloc):
    spirits = alloc.get(0x1E1)
    doc = _script("sc_bnw_leo_grave", [
        ("Main", [_op("BranchByCharacter", i=[CELES, 1, 1, "@general"]),
                  _op("Branch", i=[spirits[1], 0, "@avenge"], s=[spirits[0], EQ, "imm"])]
         + _say(0xB60) + [
             _op("Wait", r=[0.5]),
             _op("PlaySE", i=[SE_POUR, 1]),
             _op("Wait", r=[1.0]),
             _op("PlaySE", i=[SE_GOT, 1]),
             _op("Tag", s=["ITEM_N", LEO_CREST_NAME]),
             _op("Msg", s=[OBTAINED]),
             _op("GetItem", i=[LEO_CREST, 1]),
             _op("ResetFlag", i=[spirits[1]], s=[spirits[0]])]),
        ("avenge", _say(0x529) + [_op("Exit")]),
        ("general", [_op("Msg", s=[GRAVE_LINE]), _op("Exit")])])
    group, folder, sub = GRAVE
    _new_script(out_dir, keys, group, folder, sub, "sc_bnw_leo_grave", doc)
    f = _vpath(group, folder, sub, "entity_default.json")
    ent = _load(out_dir, f)
    hits = 0
    for lay in ent["layers"]:
        for o in lay.get("objects") or []:
            pr = _props(o)
            if pr.get("message_key", {}).get("value") == GRAVE_LINE:
                pr["action_id"]["value"] = 2       # run a script (4: show its message)
                pr["script_id"]["value"] = SID_GRAVE
                hits += 1
    assert hits == 1, f"Leo's grave: {hits} found"
    _save(ent, out_dir, f)
    _key(akeys, keys, f)
    log(f"  bnw scenes: Leo's grave (Thamasa, {sub}): Celes, the Spirits ({spirits[0]} {spirits[1]}) -> Leo's Crest")


def traveller(out_dir, akeys, keys, log):
    """Thamasa's World of Ruin traveller (SNES 340, CB/756E): vanilla's "Wanna hear some of my stories?" and
    the airship trip to the Zone Eater's island; BNW keeps only his one line (0xB5F, "There's a tiny island
    northeast of the Veldt. Stay away unless you enjoy being eaten.") and gives the trip's dialogue
    numbers (0xB60, 0xB61) to Celes's scene at Leo's grave. The PR's sc_e_0941: its line (BNW's 0xB5F text)
    without the Yes/No and the trip, which would now show Celes's lines (the line map gives the PR's trip
    line 0xB60's text)."""
    f = S.path("sc_e_0941")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    at = next(n for n, m in enumerate(ms) if m["mnemonic"] == "Msg" and m["operands"]["sValues"][0] == "E0941_00_305_a_01")
    assert ms[at + 1]["mnemonic"] == "Select" and ms[at + 2]["mnemonic"] == "Branch" \
        and ms[at + 3]["mnemonic"] == "Call", "sc_e_0941's question moved"
    # the Select becomes a jump to the "no" answer's end (his animation, Exit): a jump to T goes on at T + 1
    ms[at + 1] = _op("Jump", i=[at + 2], label=ms[at + 1]["label"], kind=ms[at + 1].get("type", 1))
    _save(sc, out_dir, f)
    _key(akeys, keys, f)
    log("  bnw scenes: Thamasa's traveller (sc_e_0941): his one line, no story trip (BNW gave its lines to Leo's grave)")


# --- Shadow hired with a full party (Kohlingen) -----------------------------------------------------------
SHADOW_FEE = 1000          # BNW's fee (CC/6FF6 gil_remove 0x3E8; the PR's price row: profile_bnw table_overrides)
FULL_LINE = "E0285_00_098_a_05"     # "Leave me alone..." (BNW's 0x3E5: "Go away.")
PARTY_MENU = "パーティ編成：１パーティ"   # one-party menu (sc_e_0262)


def kohlingen(out_dir, akeys, keys, log):
    """BNW (CC/6FEB -> ED/8DC6): Shadow asks with a full party too; paid, "Choose 3 characters." and the
    party menu; still four, the fee back and "Go away."; else he joins. The PR's sc_e_0285 sent a full
    party off with "Leave me alone..." before asking: that line's block asks now."""
    f = S.path("sc_e_0285")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    full = next(n for n, m in enumerate(ms) if m["mnemonic"] == "BranchByNumOfMember")
    at = ms[full]["operands"]["iValues"][2] + 1           # the full party's first op: its "Leave me alone..."
    assert ms[at]["mnemonic"] == "Msg" and ms[at]["operands"]["sValues"][0] == FULL_LINE, "sc_e_0285's full-party line moved"
    ask = ms[full + 1]
    assert ask["mnemonic"] == "Msg" and ask["operands"]["sValues"][0] == "E0285_00_098_a_02", "sc_e_0285's offer moved"
    sel, no_go, pay, paid, poor = (ms[full + k] for k in (2, 4, 6, 7, 8))
    assert sel["mnemonic"] == "Select" and no_go["mnemonic"] == "Jump" and pay["mnemonic"] == "RemoveGil" \
        and paid["mnemonic"] == "Branch" and poor["mnemonic"] == "Msg", "sc_e_0285 changed"
    join = paid["operands"]["iValues"][2]                 # "お金ある": he joins
    leave = no_go["operands"]["iValues"][0]               # Shadow walks off
    ops = [copy.deepcopy(ask), copy.deepcopy(sel),
           _op("Branch", i=[0, 0, "@yes"], s=["MessageSelection", EQ, "imm"]),
           _op("Jump", i=[leave]),
           S.label("yes"),
           copy.deepcopy(pay),
           _op("Branch", i=[0, 1, "@paid"], s=["MapLocalVariable", EQ, "imm"]),
           copy.deepcopy(poor), _op("Jump", i=[leave]),
           S.label("paid"),
           *_say(0x84D),
           # the PR's own party-menu steps (sc_e_0262), then back on the field
           _op("ColorFade", i=[0, 0, 0, 255]), _op("HideMob"), _op("SysCall", s=[PARTY_MENU]),
           _op("FadeOut", i=[0, 0, 0, 255], r=[0.7]), _op("RestoreMob"), _op("ColorFade"),
           _op("FadeIn", i=[0, 0, 0, 255], r=[0.7]),
           _op("BranchByNumOfMember", i=[0, 4, "@still_full"], s=[EQ]),
           _op("Jump", i=[join]),
           S.label("still_full"),
           _op("GetItem", i=[1, SHADOW_FEE]),
           _op("Jump", i=["@vanilla"])]                  # "Leave me alone..." and he walks off, as before
    S.hook(sc, at, "bnw_full_party", ops)
    _save(sc, out_dir, f)
    _key(akeys, keys, f)
    log("  bnw scenes: Shadow in Kohlingen (sc_e_0285): hired with a full party too (the party menu; the fee back if it stays full)")


# --- scenes handed over from bnw-enemy-ai's review rows -----------------------------------------------------
SHADOW_JOIN = "メンバー加入：シャドウ"   # メンバー加入：シャドウ


def coliseum_shadow(out_dir, akeys, keys, log):
    """The Coliseum (SNES 413, CB/78C3): BNW's attendant only opens the wagers; vanilla's Shadow after the
    Striker match (his scene, his join) is gone (BNW's Shadow comes back by the Veldt's cave alone). The
    PR's game code runs sc_e_0750 after that match: it now only does the Coliseum's own restore and
    fades in (no Shadow scene, no join)."""
    f = S.path("sc_e_0750")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    assert ms[1]["mnemonic"] == "SysCall" and any(m["mnemonic"] == "SysCall" and m["operands"]["sValues"][0] == SHADOW_JOIN for m in ms), \
        "sc_e_0750 changed"
    fade = next(m for m in ms if m["mnemonic"] == "FadeIn")
    blk = S.append(sc, "bnw_no_shadow", [copy.deepcopy(fade), _op("Exit")])
    ms[2] = _op("Jump", i=[blk], label=ms[2]["label"], kind=ms[2].get("type", 1))   # after its restore (ms[1])
    _save(sc, out_dir, f)
    _key(akeys, keys, f)
    log("  bnw scenes: the Coliseum (sc_e_0750): no Shadow after the Striker match")


ATMA_DOWN = ("ScenarioFlag2", 223)    # Kefka's Tower: AtmaWeapon beaten (sc_e_0977_1; BNW's bit 0x0E1, CB/524B)


def tower_seal(out_dir, akeys, keys, log):
    """Kefka's Tower (SNES 331 = the PR's Map_30871_12): BNW seals the drop outside at (81, 60) ("Sealed by
    a mysterious force.", the party steps back) until AtmaWeapon there is beaten (bit 0x0E1, set in its
    after-battle call CB/524B). The PR's sc_e_0978 (the hole) checks the PR's own flag for that battle."""
    f = S.path("sc_e_0978")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    assert ms[1]["mnemonic"] == "ChangeMap", "sc_e_0978 changed"
    push = S.append(sc, "bnw_step_back", [
        _op("SetPuppet", i=[0, -1001, "@bnw_step_back_00"], kind=2), _op("ExecPuppet", kind=2), _op("Return", kind=2),
        S.label("bnw_step_back_00", kind=2), _op("Move", i=[0, 1], kind=2), _op("Exit", kind=2)])
    sc["Mnemonics"][push]["type"] = 2
    S.hook(sc, 1, "bnw_sealed", [_op("Branch", i=[ATMA_DOWN[1], 1, "@vanilla"], s=[ATMA_DOWN[0], EQ, "imm"]),
                                 *_say(0x0FB), _op("Call", i=[push]), _op("Exit")])
    _save(sc, out_dir, f)
    _key(akeys, keys, f)
    log(f"  bnw scenes: Kefka's Tower's drop outside (sc_e_0978) sealed until AtmaWeapon ({ATMA_DOWN[0]} {ATMA_DOWN[1]})")


DEATHGAZE_DONE = ("ScenarioFlag2", 216)   # Bahamut from Deathgaze (sc_e_0964_2)
DEATHGAZE_PARTY = 464                     # the PR's Deathgaze fight (sc_e_0964 on the World of Ruin's map)
WOR_WORLD = ("map_10020", "Map_10020", "Map_10020")


def falcon_deathgaze(out_dir, akeys, keys, log):
    """The Falcon's wheel (BNW CB/5230, dialogue 0x6A5): "(Lift off) / (Find Doom Gaze) / (Do nothing)"
    while Doom Gaze lives; finding him flies the Falcon to him (EE/AF01). The PR's wheel (sc_e_FAoutboard
    on the deck) gets the third option until Bahamut is had; it takes off as the wheel does
    (sc_e_FAoutboard_1's steps) and starts the PR's Deathgaze fight (EncountBoss 464 -> sc_e_0964_1:
    his kept HP, Bahamut once he falls), wherever the Falcon is."""
    f = S.path("sc_e_FAoutboard")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    at = next(n for n, m in enumerate(ms) if m["mnemonic"] == "Select")
    lift = ms[at + 1]
    assert lift["mnemonic"] == "Branch" and lift["operands"]["sValues"][0] == "MessageSelection", "sc_e_FAoutboard changed"
    never = lift["operands"]["iValues"][2]             # "何もしない": Exit after it
    go = next(n for n in range(at, len(ms)) if ms[n]["mnemonic"] == "ChangeMap")
    sel = copy.deepcopy(ms[at])
    sel["label"] = ""
    sel["operands"]["sValues"][:3] = [f"BNW_D06A5_c_{k:02d}" for k in (1, 2, 3)]
    ops = [_op("Branch", i=[DEATHGAZE_DONE[1], 1, "@vanilla"], s=[DEATHGAZE_DONE[0], EQ, "imm"]),
           sel,
           _op("Branch", i=[0, 1, "@find"], s=["MessageSelection", EQ, "imm"]),
           _op("Branch", i=[0, 2, never], s=["MessageSelection", EQ, "imm"]),
           _op("Jump", i=[at + 1]),                     # (Lift off): the wheel's own take-off (at + 2 on)
           S.label("find")]
    ops += S.copy_ops(sc, at + 3, go)                   # its hand-up, the propellers, the GPS on the player
    take = copy.deepcopy(ms[go])
    take["operands"]["sValues"][0] = "sc_bnw_find_deathgaze"
    ops += [take, _op("Exit")]
    S.hook(sc, at, "bnw_deathgaze", ops)
    _save(sc, out_dir, f)
    _key(akeys, keys, f)
    # on the World of Ruin's map: take off as sc_e_FAoutboard_1 does, then the fight
    up = S.load("sc_e_FAoutboard_1")
    body = [copy.deepcopy(m) for m in up["Mnemonics"][1:] if m["mnemonic"] != "Exit"]
    assert any(m["mnemonic"] == "OnTheVehicle" for m in body), "sc_e_FAoutboard_1 changed"
    doc = _script("sc_bnw_find_deathgaze", [("Main", body + [_op("Wait", r=[0.5]),
                                                             _op("EncountBoss", i=[DEATHGAZE_PARTY], s=["sc_e_0964_1"]),
                                                             _op("Exit")])])
    _new_script(out_dir, keys, *WOR_WORLD, "sc_bnw_find_deathgaze", doc)
    log(f"  bnw scenes: the Falcon's wheel (sc_e_FAoutboard): Find Doom Gaze until Bahamut ({DEATHGAZE_DONE[0]} {DEATHGAZE_DONE[1]})")


CONDUCTOR_ROOM = ("map_30791", "Map_30791", "Map_30791_2")
CONDUCTORS = (36, 37, 38)                   # the PR's three "見張り" (sc_e_0833) in that room
SID_CONDUCTORS = (9983, 9984, 9985)


def zone_eater_conductors(out_dir, akeys, keys, log, alloc, tables_rows):
    """The Zone Eater's belly (SNES 276; bnw 3.0/asm/new_events.asm, Khaos 2026-07-17): each of the three
    train conductors who knock the party off the bridge (CB/8251) goes away after doing it once (bits
    0x0EE-0x0F0, the room's entrance hides the ones already met). The PR's room (Map_30791_2): each
    conductor gets a script of its own (its flag, then the PR's knock-down sc_e_0833), and the room's
    (empty) map script hides the ones whose flag is set."""
    group, folder, sub = CONDUCTOR_ROOM
    flags = [alloc.get(b) for b in (0x0EE, 0x0EF, 0x0F0)]
    for (cat, idx), sid, ent in zip(flags, SID_CONDUCTORS, CONDUCTORS):
        name = f"sc_bnw_conductor_{ent}"
        doc = _script(name, [("Main", [_op("SetFlag", i=[idx], s=[cat]), _op("ChangeScript", s=["sc_e_0833"]), _op("Exit")])])
        _new_script(out_dir, keys, group, folder, sub, name, doc)
        tables_rows[sid] = name
    f = _vpath(group, folder, sub, "entity_default.json")
    ent = _load(out_dir, f)
    hits = 0
    for lay in ent["layers"]:
        for o in lay.get("objects") or []:
            pr = _props(o)
            e = pr.get("entity_id", {}).get("value")
            if e in CONDUCTORS and pr.get("script_id", {}).get("value") and o.get("name") == "見張り":
                pr["script_id"]["value"] = SID_CONDUCTORS[CONDUCTORS.index(e)]
                hits += 1
    assert hits == 3, f"the Zone Eater's conductors: {hits} found"
    _save(ent, out_dir, f)
    _key(akeys, keys, f)
    # the room's map script: the conductors already met are gone
    f = _vpath(group, folder, sub, f"sc_map_{sub[4:].lower()}.json")
    sc = _load(out_dir, f)
    assert [m["mnemonic"] for m in sc["Mnemonics"]] == ["Nop", "Exit"], f"{sub}'s map script isn't empty any more"
    n = len(flags)
    first = 1 + 3 * n + 1                       # the hide blocks start after Main (its ops + Exit)
    main = []
    for k, ((cat, idx), e) in enumerate(zip(flags, CONDUCTORS)):
        main += [_op("Branch", i=[idx, 0, f"@keep_{k}"], s=[cat, EQ, "imm"]), _op("Call", i=[first + 7 * k]),
                 S.label(f"keep_{k}")]
    main.append(_op("Exit"))
    S._resolve(main, 1)
    sc["Mnemonics"] = [sc["Mnemonics"][0]] + main
    sc["Segments"] = [{"Label": "Main", "EntryPoint": 0, "Count": 1 + len(main)}]
    for k, e in enumerate(CONDUCTORS):
        seg = S.append(sc, f"bnw_gone_{k}", [_op("SetPuppet", i=[0, e, f"@bnw_gone_{k}_00"], kind=2), _op("ExecPuppet", kind=2),
                                             _op("Return", kind=2), S.label(f"bnw_gone_{k}_00", kind=2), _op("Hide", kind=2),
                                             _op("Exit", kind=2)])
        assert seg == first + 7 * k, "the hide blocks moved"
        sc["Mnemonics"][seg]["type"] = 2
    _save(sc, out_dir, f)
    _key(akeys, keys, f)
    log(f"  bnw scenes: the Zone Eater's conductors leave after knocking the party off once ({sub}; flags {flags})")


# --- South Figaro: the rich man's basement stairs walled off on the first visit -----------------------------
LETE = ("ScenarioFlag1", 48)        # SNES 0x019: the Lete River raft set off (the PR's sc_e_0063_1 sets it)
WALL_ROOM = ("map_20071", "Map_20071", "Map_20071_8")
WALL_AT = (6, 10)                   # SNES map 81 (29, 9): the one way to the stairs (4, 11) down to the basement
WALL_ENTITY = 50


def south_figaro_wall(out_dir, akeys, keys, log):
    """BNW's South Figaro (SNES 81's entrance, CB/51A5): until the Lete River (bit 0x19), the cell before
    the rich man's stairs to the basement (map 83) is wall (tiles_set (29, 9) = 0x57, the wall below
    it), so the first visit (before the Returners' Hideout; Locke's scenario always comes after the
    raft) can't go down early. The PR's room (Map_20071_8) gets an invisible blocking object there (the
    PR's own kind: Thamasa's grave, object type 5 with collision), an entity set its map script shows
    outside Locke's scenario while the raft hasn't set off."""
    group, folder, sub = WALL_ROOM
    grave = next(o for lay in json.load(open(_vpath(*GRAVE, "entity_default.json"), encoding="utf-8"))["layers"]
                 for o in lay.get("objects") or [] if _props(o).get("message_key", {}).get("value") == GRAVE_LINE)
    wall = copy.deepcopy(grave)
    # a Tiled id past the room's others (it was 1, the room's GotoMap's; pr_script.room_ids)
    oid = 1 + S.room_ids(out_dir, os.path.dirname(_vpath(group, folder, sub, "entity_default.json")), skip=["ev_bnw_sf_wall.json"])[0]
    wall.update(id=oid, name="BNW wall (the basement stairs, until the Lete River)", x=16 * WALL_AT[0], y=16 * (WALL_AT[1] + 1))
    pr = _props(wall)
    for k, v in (("action_id", 0), ("message_key", ""), ("entity_id", WALL_ENTITY), ("accept_action_direction", 0),
                 ("interactive_icon_type", 0), ("collision", True), ("asset_id", 0)):
        pr[k]["value"] = v
    like = json.load(open(_vpath(group, folder, sub, "ev_e_0038.json"), encoding="utf-8"))
    ev = copy.deepcopy(like)
    ev["name"] = "ev_bnw_sf_wall"
    ev["layers"] = [dict(like["layers"][0], objects=[wall])]
    room = os.path.join(out_dir, group, "Assets", "GameAssets", "Serial", "Res", "Map", folder, sub)
    os.makedirs(room, exist_ok=True)
    with open(os.path.join(room, "ev_bnw_sf_wall.json"), "w", encoding="utf-8") as fh:
        fh.write(json.dumps(ev, ensure_ascii=False, separators=(",", ":")))
    # the map script: outside Locke's scenario (its own branch), the wall until the raft
    f = _vpath(group, folder, sub, "sc_map_20071_8.json")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    at = next(n for n, m in enumerate(ms) if m["mnemonic"] == "SetEntities" and m["operands"]["sValues"][0] == "ev_e_0038")
    S.hook(sc, at, "bnw_sf_wall", [_op("Branch", i=[LETE[1], 1, "@vanilla"], s=[LETE[0], EQ, "imm"]),
                                   _op("SetEntities", s=["ev_bnw_sf_wall"])])
    _save(sc, out_dir, f)
    _key(akeys, keys, f)
    # the room's package lists the entity set
    pkg_file = os.path.join(out_dir, group, "Assets", "GameAssets", "Serial", "Res", "Map", folder, "package.json")
    src = pkg_file if os.path.exists(pkg_file) else os.path.join(P.VANILLA, group, "Assets", "GameAssets", "Serial", "Res", "Map", folder, "package.json")
    doc = json.load(open(src, encoding="utf-8"))
    entry = next(m for m in doc["map"] if m["name"] == sub)
    if not any(x["name"] == "ev_bnw_sf_wall" for x in entry.get("entity", [])):
        entry.setdefault("entity", []).append({"name": "ev_bnw_sf_wall", "asset": f"{sub}/ev_bnw_sf_wall"})
    os.makedirs(os.path.dirname(pkg_file), exist_ok=True)
    with open(pkg_file, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))
    kv = keys.setdefault(group, {})
    kv[f"{sub}/ev_bnw_sf_wall"] = f"Assets/GameAssets/Serial/Res/Map/{folder}/{sub}/ev_bnw_sf_wall"
    kv["package"] = f"Assets/GameAssets/Serial/Res/Map/{folder}/package"
    log(f"  bnw scenes: South Figaro's basement stairs walled off until the Lete River ({sub} {WALL_AT}, {LETE[0]} {LETE[1]})")


# --- the porter's entry --------------------------------------------------------------------------------
SCRIPTS = {SID_GRAVE: "sc_bnw_leo_grave", SID_SPIRITS: "sc_bnw_spirits"}


def port(out_dir, log=print, tables=None):
    import event_translate as T
    import port_chests as C
    akeys, keys = C.asset_keys(), {}
    alloc = T.FlagAlloc(ALLOC)
    falcon_barrier(out_dir, akeys, keys, log, alloc)
    spirits_trade(out_dir, akeys, keys, log, alloc)
    leo_grave(out_dir, akeys, keys, log, alloc)
    traveller(out_dir, akeys, keys, log)
    kohlingen(out_dir, akeys, keys, log)
    south_figaro_wall(out_dir, akeys, keys, log)
    coliseum_shadow(out_dir, akeys, keys, log)
    tower_seal(out_dir, akeys, keys, log)
    falcon_deathgaze(out_dir, akeys, keys, log)
    rows = dict(SCRIPTS)
    zone_eater_conductors(out_dir, akeys, keys, log, alloc, rows)
    alloc.save()
    changed = set()
    if tables is not None:
        scripts = tables.setdefault("script", P.Table("script"))
        for sid, name in rows.items():
            assert sid not in scripts.rows or scripts.rows[sid]["script_name"] == name, f"script id {sid} taken"
            scripts.rows[sid] = {"id": str(sid), "script_name": name}
            changed.add(sid)
    for group, kv in keys.items():
        kdir = os.path.join(out_dir, group, "keys")
        os.makedirs(kdir, exist_ok=True)
        path = os.path.join(kdir, "bnw_added_scenes.json")
        with open(path, "w", encoding="utf-8") as fh:
            json.dump({"keys": list(kv), "values": list(kv.values())}, fh, indent=1)
    return {"script": changed}


if __name__ == "__main__":
    out = sys.argv[1]
    print(port(out))
    for name in ("sc_e_0966_3",):
        print(S.dumps(_load(out, S.path(name))))
