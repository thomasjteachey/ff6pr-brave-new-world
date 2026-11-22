"""BNW's story fights the PR has no event for, wired into the PR's own map scripts (the enemy-AI session,
2026-11-20; the coordinator: "take the triggers too"). The fights' parties are port_ai.new_fights'
(8000 + BNW formation); this starts them where BNW does, with BNW's flags.

Kaiser (formation 0x19A, party 8410), BNW's post-game superboss. BNW (read with tools/snes_events.py):
each dragon's fight ends in a tally scene (CB/5090 -> CC/1F9F: "N dragons left", event word 6); at the
eighth, "No dragons left..." and bit 0x3A3 is set, which shows Kaiser (NPC 0 of map 304 at (82, 33),
the dragon sprite) in a corridor BNW added to Kefka's Tower (new exits from maps 334 and 337). Talking
to him (CB/52C5): the battle, then he's gone (0x3A3 cleared), "Broke the dragon seal! Got the magicite
Ragnarok" and the esper (0x45: index 15, the PR's content 87). Vanilla's eighth tally broke the seal
and gave that esper at once. BNW's corridor is map 304's dragon-platform room (layout 315, the room the
Yellow and the Green Dragon stand in on maps 335/354), which vanilla never let anyone into: BNW reroutes
the door between maps 334 and 337 through it (334 (29, 36) -> 304 (82, 42); 304 (82, 43) -> 334 (29, 38);
304 (82, 30), a doorway it cut into the back wall -> 337 (8, 29); 337 (8, 31) -> 304 (82, 32)), closes
its side alcoves with pillars and puts Kaiser on the platform. The PR gets the same room as a new map
(snes_rooms.clone_map of the PR's own drawing of it, Map_30871_9, as Map_30871_33: map 806), with:
  * the doorway BNW cut (82, 29-31), drawn as the PR drew that room's other top-wall doorway (77, 30-32),
    and the side alcoves' pillars (snes_rooms' model of BNW's tiles); BNW's 8 dragon markers (statues its
    entrance shows while each dragon lives) as the PR's floor switches work: TileAnimRegions the map script
    cues to an empty frame for each fallen dragon (MARKERS);
  * its doors: the PR's door from Map_30870 (SNES 334) at (29, 36) and Map_30871_7's (SNES 337) at
    (14, 31) now lead into it, its own lead on to where those did;
  * its map script spawns Kaiser (ev_bnw_kaiser: the Yellow Dragon's entity with his script) once all 8
    dragon flags are set and until the seal breaks (ScenarioFlag2 221);
  * sc_e_0975 (the PR's dragon tally, one copy per dragon map): the eighth only says "No dragons
    left..." (E0981_00_551_a_01, BNW's text on the PR's key); the seal's effects, line a_02, its flag
    and the esper move to Kaiser's after-battle script;
  * sc_bnw_kaiser (talk: the PR's dragon talk, EncountBoss 8410) and sc_bnw_kaiser_1 (after the
    battle: he's hidden, the seal breaks, the esper).

Kefka with two Centurions (formation 0x179, party 8377), at the Sealed Gate. BNW's CB/39CA scene
replaces vanilla's first event battle there (pack 0x79: vanilla's formation 0x180, an event-only
battle; the PR's party 454 with its battle script sc_b_0460_1) with a real fight, after six new lines
(SNES dialogues 0xA60-0xA65: Kefka followed the party; "Think again, Kefka!"). The PR's scene is
sc_e_0460_1 (map 40034): its first Encount takes party 8377 instead of 454, and BNW's lines come
before it when the text pipeline has keys for them (profile bnw_event_lines).

Nothing here runs the game; tests go through the PC test runner (harness: warp, flags, script).
"""
import copy
import json
import os

import port_chests as C
import pr_script as Sc
import prdata as P

