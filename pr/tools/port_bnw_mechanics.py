"""BNW's event mechanics beyond its text (the enemy-AI session, 2026-11-20; the coordinator: "port the
mechanics part" of bnw-text-ui's event audit, PORTING.md "BNW's changed scenes beyond its text").

Save points: BNW adds 11 (tools/event_diff.py: each a new NPC with the save-point sprite 111 and an
event tile running CC/9AEB on the same cell). The PR's save point is a pair of objects on a map's
GroundObject layer (vanilla's Map_20011_2): "SavePoint" (object type 9: the cell that lets the menu
save) and "savepoint" (type 10, asset 256: the sparkle, 8 px up). Each of BNW's goes on the PR room
that draws its SNES cell (snes_maps.to_pr); map 414 has no PR room.

    port(out_dir, log)    (from port_boss_events, with the profile's bnw_save_points)
"""
import copy
import json
import os

import ff6snes as S
import port_chests as C
import prdata as P
import snes_maps as SM

TEMPLATE = os.path.join("map_20011", "Assets", "GameAssets", "Serial", "Res", "Map", "Map_20011", "Map_20011_2",
                        "entity_default.json")
# BNW's new save points: (SNES map, x, y) of the sprite-111 NPC and its CC/9AEB tile
SAVE_POINTS = [(63, 56, 5), (225, 36, 59), (276, 10, 12), (281, 59, 56), (337, 8, 26), (351, 21, 52),
               (353, 7, 51), (369, 9, 9), (384, 90, 53), (408, 10, 10), (414, 82, 47)]


def _template():
    doc = json.load(open(os.path.join(P.VANILLA, TEMPLATE), encoding="utf-8"))
    objs = {o["name"]: o for lay in doc["layers"] for o in lay.get("objects") or [] if o.get("name") in ("SavePoint", "savepoint")}
    return objs["SavePoint"], objs["savepoint"]


def _layers(ls):
    for lay in ls:
        yield lay
        yield from _layers(lay.get("layers", []))


def _entity_file(pr_map):
    return os.path.join(os.path.dirname(SM.pr_tilemaps()[pr_map]), "entity_default.json")


def save_points(out_dir, log, akeys, keys):
    action, sparkle = _template()
    placed, missing = [], []
    import snes_rooms as R
    for m, x, y in SAVE_POINTS:
        # the PR room that draws the cell and lets the party stand there (a SNES map's rooms are several
        # PR maps; the first that has it walkable: snes_maps' order, best match first)
        at = None
        for t in SM.pr_maps(m):
            px, py = x + t.dx, y + t.dy
            if SM.drawn(t.pr, px, py):
                rows, _tm = R.pr_cells(t.pr)
                if rows[py][px][1] == 0:
                    at = (t.pr, px, py)
                    break
        if at is None:
            missing.append(m)
            continue
        pr_map, px, py = at
        f = _entity_file(pr_map)
        rel = os.path.relpath(f, P.VANILLA)
        mine = os.path.join(out_dir, rel)
        doc = json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))
        # the room's floor objects: GroundObject, or UpperObject where it has only that (Figaro's upper floor)
        groups = {l.get("name"): l for l in _layers(doc["layers"]) if l.get("type") == "objectgroup"}
        ground = groups.get("GroundObject") or groups["UpperObject"]
        objs = [o for l in _layers(doc["layers"]) for o in l.get("objects") or []]
        if any(o.get("name") == "SavePoint" and o["x"] == 16 * px and o["y"] == 16 * (py + 1) for o in objs):
            continue   # already there (a re-run on the port's own copy)
        nid = 1 + max([o["id"] for o in objs] or [0])
        ent = 1 + max([p["value"] for o in objs for p in o.get("properties", []) if p["name"] == "entity_id"] or [0])
        a, s = copy.deepcopy(action), copy.deepcopy(sparkle)
        a["id"], s["id"] = nid, nid + 1
        a["x"] = s["x"] = 16 * px
        a["y"], s["y"] = 16 * (py + 1), 16 * (py + 1) - 8
        for p in a["properties"]:
            if p["name"] == "entity_id":
                p["value"] = ent
        ground["objects"] += [a, s]
        os.makedirs(os.path.dirname(mine), exist_ok=True)
        with open(mine, "w", encoding="utf-8") as fh:
            json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))
        group, value = rel.replace("\\", "/").split("/", 1)
        k = akeys.get((group, os.path.splitext(value)[0]))
        if k:
            keys.setdefault(group, {})[k] = os.path.splitext(value)[0]
        placed.append(f"{m}->{pr_map} ({px}, {py})")
    log(f"  save points: {len(placed)} of BNW's {len(SAVE_POINTS)} new ones ({', '.join(placed)})"
        + (f"; SNES map(s) {missing}: no PR room has the cell walkable (Figaro 63's room isn't among the PR maps "
           f"snes_maps pairs with it; 414 has none)" if missing else ""))



