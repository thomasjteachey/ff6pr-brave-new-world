"""BNW's menu-side event changes (readme audit batch C, 2026-11-21: items 4, 6, 7), as PR map scripts.

1. Ultros's EL respec (readme 598-606, 2574; ca_cb_cc.asm:499-562 CB/4E5E, the Free respec refund CB/51BD at
   ca_cb_cc.asm:797-803; c0.asm:64-69 + 232-266 RespecELs, general action $67). The Coliseum's Ultros (SNES map 413
   NPC 1 at (27, 10), sprite 31; vanilla's CB/797A receptionist line) runs BNW's script instead:
       caption 2650 (BNW_D0A5B); no Receipt (event bit 1DD, rare item 13): 2651 "Reset your party's ELs for 25,000
       GP?" -> take 25,000, give the Receipt; with it: 2652 "...that'll be 100,000 GP." -> take 60,000 + 40,000
       (the 16-bit gil command; the second failing gives the 60,000 back: "subsequent EL resets are now properly
       denied", readme 2574); short of gil: CB/69FF (BNW_D0B19 "...not enough GP."); "No": 2654 (BNW_D0A5F);
       paid: sound 79 twice, then for each active-party member Terra..Gau: 8D unequip + 67 respec (the EL bank =
       every EL earned, Vigor/Speed/Stamina/Magic back to base, max HP/MP rebuilt from level 1 and HP/MP filled:
       C0/9F4A), then 2653 (BNW_D0A5E).
   The PR's Ultros is Map_20421_1's message NPC (N041_C00_297_04_01; the world map's Coliseum door leads to that
   room, 279). He gets script sc_bnw_ultros_respec (script id 9991): the gil through the plugin's "BNW:pay <gil>"
   (ShopCalls.cs), the Receipt as a flag (FlagAlloc, bit 1DD; the PR has no Receipt item), the reset through the
   plugin's "BNW_MENUS:respec" (BnwPlugin/MenuRules.cs: unequip + EsperStore's bank = EL, spent bonuses cleared,
   HP/MP filled). Its lines are BNW_D0A5B..BNW_D0A5F (bnw_dialog.ADDED_DIALOGUES; BNW_D0B19 is Bob's already).
2. Umaro unequipped by the airship NPCs (readme 1779-1787 "Bare Naked Yeti"; ca_cb_cc.asm:1355-1367 CC/3591 and
   CC/3664 -> ee.asm:144-166 EE/B053 / EE/B066: Gogo and Umaro too, when recruited; for "those not in the party"
   when not in it). The PR's two SysCalls (Current.RemoveEquipmentFromMembers, tools/decomp/unequip) skip
   character status 14 only (Gogo is already theirs): after each, "BNW_MENUS:umaro_unequip all|reserve". The menu
   lock itself is the plugin's (MenuRules.cs: the PR's IsChangeEquipmentOnlyAccessory).
3. HP refills restore MP (readme 2431-2435 "MP Party Restore", Seibaby; c0.asm:157-169: event command 8B's
   "HP to max" goes on into 8C's code, C0/AED5 JMP $AF3E, so every event that fills HP fills MP). The PR's
   party-select scripts (RecoveryHP [0, 1, 1, 0, -1] before "パーティ編成", 44 of them: SNES CA/CBAF, the party
   menu's set-up) and its other full-HP restores without an MP one get the matching RecoveryMP. Run it last
   (port_late): it edits whatever the other modules wrote.

    port(out_dir, log, profile, tables)   1 and 2 (profile bnw_respec_npc, bnw_umaro_equip)
    port_late(out_dir, log, profile)      3 (profile bnw_hp_refills_mp)
"""
import copy
import glob
import json
import os

import port_chests as C
import prdata as P

# --- 1. Ultros's respec --------------------------------------------------------------------------------------
ALLOC = os.path.join(P.ROOT, "data", "snes_event_alloc_bnw.json")   # Bob's file (port_bnw_mechanics.BOB_ALLOC)
BIT_RECEIPT = 0x1DD          # CB/4E61 C0 DD 81 (has the Receipt), CB/4E86 D2 DD (give it)
FIRST_PRICE, LATER_PRICE = 25000, 100000   # CB/4E7D 85 A8 61; CB/4E8D 85 60 EA + CB/4E95 85 40 9C
SE_UNEQUIP = 904             # SNES sound 4F (F4 4F at CB/4EA0) -> the PR's unequip NPC sound (sc_e_0345's PlaySE)
SID_RESPEC = 9991            # script ids: merchants 9901, mechanics 9961-9964, dream 9971-, added scenes 9981-
RESPEC = "sc_bnw_ultros_respec"
ULTROS_ROOM = ("map_20421", "Map_20421", "Map_20421_1")
ULTROS_LINE = "N041_C00_297_04_01"   # the PR's Ultros: "Me, the great and noble Ultros, reduced to ..."
LINES = {"intro": "BNW_D0A5B", "first": "BNW_D0A5C", "again": "BNW_D0A5D", "done": "BNW_D0A5E", "bye": "BNW_D0A5F",
         "poor": "BNW_D0B19"}