KAISER_PARTY, KEFKA_PARTY = 8410, 8377
DRAGON_FLAGS = (108, 138, 148, 166, 175, 178, 220, 225)   # ScenarioFlag2, the 8 dragons (sc_e_0975)
YELLOW_FLAG = 225                                          # the dragon of Map_30871_9
SEAL_FLAG = 221                                            # ScenarioFlag2: the dragon seal broken
# the PR's drawing of the dragon-platform room (SNES 335's: the same layout as BNW's map 304 room) and
# the new map that is BNW's map 304 room (ids past the game's: map.csv ends at 805, map_script at 628)
LIKE_ROOM = "Map_30871_9"
KAISER_ROOM = ("map_30871", "Map_30871", "Map_30871_33")
ROOM_ID, ROOM_SCRIPT = 806, 629
# the doors BNW reroutes through the room: (PR map, its door at (x, y)) -> the room's arrival point;
# the room's doors lead on to where those went (point ids of the PR's own arrivals)
SOUTH_MAP, SOUTH_ID, SOUTH_DOOR, SOUTH_POINT = "Map_30870", 542, (29, 36), 16     # SNES 334
NORTH_MAP, NORTH_ID, NORTH_DOOR, NORTH_POINT = "Map_30871_7", 549, (14, 31), 1    # SNES 337
GOTO_GID, POINT_GID = 996, 997      # the PR's GotoMap / PointIn tile objects (as ted_rooms)
# BNW's 8 dragon markers: statues around the platform (BG2 tile 0x5F) that the room's entrance (CB/4FE5)
# shows while each dragon lives and its tally (CB/50A8...: the tiles_set after CB/5090) takes away when it
# falls. Cell -> the PR's flag for that dragon (sc_e_0975), by the dragon whose tally clears it: Brown (PR
# party 369, Earth 178), Purple (368, Storm 108), White (482's place, Holy 166), Silver (367, Ice 138), Red
# (373, 148), Blue (372, 175), Green (371, Skull 220), Gold (370, Yellow 225). Four stand where vanilla's
# room has its statues (the PR draws them: Bottom 90, Ground 160, Upper 279), four are BNW's own.
MARKERS = {(77, 34): 178, (87, 34): 108, (79, 33): 166, (85, 33): 138,
           (80, 35): 148, (84, 35): 175, (80, 31): 220, (84, 31): 225}
STATUE = (("map255", 160), ("map255", 279))   # the statue's Ground and Upper tiles in the PR's drawing
BLANK = 1023          # an atlas cell no PR map uses (map255 has 1024; the maps use 124, up to 484): empty
MARKER_ENTITY = 301   # entity ids of the markers' TileAnimRegions (301-308)
SCRIPT_BASE = 9960     # new scripts 9961 up (merchants 9901 up, boxes 9801 up; script.csv's own end at 9029)
EQ = "＝"


def _map_path(root, group, folder, sub, name):
    return os.path.join(root, group, "Assets", "GameAssets", "Serial", "Res", "Map", folder, sub, name + ".json")


def _load(out_dir, f):
    """The port's copy of a vanilla file if an earlier step wrote one, else vanilla's."""
    rel = os.path.relpath(f, P.VANILLA)
    mine = os.path.join(out_dir, rel)
    return json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))


def _key(akeys, keys, f):
    """The asset key of a vanilla file the port rewrites (its map group's keys file)."""
    rel = os.path.relpath(f, P.VANILLA).replace("\\", "/")
    group, value = rel.split("/", 1)
    value = os.path.splitext(value)[0]
    k = akeys.get((group, value))
    if k:
        keys.setdefault(group, {})[k] = value


def _nop():
    return Sc.op("Nop")


