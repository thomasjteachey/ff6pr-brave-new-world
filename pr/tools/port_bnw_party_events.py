"""BNW's party events in the PR's own scripts (batch C of the readme audit, 2026-11-21: readme "Can't Lose Our
Shadow", "Zozo Unequip", "Scenario Unequip"), plus Gau's return after a Leap (4). Each where the SNES makes it:

1. Shadow never runs off at random. Vanilla rolls at a battle's end (C2/4880: LDA #$08 : BIT $3EBD skips the
   leave while battle bit 0x4B is set; C2/2437 copies the bits from $1DC9, so 0x4B is $1DD2 bit 3) and its
   events clear that bit where he may leave: CA/96B7 (Zozo, after Dadaluma: the Kohlingen hire), CB/1B03
   (Sabin's scenario after the Imperial Camp), CB/BE60 (after the Phantom Train). BNW makes each B9 4B a B8 4B
   (set: never): mods/brave-new-world/source/asm/banks/ca_cb_cc.asm:188-190, 353-355, 1135-1137 (its 399-403
   and 465-471 hit no B9 4B: the asm's own notes call them wrong). The PR rolls in its battle scripts instead:
   SysCall "シャドウ離脱抽選" (Current.LotSecedeShadow, decompiled tools/decomp/shadow_leave: inside one of
   three ScenarioFlag1 windows, shadowSecedeStartList/StopList, with Shadow in the party beside someone else,
   Random.Range(0, 100) < 1 sets script-local 3), then a Branch into the leave event (SysCall
   "バトルでシャドウ離脱"). Four scripts have it: sc_b_0239 (script 8010: the World of Balance's generic
   battle, ~413 parties), sc_b_2006 (9006, generic), sc_b_2008 (9008), sc_b_0097 (8008). Both ops become Nops
   (nothing else writes that local, so the leave event is unreachable).

2. Zozo: everyone not in the party unequipped, and Shadow. BNW's Ramuh scene (SNES map 226) ends at CA/AC86
   with JSR EE/B034 (ca_cb_cc.asm:211-213; ee.asm:135-141): CA/CB95 as before, then CC/35A4 (vanilla's
   "char_unequip each available character not in the active party": Terra..Gau) and char_unequip 3
   (Shadow), right before the map change. The PR's scene is sc_e_0307_1 (map 30510, paired through SNES
   dialogues 0x47B-0x47F): after its party split (SysCall "パーティ編成：オペラ入場": Locke and Celes), its
   ChangeMap [383, ...] gets, first, the game's own SysCall "控えメンバーの装備を外す" (Current.
   RemoveEquipmentReserveMember: RemoveEquipmentFromMembers(false, false), every roster member not in the
   party, Umaro skipped; decompiled tools/decomp/unequip) and the plugin's "BNW:unequip 4" (ShopCalls: Shadow,
   who left the roster earlier in the scene, SysCall "メンバー離脱：シャドウ").

3. Terra's scenario: Terra and Edgar optimized. BNW's scenario select (SNES map 9) sends Terra, Edgar and
   Banon down the Lete River straight into a battle; at CB/0999, after the party is made and the screen
   faded, JSR CB/52DB (ca_cb_cc.asm:326-328, 1001-1007): CA/CB95, optimize 0 (Terra), optimize 4 (Edgar),
   then the map change. BNW optimizes nowhere else at a scenario's start or end. The PR's scene is sc_e_0087
   (map 40012, SNES dialogue 0x168): its ChangeMap [349, ...] gets the game's own SysCalls "最強装備：ティナ"
   and "最強装備：エドガー" first (SetStrongestEquipTina/Edgar -> SetStrongestEquipCommon(1)/(5): the
   strongest weapon, shield, helmet and armor, no relics, as the SNES's 9C).

4. Gau back from a Leap can't be scared off. The SNES brings him back after a won Veldt fight with no window to
   hit him (C2's end-of-battle Leap return). The PR turns the enemy side into a stand-in Gau after the win
   (sc_b_0239 Mode03Before: "敵パーティをガウに", monsters 549/550, AI sc_ai_549_Gau) and resumes the battle; the
   stand-in's AI sets mode 1 on its turn (Event00: "バトル加入：ガウ", he rejoins) but mode 3 when anyone hits it,
   uses a command or an item on it (Event02: he runs off, still away). Auto-battle attacks the only enemy there,
   so a fight ended on auto lost the return (the user, 2026-11-27). The stand-in's 11 checks that jump to its
   "ダメージを受けた時" block become Nops: it always takes the rejoin (dried meat still works). The first meeting's
   Gau (sc_ai_172_Gau) still runs off when attacked, as on the SNES.

5. A Leap ends every Veldt fight. The SNES's Leap ends any battle it's used in. The PR's Veldt scripts (sc_b_0239, the
   generic battle; sc_b_2006/2008/2027) end it themselves: each loop "とびこむで離脱" (Current.RemoveGauInBattle,
   tools/decomp/leap_end: a party member with Dive leaves the party, script-local 0 = -1), then "RemoveGau":
   BattleEndEnabled 1, BattlePause, RemoveAllAct, BattleForceExitOnSave. BNW's Veldt also offers the Phantom Train's
   formations (port_veldt: parties 2050-2080, their train look and battle script): sc_b_2026 (9026, "魔列車で通常戦闘")
   turns the end judgment off at its start (BattleEndEnabled 0) and on only at its own end ("とりつくで離脱", the
   Ghosts' possessions undone), so the Leap's own end (DiveConditionFunction.ActEndUpdate: isForceOnSave) never
   came and the fight went on until the monsters died (the user, Deck, 2026-11-27, party 2052). Its loop gets the
   same Leap check before its "プレイヤーの状況で戦闘終了" (skipped while script-local 0 is already -1, so the script's own
   end is never mistaken for a Leap), then its possession clean-up and the RemoveGau steps. On the Phantom Train
   Gau can't Leap (SpecialCommands: Leap only in the Veldt's parties), so the train's fights run as before.

    port(out_dir, log, profile=None)   writes the scripts' copies and <group>/keys/bnw_party_events.json
    check(mod_dir)                      problems in a port's copies ([] when all are in)

Each edit asserts the PR script still looks as described (a changed script fails the port) and reads the
port's own copy when an earlier step wrote one; a re-run on the port's copy changes nothing.
"""
import copy
import json
import os