SYSCALL_RESPEC = "BNW_MENUS:respec"
EQ = "＝"

# --- 2. Umaro and the airship NPCs ---------------------------------------------------------------------------
UNEQUIP_RESERVE = "控えメンバーの装備を外す"   # 控えメンバーの装備を外す
UNEQUIP_ALL = "全員の装備を外す"                                 # 全員の装備を外す
AIRSHIP_NPCS = ("sc_e_0345", "sc_e_0678", "sc_e_0704")   # the Blackjack's, the Falcon's (grounded, inside)

# --- 3. HP refills restore MP --------------------------------------------------------------------------------
HOOK_LABEL = "bnw_mp_with_hp"


def _full(v):
    """A full restore: External.Character's kFullRecoveryValue (0), or the event translator's 9999."""
    return v == 0 or v >= 9999


def _op(*a, **k):
    import pr_script as Sc
    return Sc.op(*a, **k)


def _layers(ls):
    for lay in ls:
        yield lay
        yield from _layers(lay.get("layers", []))


def _map_dir(root, group, folder, sub):
    return os.path.join(root, group, "Assets", "GameAssets", "Serial", "Res", "Map", folder, sub)


def respec_script(receipt):
    """BNW's CB/4E5E as a PR script (port_bnw_mechanics' helpers: Bob's offers and payments); `receipt` = the
    (category, index) flag of event bit 1DD."""
    import port_bnw_mechanics as M

    def pay(price):   # BNW: 85 (take gil) + C0 BE 81 (short: CB/69FF); the plugin: MapLocalVariable 0 = paid
        return [_op("SysCall", s=[f"BNW:pay {price}"]), _op("Branch", i=[0, 0, "@poor"], s=["MapLocalVariable", EQ, "imm"])]

    return M._script(RESPEC, [
        ("Main", [_op("Msg", i=[0, 2], s=[LINES["intro"]]),
                  _op("Branch", i=[receipt[1], 1, "@again"], s=[receipt[0], EQ, "imm"])]
         + M._offer(LINES["first"], "bye") + pay(FIRST_PRICE)
         + [_op("SetFlag", i=[receipt[1]], s=[receipt[0]]), _op("Jump", i=["@reset"])]),
        ("again", M._offer(LINES["again"], "bye") + pay(LATER_PRICE)),
        ("reset", [_op("PlaySE", i=[SE_UNEQUIP, 1]), _op("Wait", r=[0.5]),
                   _op("PlaySE", i=[SE_UNEQUIP, 1]), _op("Wait", r=[0.5]),
                   _op("SysCall", s=[SYSCALL_RESPEC]),
                   _op("Msg", i=[0, 2], s=[LINES["done"]]), _op("Exit")]),
        ("bye", [_op("Msg", i=[0, 2], s=[LINES["bye"]]), _op("Exit")]),
        ("poor", [_op("Msg", i=[0, 2], s=[LINES["poor"]]), _op("Exit")])])