# BNW's full-recovery save points (readme 2381-2412: "some save points are now also full recovery points"; the user,
# 2026-11-21: "doesn't BNW have special colored save points that heal you"): 14 save tiles run CB/51C7 instead of the
# save tile's CC/9AEB: sound $E9, CA/CFBD (each party member's two statuses lifted, HP and MP to full), then the
# save tile's own flash and bits. Their save-point sprite uses palette 5 (the plain ones 6): the plugin tints those
# (BnwPlugin RecoveryPoints, from bnw_data "recovery_points"). Three more palette-5 sprites (maps 313, 412, 414) have
# no tile under them in BNW: shown only, nothing to port. In the PR each recovery cell gets a step-on trigger
# (object type 2, vanilla's EventGotoMap object as the template) running sc_bnw_recovery: the PR's own full recovery
# (vanilla sc_e_0555_11: RemoveCondition, RecoveryHP, RecoveryMP) with its heal sound (SE 934).
RECOVERY_TILE_EVENT = 0xCB51C7
LAST_RECOVERY_CELLS = []   # the last port's [{snes_map, pr_map, x, y}]
SID_RECOVERY = 9964
TRIGGER_TEMPLATE = (os.path.join("map_20010", "Assets", "GameAssets", "Serial", "Res", "Map", "Map_20010", "Map_20010",
                                 "entity_default.json"), "EventGotoMap")


def recovery_tiles(rom):
    """[(SNES map, x, y)] of the save tiles that run BNW's full recovery."""
    out = []
    for m in range(415):
        try:
            tiles = SM.event_tiles(rom, m)
        except Exception:
            continue
        out += [(m, t["x"], t["y"]) for t in tiles if t["event"] == RECOVERY_TILE_EVENT]
    return out


def _recovery_script():
    import pr_script as Sc
    sc = copy.deepcopy(Sc.load("sc_e_0555_11"))
    sc["Name"] = "sc_bnw_recovery.json"
    heal = [m for m in sc["Mnemonics"] if m["mnemonic"] in ("RemoveCondition", "RecoveryHP", "RecoveryMP")]
    assert [m["mnemonic"] for m in heal] == ["RemoveCondition", "RecoveryHP", "RecoveryMP"], "vanilla's recovery moved"
    sc["Mnemonics"] = [Sc.label("Main"), Sc.op("PlaySE", i=[934, 1, 0])] + copy.deepcopy(heal) + [Sc.op("Exit")]
    sc["Segments"] = [{"Label": "Main", "EntryPoint": 0, "Count": len(sc["Mnemonics"])}]
    sc["ScriptLocal"], sc["ScriptLocalValue"] = ["なし"], [0]   # PlaySE keeps its handle in a script local
    return sc


