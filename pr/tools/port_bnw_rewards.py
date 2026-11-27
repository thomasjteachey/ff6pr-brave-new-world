"""BNW's changed event rewards in the PR's own scripts (the enemy-AI session, 2026-11-20; the coordinator:
"port the mechanics part" of bnw-text-ui's event audit, PORTING.md "BNW's changed scenes beyond its text").

The PR's story scripts give vanilla's items, espers and gil (GetItem [content, count]; gil is content 1,
the count its amount; a box's "Obtained X!" line names the item through Tag ITEM_N). BNW changed 18 of
those gifts (tools/event_diff.py, read with tools/snes_events.py: vanilla's and BNW's item_add /
esper_add / gil_add in each changed scene). Each entry below names the PR script (paired with the SNES
scene through its dialogue: event_translate.pairs), the vanilla gift it gives and BNW's in SNES terms;
the content ids come from idmap (BNW's item in SNES slot n is the PR's row for slot n after the port).
An entry whose script no longer gives the vanilla gift fails the port (the PR or an earlier step moved it).
The lines around a gift are bnw-text-ui's (BNW's text on the PR's keys); a box's item name tag is ours.
The Sealed Gate's four gifts and the restored Odin need replacement narration; removed gifts suppress
their old "Obtained" line.
"""
import copy
import json
import os
import re

import idmap as M
import port_chests as C
import pr_script as Sc
import prdata as P

GIL = 1

# (PR script, what the PR gives: ("item"|"esper", SNES id) or ("gil", amount), BNW's gifts [(kind, id or amount)])
SWAPS = [
    # the Returners' Hideout: Banon's gloves (SNES 108/110: vanilla Gauntlet / Genji Glove, BNW Safety Glove /
    # Barrier Cube)
    ("sc_e_0062", ("item", 208), [("item", 184)]),
    ("sc_e_0062_1", ("item", 209), [("item", 183)]),
    ("sc_e_1073", ("item", 209), [("item", 183)]),
    # Mobliz: the magicite Phunbaba leaves (SNES 154: vanilla's esper 0x4E, BNW's 0x3F)
    ("sc_e_0651", ("esper", 0x4E), [("esper", 0x3F)]),
    # Owzer's Mansion: Chadarnook's magicite (SNES 208: BNW gives none there)
    ("sc_e_0775", ("esper", 0x4F), []),
    # Figaro Castle: Edgar's Auto Crossbow (SNES 58: BNW gives none)
    ("sc_e_0026", ("item", 170), []),
    # (Narshe's blacksmith, SNES 24, is bnw-text-ui's tools/port_bnw_scenes.py: its options and gifts)
    # the Cave to the Sealed Gate's chest (SNES 384): BNW's "ninja stuff"
    ("sc_e_0450", ("content", 29), [("item", 171), ("item", 172), ("item", 173), ("item", 67)]),
    # Owzer's basement boxes (SNES 207): gil 293 -> 20000, Tonic -> X-Potion, Tincture -> X-Ether, Soft -> Elixir
    ("sc_e_0767_1", ("gil", 293), [("gil", 20000)]),
    ("sc_e_0768_1", ("item", 232), [("item", 234)]),
    ("sc_e_0769_1", ("item", 235), [("item", 237)]),
    ("sc_e_0770_1", ("item", 244), [("item", 238)]),
    # the Cultists' Tower dragon's Pearl Lance (SNES 368: BNW's White Dragon gives none)
    ("sc_e_0879_1", ("item", 33), []),
    # SNES 398 (CA/55E9): the esper vanilla gives at Mobliz (BNW swapped the two places' espers)
    ("sc_e_0963", ("esper", 0x3F), [("esper", 0x4E)]),
    # the Ancient Castle: Odin's statue gives no esper (SNES 406 CC/1EA5: BNW sets a bit of its own instead),
    # the Queen's statue gives Odin and takes nothing (408 CC/1F49: vanilla took Odin for Raiden)
    ("sc_e_0886", ("esper", 0x41), []),
    ("sc_e_0890", ("esper", 0x42), [("esper", 0x41)]),
]