import port_chests as C
import pr_script as Sc
import prdata as P

TAG = "bnw_party_events"
BATTLE_DIR = os.path.join(P.VANILLA, "battle_script", "Assets", "GameAssets", "Serial", "Res", "Battle", "BattleScript")

# 1. Shadow's random leave: the PR's battle scripts that roll it
LOTTERY = "シャドウ離脱抽選"          # Current.LotSecedeShadow
LEAVE = "バトルでシャドウ離脱"        # Current.RemoveShadowInBattle
LEAVE_SCRIPTS = ("sc_b_0097", "sc_b_0239", "sc_b_2006", "sc_b_2008")
NO_LEAVE = "BNW: Shadow never leaves at random (port_bnw_party_events)"

# 4. Gau back from a Leap: the returning stand-in's AI, and its jumps to the "attacked" block (target operand index)
AI_DIR = os.path.join(P.VANILLA, "monster_ai", "Assets", "GameAssets", "Serial", "Res", "Battle", "MonsterAI")
RETURN_AI = "sc_ai_549_Gau"
ATTACKED = "ダメージを受けた時"
FLEE_JUMPS = {"DamagedJump": 0, "IsCheckReceiveCommand": 1, "IsRecevieItem": 1}
NO_FLEE = "BNW: Gau's return after a Leap can't be scared off (port_bnw_party_events)"