# The PR's save point for a SNES map the map table doesn't lead to one (the PR moved it or cut the room differently):
# paired 2026-11-21 by the save points both games have left over after the table's matches (vanilla 41, the PR 42):
# 114 / 358 / 272 by the rooms' walkable layout aligned on the two save points (walk agreement 0.77 with the exact
# cell, 1.0, 1.0), 317 (0.94), 240 the one save point of its room (the PR's 5 cells down from the SNES cell), 41 the
# one left in its room group (Map_30021, the table's rooms for 41; 0.63: the PR moved it in a reworked room)
RECOVERY_ROOMS = {41: [("Map_30021_1", 11, 25)], 114: [("Map_30391_1", 6, 13)], 240: [("Map_20210", 58, 12)],
                  272: [("Map_30521_9", 8, 14)], 317: [("Map_30821_1", 25, 53)], 358: [("Map_30601_1", 9, 13)]}


def _room_docs(out_dir, pr_map):
    """[(path relative to the game data, path to read, doc)] of a PR room's entity files (entity_default and its
    event layers), the port's copy where it wrote one."""
    room = os.path.dirname(SM.pr_tilemaps()[pr_map])
    out = []
    for f in sorted(os.listdir(room)):
        if not f.endswith(".json") or f.startswith("sc_") or f == "package.json":
            continue
        rel = os.path.relpath(os.path.join(room, f), P.VANILLA)
        mine = os.path.join(out_dir, rel)
        path = mine if os.path.exists(mine) else os.path.join(room, f)
        try:
            doc = json.load(open(path, encoding="utf-8"))
        except (OSError, ValueError):
            continue
        if isinstance(doc, dict) and "layers" in doc:
            out.append((rel, doc))
    return out