def story_lines(messages):
    """Reward narration for swaps that no longer fit one vanilla item name."""
    contents = P.Table("content")
    def name(kind, item):
        key = contents[content(kind, item)]["mes_id_name"]
        return re.sub(r"<[^>]*>", "", messages[key]).strip()
    cave = next(gifts for script, _, gifts in SWAPS if script == "sc_e_0450")
    scrolls = [name(kind, item) for kind, item in cave]
    assert len(scrolls) == 4
    return {
        "E0450_00_408_a_02": (f"<CT>Obtained {scrolls[0]}, {scrolls[1]},\\n"
                               f"<CT>{scrolls[2]} and {scrolls[3]}!"),
        "E0890_00_519_a_04": f"<CT>Obtained {name('esper', 0x41)} magicite!",
    }
# vanilla's takings BNW dropped: (PR script, what the PR's RemoveItem takes)
REMOVALS = [("sc_e_0890", ("esper", 0x41))]   # the Queen's statue: Odin stays


# BNW's new gifts where the PR's scene gives nothing: (PR script, the op they go before (mnemonic, its first
# sValue or None[, "after": after it]), BNW's gifts [(kind, id, count)] ("remove": an item taken, "call": a
# plugin SysCall, "heal": the party restored), the SNES bit they need
# (a story bit for the PR's flag (event_translate.maps) or ("battle", n): a battle event bit, the AI host's
# PR flag, profile snes_ai_event_bits), what it is). Silent, as BNW's item_add.
ADDS = [
    # Narshe mines (SNES map 42, CC/A00A): after Tritoch's flash, BNW's 5 Dried Meat (vanilla's 2 Tents, which
    # the PR's scene never gave; Dadaluma below takes Dried Meat)
    ("sc_e_0012", ("StopBGM", None), [("item", 0xFE, 5)], None, "Tritoch's flash: 5 Dried Meat"),
    # Darill's Tomb (SNES 299 CA/4037, the NPC copies CA/4097): the epitaph done, Daryl's Soul before Setzer's
    # line (BNW took out the epitaph's second showing)
    ("sc_e_0696", ("Msg", "E0696_00_466_a_03"), [("item", 0xE4, 1)], None, "Darill's Tomb: Daryl's Soul"),
    # Zozo's Dadaluma (SNES 221 CA/96A9 -> ED/8E09): a Hell Claw after the fight if he was fed Dried Meat (his
    # counter sets battle bit 4F)
    ("sc_e_0301_1", ("FadeIn", None), [("item", 0x57, 1)], ("battle", 0x4F), "Dadaluma fed: Hell Claw"),
    # the Floating Continent's end (SNES 394 CA/E3F1 -> EE/B136): an Imperial with Shadow's parting line, and
    # Celes's best equipment once she's back (BNW's optimize 6: the plugin's "BNW:optimize")
    ("sc_e_0594_2", ("Msg", "E0594_02_431_a_10"), [("item", 0x13, 1)], None, "Floating Continent: Imperial"),
    ("sc_e_0594_2", ("PlayBGM", None), [("call", "BNW:optimize 7", 0)], None, "Floating Continent: Celes optimized"),
    # the Cave on the Veldt (SNES 353 CB/7A7C -> CB/52E4): story bit A3 set, one Kagenui (0x29) for the other (2)
    ("sc_e_0744_2", ("FadeIn", None), [("remove", 0x29, 1), ("item", 0x02, 1)], 0xA3, "Veldt cave: Kagenui swap"),
    ("sc_e_0745_2", ("FadeIn", None), [("remove", 0x29, 1), ("item", 0x02, 1)], 0xA3, "Veldt cave: Kagenui swap"),
    # Shadow leaving keeps none of the party's gear (BNW's char_unequip 3 added at Baren Falls, SNES 156/391
    # CB/C01E, and at Narshe, SNES 20 CC/D3C0 -> ED/8E02): right after the PR's own leave
    ("sc_e_0216", ("SysCall", "メンバー離脱：シャドウ", "after"), [("call", "BNW:unequip 4", 0)], None, "Baren Falls: Shadow unequipped"),
    ("sc_e_0286", ("SysCall", "メンバー離脱：シャドウ", "after"), [("call", "BNW:unequip 4", 0)], None, "Narshe: Shadow unequipped"),
    ("sc_e_0286_1", ("SysCall", "メンバー離脱：シャドウ", "after"), [("call", "BNW:unequip 4", 0)], None, "Narshe: Shadow unequipped"),
    # the Floating Continent's escape (SNES 393 CA/DA48 -> EE/B14A): the party fully restored (CA/CFBD: HP, MP,
    # statuses) before the last fight there (pack 0x51, formation 0x1C3: the PR's party 452), as the PR's
    # own rest scenes do it
    ("sc_e_0606", ("Encount", None), [("heal", 0, 0)], None, "Floating Continent: restored before the escape's fight"),
]
EQ = "＝"


