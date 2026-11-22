using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BraveNewWorld.Rules;
using HarmonyLib;
using Last.Battle;
using Last.Data;
using Last.Data.Master;
using Last.Data.User;
using Last.Defaine;
using Last.Management;
using Last.Systems;
using Hooks = PrKit.Hooks;
using ItemSeq = Il2CppSystem.Collections.Generic.IEnumerable<Last.Data.User.OwnedItemData>;
using ItemList = Il2CppSystem.Collections.Generic.List<Last.Data.User.OwnedItemData>;

namespace BraveNewWorld.PR
{
    // Readme audit batch C (2026-11-21), the engine parts:
    //
    //  * Spears can't be dual-wielded (c3.asm DW_Chk: Item_Chk at C3/F13D; c1.asm SwapDual in battle;
    //    Rules/BattleExtrasRules.cs DualWieldRules). The PR lets a weapon into the off hand whenever the
    //    character has a TwoSwordStyle ability (OwnedCharacterData.HasTwoSwordStyle -> EquipUtility.IsEquipSlot,
    //    decompiled tools/decomp/bnw_batchc), and BNW's dual-wield weapons carry it (equip ability 252), so a
    //    dagger let a spear (or a second katana, or a plain sword next to a katana) into the other hand.
    //    Every equip list the game builds goes through ItemFilters.GetEquipItems (7 callers: the Equip menu,
    //    keyboard and touch, the battle's equip change, both "strongest equipment" pickers, IsChangeEquip);
    //    a postfix drops the weapons BNW wouldn't pair with the one in the other hand. Config [Battle]
    //    RelicRules (the dual wield and grip rules' switch).
    //
    //  * NoGainz and the Veldt (bnw 3.0/asm/noexp_overhaul.asm, in BNW's ROM at C2/5DAB): with the Config
    //    menu's "Exp. Gain" off, or in a battle on the Veldt, no EXP (and so no EP: XP/8 x SP), SP still
    //    banks, and the gil is doubled. The PR builds the result in BattleResultProvider.Genelate (EXP: a
    //    prefix after BattleRewards' raises it to 0; SP for those who would have shared the EXP is banked in
    //    the postfix, since EsperHooks banks only for characters with EXP) and pays the gil in
    //    BattleController.SaveGil from BattleAddExpPlug.GetGil (decompiled: not from the result's gil), so the
    //    doubling is a GetGil postfix (it also feeds the result screen through Genelate's gilValue).
    //    The Veldt: the PR's own test, BeastFieldEncount.IsBeastField() (Veldt ground under the walking party,
    //    the one that draws the fight from the Veldt table, EncounterLot.RunEncount). Config [Battle] NoGainz
    //    (off; the in-game Config row "Exp. Gain") and [Battle] VeldtRewards (on).
    static class BattleExtras
    {
        internal static ConfigEntry<bool> NoGainz, VeldtRewards;
        const int RowType = 7202;           // the Config row (BnwEncounters' Encounters is 7201)
        const int EquipAbilityFunction = 62;
        const int TwoSwordStyleType = 12, BothHandsType = 627;   // AbilityType (252, 251 in the port's data)
        const int UnarmedType = 2;           // the empty hand's weapon (Weapon.TypeId)

        public static void Apply(Harmony h, ConfigFile config)
        {
            NoGainz = config.Bind("Battle", "NoGainz", false,
                "BNW's NoGainz (the in-game Config menu's Exp. Gain row, Off): battles give no EXP and no EP (SP still " +
                "banks) and twice the gil.");
            VeldtRewards = config.Bind("Battle", "VeldtRewards", true,
                "As in BNW 3.0: battles on the Veldt give no EXP and no EP (SP still banks) and twice the gil.");

            Hooks.Patch(h, typeof(ItemFilters), "GetEquipItems", typeof(BattleExtras), postfix: nameof(EquipItems_Postfix));
            Hooks.Patch(h, typeof(OwnedCharacterData), "get_HasTwoSwordStyle", typeof(BattleExtras), postfix: nameof(TwoSword_Postfix));
            var gen = AccessTools.Method(typeof(BattleResultProvider), "Genelate");
            // after BattleRewards' prefix (it raises EXP to the defeated monsters' total)
            Hooks.Patch(h, gen, typeof(BattleExtras), prefix: nameof(Result_Prefix), postfix: nameof(Result_Postfix),
                priority: Priority.Last);
            Hooks.Patch(h, typeof(BattleAddExpPlug), "GetGil", typeof(BattleExtras), postfix: nameof(Gil_Postfix));

            PrKit.ConfigRows.Add(h, new PrKit.ConfigRow
            {
                Type = RowType, Key = "ExpGain", Name = "Exp. Gain",
                Help = "Off: battles give no EXP or EP (SP still banks) and twice the gil (BNW's NoGainz).",
                Get = () => !NoGainz.Value,
                Set = v =>
                {
                    if (NoGainz.Value == !v) return;
                    if (PrKit.TestRun.Running || FormulaProbe.Running) NoGainz.ConfigFile.SaveOnConfigSet = false;   // a test run saves nothing of the player's
                    NoGainz.Value = !v;
                    Plugin.L.LogInfo($"[rewards] Exp. Gain {(v ? "on" : "off (NoGainz)")}");
                },
            });
            Plugin.L.LogInfo($"[rewards] NoGainz {(NoGainz.Value ? "on" : "off")}, Veldt rule {(VeldtRewards.Value ? "on" : "off")}; " +
                             $"[equip] spear / dual-wield pairing {(RelicRules.Enabled ? "on" : "off")}");
        }