def respec_npc(out_dir, log, akeys, keys, tables, alloc_path=ALLOC):
    """Ultros (Map_20421_1) runs sc_bnw_ultros_respec. Returns the script ids it added."""
    import event_translate as T
    import pr_script as Sc
    alloc = T.FlagAlloc(alloc_path)
    receipt = alloc.get(BIT_RECEIPT)
    alloc.save()
    scripts = tables.setdefault("script", P.Table("script"))
    assert SID_RESPEC not in scripts.rows or scripts.rows[SID_RESPEC]["script_name"] == RESPEC, f"script id {SID_RESPEC} taken"
    scripts.rows[SID_RESPEC] = {"id": str(SID_RESPEC), "script_name": RESPEC}
    group, folder, sub = ULTROS_ROOM
    room = _map_dir(out_dir, group, folder, sub)
    os.makedirs(room, exist_ok=True)
    with open(os.path.join(room, RESPEC + ".json"), "w", encoding="utf-8") as fh:
        json.dump(respec_script(receipt), fh, ensure_ascii=False, separators=(",", ":"))
    mine = os.path.join(room, "entity_default.json")
    src = mine if os.path.exists(mine) else os.path.join(_map_dir(P.VANILLA, group, folder, sub), "entity_default.json")
    doc = json.load(open(src, encoding="utf-8"))
    hits = 0
    for lay in _layers(doc["layers"]):
        for o in lay.get("objects") or []:
            pr = {q["name"]: q for q in o.get("properties", [])}
            if pr.get("message_key", {}).get("value") == ULTROS_LINE or pr.get("script_id", {}).get("value") == SID_RESPEC:
                # the PR's scripted NPCs: action 2 + script_id, no message (183 vanilla NPCs; none has action 4 + script)
                pr["script_id"]["value"] = SID_RESPEC
                pr["action_id"]["value"] = 2
                pr["message_key"]["value"] = ""
                hits += 1
    assert hits == 1, f"the Coliseum's Ultros: {hits} found"
    with open(mine, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, ensure_ascii=False, separators=(",", ":"))
    pkg = Sc.list_in_map(out_dir, group, folder, sub, [RESPEC])
    base = f"Assets/GameAssets/Serial/Res/Map/{folder}/{sub}/"
    kv = keys.setdefault(group, {})
    kv[f"{sub}/{RESPEC}"] = base + RESPEC
    k = akeys.get((group, base + "entity_default"))
    kv[k or f"{sub}/entity_default"] = base + "entity_default"
    kv["package"] = pkg
    log(f"  Ultros's EL respec: the Coliseum's Ultros ({sub}) runs {RESPEC} ({SID_RESPEC}): {FIRST_PRICE:,} GP once "
        f"(the Receipt, flag {receipt}), then {LATER_PRICE:,} GP; plugin {SYSCALL_RESPEC}")
    return {SID_RESPEC}


def umaro_unequip(out_dir, log, akeys, keys):
    """The airship NPCs' two unequip services also take Umaro's equipment (BNW EE/B053, EE/B066)."""
    import pr_script as Sc
    done = []
    for name in AIRSHIP_NPCS:
        for f in Sc.all_paths(name):
            rel = os.path.relpath(f, P.VANILLA)
            mine = os.path.join(out_dir, rel)
            sc = json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))
            ms = sc["Mnemonics"]
            group, value = rel.replace("\\", "/").split("/", 1)
            k = akeys.get((group, os.path.splitext(value)[0]))
            if k:
                keys.setdefault(group, {})[k] = os.path.splitext(value)[0]
            if any(o["label"].startswith("bnw_umaro_") for o in ms):
                continue   # a re-run on the port's own copy
            n = 0
            for call, which in ((UNEQUIP_RESERVE, "reserve"), (UNEQUIP_ALL, "all")):
                at = [i for i, o in enumerate(ms) if o["mnemonic"] == "SysCall" and o["operands"]["sValues"][0] == call]
                assert len(at) == 1 and not ms[at[0]]["label"], f"{name}: unequip ({which}) changed"
                theirs = copy.deepcopy(ms[at[0]])
                # the PR's call, then the plugin's for Umaro, then back (as WC's -eu: wc_events.airship_party_npc)
                Sc.hook(sc, at[0], f"bnw_umaro_{which}",
                        [theirs, _op("SysCall", s=[f"BNW_MENUS:umaro_unequip {which}"]), _op("Jump", i=[at[0]])])
                n += 1
            Sc.save_path(sc, out_dir, f)
            done.append(f"{name} ({n})")
    log(f"  Umaro unequipped by the airship NPCs too (BNW EE/B053, EE/B066): {', '.join(done)}; "
        "plugin BNW_MENUS:umaro_unequip")


def _write_keys(out_dir, keys, fname):
    for group, kv in keys.items():
        kdir = os.path.join(out_dir, group, "keys")
        os.makedirs(kdir, exist_ok=True)
        with open(os.path.join(kdir, fname), "w", encoding="utf-8") as fh:
            json.dump({"keys": list(kv), "values": list(kv.values())}, fh, indent=1)