def kaiser(out_dir, scripts, changed, akeys, keys, log, bnw=None, visits=None):
    group, folder, sub = KAISER_ROOM
    # the tally: the eighth dragon only says "No dragons left..."; the seal is Kaiser's
    seal = None
    seal_locals = None
    for f in Sc.all_paths("sc_e_0975"):
        sc = _load(out_dir, f)
        ms = sc["Mnemonics"]
        at = next(n for n, m in enumerate(ms) if m["label"] == "8匹以上")
        end = next(n for n in range(at + 1, len(ms)) if ms[n]["mnemonic"] == "Exit")
        assert ms[at + 1]["mnemonic"] == "Msg", f"{f}: the eighth's line moved"
        if seal is None:
            # the seal's PlaySE [187, 1, 1] / StopSE [1] keep the sound's handle in script-local variable 1:
            # the script that runs them needs sc_e_0975's two locals (with one, the game threw at the first
            # PlaySE and Kaiser's after-battle script ended before SetFlag 221: QA ai-triggers, 2026-11-21)
            seal_locals = (list(sc.get("ScriptLocal") or []), list(sc.get("ScriptLocalValue") or []))
            seal = Sc.copy_ops(sc, at + 2, end)   # Wait/PlaySE/Shake... Msg a_02, SetFlag 221, GetItem 87
        for n in range(at + 2, end):
            ms[n] = _nop()
        Sc.save_path(sc, out_dir, f)
        _key(akeys, keys, f)
    assert seal and any(o["mnemonic"] == "GetItem" for o in seal), "the seal's reward wasn't found"
    import snes_rooms as R
    vroom = os.path.dirname(_map_path(P.VANILLA, group, folder, LIKE_ROOM, "x"))
    sid_talk, sid_after = SCRIPT_BASE + 1, SCRIPT_BASE + 2
    for sid, name in ((sid_talk, "sc_bnw_kaiser"), (sid_after, "sc_bnw_kaiser_1")):
        assert sid not in scripts.rows or scripts.rows[sid]["script_name"] == name, f"script id {sid} taken"
        scripts.rows[sid] = {"id": str(sid), "script_name": name}
        changed.add(sid)
    # talk: the PR's own dragon talk (the dragon shakes, the party steps up), Kaiser's fight
    talk = copy.deepcopy(Sc.load("sc_e_0981"))
    talk["Name"] = "sc_bnw_kaiser.json"
    enc = next(m for m in talk["Mnemonics"] if m["mnemonic"] == "EncountBoss")
    enc["operands"]["iValues"][0] = KAISER_PARTY
    enc["operands"]["sValues"][0] = "sc_bnw_kaiser_1"
    # after: he's hidden (the dragon's own "position" block), the seal breaks, the esper
    after = copy.deepcopy(Sc.load("sc_e_0981_1"))
    after["Name"] = "sc_bnw_kaiser_1.json"
    ms = after["Mnemonics"]
    flag = next(n for n, m in enumerate(ms) if m["mnemonic"] == "SetFlag" and m["operands"]["iValues"][0] == YELLOW_FLAG)
    ms[flag] = _nop()
    cs = next(n for n, m in enumerate(ms) if m["mnemonic"] == "ChangeScript")
    if len(after.get("ScriptLocal") or []) < len(seal_locals[0]):
        after["ScriptLocal"], after["ScriptLocalValue"] = seal_locals
    blk = Sc.append(after, "bnw_seal", seal + [Sc.op("Exit")])
    ms[cs] = Sc.op("Jump", i=[blk])
    # Kaiser: the Yellow Dragon's entity set with his script
    ev = json.load(open(os.path.join(vroom, "ev_e_0981.json"), encoding="utf-8"))
    ev["name"] = "ev_bnw_kaiser"
    obj = ev["layers"][0]["objects"][0]
    obj["name"] = "Kaiser"
    for p in obj["properties"]:
        if p["name"] == "script_id":
            p["value"] = sid_talk
    # the room: the PR's drawing of the platform room with BNW's changes. Its back-wall doorway (82,
    # 29-31) as the PR drew the room's other top-wall doorway (77, 30-32: the arch, the door cell, the
    # step); the side alcoves BNW walls up (77/87, 30-33) blocked, drawn with snes_rooms' model of
    # BNW's tiles where it has them
    cells, _tm = R.pr_cells(LIKE_ROOM)
    patch = {(82, 29 + k): (cells[30 + k][77][0], 1 if k == 0 else 0) for k in range(3)}
    pillars = {(87, 30): 0x65, (87, 31): 0x75, (87, 32): 0x74, (87, 33): 0x71,
               (77, 30): 0x65, (77, 31): 0x75, (77, 32): 0x75, (77, 33): 0x71}
    for (x, y) in MARKERS:   # BNW's statues where the PR's room has none (on their cell's own floor)
        st, coll = cells[y][x]
        if st[1:3] != STATUE:
            patch[(x, y)] = ((st[0],) + STATUE + (0,), 1)
    drawn = 0
    if bnw is not None:
        model = R.model(bnw, 304)
        bg1 = [list(r) for r in R.SM.layout(bnw, 304, 0)]
        bg2 = [list(r) for r in R.SM.layout(bnw, 304, 1)]
        for (x, y), t in pillars.items():
            bg1[y][x] = t
        for (x, y) in pillars:
            st, _how = model.stack(bg1[y][x], bg2[y][x], R._around(bg1, x, y))
            patch[(x, y)] = (st if st is not None else cells[y][x][0], 1)
            drawn += st is not None
    else:
        for (x, y) in pillars:
            patch[(x, y)] = (cells[y][x][0], 1)

    def obj_(name, x, y, gid, **props):
        return {"name": name, "x": x, "y": y, "gid": gid, "properties": props}
    objects = [obj_("PointIn", 82, 42, POINT_GID, direction=1, entity_id=201, link_object_id=0, object_id=1,
                    object_type=0, title_id="MSG_ARA_NAME_13", title_type=1),
               obj_("GotoMap", 82, 43, GOTO_GID, action_id=3, direction=0, entity_id=202, link_object_id=0,
                    map_id=SOUTH_ID, object_id=0, object_type=3, point_id=SOUTH_POINT, script_id=0),
               obj_("PointIn", 82, 32, POINT_GID, direction=4, entity_id=203, link_object_id=0, object_id=2,
                    object_type=0, title_id="MSG_ARA_NAME_13", title_type=1),
               obj_("GotoMap", 82, 30, GOTO_GID, action_id=3, direction=0, entity_id=204, link_object_id=0,
                    map_id=NORTH_ID, object_id=0, object_type=3, point_id=NORTH_POINT, script_id=0)]
    assets = R.clone_map(out_dir, LIKE_ROOM, sub, ROOM_ID, ROOM_SCRIPT, objects, patch=patch)
    out_room = os.path.dirname(_map_path(out_dir, group, folder, sub, "x"))
    # the markers the PR's way (Kefka's Tower's floor switches, Map_30871_18): the statue's tiles get a
    # second frame (the empty cell), a TileAnimRegion over each marker, and the map script cues a fallen
    # dragon's region to that frame (CueAnim [1]) as the room loads
    tm_path = os.path.join(out_room, "tilemap.json")
    tm = json.load(open(tm_path, encoding="utf-8"))
    ts = next(t for t in tm["tilesets"] if t["name"] == STATUE[0][0])
    tiles = ts.setdefault("tiles", [])
    for _atlas, tid in STATUE:
        tiles[:] = [t for t in tiles if t.get("id") != tid]
        tiles.append({"animation": [{"duration": 1, "tileid": tid}, {"duration": 1, "tileid": BLANK}], "id": tid})
    tiles.sort(key=lambda t: t["id"])
    with open(tm_path, "w", encoding="utf-8") as fh:
        json.dump(tm, fh, ensure_ascii=False)
    ent_path = os.path.join(out_room, "entity_default.json")
    ent = json.load(open(ent_path, encoding="utf-8"))
    ground = next(l for l in ent["layers"] if l.get("name") == "GroundObject")
    nid = 1 + max([o["id"] for l in ent["layers"] for o in l.get("objects") or []] or [0])
    for k, (x, y) in enumerate(MARKERS):
        props = {"all_layers": True, "entity_id": MARKER_ENTITY + k, "object_id": 0, "object_type": 1, "reverse": False}
        ground["objects"].append({"height": 16, "id": nid + k, "name": "TileAnimRegion（BNW dragon marker）", "rotation": 0,
                                  "type": "", "visible": True, "width": 16, "x": 16 * x, "y": 16 * y,
                                  "properties": [{"name": n, "type": "bool" if isinstance(v, bool) else "int", "value": v}
                                                 for n, v in sorted(props.items())]})
    with open(ent_path, "w", encoding="utf-8") as fh:
        json.dump(ent, fh, ensure_ascii=False)
    # its map script: the fallen dragons' markers gone; Kaiser while all 8 dragons are down and the seal holds
    sc_name = f"sc_map_{sub[4:].lower()}"
    entr = json.load(open(os.path.join(out_room, sc_name + ".json"), encoding="utf-8"))
    entr["Mnemonics"] = [entr["Mnemonics"][0]]
    entr["Segments"] = [{"Label": "Main", "EntryPoint": 0, "Count": 0}]
    blocks = []
    for k, ((x, y), fl) in enumerate(MARKERS.items()):
        seg = Sc.append(entr, f"marker_{k}", [Sc.op("SetPuppet", i=[0, MARKER_ENTITY + k, f"@marker_{k}_00"], kind=2),
                                              Sc.op("ExecPuppet", kind=2), Sc.op("Return", kind=2),
                                              Sc.label(f"marker_{k}_00", kind=2), Sc.op("CueAnim", r=[1], kind=2),
                                              Sc.op("Exit", kind=2)])
        entr["Mnemonics"][seg]["type"] = 2
        blocks.append((fl, seg))
    def room_main(blocks):
        # a fallen dragon's marker goes as the room loads, but not the one whose tally visit this is (the
        # visit's script takes it off in view: dragon_visits)
        main = []
        for k, (fl, seg) in enumerate(blocks):
            main.append(Sc.op("Branch", i=[fl, 0, f"@kept_{k}"], s=["ScenarioFlag2", EQ, "imm"]))
            if visits:
                cat, idx = visits[fl]
                main.append(Sc.op("Branch", i=[idx, 1, f"@kept_{k}"], s=[cat, EQ, "imm"]))
            main += [Sc.op("Call", i=[seg]), Sc.label(f"kept_{k}")]
        main += [Sc.op("Branch", i=[fl, 0, "@done"], s=["ScenarioFlag2", EQ, "imm"]) for fl in DRAGON_FLAGS]
        main += [Sc.op("Branch", i=[SEAL_FLAG, 1, "@done"], s=["ScenarioFlag2", EQ, "imm"]),
                 Sc.op("SetEntities", s=["ev_bnw_kaiser"]), Sc.label("done"), Sc.op("Exit")]
        return main
    main = room_main(blocks)
    # Main goes first: the marker segments move after it
    segs = entr["Mnemonics"][1:]
    shift = len(main)
    for o in segs:
        if o["mnemonic"] == "SetPuppet":
            o["operands"]["iValues"][2] += shift
    for k, (fl, seg) in enumerate(blocks):
        blocks[k] = (fl, seg + shift)
    main = room_main(blocks)
    assert len(main) == shift
    Sc._resolve(main, 1)
    entr["Mnemonics"] = [entr["Mnemonics"][0]] + main + segs
    entr["Segments"] = [{"Label": "Main", "EntryPoint": 0, "Count": 1 + len(main)}] + [
        {"Label": seg["Label"], "EntryPoint": seg["EntryPoint"] + shift, "Count": seg["Count"]} for seg in entr["Segments"][1:]]
    for name, doc in (("sc_bnw_kaiser", talk), ("sc_bnw_kaiser_1", after), ("ev_bnw_kaiser", ev), (sc_name, entr)):
        with open(os.path.join(out_room, name + ".json"), "w", encoding="utf-8") as fh:
            fh.write(json.dumps(doc, ensure_ascii=False, separators=(",", ":")))
    # BNW's rerouted doors: the PR's door from SNES 334 and from SNES 337 lead into the room
    for pr_map, door, point in ((SOUTH_MAP, SOUTH_DOOR, 1), (NORTH_MAP, NORTH_DOOR, 2)):
        f = os.path.join(os.path.dirname(R.SM.pr_tilemaps()[pr_map]), "entity_default.json")
        ent = _load(out_dir, f)
        hits = 0
        for lay in ent["layers"]:
            for o in lay.get("objects") or []:
                pr = {q["name"]: q for q in o["properties"]}
                if o.get("name") == "GotoMap" and (o["x"] // 16, o["y"] // 16 - 1) == door:
                    pr["map_id"]["value"], pr["point_id"]["value"] = ROOM_ID, point
                    hits += 1
        assert hits == 1, f"{pr_map}: the door at {door} wasn't found ({hits})"
        Sc.save_path(ent, out_dir, f)
        _key(akeys, keys, f)
    # the room loads only what its package lists
    pkg = Sc.list_in_map(out_dir, group, folder, sub, ["sc_bnw_kaiser", "sc_bnw_kaiser_1"])
    pkg_file = os.path.join(out_dir, group, "Assets", "GameAssets", "Serial", "Res", "Map", folder, "package.json")
    doc = json.load(open(pkg_file, encoding="utf-8"))
    entry = next(m for m in doc["map"] if m["name"] == sub)
    if not any(x["name"] == "ev_bnw_kaiser" for x in entry.get("entity", [])):
        entry.setdefault("entity", []).append({"name": "ev_bnw_kaiser", "asset": f"{sub}/ev_bnw_kaiser"})
    with open(pkg_file, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))
    base = f"Assets/GameAssets/Serial/Res/Map/{folder}/{sub}/"
    kv = keys.setdefault(group, {})
    for name in ("sc_bnw_kaiser", "sc_bnw_kaiser_1", "ev_bnw_kaiser"):
        kv[f"{sub}/{name}"] = base + name
    kv["package"] = pkg
    log(f"  boss event: Kaiser (party {KAISER_PARTY}) in BNW's corridor room, new map {sub} ({ROOM_ID}; "
        f"{len(assets)} assets, {drawn}/{len(pillars)} pillar cells drawn from BNW's tiles), between "
        f"{SOUTH_MAP} {SOUTH_DOOR} and {NORTH_MAP} {NORTH_DOOR}; after all 8 dragons, until the seal "
        f"(ScenarioFlag2 {SEAL_FLAG}); the 8 dragon tallies leave the seal and the esper to him")