# 5. A Leap in a Veldt fight whose PR battle script turns the end judgment off: (script, the op the check goes before)
LEAP_EXIT = "とびこむで離脱"          # Current.RemoveGauInBattle
LEAP_SCRIPTS = {"sc_b_2026": ("SysCall", "プレイヤーの状況で戦闘終了")}
LEAP_CLEANUP = {"sc_b_2026": ["とりつくで離脱"]}   # the script's own end steps before its BattleEndEnabled 1
LEAP_TAG = TAG + "_leap"

# 2. Zozo: (PR script, the op after which the party is final, the ChangeMap's first iValue, SysCalls before it)
ZOZO = ("sc_e_0307_1", ("SysCall", "パーティ編成：オペラ入場"), 383,
        ["控えメンバーの装備を外す", "BNW:unequip 4"])
# 3. Terra's scenario: the party Terra 1, Edgar 5, Banon 15 (OrganizePartySlot [1, 1, 5, 15, -2])
TERRA = ("sc_e_0087", ("OrganizePartySlot", [1, 1, 5, 15]), 349,
         ["最強装備：ティナ", "最強装備：エドガー"])


def _read(out_dir, f):
    """The port's copy of a vanilla file when an earlier step wrote one, else vanilla's."""
    mine = os.path.join(out_dir, os.path.relpath(f, P.VANILLA))
    return json.load(open(mine if os.path.exists(mine) else f, encoding="utf-8"))


def _key(akeys, keys, f):
    rel = os.path.relpath(f, P.VANILLA).replace("\\", "/")
    group, value = rel.split("/", 1)
    value = os.path.splitext(value)[0]
    k = akeys.get((group, value))
    assert k, f"{rel}: no asset key"
    keys.setdefault(group, {})[k] = value


def _sv0(m):
    sv = m["operands"].get("sValues") or [""]
    return sv[0]


def _leads_to_leave(ms, target):
    """A jump to `target` goes on at target + 1: does that block reach the leave call before it ends?"""
    for m in ms[target + 1:]:
        if m["mnemonic"] == "SysCall" and _sv0(m) == LEAVE:
            return True
        if m["mnemonic"] in ("Exit", "Jump", "Return"):
            return False
    return False


def no_random_leave(out_dir, akeys, keys):
    """The lottery and the branch on its result: Nops in each battle script that has them."""
    done = []
    for name in LEAVE_SCRIPTS:
        f = os.path.join(BATTLE_DIR, name + ".json")
        sc = _read(out_dir, f)
        ms = sc["Mnemonics"]
        at = [n for n, m in enumerate(ms) if m["mnemonic"] == "SysCall" and _sv0(m) == LOTTERY]
        if not at:
            assert any(m.get("comment") == NO_LEAVE for m in ms), f"{name}: no {LOTTERY} to remove"
            _key(akeys, keys, f)   # a re-run on the port's own copy
            continue
        for n in at:
            br = ms[n + 1]
            assert br["mnemonic"] == "Branch" and _leads_to_leave(ms, br["operands"]["iValues"][2]), \
                f"{name}[{n + 1}]: the lottery isn't followed by its branch into the leave"
            for k in (n, n + 1):
                nop = Sc.op("Nop", label=ms[k]["label"], kind=ms[k].get("type", 1))
                nop["comment"] = NO_LEAVE
                ms[k] = nop
        Sc.save_path(sc, out_dir, f)
        _key(akeys, keys, f)
        done.append(name)
    return done