def _save_cells(doc):
    return [(o["x"] // 16, o["y"] // 16 - 1) for l in _layers(doc["layers"]) for o in l.get("objects") or []
            if {q["name"]: q["value"] for q in o.get("properties", [])}.get("object_type") == 9]


def recovery_points(out_dir, log, akeys, keys, tables, rom):
    import pr_script as Sc
    scripts = tables.setdefault("script", P.Table("script"))
    assert SID_RECOVERY not in scripts.rows or scripts.rows[SID_RECOVERY]["script_name"] == "sc_bnw_recovery",         f"script id {SID_RECOVERY} taken"
    scripts.rows[SID_RECOVERY] = {"id": str(SID_RECOVERY), "script_name": "sc_bnw_recovery"}
    tdoc = json.load(open(os.path.join(P.VANILLA, TRIGGER_TEMPLATE[0]), encoding="utf-8"))
    trig = next(o for l in _layers(tdoc["layers"]) for o in l.get("objects") or [] if o.get("name") == TRIGGER_TEMPLATE[1])
    script = _recovery_script()
    placed, missing, cells = [], [], []
    for m, x, y in recovery_tiles(rom):
        # the PR save points that are this one: the table's rooms (each SNES map's PR rooms, best first; a map the PR
        # has twice, both), a save point within 3 cells of the SNES cell; else RECOVERY_ROOMS
        targets = []
        for t in SM.pr_maps(m):
            for rel, doc in _room_docs(out_dir, t.pr):
                for sx, sy in _save_cells(doc):
                    if abs(sx - x - t.dx) + abs(sy - y - t.dy) <= 3 and not any(r == rel for r, *_ in targets):
                        targets.append((rel, doc, t.pr, sx, sy))
        if not targets:
            for pr, sx, sy in RECOVERY_ROOMS.get(m, []):
                for rel, doc in _room_docs(out_dir, pr):
                    if (sx, sy) in _save_cells(doc):
                        targets.append((rel, doc, pr, sx, sy))
        if not targets:
            missing.append(m)
            continue
        for rel, doc, pr_map, px, py in targets:
            cells.append({"snes_map": m, "pr_map": pr_map, "x": px, "y": py})
            objs = [o for l in _layers(doc["layers"]) for o in l.get("objects") or []]
            mine = os.path.join(out_dir, rel)
            if not any(o.get("name") == "BNW Recovery" and o["x"] == 16 * px and o["y"] == 16 * (py + 1) for o in objs):
                # beside the save point, in its layer (an event layer's save point shows only with that layer)
                lay = next(l for l in _layers(doc["layers"]) for o in l.get("objects") or []
                           if {q["name"]: q["value"] for q in o.get("properties", [])}.get("object_type") == 9
                           and o["x"] == 16 * px and o["y"] == 16 * (py + 1))
                n = copy.deepcopy(trig)
                n["id"] = 1 + max([o["id"] for o in objs] or [0])
                n["name"] = "BNW Recovery"
                n["x"], n["y"] = 16 * px, 16 * (py + 1)
                ent = 1 + max([q["value"] for o in objs for q in o.get("properties", []) if q["name"] == "entity_id"] or [0])
                for q in n["properties"]:
                    if q["name"] == "entity_id":
                        q["value"] = ent
                    elif q["name"] == "script_id":
                        q["value"] = SID_RECOVERY
                lay["objects"].append(n)
                os.makedirs(os.path.dirname(mine), exist_ok=True)
                with open(mine, "w", encoding="utf-8") as fh:
                    json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))
            # the script beside the room's files, and in the room's script list
            with open(os.path.join(os.path.dirname(mine), "sc_bnw_recovery.json"), "w", encoding="utf-8") as fh:
                json.dump(script, fh, ensure_ascii=False, separators=(",", ":"))
            parts = rel.replace("\\", "/").split("/")
            group, folder, sub, fname = parts[0], parts[-3], parts[-2], os.path.splitext(parts[-1])[0]
            pkg = Sc.list_in_map(out_dir, group, folder, sub, ["sc_bnw_recovery"])
            base = f"Assets/GameAssets/Serial/Res/Map/{folder}/{sub}/"
            kv = keys.setdefault(group, {})
            kv[f"{sub}/sc_bnw_recovery"] = base + "sc_bnw_recovery"
            k = akeys.get((group, base + fname))
            kv[k or f"{sub}/{fname}"] = base + fname
            kv["package"] = pkg
            placed.append(f"{m}->{pr_map} ({px}, {py})")
    log(f"  recovery points: {len(placed)} PR save points heal, for {len(set(c['snes_map'] for c in cells))} of BNW's "
        f"{len(set(c['snes_map'] for c in cells)) + len(missing)} full-recovery save points ({', '.join(placed)})"
        + (f"; no PR save point for SNES map(s) {missing}" if missing else ""))
    return {SID_RECOVERY}, cells

# --- Bob: BNW's esper seller (the auction house is gone) ------------------------------------------------
# BNW (read with tools/snes_events.py): Bob at the Dragon's Neck Cabin (SNES 186, CB/4F27: Golem, then
# Zoneseek once story bit 107 is set, 10,000 GP each; before bit 83 only his old line) and at the
# Coliseum (413, CB/4F85: "donate" 10,000 GP for whichever of the two is left), and a man in the
# Cultists' Tower (362, CC/51FF: Alexandr for 32,768 GP). The PR's NPCs there: Map_20141_1's old man
# (entity 3, his line by message_key), the Coliseum's sc_npc_20421_3_1, the tower's sc_e_0877 (which
# pays by the PR's RemoveGil and its price row: ours pays by the plugin's "BNW:pay <gil>", ShopCalls.cs).
# Lines: bnw-text-ui's keys for BNW's dialogues (BNW_D<hex>), the PR's own where the index is the same.
BOB_ALLOC = os.path.join(P.ROOT, "data", "snes_event_alloc_bnw.json")
GOLEM, ZONESEEK, ALEXANDR = 0x4C, 0x48, 0x46          # BNW's esper_add arguments
BIT_GOLEM, BIT_ZONESEEK, BIT_ALEXANDR = 0x16D, 0x16C, 0xE5
BIT_83, BIT_107 = ("ScenarioFlag1", 149), ("ScenarioFlag1", 195)   # snes_event_maps.json's ties
EQ = "\uff1d"
SE_MAGICITE = 105     # the PR's "magicite" sound (sc_e_0651)
POOR = "BNW_D0B19"    # "...not enough GP."