# BNW's tally visit (CB/5090, after each dragon: CB/50A8...): the party is taken to Kaiser's room, the fallen
# dragons' markers are drawn (CB/4FE5), the screen fades in, shakes with a rumble, this dragon's marker goes
# (its tiles_set), "N dragons left" is said there (CC/1F9F), the screen fades out and the dragon's own event
# goes on in its room (one of them, SNES 232, reloads its map at a fixed spot: the PR's after-battle for it
# puts the party at the same (60, 44)). The PR's after-battle scripts end in the tally (ChangeScript
# sc_e_0975, a copy per dragon room): that hand-off becomes the trip. Each dragon's after-battle script: its
# flag, the party's cell back in its room (the dragon's own cell, free after the fight; Map_20440's after-battle
# spot), and what its script did after the tally (ChangeScript [1] returns there).
DRAGON_VISITS = {
    "sc_e_0779_1": (138, (17, 20)),   # Narshe's cliffs (Map_20030)
    "sc_e_0893_3": (178, (60, 44)),   # Map_20440, after the fight in Map_20441_3
    "sc_e_0734_1": (108, (47, 9)),    # Mt. Zozo (Map_30671_1)
    "sc_e_0879_1": (166, (6, 12)),    # the Cultists' Tower (Map_30731_1)
    "sc_e_0809_1": (148, (21, 46)),   # Map_30781_1
    "sc_e_0889_1": (175, (53, 49)),   # the Ancient Castle (Map_30811_7)
    "sc_e_0974_1": (220, (82, 33)),   # Kefka's Tower (Map_30871_10)
    "sc_e_0981_1": (225, (82, 33)),   # Kefka's Tower (Map_30871_9)
}
VISIT_SCRIPT, BACK_SCRIPT = "sc_bnw_dragon_tally", "sc_bnw_dragon_back"
VISIT_CELL, VISIT_POINT = (82, 34), 2   # BNW's hidden party at (82, 34); the room's north entry (82, 32)
GE = "≧"