def _calls_before_map(out_dir, akeys, keys, spec, tag):
    """SysCalls run right before the ChangeMap that ends a scene: the ChangeMap's place jumps to an appended
    block of the calls, the ChangeMap and the scene's Exit (as pr_script.hook moves an op)."""
    name, (mn, want), map_id, calls = spec
    paths = Sc.all_paths(name)
    assert paths, f"{name}: no such PR script"
    for f in paths:
        sc = _read(out_dir, f)
        ms = sc["Mnemonics"]
        if any(m["label"] == tag for m in ms):
            _key(akeys, keys, f)   # a re-run on the port's own copy
            continue
        if mn == "SysCall":
            first = [n for n, m in enumerate(ms) if m["mnemonic"] == mn and _sv0(m) == want]
        else:
            first = [n for n, m in enumerate(ms) if m["mnemonic"] == mn and m["operands"]["iValues"][:len(want)] == want]
        assert len(first) == 1, f"{name}: {len(first)} {mn} {want} (the scene changed)"
        maps = [n for n in range(first[0] + 1, len(ms)) if ms[n]["mnemonic"] == "ChangeMap"]
        assert maps and ms[maps[0]]["operands"]["iValues"][0] == map_id, f"{name}: no ChangeMap [{map_id}] after {mn}"
        n = maps[0]
        # the scene ends there (ChangeMap, Exit): the block ends the same way, so nothing jumps back into
        # the scene's own code (event_translate.check: every target a labelled Nop)
        assert n + 1 < len(ms) and ms[n + 1]["mnemonic"] == "Exit", f"{name}[{n}]: the ChangeMap doesn't end the scene"
        moved = copy.deepcopy(ms[n])
        moved["label"] = ""
        ops = [Sc.op("SysCall", s=[c]) for c in calls] + [moved, Sc.op("Exit")]
        blk = Sc.append(sc, tag, ops)
        ms[n] = Sc.op("Jump", i=[blk], label=ms[n]["label"], kind=ms[n].get("type", 1))
        Sc.save_path(sc, out_dir, f)
        _key(akeys, keys, f)
    return name


def _flee_jumps(ms):
    """The stand-in's checks that jump to its "attacked" block (mode 3: Gau runs off)."""
    at = [n for n, m in enumerate(ms) if m["label"] == ATTACKED]
    assert len(at) == 1, f"{RETURN_AI}: {len(at)} {ATTACKED} labels"
    out = []
    for n, m in enumerate(ms):
        k = FLEE_JUMPS.get(m["mnemonic"])
        iv = m["operands"].get("iValues") or []
        if k is not None and len(iv) > k and iv[k] == at[0]:
            out.append(n)
    return out


def gau_stays(out_dir, akeys, keys):
    """sc_ai_549_Gau without its jumps to the "attacked" block: Nops. Returns how many went."""
    f = os.path.join(AI_DIR, RETURN_AI + ".json")
    sc = _read(out_dir, f)
    ms = sc["Mnemonics"]
    jumps = _flee_jumps(ms)
    if not jumps:
        assert any(m.get("comment") == NO_FLEE for m in ms), f"{RETURN_AI}: no jumps to {ATTACKED} to remove"
        _key(akeys, keys, f)   # a re-run on the port's own copy
        return 0
    assert len(jumps) == 11, f"{RETURN_AI}: {len(jumps)} jumps to {ATTACKED} (vanilla has 11: the script changed)"
    for n in jumps:
        nop = Sc.op("Nop", label=ms[n]["label"], kind=ms[n].get("type", 1))
        nop["comment"] = NO_FLEE
        ms[n] = nop
    Sc.save_path(sc, out_dir, f)
    _key(akeys, keys, f)
    return len(jumps)


def _leap_ops(name):
    """The Leap check: script-local 0 still 0, "とびこむで離脱", and if it took Gau out (local 0 = -1) the
    PR's RemoveGau end (sc_b_0239[29-37]) after the script's own clean-up; else on with the script."""
    br = lambda target: Sc.op("Branch", i=[0, -1, target], s=["ScriptLocalVariable", "＝", "imm"])
    return ([br("@vanilla"), Sc.op("SysCall", s=[LEAP_EXIT]), br("@leap"), Sc.op("Jump", i=["@vanilla"]),
             Sc.label("leap")]
            + [Sc.op("SysCall", s=[c]) for c in LEAP_CLEANUP.get(name, [])]
            + [Sc.op("BattleEndEnabled", i=[1]), Sc.op("Wait", r=[1.4]), Sc.op("BattlePause", i=[1]),
               Sc.op("SysCall", s=["RemoveAllAct"]), Sc.op("BattleForceExitOnSave"), Sc.op("Exit")])