def _op(*a, **k):
    import pr_script as Sc
    return Sc.op(*a, **k)


def _sale(price, esper, got, flag, no_label):
    """Offer already shown and chosen: pay (or "not enough GP"), the esper, its line, the flag."""
    import idmap as M
    return [_op("SysCall", s=[f"BNW:pay {price}"]),
            _op("Branch", i=[0, 0, "@poor"], s=["MapLocalVariable", EQ, "imm"]),
            _op("PlaySE", i=[SE_MAGICITE, 1]),
            _op("GetItem", i=[M.esper_content(esper - 0x36), 1]),
            _op("Msg", i=[0, 2], s=[got]),
            _op("SetFlag", i=[flag[1]], s=[flag[0]]),
            _op("Exit")]


def _offer(key, no_label):
    return [_op("Msg", i=[0, 2], s=[key]), _op("SysCall", s=["Entity\u66f4\u65b0\u72b6\u614b\uff1a\u30e1\u30c3\u30bb\u30fc\u30b8"]),
            _op("Select", s=[key + "_c_01", key + "_c_02"]),
            _op("Branch", i=[0, 1, f"@{no_label}"], s=["MessageSelection", EQ, "imm"])]


def _script(name, blocks):
    """A script from labelled blocks [(label or None, ops)], "@label" targets resolved."""
    import pr_script as Sc
    ops = []
    for lab, body in blocks:
        if lab:
            ops.append(Sc.label(lab))
        ops += body
    Sc._resolve(ops, 0)
    ops[0]["label"] = ops[0]["label"] or "Main"
    return {"SystemFlag": {"SkipInitialize": 0, "KeepPlayerPuppet": 0, "KeepBadStatusVisual": 0, "RidingVehicle": 0},
            "Segments": [{"Label": ops[0]["label"], "EntryPoint": 0, "Count": len(ops)}],
            "ScriptLocal": ["\u306a\u3057"], "ScriptLocalValue": [0], "Mnemonics": ops, "Animations": [],
            "Name": name + ".json", "Title": {"main": "", "sub": ""}}