def _segments(name, main, puppets, local=False):
    """A script: Main (type 1), then puppet segments [(label, ops)] (type 2); "@label" targets resolved."""
    ops, segs = [Sc.label("Main")] + main, []
    segs.append({"Label": "Main", "EntryPoint": 0, "Count": len(ops)})
    for lab, body in puppets:
        at = len(ops)
        block = [Sc.label(lab, kind=2)] + body
        for o in block:
            o["type"] = 2
        ops += block
        segs.append({"Label": lab, "EntryPoint": at, "Count": len(block)})
    Sc._resolve(ops, 0)
    return {"SystemFlag": {"SkipInitialize": 0, "KeepPlayerPuppet": 0, "KeepBadStatusVisual": 0, "RidingVehicle": 0},
            "Segments": segs, "Mnemonics": ops, "Animations": [], "Name": name,
            "ScriptLocal": ["スクリプト変数作成", "なし"] if local else ["なし"], "ScriptLocalValue": [0, 0] if local else [0],
            "Title": {"main": "汎用スクリプト", "sub": ""}}


def _place(x, y, look=None):
    """Puppet segments putting the party and the camera on a cell."""
    party = [Sc.op("SetPos", i=[x, y])] + ([Sc.op("LookAt", i=[look])] if look is not None else []) + [Sc.op("Exit")]
    return [("place", [Sc.op("SetPuppet", i=[0, -1001, "@place_00"]), Sc.op("SetPuppet", i=[1, 0, "@place_01"]),
                       Sc.op("ExecPuppet"), Sc.op("Return")]),
            ("place_00", party), ("place_01", [Sc.op("SetPos", i=[x, y]), Sc.op("Exit")])]