def port(out_dir, log, profile, tables=None):
    akeys, keys = C.asset_keys(), {}
    changed = {}
    if getattr(profile, "bnw_respec_npc", False) and tables is not None:
        changed["script"] = respec_npc(out_dir, log, akeys, keys, tables)
    if getattr(profile, "bnw_umaro_equip", False):
        umaro_unequip(out_dir, log, akeys, keys)
    _write_keys(out_dir, keys, "bnw_menus.json")
    return changed


# --- 3. HP refills restore MP --------------------------------------------------------------------------------

def _unpaired(ms):
    """Indices of full RecoveryHP instructions with no full RecoveryMP for the same target within 4."""
    out = []
    for i, m in enumerate(ms):
        if m["mnemonic"] != "RecoveryHP" or not _full(m["operands"]["iValues"][0]):
            continue
        target = m["operands"]["iValues"][4]
        if not any(x["mnemonic"] == "RecoveryMP" and _full(x["operands"]["iValues"][0])
                   and x["operands"]["iValues"][4] == target for x in ms[max(0, i - 4):i + 5]):
            out.append(i)
    return out


def mp_with_hp_script(sc):
    """BNW C0/AED5 on one PR script: each full RecoveryHP without a RecoveryMP gets one (same amount, flags and
    target; iValues[3] = 1, the MP count flag every vanilla RecoveryMP sets). Indices stay (pr_script.hook).
    Returns [(index, target)] of the sites changed."""
    import pr_script as Sc
    ms = sc["Mnemonics"]
    if any(o["label"].startswith(HOOK_LABEL) for o in ms):
        return []
    done = []
    for i in _unpaired(ms):
        hp = ms[i]
        if hp.get("type", 1) != 1:
            continue   # a puppet's instruction (none in vanilla)
        mp = copy.deepcopy(hp)
        mp["mnemonic"], mp["label"], mp["comment"] = "RecoveryMP", "", "BNW: HP refills restore MP (C0/AED5)"
        mp["operands"]["iValues"][3] = 1
        Sc.hook(sc, i, f"{HOOK_LABEL}_{i}", [mp])
        done.append((i, hp["operands"]["iValues"][4]))
    return done


def mp_with_hp(out_dir, log):
    import pr_script as Sc
    akeys, keys = C.asset_keys(), {}
    vanilla = [os.path.join(P.VANILLA, r) for r in Sc._script_files()]
    seen, changed, sites, party = set(), [], 0, 0
    todo = []
    for f in vanilla:
        rel = os.path.relpath(f, P.VANILLA)
        mine = os.path.join(out_dir, rel)
        seen.add(os.path.normcase(os.path.abspath(mine)))
        todo.append((mine if os.path.exists(mine) else f, f, rel))
    # the port's own scripts (no vanilla file): written where they're served
    for mine in glob.glob(os.path.join(out_dir, "map_*", "**", "s*.json"), recursive=True):
        if os.path.normcase(os.path.abspath(mine)) not in seen:
            todo.append((mine, None, os.path.relpath(mine, out_dir)))
    for src, van, rel in todo:
        if not os.path.basename(src).startswith("sc_"):
            continue
        try:
            text = open(src, encoding="utf-8").read()
            if '"RecoveryHP"' not in text:
                continue
            sc = json.loads(text)
        except (ValueError, UnicodeDecodeError):
            continue
        if not isinstance(sc, dict) or "Mnemonics" not in sc:
            continue
        again = any(o["label"].startswith(HOOK_LABEL) for o in sc["Mnemonics"])   # a re-run on the port's copy
        done = mp_with_hp_script(sc)
        if van and (done or again):
            group, value = rel.replace("\\", "/").split("/", 1)
            k = akeys.get((group, os.path.splitext(value)[0]))
            if k:
                keys.setdefault(group, {})[k] = os.path.splitext(value)[0]
        if not done:
            continue
        sites += len(done)
        party += sum(1 for _, t in done if t == -1)
        if van:
            Sc.save_path(sc, out_dir, van)
        else:
            with open(src, "w", encoding="utf-8") as fh:
                fh.write(json.dumps(sc, ensure_ascii=False, separators=(",", ":")))
        changed.append(os.path.splitext(os.path.basename(src))[0])
    _write_keys(out_dir, keys, "bnw_hp_mp.json")
    log(f"  HP refills restore MP (BNW C0/AED5): {sites} full RecoveryHP in {len(changed)} scripts got a RecoveryMP "
        f"({party} of them the party select's all-character heal)")
    return changed


def port_late(out_dir, log, profile):
    if getattr(profile, "bnw_hp_refills_mp", False):
        return mp_with_hp(out_dir, log)
    return []