def bob(out_dir, log, akeys, keys, tables):
    import event_translate as T
    import pr_script as Sc
    alloc = T.FlagAlloc(BOB_ALLOC)
    golem_f, zone_f, alex_f = alloc.get(BIT_GOLEM), alloc.get(BIT_ZONESEEK), alloc.get(BIT_ALEXANDR)
    alloc.save()
    scripts = tables.setdefault("script", P.Table("script"))
    changed = set()
    # the Dragon's Neck Cabin: Map_20141_1's old man gets a script
    cabin = _script("sc_bnw_bob_cabin", [
        ("Main", [_op("Branch", i=[BIT_83[1], 0, "@before"], s=[BIT_83[0], EQ, "imm"]),
                  _op("Branch", i=[golem_f[1], 0, "@golem"], s=[golem_f[0], EQ, "imm"]),
                  _op("Branch", i=[zone_f[1], 0, "@zone"], s=[zone_f[0], EQ, "imm"]),
                  _op("Msg", i=[0, 2], s=["BNW_D0A59"]), _op("Exit")]),
        ("before", [_op("Msg", i=[0, 2], s=["N004_C00_104_01_01"]), _op("Exit")]),
        ("golem", _offer("BNW_D0A56", "no") + _sale(10000, GOLEM, "BNW_D0AC3", golem_f, "no")),
        ("zone", [_op("Branch", i=[BIT_107[1], 0, "@later"], s=[BIT_107[0], EQ, "imm"])]
         + _offer("BNW_D0A56", "no") + _sale(10000, ZONESEEK, "BNW_D0AC4", zone_f, "no")),
        ("later", [_op("Msg", i=[0, 2], s=["BNW_D0A57"]), _op("Exit")]),
        ("no", [_op("Msg", i=[0, 2], s=["BNW_D0A58"]), _op("Exit")]),
        ("poor", [_op("Msg", i=[0, 2], s=[POOR]), _op("Exit")])])
    # the Coliseum's Bob: a "donation" for whichever esper is left, then his old line
    coli = _script("sc_npc_20421_3_1", [
        ("Main", [_op("Branch", i=[zone_f[1], 1, "@done"], s=[zone_f[0], EQ, "imm"]),
                  _op("Branch", i=[golem_f[1], 0, "@golem"], s=[golem_f[0], EQ, "imm"])]
         + _offer("BNW_D0A5A", "no") + _sale(10000, ZONESEEK, "BNW_D0AC4", zone_f, "no")),
        ("golem", _offer("BNW_D0A5A", "no") + _sale(10000, GOLEM, "BNW_D0AC3", golem_f, "no")),
        ("done", [_op("Msg", s=["N004_C00_281_02_02"]), _op("Exit")]),
        ("no", [_op("Msg", i=[0, 2], s=["BNW_D0A55"]), _op("Exit")]),
        ("poor", [_op("Msg", i=[0, 2], s=[POOR]), _op("Exit")])])
    # the Cultists' Tower's dealer
    cult = _script("sc_e_0877", [
        ("Main", [_op("Branch", i=[alex_f[1], 1, "@done"], s=[alex_f[0], EQ, "imm"])]
         + _offer("BNW_D08C8", "no") + _sale(32768, ALEXANDR, "BNW_D08C9", alex_f, "no")),
        ("done", [_op("Msg", i=[0, 2], s=["BNW_D09E6"]), _op("Exit")]),
        ("no", [_op("Exit")]),
        ("poor", [_op("Msg", i=[0, 2], s=[POOR]), _op("Exit")])])
    written = []
    for name, sc, room in (("sc_npc_20421_3_1", coli, None), ("sc_e_0877", cult, None)):
        f = Sc.path(name)
        Sc.save_path(sc, out_dir, f)
        rel = os.path.relpath(f, P.VANILLA).replace("\\", "/")
        group, value = rel.split("/", 1)
        k = akeys.get((group, os.path.splitext(value)[0]))
        if k:
            keys.setdefault(group, {})[k] = os.path.splitext(value)[0]
        written.append(name)
    # the cabin: a new script, the old man's script_id, the map's script list
    sid = 9963
    assert sid not in scripts.rows or scripts.rows[sid]["script_name"] == "sc_bnw_bob_cabin", f"script id {sid} taken"
    scripts.rows[sid] = {"id": str(sid), "script_name": "sc_bnw_bob_cabin"}
    changed.add(sid)
    room = os.path.join("map_20141", "Assets", "GameAssets", "Serial", "Res", "Map", "Map_20141", "Map_20141_1")
    os.makedirs(os.path.join(out_dir, room), exist_ok=True)
    with open(os.path.join(out_dir, room, "sc_bnw_bob_cabin.json"), "w", encoding="utf-8") as fh:
        json.dump(cabin, fh, ensure_ascii=False, separators=(",", ":"))
    ent_rel = os.path.join(room, "entity_default.json")
    mine = os.path.join(out_dir, ent_rel)
    doc = json.load(open(mine if os.path.exists(mine) else os.path.join(P.VANILLA, ent_rel), encoding="utf-8"))
    hits = 0
    for lay in _layers(doc["layers"]):
        for o in lay.get("objects") or []:
            pr = {q["name"]: q for q in o.get("properties", [])}
            if pr.get("message_key", {}).get("value") == "N004_C00_104_01_01":
                pr["script_id"]["value"] = sid
                # action 2 runs the script (vanilla's 183 scripted NPCs); his 4 only shows the message (197 NPCs):
                # with 4 he'd say his old line and never sell (bnw-atb's review, 2026-11-21)
                pr["action_id"]["value"] = 2
                hits += 1
    assert hits == 1, f"Bob's cabin NPC: {hits} found"
    with open(mine, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))
    pkg = Sc.list_in_map(out_dir, "map_20141", "Map_20141", "Map_20141_1", ["sc_bnw_bob_cabin"])
    base = "Assets/GameAssets/Serial/Res/Map/Map_20141/Map_20141_1/"
    kv = keys.setdefault("map_20141", {})
    kv["Map_20141_1/sc_bnw_bob_cabin"] = base + "sc_bnw_bob_cabin"
    k = akeys.get(("map_20141", base + "entity_default"))
    kv[k or "Map_20141_1/entity_default"] = base + "entity_default"
    kv["package"] = pkg
    log(f"  Bob: Golem and Zoneseek at the Dragon's Neck Cabin (sc_bnw_bob_cabin) and the Coliseum, Alexandr in the "
        f"Cultists' Tower ({', '.join(written)}); flags {golem_f}, {zone_f}, {alex_f} (data/snes_event_alloc_bnw.json)")
    return changed