def dragon_visits(out_dir, akeys, keys, log, visits):
    group, folder, sub = KAISER_ROOM
    maps = {r["map_name"]: int(r["id"]) for r in P.Table("map").rows.values()}
    tally = Sc.load("sc_e_0975")
    told = [m["operands"]["sValues"][0] for m in tally["Mnemonics"] if m["mnemonic"] == "Msg"][:8]
    assert len(told) == 8, "the tally's eight lines (1..8 dragons down) changed"
    order = list(MARKERS.values())
    # the visit, in Kaiser's room
    main = [Sc.op("HidePlayer"), Sc.op("Call", i=["@place"]), Sc.op("FadeIn", i=[0, 0, 0, 255], r=[0.7]),
            Sc.op("Wait", r=[1.0]), Sc.op("PlaySE", i=[187, 1, 1]), Sc.op("Shake", i=[1, 1])]
    puppets = _place(*VISIT_CELL)
    for fl, (cat, idx) in sorted(visits.items()):
        k = order.index(fl)
        main += [Sc.op("Branch", i=[idx, 0, f"@nocue_{fl}"], s=[cat, EQ, "imm"]), Sc.op("Call", i=[f"@cue_{fl}"]),
                 Sc.label(f"nocue_{fl}")]
        puppets += [(f"cue_{fl}", [Sc.op("SetPuppet", i=[0, MARKER_ENTITY + k, f"@cue_{fl}_00"]), Sc.op("ExecPuppet"),
                                   Sc.op("Return")]),
                    (f"cue_{fl}_00", [Sc.op("CueAnim", r=[1]), Sc.op("Exit")])]
    main += [Sc.op("Wait", r=[0.75]), Sc.op("Shake", i=[0, 1]), Sc.op("StopSE", i=[1], r=[0.7])]
    for fl in DRAGON_FLAGS:   # the tally's own count and lines (sc_e_0975)
        main += [Sc.op("Branch", i=[fl, 0, f"@up_{fl}"], s=["ScenarioFlag2", EQ, "imm"]),
                 Sc.op("Calc", i=[0, 1], s=["ScriptLocalVariable", "＋", "imm"]), Sc.label(f"up_{fl}")]
    for n in range(1, 8):
        main += [Sc.op("Branch", i=[0, n + 1, f"@ge_{n + 1}"], s=["ScriptLocalVariable", GE, "imm"]),
                 Sc.op("Msg", i=[3, 2], s=[told[n - 1]]), Sc.op("Jump", i=["@told"]), Sc.label(f"ge_{n + 1}")]
    main += [Sc.op("Msg", i=[3, 2], s=[told[7]]), Sc.label("told"),
             Sc.op("FadeOut", i=[0, 0, 0, 255], r=[0.7]), Sc.op("ShowPlayer")]
    rooms = {}
    for after, (fl, cell) in DRAGON_VISITS.items():
        f = Sc.all_paths(after)[0]
        room = os.path.basename(os.path.dirname(f))
        ent = _load(out_dir, os.path.join(os.path.dirname(f), "entity_default.json"))
        points = sorted({p["value"] for lay in ent["layers"] for o in lay.get("objects") or [] if o.get("name") == "PointIn"
                         for p in o["properties"] if p["name"] == "object_id"})
        assert points, f"{room}: no entry point to come back to"
        rooms[after] = (room, maps[room], points[0], f)
        cat, idx = visits[fl]
        main += [Sc.op("Branch", i=[idx, 1, f"@back_{fl}"], s=[cat, EQ, "imm"])]
    main += [Sc.op("Exit")]
    for after, (fl, cell) in DRAGON_VISITS.items():
        room, map_id, point, _f = rooms[after]
        cat, idx = visits[fl]
        main += [Sc.label(f"back_{fl}"), Sc.op("ResetFlag", i=[idx], s=[cat]),
                 Sc.op("ChangeMap", i=[map_id, point, 0, 0], s=[BACK_SCRIPT]), Sc.op("Exit")]
    visit = _segments(VISIT_SCRIPT, main, puppets, local=True)
    out_room = os.path.dirname(_map_path(out_dir, group, folder, sub, "x"))
    with open(os.path.join(out_room, VISIT_SCRIPT + ".json"), "w", encoding="utf-8") as fh:
        fh.write(json.dumps(visit, ensure_ascii=False, separators=(",", ":")))
    pkg = Sc.list_in_map(out_dir, group, folder, sub, [VISIT_SCRIPT])
    kv = keys.setdefault(group, {})
    kv[f"{sub}/{VISIT_SCRIPT}"] = f"Assets/GameAssets/Serial/Res/Map/{folder}/{sub}/{VISIT_SCRIPT}"
    kv["package"] = pkg
    # each dragon's after-battle: its tally becomes the trip; back in its room, the party on its cell
    for after, (fl, (x, y)) in DRAGON_VISITS.items():
        room, map_id, point, f = rooms[after]
        sc = _load(out_dir, f)
        ms = sc["Mnemonics"]
        at = [n for n, m in enumerate(ms) if m["mnemonic"] == "ChangeScript" and m["operands"]["sValues"][0] == "sc_e_0975"]
        assert len(at) == 1, f"{after}: its tally hand-off changed"
        n = at[0]
        sets = [m for m in ms[:n] if m["mnemonic"] == "SetFlag" and m["operands"]["iValues"][0] == fl]
        assert sets, f"{after}: doesn't set dragon flag {fl}"
        tail = []
        if ms[n]["operands"]["iValues"][0] == 1:   # the tally as a call: what came after it runs back in the room
            for m in ms[n + 1:]:
                if m["mnemonic"] == "Exit":
                    break
                tail.append(copy.deepcopy(m))
        cat, idx = visits[fl]
        blk = Sc.append(sc, "bnw_dragon_visit", [Sc.op("SetFlag", i=[idx], s=[cat]), Sc.op("FadeOut", i=[0, 0, 0, 255], r=[0.7]),
                                                 Sc.op("ChangeMap", i=[ROOM_ID, VISIT_POINT, 0, 0], s=[VISIT_SCRIPT]),
                                                 Sc.op("Exit")])
        ms[n] = Sc.op("Jump", i=[blk])
        Sc.save_path(sc, out_dir, f)
        _key(akeys, keys, f)
        back = _segments(BACK_SCRIPT, [Sc.op("Call", i=["@place"]), Sc.op("FadeIn", i=[0, 0, 0, 255], r=[0.7])] + tail
                         + [Sc.op("Exit")], _place(x, y, 0))
        rel = os.path.relpath(f, P.VANILLA).replace("\\", "/")
        rgroup, value = rel.split("/", 1)
        rfolder, rsub = value.split("/")[-3], value.split("/")[-2]
        dest = os.path.join(out_dir, os.path.dirname(rel), BACK_SCRIPT + ".json")
        with open(dest, "w", encoding="utf-8") as fh:
            fh.write(json.dumps(back, ensure_ascii=False, separators=(",", ":")))
        rpkg = Sc.list_in_map(out_dir, rgroup, rfolder, rsub, [BACK_SCRIPT])
        rkv = keys.setdefault(rgroup, {})
        rkv[f"{rsub}/{BACK_SCRIPT}"] = f"Assets/GameAssets/Serial/Res/Map/{rfolder}/{rsub}/{BACK_SCRIPT}"
        rkv["package"] = rpkg
    log(f"  boss event: the dragon tally visits Kaiser's room after each of the 8 dragons ({VISIT_SCRIPT}; "
        f"back on the dragon's cell by {BACK_SCRIPT}; visit flags {sorted(visits.values())})")