def _need_flag(need, profile):
    """The PR flag a gift waits on: (category, index)."""
    import event_translate as T
    if isinstance(need, tuple):
        kind, n = need
        assert kind == "battle" and n in getattr(profile, "snes_ai_event_bits", ()), f"battle bit {n:02X}: not in snes_ai_event_bits"
        path = profile.snes_event_alloc
        alloc = T.FlagAlloc(path if os.path.isabs(path) else os.path.join(P.ROOT, path))
        flag = alloc.get(0x1000 + n)   # the AI host's flag for it (port_ai's "event_bits": the same file)
        alloc.save()
        return flag
    flag = T.maps()["flags"].get(need)
    assert flag, f"story bit {need:03X}: no PR flag"
    return flag


def add_gifts(out_dir, log, profile, akeys, keys):
    done = []
    for k, (name, (mn, sval, *where), gifts, need, why) in enumerate(ADDS):
        tag = f"bnw_add_{k}"
        paths = Sc.all_paths(name)
        assert paths, f"{name}: no such PR script"
        for f in paths:
            rel = os.path.relpath(f, P.VANILLA)
            mine = os.path.join(out_dir, rel)
            sc = json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))
            ms = sc["Mnemonics"]
            if any(o["label"] == tag for o in ms):
                continue   # a re-run on the port's own copy
            at = [n for n, m in enumerate(ms) if m["mnemonic"] == mn and (sval is None or sval in m["operands"]["sValues"])]
            assert at, f"{name}: no {mn} {sval or ''} to put BNW's gift before"
            n = at[0]
            ops = []
            if need is not None:
                cat, idx = _need_flag(need, profile)
                ops.append(Sc.op("Branch", i=[idx, 0, f"@{tag}_end"], s=[cat, EQ, "imm"]))
            for kind, x, count in gifts:
                if kind == "call":   # a plugin SysCall (BnwPlugin ShopCalls)
                    ops.append(Sc.op("SysCall", s=[x]))
                elif kind == "heal":   # the whole party: HP, MP, statuses (the PR's rest scenes, sc_e_0262)
                    ops += [Sc.op("RecoveryHP", i=[0, 1, 1, 0]), Sc.op("RecoveryMP", i=[0, 1, 1, 1]),
                            Sc.op("RemoveCondition", i=[0, -1])]
                else:
                    ops.append(Sc.op("RemoveItem" if kind == "remove" else "GetItem", i=[content("item", x), count]))
            moved = copy.deepcopy(ms[n])
            moved["label"] = ""
            if where == ["after"]:
                ops = [moved] + ops + [Sc.label(f"{tag}_end")]
            else:
                ops += [Sc.label(f"{tag}_end"), moved]
            # the op itself moves into the block, which jumps back to the op after it (pr_script.hook's way:
            # no call frame, so an Encount or a map change in there is as safe as anywhere)
            blk = Sc.append(sc, tag, ops + [Sc.op("Jump", i=[n])])
            ms[n] = Sc.op("Jump", i=[blk], label=ms[n]["label"], kind=ms[n].get("type", 1))
            Sc.save_path(sc, out_dir, f)
            group, value = rel.replace("\\", "/").split("/", 1)
            ak = akeys.get((group, os.path.splitext(value)[0]))
            if ak:
                keys.setdefault(group, {})[ak] = os.path.splitext(value)[0]
            done.append(name)
    log(f"  BNW's new gifts: {len(done)} PR scripts ({', '.join(sorted(set(done)))})")


def content(kind, x):
    if kind == "item":
        return M.item_content(x)
    if kind == "esper":
        return M.esper_content(x - 0x36)
    if kind == "content":
        return x
    raise ValueError(kind)


def _gift_ops(kind, x, contents, tables=None, changed=None):
    """BNW's gift: an item / esper (GetItem), gil (its tag, the PR's own "gil" line, GetItem 1). SetGilTag takes a
    product group, not the amount (port_chests.gil_price_group): SetGilTag [20000] had stopped the script there."""
    if kind == "gil":
        assert tables is not None, "a gil gift needs the porter's tables (its SetGilTag price group)"
        return [Sc.op("SetGilTag", i=[C.gil_price_group(tables, x, changed if changed is not None else {})]),
                Sc.op("Msg", s=[C.MSG_GIL_GIFT]), Sc.op("GetItem", i=[GIL, x])]
    return [Sc.op("GetItem", i=[content(kind, x), 1])]