# BNW's Jidoor has no way into the auction house (SNES map 198's exit to 200 is gone; map 200 keeps no
# NPCs): the PR's town maps lose their door into it (the auction would sell Bob's espers a second time)
AUCTION_DOORS = [("map_20150", "Map_20150", 111), ("map_20430", "Map_20430", 288)]


def close_auction(out_dir, log, akeys, keys):
    closed = []
    for group, name, dest in AUCTION_DOORS:
        rel = os.path.join(group, "Assets", "GameAssets", "Serial", "Res", "Map", name, name, "entity_default.json")
        mine = os.path.join(out_dir, rel)
        doc = json.load(open(mine if os.path.exists(mine) else os.path.join(P.VANILLA, rel), encoding="utf-8"))
        n = 0
        for lay in _layers(doc["layers"]):
            objs = lay.get("objects") or []
            keep = [o for o in objs if not (o.get("name") == "GotoMap" and any(
                p["name"] == "map_id" and p["value"] == dest for p in o.get("properties", [])))]
            n += len(objs) - len(keep)
            if objs:
                lay["objects"] = keep
        if n == 0 and os.path.exists(mine):
            continue   # a re-run on the port's own copy
        assert n == 1, f"{name}: {n} doors into the auction house (map {dest})"
        os.makedirs(os.path.dirname(mine), exist_ok=True)
        with open(mine, "w", encoding="utf-8") as fh:
            json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))
        value = rel.replace("\\", "/").split("/", 1)[1]
        k = akeys.get((group, os.path.splitext(value)[0]))
        if k:
            keys.setdefault(group, {})[k] = os.path.splitext(value)[0]
        closed.append(name)
    log(f"  auction house: no door into it from Jidoor ({', '.join(closed)}), as BNW")


# BNW's Veldt freebies (mods/brave-new-world/source/bnw 3.0/asm/veldt-freebies.asm, by seibaby): its new event
# command $68, run in the world's-end cutscene (CA/51CD), ORs this table into the Veldt's met-formation bits
# ($1DDD+, formation n = byte n / 8, bit n % 8), so the World of Balance's missable formations (and the
# Floating Continent's) are on the Veldt in the World of Ruin. "Hardcoded to formations as they appeared in
# BNW 2.1": taken as 3.0 ships it, for the formations 3.0's Veldt allows.
VELDT_FREEBIES = [0xEE, 0x77, 0xA0, 0x48, 0x88, 0xA0, 0x54, 0x11, 0x00, 0x00, 0x19, 0xC8, 0x31, 0x50, 0xC9, 0x98, 0x11]
VELDT_SCENE, VELDT_BEFORE = "sc_e_0607_7", "StopSE"   # the PR's world-map collapse (after the explosions)