def kefka(out_dir, akeys, keys, log, lines):
    f = Sc.path("sc_e_0460_1")
    sc = _load(out_dir, f)
    ms = sc["Mnemonics"]
    at = next(n for n, m in enumerate(ms) if m["mnemonic"] == "Encount" and m["operands"]["iValues"][0] in (454, KEFKA_PARTY))
    ms[at]["operands"]["iValues"][0] = KEFKA_PARTY
    shown = 0
    if lines:
        tmpl = next(m for m in ms if m["mnemonic"] == "Msg")
        ops = []
        for key in lines:
            o = copy.deepcopy(tmpl)
            o["label"] = ""
            o["operands"]["sValues"] = [key] + [""] * 7
            ops.append(o)
        # before the scene's fade to the battle (Kefka is still on the field), as BNW shows them
        where = at - 1 if ms[at - 1]["mnemonic"] == "FadeOut" and not ms[at - 1]["label"] else at
        Sc.hook(sc, where, "bnw_kefka_lines", ops)
        shown = len(ops)
    Sc.save_path(sc, out_dir, f)
    _key(akeys, keys, f)
    log(f"  boss event: the Sealed Gate's first battle is Kefka and two Centurions (party {KEFKA_PARTY}), "
        f"{shown} of BNW's lines before it" + ("" if lines else " (no keys yet: profile bnw_event_lines)"))