def leap_ends(out_dir, akeys, keys):
    """Each of LEAP_SCRIPTS: its op becomes a Jump to the Leap check, which goes on with the op and the rest of
    its block. Returns the scripts done."""
    done = []
    for name, (mn, want) in sorted(LEAP_SCRIPTS.items()):
        f = os.path.join(BATTLE_DIR, name + ".json")
        sc = _read(out_dir, f)
        ms = sc["Mnemonics"]
        if any(m["label"] == LEAP_TAG for m in ms):
            _key(akeys, keys, f)   # a re-run on the port's own copy
            continue
        assert any(m["mnemonic"] == "BattleEndEnabled" and not m["operands"]["iValues"][0] for m in ms),             f"{name}: no BattleEndEnabled 0 (the script changed: is the check still needed?)"
        assert not any(m["mnemonic"] == "SysCall" and _sv0(m) == LEAP_EXIT for m in ms), f"{name}: already has {LEAP_EXIT}"
        at = [n for n, m in enumerate(ms) if m["mnemonic"] == mn and _sv0(m) == want]
        assert len(at) == 1, f"{name}: {len(at)} {mn} {want} (the script changed)"
        # the block carries on with the script's own code from the op to its next label (a jump target: the
        # checker wants every target a labelled Nop, so no jump back into the middle of the loop)
        n = at[0]
        nxt = next(k for k in range(n + 1, len(ms)) if ms[k]["mnemonic"] == "Nop" and ms[k]["label"])
        rest = Sc.copy_ops(sc, n, nxt) + [Sc.op("Jump", i=[nxt])]
        start = Sc.append(sc, LEAP_TAG, _leap_ops(name) + [Sc.label("vanilla")] + rest)
        ms[n] = Sc.op("Jump", i=[start], label=ms[n]["label"], kind=ms[n].get("type", 1))
        Sc.save_path(sc, out_dir, f)
        _key(akeys, keys, f)
        done.append(name)
    return done


def port(out_dir, log, profile=None):
    akeys, keys = C.asset_keys(), {}
    left = no_random_leave(out_dir, akeys, keys)
    zozo = _calls_before_map(out_dir, akeys, keys, ZOZO, TAG + "_zozo")
    terra = _calls_before_map(out_dir, akeys, keys, TERRA, TAG + "_terra")
    stays = gau_stays(out_dir, akeys, keys)
    leaps = leap_ends(out_dir, akeys, keys)
    for group, kv in keys.items():
        kdir = os.path.join(out_dir, group, "keys")
        os.makedirs(kdir, exist_ok=True)
        with open(os.path.join(kdir, TAG + ".json"), "w", encoding="utf-8") as fh:
            json.dump({"keys": list(kv), "values": list(kv.values())}, fh, indent=1)
    log(f"  party events: Shadow's random leave gone ({len(left)} battle scripts: {', '.join(left) or 'already'}), "
        f"Zozo unequips the bench and Shadow ({zozo}), Terra and Edgar optimized for the Lete River ({terra}), "
        f"Gau's return after a Leap can't be scared off ({stays or 'already'} checks in {RETURN_AI}), "
        f"a Leap ends the Veldt's Phantom Train fights ({', '.join(leaps) or 'already'})")
    return {}