        // --- spears and dual wield -----------------------------------------------------------

        static readonly Dictionary<int, WeaponGrip> grips = new Dictionary<int, WeaponGrip>();
        static bool counted;

        internal static WeaponGrip GripOf(Weapon w)
        {
            if (w == null) return WeaponGrip.None;
            if (grips.TryGetValue(w.Id, out var known)) return known;
            bool twoHanded = false, dual = false;
            var mm = MasterManager.Instance;
            var g = mm?.GetData<FunctionGroup>(w.WearFunctionGroupId);
            if (g != null)
                foreach (var (fn, value) in new[] { (g.Function1Id, g.Function1Value1), (g.Function2Id, g.Function2Value1), (g.Function3Id, g.Function3Value1) })
                {
                    if (fn != EquipAbilityFunction) continue;
                    var a = mm.GetData<Ability>(value);
                    if (a == null) continue;
                    if (a.TypeId == TwoSwordStyleType) dual = true;
                    if (a.TypeId == BothHandsType) twoHanded = true;
                }
            var grip = DualWieldRules.Grip(twoHanded, dual);
            grips[w.Id] = grip;
            return grip;
        }

        // the weapon in a hand, null for an empty hand or a shield
        static Weapon HeldWeapon(OwnedCharacterData c, EquipSlotType slot)
        {
            var eq = c?.EquipmentData;
            if (eq == null || !eq.ContainsKey(slot)) return null;
            var w = eq[slot]?.Weapon;
            return w == null || w.TypeId == UnarmedType ? null : w;
        }

        // the startup line's numbers: BNW 3.0 has 7 spears, 28 dual-wield weapons, 8 with both bits
        static string Census()
        {
            var list = MasterManager.Instance?.GetList<Weapon>();
            if (list == null) return "no weapon list";
            int spear = 0, dual = 0, both = 0;
            foreach (var w in list.Values)
            {
                if (w == null) continue;
                switch (GripOf(w))
                {
                    case WeaponGrip.Spear: spear++; break;
                    case WeaponGrip.DualWield: dual++; break;
                    case WeaponGrip.Both: both++; break;
                }
            }
            return $"{spear} spears, {dual} dual-wield weapons, {both} with both (BNW 3.0: 7, 28, 8)";
        }

        // The game's own "may this character hold a second weapon" (OwnedCharacterData.HasTwoSwordStyle: an owned
        // ability of type 12) never sees a weapon's equip ability: the game puts no weapon ability in the character's
        // lists (probe bnwdual, laptop 2026-11-22: a Dirk or Boomerang in hand, EquipmentAbilitys [0 0 0], no type-12
        // ability, before and after UpdateEquipParameter). OwnedCharacterClient.SetEquip then empties the other hand
        // whenever a second weapon goes in (decompiled SetEquip#b1da50: neither two-handed nor a bow, and no
        // TwoSwordStyle), so a second Boomerang took the first one off (the user's Deck). BNW: dual wield is a weapon
        // property, so a dual-wield weapon (ability 252) in either hand answers yes; the pairing rule above still
        // decides which weapons the menus offer beside it.
        public static void TwoSword_Postfix(OwnedCharacterData __instance, ref bool __result)
        {
            if (__result || !RelicRules.Enabled || FormulaProbe.Active || __instance == null) return;
            try
            {
                foreach (var slot in new[] { EquipSlotType.Slot1, EquipSlotType.Slot2 })
                {
                    var g = GripOf(HeldWeapon(__instance, slot));
                    if (g == WeaponGrip.DualWield || g == WeaponGrip.Both) { __result = true; return; }
                }
            }
            catch (Exception e) { Hooks.WarnOnce("BattleExtras.TwoSword", e); }
        }