def port(out_dir, tables, log, profile, bnw=None):
    if not getattr(profile, "bnw_boss_events", False):
        return {}
    scripts = tables.setdefault("script", P.Table("script"))
    changed = set()
    akeys, keys = C.asset_keys(), {}
    visits = None
    if getattr(profile, "bnw_dragon_visits", False):
        # a flag per dragon while its tally visit is on (the mod's FlagAlloc file, keyed 0x2000 + its ScenarioFlag2)
        import event_translate as T
        path = profile.snes_event_alloc
        alloc = T.FlagAlloc(path if os.path.isabs(path) else os.path.join(P.ROOT, path))
        visits = {fl: alloc.get(0x2000 + fl) for fl in DRAGON_FLAGS}
        alloc.save()
    kaiser(out_dir, scripts, changed, akeys, keys, log, bnw, visits)
    if visits:
        dragon_visits(out_dir, akeys, keys, log, visits)
    kefka(out_dir, akeys, keys, log, getattr(profile, "bnw_event_lines", None) or [])
    if getattr(profile, "bnw_rewards", False):
        import port_bnw_rewards   # BNW's changed event gifts (the event audit's mechanics)
        port_bnw_rewards.port(out_dir, log, profile)
    if getattr(profile, "bnw_party_events", False):
        import port_bnw_party_events   # readme audit C: Shadow's lottery, Zozo unequip, Terra/Edgar optimize
        port_bnw_party_events.port(out_dir, log, profile)
    import port_bnw_mechanics   # save points, Bob, leavers... (the event audit's mechanics)
    for name, ids in port_bnw_mechanics.port(out_dir, log, profile, tables).items():
        changed.update(ids) if name == "script" else None
    # the other BNW event sessions' steps that add script-table rows (they need `tables`): each returns the
    # script ids it added (bnw-dream: 9971-9979, bnw-scenes: 9981-9989; ours 9961-9963)
    for flag, mod in (("bnw_dream", "port_bnw_dream"),                 # Cyan's dream as BNW's (bnw-dream)
                      ("bnw_added_scenes", "port_bnw_added_scenes")):   # BNW's added scenes (bnw-scenes)
        if getattr(profile, flag, False):
            r = __import__(mod).port(out_dir, log, tables)
            changed.update((r or {}).get("script", ()) if isinstance(r, dict) else (r or ()))
    import port_bnw_menus   # readme audit C: Ultros's EL respec NPC, the airship NPCs unequipping Umaro
    for name, ids in port_bnw_menus.port(out_dir, log, profile, tables).items():
        changed.update(ids) if name == "script" else None
    for group, kv in keys.items():
        kdir = os.path.join(out_dir, group, "keys")
        os.makedirs(kdir, exist_ok=True)
        with open(os.path.join(kdir, "bnw_boss_events.json"), "w", encoding="utf-8") as fh:
            json.dump({"keys": list(kv), "values": list(kv.values())}, fh, indent=1)
    return {"script": changed}
