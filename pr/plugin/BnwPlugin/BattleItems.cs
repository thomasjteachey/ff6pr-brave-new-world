using System;
using System.Collections.Generic;
using BraveNewWorld.Rules;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Last.Battle;
using Last.Battle.Calc;
using Last.Data;
using Last.Data.Master;
using Last.Data.User;
using Last.Defaine;
using Last.Management;
using Last.Systems;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // The readme audit's battle items for v0.25.0 (the BNW lead, 2026-11-21; asm read, the PR's side decompiled into
    // tools/decomp/throw, throw2, golem_pali, golem2). Every hooked method's native code checked (none tiny or shared).
    //  1. Throw (readme 322-325): BNW's stars (item byte 14 = $61: Shuriken, Ninja Star) aim at one enemy or a group;
    //     knives stay single ($41). BNW's Throw sets $11A2 = $21 (physical, ignore defense: C2/2A44), not "no split", so
    //     a star thrown at several targets does split damage, which FF6 halves (C2/2B9x). The PR throws every weapon
    //     with one range (7, one of a side) and its Throw formula (CalcExecuteFF6.ThrowExecution) has no split: the
    //     target cursor's range becomes 6 for a star (both BattleTargetSelectControllers, as BC's Salve), and
    //     GetThrowStatus's damage is halved when a star's act has more than one target. NOT A GAP: Throw takes only
    //     knives, scrolls and stars (item byte 0 bit 4, the porter's throw_flag); scrolls are vanilla's all-enemies
    //     split spells (Tool_Data, spells $51-$53) and the PR already splits them.
    //  2. Golem (readme 2011-2024; c2.asm): the wall's HP is the caster's MAX HP (C2/3F67 LDA $3C1C; the PR copies
    //     current HP: UniqueUpdateParameterProvider "GOLEM" -> BattleFieldConditionController.Add(Golem, DataA)), and it
    //     blocks with Interceptor's 50% (C2/2282-22C3; the PR's BattleUtility.IsDefencedGolem blocks every physical hit
    //     that lands). PR-native: Golem's own animation (HitType.Golem), physical hits only. Left as the PR has it
    //     (unmeasured): which of the protectee's Safe / row / element bonuses the wall's damage keeps (BNW: none,
    //     defense 192, c2.asm 616-645), and the PR's block coming after the hit roll (BNW's before evasion).
    //  3. Palidor (readme 2026-2036): the porter lost Palidor's Jump (profile_bnw function_overrides 103 puts vanilla's
    //     Jump + landing act back). Open, for a probe: riders landing together and keeping their ATB (c0.asm
    //     WaitTimer's fixed $C8, C2/11D4 GetATurn), Hide on the riders (c2.asm PaliHide).
    //  4. MP-critical procs (c4.asm SpellCastId 630-642, CastTarget 644-662): after an MP-critical rod swing, Doom's
    //     proc becomes X-Zone and Quartr / X-Zone hit every enemy; a "never critical" spell (Holy, Flare, Quartr: spell
    //     byte 7 bit 5) keeps its normal damage. The PR already doubles every MP-crit proc (BattleActData
    //     .IsCriticalMagic, set before the proc is queued: plan_weapons 2.7 is PR-NATIVE); here, in the queued proc's
    //     BattleActExection.SetBattleActData prefix, the swap, the targets and the no-crit.
    // Gated on [Battle] ItemEffects (1, 4) and DamageRules (2).
    static class BattleItems
    {
        internal static bool Items = true, Damage = true;
        static BnwData D => EsperHooks.D;
        static readonly Random Rng = new Random();
        const int ThrowCommand = 13;
        internal static int Widened, Halved, WallsSet, GolemRolls, GolemBlocks, Swapped, Spread, Uncrit;

        public static void Apply(Harmony h, bool items, bool damage)
        {
            Items = items;
            Damage = damage;
            int n = 0, of = 0;
            void P(bool ok) { of++; if (ok) n++; }
            // 1
            foreach (var t in new[] { typeof(Last.UI.Touch.BattleTargetSelectController), typeof(Last.UI.KeyInput.BattleTargetSelectController) })
            {
                P(Hooks.Patch(h, t, "SetCommandData", typeof(BattleItems), prefix: nameof(Item_Prefix), args: new[] { typeof(Command), typeof(OwnedItemData) }));
                P(Hooks.Patch(h, t, "SetCommandData", typeof(BattleItems), prefix: nameof(Ability_Prefix), args: new[] { typeof(Command), typeof(Ability) }));
                P(Hooks.Patch(h, t, "SetDefaultTarget", typeof(BattleItems), prefix: nameof(Range_Prefix)));
            }
            P(Hooks.Patch(h, typeof(CalcControllerProvider), "GetThrowStatus", typeof(BattleItems), postfix: nameof(Throw_Postfix)));
            // 2
            P(Hooks.Patch(h, typeof(BattleFieldConditionController), "Add", typeof(BattleItems), prefix: nameof(Field_Prefix),
                          args: new[] { typeof(FieldConditionType), typeof(FieldConditionData) }));
            P(Hooks.Patch(h, typeof(BattleUtility), "IsDefencedGolem", typeof(BattleItems), postfix: nameof(Golem_Postfix)));
            // 4
            P(Hooks.Patch(h, typeof(BattleActExection), "SetBattleActData", typeof(BattleItems), prefix: nameof(Proc_Prefix),
                          args: new[] { typeof(BattleActData), typeof(BattleActData), typeof(bool) }));
            Plugin.L.LogInfo($"[items] {n} of {of} hooks: spread stars {D?.Procs.ThrowSpread.Count}, Golem (max HP, 50%), MP-crit procs " +
                             $"(swap {D?.Procs.CritSwap.Count}, all enemies {D?.Procs.CritAllEnemies.Count}, no crit {D?.Procs.NoCrit.Count})");
        }

        static BattleActData Staging() => BattlePlugManager.Instance()?.BattleActExection?.InStagingBattleActData;

        // --- 1. Throw: stars spread, and split --------------------------------------------------------------------

        static int pendingItem, pendingCommand;

        public static void Item_Prefix(Command __0, OwnedItemData __1)
        {
            pendingCommand = __0?.Id ?? 0;
            pendingItem = __1?.ContentId ?? 0;
        }

        public static void Ability_Prefix() { pendingCommand = 0; pendingItem = 0; }

        public static void Range_Prefix(ref Last.Defaine.Master.AbilityRangeType __0)
        {
            if (!Items || D == null || pendingCommand != ThrowCommand || pendingItem <= 0) return;
            try
            {
                if ((int)__0 != 7 || !D.Procs.ThrowSpread.Contains(pendingItem)) return;   // 7: one of a side
                __0 = (Last.Defaine.Master.AbilityRangeType)6;                              // 6: one or all of a side
                Widened++;
                int item = pendingItem;
                Hooks.NoteOnce("items-star", () => $"[items] Throw: star {item} may be spread over a group (BNW's targeting $61)");
            }
            catch (Exception e) { Hooks.WarnOnce("BattleItems.Range", e); }
        }

        public static void Throw_Postfix(CalcControllerProvider __instance, ICalcResult __result)
        {
            if (!Items || D == null || __instance == null || __result == null) return;
            try
            {
                int content = __instance.item?.ContentId ?? 0;
                if (!D.Procs.ThrowSpread.Contains(content)) return;
                var ht = __result.GetHitType();
                if (ht != HitType.Hit && ht != HitType.Critical) return;
                int targets = Staging()?.TargetUnitDatas?.Length ?? 1;
                if (targets <= 1) return;
                int was = __result.GetValue(false), now = Math.Max(1, was / 2);
                __result.SetValue(now);
                Halved++;
                Hooks.NoteOnce("items-star-split", () => $"[items] Throw: a star spread over {targets} targets splits: {was} -> {now}");
            }
            catch (Exception e) { Hooks.WarnOnce("BattleItems.Throw", e); }
        }

        // --- 2. Golem -----------------------------------------------------------------------------------------------

        public static void Field_Prefix(FieldConditionType __0, FieldConditionData __1)
        {
            if (!Damage || __0 != FieldConditionType.Golem || __1 == null) return;
            try
            {
                var caster = Staging()?.AttackUnitData?.BattleUnitDataInfo?.Parameter;
                if (caster == null) return;
                int was = __1.DataA, max = caster.ConfirmedMaxHp();
                if (max <= 0 || max == was) return;
                __1.DataA = max;
                WallsSet++;
                Hooks.NoteOnce("items-golem-hp", () => $"[items] Golem: the wall's HP {was} -> {max}, the caster's max HP (BNW)");
            }
            catch (Exception e) { Hooks.WarnOnce("BattleItems.GolemHp", e); }
        }

        // one 50% roll per act and target (the game asks several times a hit)
        static readonly Dictionary<(IntPtr, IntPtr), bool> golemRoll = new Dictionary<(IntPtr, IntPtr), bool>();
        static int golemFrame = -1;

        public static void Golem_Postfix(CharacterParameterBase __0, ref bool __result)
        {
            if (!__result || !Damage || __0 == null) return;
            try
            {
                var act = Staging();
                int f = UnityEngine.Time.frameCount;
                if (f != golemFrame) { golemRoll.Clear(); golemFrame = f; }
                var key = (act?.Pointer ?? IntPtr.Zero, __0.Pointer);
                if (!golemRoll.TryGetValue(key, out bool block))
                {
                    block = Rng.Next(2) == 0;   // Interceptor's check (C2/2282 ff.)
                    golemRoll[key] = block;
                    GolemRolls++;
                    if (block) GolemBlocks++;
                    Hooks.NoteOnce("items-golem-roll", () => "[items] Golem blocks with Interceptor's 50% (BNW)");
                }
                if (!block) __result = false;
            }
            catch (Exception e) { Hooks.WarnOnce("BattleItems.Golem", e); }
        }

        // --- 4. MP-critical procs ---------------------------------------------------------------------------------

        public static void Proc_Prefix(BattleActData __1, bool __2)
        {
            if (!__2 || !Items || D == null || __1 == null) return;
            try
            {
                if (!__1.IsCriticalMagic) return;   // the swing paid an MP crit (the PR set it on the proc)
                var list = __1.abilityList;
                if (list == null || list.Count == 0 || list[0] == null) return;
                int id = list[0].Id;
                if (D.Procs.CritSwap.TryGetValue(id, out int to))
                {
                    var ab = MasterManager.Instance?.GetData<Ability>(to);
                    if (ab != null)
                    {
                        list[0] = ab;
                        Swapped++;
                        int was = id;
                        Hooks.NoteOnce("items-xzone", () => $"[items] MP crit: proc {was} (Doom) -> {to} (X-Zone)");
                        id = to;
                    }
                }
                if (D.Procs.CritAllEnemies.Contains(id))
                {
                    var enemies = new List<BattleUnitData>();
                    var battle = BattlePlugManager.Instance();
                    if (battle != null)
                        foreach (var u in battle.GetEnemyUnits())
                        {
                            var p = u?.BattleUnitDataInfo?.Parameter;
                            if (p != null && p.CurrentHP > 0) enemies.Add(u);
                        }
                    if (enemies.Count > 0)
                    {
                        var arr = new Il2CppReferenceArray<BattleUnitData>(enemies.Count);
                        for (int i = 0; i < enemies.Count; i++) arr[i] = enemies[i];
                        __1.TargetUnitDatas = arr;
                        Spread++;
                        int n = enemies.Count, pid = id;
                        Hooks.NoteOnce("items-crit-all", () => $"[items] MP crit: proc {pid} hits all {n} enemies");
                    }
                }
                if (D.Procs.NoCrit.Contains(id))
                {
                    __1.IsCriticalMagic = false;   // "never critical": the PR would double it
                    Uncrit++;
                    int pid = id;
                    Hooks.NoteOnce("items-nocrit", () => $"[items] MP crit: proc {pid} is never critical (BNW's spell flag)");
                }
            }
            catch (Exception e) { Hooks.WarnOnce("BattleItems.Proc", e); }
        }
    }
}