def port(out_dir, log, profile=None, tables=None):
    """-> {table: ids} the gifts add (gil price groups: product, product_group)."""
    contents = P.Table("content")
    prices = {}
    akeys, keys = C.asset_keys(), {}
    done = 0
    for name, (vk, vx), gifts in SWAPS:
        old_cid = GIL if vk == "gil" else content(vk, vx)
        for f in Sc.all_paths(name):
            rel = os.path.relpath(f, P.VANILLA)
            mine = os.path.join(out_dir, rel)
            sc = json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))
            ms = sc["Mnemonics"]
            at = [n for n, m in enumerate(ms) if m["mnemonic"] == "GetItem" and m["operands"]["iValues"][0] == old_cid
                  and (vk != "gil" or m["operands"]["iValues"][1] == vx)]
            assert at, f"{name}: no GetItem of {vk} {vx} (content {old_cid}) left to change"
            for n in at:
                new = []
                for kind, x in gifts:
                    new += _gift_ops(kind, x, contents, tables, prices)
                if vk == "gil":
                    # the PR's own fixed line ("293 GP") right before it: the gil line names BNW's amount
                    if n > 0 and ms[n - 1]["mnemonic"] == "Msg":
                        ms[n - 1] = Sc.op("Nop")
                else:
                    new = [o for o in new]   # items keep the scene's own lines
                    # A removed gift must not announce the vanilla item.
                    if not gifts:
                        prior = next((j for j in range(n - 1, max(-1, n - 4), -1)
                                      if ms[j]["mnemonic"] == "Msg"), None)
                        if prior is not None:
                            key = ms[prior]["operands"]["sValues"][0]
                            if "Obtained" in P.messages("story_mes", "en").get(key, ""):
                                ms[prior] = Sc.op("Nop")
                    # a box's "Obtained X!" (Tag ITEM_N after the gift) names BNW's item
                    tag = n + 1 if n + 1 < len(ms) and ms[n + 1]["mnemonic"] == "Tag" else None
                    if tag is not None:
                        if gifts:
                            ms[tag]["operands"]["sValues"][1] = contents[content(*gifts[0])]["mes_id_name"]
                        else:
                            ms[tag] = Sc.op("Nop")
                if len(new) <= 1:
                    ms[n] = new[0] if new else Sc.op("Nop")
                else:
                    # more ops than the one they replace: run them from a block at the end
                    blk = Sc.append(sc, f"bnw_gift_{n}", new + [Sc.op("Return")])
                    ms[n] = Sc.op("Call", i=[blk])
            Sc.save_path(sc, out_dir, f)
            group, value = rel.replace("\\", "/").split("/", 1)
            k = akeys.get((group, os.path.splitext(value)[0]))
            if k:
                keys.setdefault(group, {})[k] = os.path.splitext(value)[0]
            done += 1
    for name, (vk, vx) in REMOVALS:
        cid = content(vk, vx)
        for f in Sc.all_paths(name):
            rel = os.path.relpath(f, P.VANILLA)
            mine = os.path.join(out_dir, rel)
            sc = json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))
            at = [n for n, m in enumerate(sc["Mnemonics"]) if m["mnemonic"] == "RemoveItem" and m["operands"]["iValues"][0] == cid]
            assert at, f"{name}: no RemoveItem of {vk} {vx:#x} (content {cid}) left"
            for n in at:
                sc["Mnemonics"][n] = Sc.op("Nop")
            Sc.save_path(sc, out_dir, f)
            group, value = rel.replace("\\", "/").split("/", 1)
            k = akeys.get((group, os.path.splitext(value)[0]))
            if k:
                keys.setdefault(group, {})[k] = os.path.splitext(value)[0]
    if profile is not None:
        add_gifts(out_dir, log, profile, akeys, keys)
    for group, kv in keys.items():
        kdir = os.path.join(out_dir, group, "keys")
        os.makedirs(kdir, exist_ok=True)
        with open(os.path.join(kdir, "bnw_rewards.json"), "w", encoding="utf-8") as fh:
            json.dump({"keys": list(kv), "values": list(kv.values())}, fh, indent=1)
    log(f"  event rewards: {done} PR scripts give BNW's gifts ({len(SWAPS)} changes)")
    return prices