def veldt_freebies(out_dir, log, profile, akeys, keys):
    """The PR opens a Veldt entry when its party is fought (BeastFieldEncount.EncounterMonsterParty): the
    plugin's "BNW:veldt_open <party>..." does that for each freebie formation's Veldt party, in the PR's
    world's-end scene. The parties: the Veldt table port_veldt wrote (its entries in formation order)."""
    import ff6snes as SN
    import port_veldt as V
    import pr_script as Sc
    path = os.path.join(out_dir, V.GROUP, *V.ASSET.split("/")) + ".json"
    assert os.path.exists(path), "the Veldt freebies need port_veldt's table (it runs before the AI step)"
    entries = [e for g in json.load(open(path, encoding="utf-8"))["beast_field_encount_table"] for e in g["party_table"]]
    mf = V.veldt_formations(SN.Rom(profile.rom))
    assert len(entries) == len(mf), f"Veldt table {len(entries)} entries, the mod's Veldt {len(mf)} formations"
    party = dict(zip(mf, (e["monster_party_id"] for e in entries)))
    wanted = [8 * i + b for i, byte in enumerate(VELDT_FREEBIES) for b in range(8) if byte >> b & 1]
    parties = [party[f] for f in wanted if f in party]
    for f in Sc.all_paths(VELDT_SCENE):
        rel = os.path.relpath(f, P.VANILLA)
        mine = os.path.join(out_dir, rel)
        sc = json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))
        ms = sc["Mnemonics"]
        if any(o["label"] == "bnw_veldt_freebies" for o in ms):
            continue
        n = next(i for i, m in enumerate(ms) if m["mnemonic"] == VELDT_BEFORE)
        Sc.hook(sc, n, "bnw_veldt_freebies", [Sc.op("SysCall", s=["BNW:veldt_open " + " ".join(map(str, parties))])])
        Sc.save_path(sc, out_dir, f)
        group, value = rel.replace("\\", "/").split("/", 1)
        k = akeys.get((group, os.path.splitext(value)[0]))
        if k:
            keys.setdefault(group, {})[k] = os.path.splitext(value)[0]
    log(f"  Veldt freebies: {len(parties)} of BNW's {len(wanted)} formations are on its Veldt; opened at the "
        f"world's end ({VELDT_SCENE}, plugin BNW:veldt_open)")


def port(out_dir, log, profile, tables=None):
    akeys, keys = C.asset_keys(), {}
    changed = {}
    if getattr(profile, "bnw_save_points", False):
        save_points(out_dir, log, akeys, keys)
    if getattr(profile, "bnw_bob", False) and tables is not None:
        changed["script"] = bob(out_dir, log, akeys, keys, tables)
    if getattr(profile, "bnw_recovery_points", False) and tables is not None:
        rom = S.Rom(profile.rom).data
        global LAST_RECOVERY_CELLS
        sids, LAST_RECOVERY_CELLS = recovery_points(out_dir, log, akeys, keys, tables, rom)
        changed.setdefault("script", set()).update(sids)
        close_auction(out_dir, log, akeys, keys)
    if getattr(profile, "bnw_veldt_freebies", False):
        veldt_freebies(out_dir, log, profile, akeys, keys)
    for group, kv in keys.items():
        kdir = os.path.join(out_dir, group, "keys")
        os.makedirs(kdir, exist_ok=True)
        path = os.path.join(kdir, "bnw_mechanics.json")
        with open(path, "w", encoding="utf-8") as fh:
            json.dump({"keys": list(kv), "values": list(kv.values())}, fh, indent=1)
    return changed