def check(mod_dir):
    """Problems in a port's copies (tests/test_mod): the lottery gone, the two scenes' calls before their map
    change, every jump on a labelled Nop (event_translate.check), the keys listing every file."""
    import event_translate as T
    out = []
    keyed = {}
    for kf in [os.path.join(mod_dir, g, "keys", TAG + ".json") for g in os.listdir(mod_dir)
               if os.path.isfile(os.path.join(mod_dir, g, "keys", TAG + ".json"))]:
        doc = json.load(open(kf, encoding="utf-8"))
        keyed[os.path.basename(os.path.dirname(os.path.dirname(kf)))] = set(doc["values"])

    def mine(f):
        rel = os.path.relpath(f, P.VANILLA)
        p = os.path.join(mod_dir, rel)
        group, value = rel.replace("\\", "/").split("/", 1)
        if os.path.splitext(value)[0] not in keyed.get(group, ()):
            out.append(f"{rel}: not in {group}/keys/{TAG}.json")
        if not os.path.exists(p):
            out.append(f"{rel}: no copy in the mod")
            return None
        sc = json.load(open(p, encoding="utf-8"))
        out.extend(f"{rel}: {e}" for e in T.check(sc))
        return sc

    for name in LEAVE_SCRIPTS:
        sc = mine(os.path.join(BATTLE_DIR, name + ".json"))
        if sc and any(m["mnemonic"] == "SysCall" and _sv0(m) == LOTTERY for m in sc["Mnemonics"]):
            out.append(f"{name}: still rolls Shadow's leave")
    for spec, tag in ((ZOZO, TAG + "_zozo"), (TERRA, TAG + "_terra")):
        name, _, map_id, calls = spec
        for f in Sc.all_paths(name):
            sc = mine(f)
            if not sc:
                continue
            ms = sc["Mnemonics"]
            at = [n for n, m in enumerate(ms) if m["label"] == tag]
            if not at:
                out.append(f"{name}: no {tag} block")
                continue
            got = [_sv0(m) for m in ms[at[0] + 1:at[0] + 1 + len(calls)]]
            nxt = ms[at[0] + 1 + len(calls)]
            if got != calls or nxt["mnemonic"] != "ChangeMap" or nxt["operands"]["iValues"][0] != map_id:
                out.append(f"{name}: {tag} runs {got} then {nxt['mnemonic']}")
            if not any(m["mnemonic"] == "Jump" and m["operands"]["iValues"][0] == at[0] for m in ms[:at[0]]):
                out.append(f"{name}: nothing jumps to {tag}")
    for name in sorted(LEAP_SCRIPTS):
        sc = mine(os.path.join(BATTLE_DIR, name + ".json"))
        if not sc:
            continue
        ms = sc["Mnemonics"]
        at = [n for n, m in enumerate(ms) if m["label"] == LEAP_TAG]
        if not at:
            out.append(f"{name}: no {LEAP_TAG} block (a Leap can't end its fights)")
            continue
        blk = ms[at[0]:]
        if not any(m["mnemonic"] == "SysCall" and _sv0(m) == LEAP_EXIT for m in blk) or                 not any(m["mnemonic"] == "BattleForceExitOnSave" for m in blk):
            out.append(f"{name}: {LEAP_TAG} lacks {LEAP_EXIT} or its forced exit")
        if not any(m["mnemonic"] == "Jump" and m["operands"]["iValues"][0] == at[0] for m in ms[:at[0]]):
            out.append(f"{name}: nothing jumps to {LEAP_TAG}")
    f = os.path.join(AI_DIR, RETURN_AI + ".json")
    rel = os.path.relpath(f, P.VANILLA)
    p = os.path.join(mod_dir, rel)
    if RETURN_AI not in {os.path.basename(v) for v in keyed.get("monster_ai", ())}:
        out.append(f"{rel}: not in monster_ai/keys/{TAG}.json")
    if not os.path.exists(p):
        out.append(f"{rel}: no copy in the mod")
    else:
        ms = json.load(open(p, encoding="utf-8"))["Mnemonics"]
        if _flee_jumps(ms):
            out.append(f"{RETURN_AI}: Gau's return still runs off when attacked")
        if sum(m.get("comment") == NO_FLEE for m in ms) != 11:
            out.append(f"{RETURN_AI}: {sum(m.get('comment') == NO_FLEE for m in ms)} of 11 checks removed")
    return out