        public static void EquipItems_Postfix(EquipSlotType slot, OwnedCharacterData target, ref ItemSeq __result)
        {
            if (!RelicRules.Enabled || FormulaProbe.Active || __result == null || target == null) return;
            if (slot != EquipSlotType.Slot1 && slot != EquipSlotType.Slot2) return;
            try
            {
                if (!counted)
                {
                    counted = true;
                    Plugin.L.LogInfo("[equip] dual-wield pairing (BNW): " + Census());
                }
                var held = HeldWeapon(target, slot == EquipSlotType.Slot1 ? EquipSlotType.Slot2 : EquipSlotType.Slot1);
                if (held == null) return;   // the other hand is empty or holds a shield: the game's own list
                var heldGrip = GripOf(held);
                var kept = new ItemList();
                var dropped = new List<string>();
                var items = __result.GetEnumerator();
                var walk = items.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (walk.MoveNext())
                {
                    var item = items.Current;
                    var w = item?.Weapon;
                    if (w != null && w.TypeId != UnarmedType && !DualWieldRules.MayPair(heldGrip, GripOf(w)))
                    {
                        if (dropped.Count < 6) dropped.Add(item.Name);
                        else if (dropped.Count == 6) dropped.Add("...");
                        continue;
                    }
                    kept.Add(item);
                }
                if (dropped.Count == 0) return;   // the game's own sequence (a sorted view of its list: walking it again is fine)
                __result = kept.Cast<ItemSeq>();
                Hooks.NoteOnce($"pair-{target.characterStatusId}-{held.Id}-{(int)slot}", () =>
                    $"[equip] {target.Name}, hand {(int)slot} beside weapon {held.Id} ({heldGrip}): not offered {string.Join(", ", dropped)} (BNW: " +
                    (heldGrip == WeaponGrip.Spear ? "a spear takes no second weapon" :
                     heldGrip == WeaponGrip.DualWield ? "no spear beside a dual-wield weapon" : "only a dual-wield weapon beside it") + ")");
            }
            catch (Exception e) { Hooks.WarnOnce("BattleExtras.DualWield", e); }
        }

        // --- NoGainz and the Veldt -----------------------------------------------------------

        // the battle's party stands on the Veldt (the PR's own test: Veldt ground, on foot)
        internal static bool OnVeldt()
        {
            var beast = Last.Map.Encount.BeastFieldEncount.Instance();
            return beast != null && beast.IsBeastField() && beast.LotteryMonsterParty != null;
        }

        static bool NoExp(out bool veldt)
        {
            veldt = VeldtRewards != null && VeldtRewards.Value && OnVeldt();
            return RewardRules.NoExp(NoGainz != null && NoGainz.Value, veldt);
        }

        public static void Result_Prefix(BattleResultProvider __instance, ref int expValue, int gilValue, out bool __state)
        {
            __state = false;
            if (!Hooks.IsA<BattleResultProvider>(__instance)) return;
            try
            {
                if (!NoExp(out bool veldt)) return;
                __state = true;
                int before = expValue;
                expValue = 0;
                var battle = BattlePlugManager.Instance();
                Plugin.L.LogInfo($"[rewards] {(veldt ? "on the Veldt" : "NoGainz")}: EXP {before} -> 0, gil {gilValue} (doubled), SP still banks " +
                                 $"(formation {battle?.MonsterParty?.Id ?? -1})");
            }
            catch (Exception e) { Hooks.WarnOnce("BattleExtras.Exp", e); }
        }

        // SP for the characters who would have shared the EXP (BNW's Add_EP runs for each living character)
        public static void Result_Postfix(BattleResultData __result, bool __state)
        {
            if (!__state || __result == null || !EsperHooks.Levels || !EsperStore.Available) return;
            try
            {
                int sp = Math.Max(__result.GetMp, __result.GetAbp);
                if (sp <= 0) return;
                var banked = new List<string>();
                foreach (var c in __result.CharacterList)
                {
                    var after = c?.AfterData;
                    var before = c?.BeforData;
                    if (after == null || before?.Parameter == null || c.GetExp > 0) continue;
                    if (!BattleUtility.IsGetExp(before.Parameter)) continue;   // the game's test for sharing the EXP
                    var esper = EsperHooks.EsperOf(after);
                    if (esper == null) continue;
                    int sid = after.characterStatusId;
                    var p = EsperStore.Get(sid);
                    EsperRules.AfterBattle(EsperHooks.D, ref p, sid, true, 0, sp);
                    EsperStore.Put(sid, p);
                    banked.Add($"{after.Name} {p.SpBank}");
                }
                if (banked.Count > 0) Plugin.L.LogInfo($"[rewards] {sp} SP banked without EXP: {string.Join(", ", banked)}");
            }
            catch (Exception e) { Hooks.WarnOnce("BattleExtras.Sp", e); }
        }

        // the gil the party gets (SaveGil) and the result screen shows (Genelate's gilValue)
        public static void Gil_Postfix(BattleAddExpPlug __instance, ref int __result)
        {
            if (__result <= 0 || !Hooks.IsA<BattleAddExpPlug>(__instance)) return;
            try
            {
                if (!NoExp(out bool veldt)) return;
                int was = __result, now = RewardRules.Gil(was, NoGainz.Value, veldt);
                __result = now;
                Hooks.NoteOnce("gil-x2-" + (veldt ? "veldt" : "nogainz"), () =>
                    $"[rewards] {(veldt ? "on the Veldt" : "NoGainz")}: gil {was} -> {now} (BNW)");
            }
            catch (Exception e) { Hooks.WarnOnce("BattleExtras.Gil", e); }
        }
    }
}
