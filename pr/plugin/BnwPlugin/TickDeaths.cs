using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Last.Battle;
using Last.Battle.Function;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW readme: "Death by sap/poison/counter-attack no longer prevents the dying attack of 'Giant' enemies"
    // (Bropedio, double-counter.asm DeathCounterFix: an entity that died since its last reactive script always gets
    // it). The PR raises a monster's final attack (FinalAttackAct, where the kit's AI host runs its SNES death script)
    // only for deaths an act deals: CounterModule.SetFinalAttack files (act, unit, calc result) and ExecuteCounter
    // hands it to the unit's controller. A Poison or Sap tick that takes a monster to 0 HP has no act, so it skipped
    // the death script (probe 2026-11-25: the Giant, party 103, died to Poison with no "enemy AI death" line), and
    // BattleActExection.ForceSetFinalAttack can't help (it reads the current act's function group: an NRE).
    // Decompiled (tools/decomp/audit_counter, ai_vm): ExecuteCounter ends in the controller's
    // ActSelectFinalAttack(targets, users, hitType, ability, item), which for a unit that can't fight (and has no
    // status in its notFinalAttackConditionTypes) raises ActSelectEnemy.isReadyFinalAttack; the monster's next AI pass
    // then runs FinalAttackAct. A tick death calls that step itself (the monster as its own "user", no ability).
    // Config: [Battle] DamageRules.
    static class TickDeaths
    {
        internal static bool Enabled = true;
        internal static int Forced;
        internal static string Last = "";
        [ThreadStatic] static int hpBefore;
        static readonly HashSet<int> done = new HashSet<int>();   // unique ids this battle

        // out of battle: the next battle starts with none done (Plugin's frame loop)
        public static void Track()
        {
            var battle = BattlePlugManager.Instance();
            if ((battle == null || !battle.IsBattle()) && done.Count > 0) done.Clear();
        }

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            Hooks.Patch(h, typeof(PoisonConditionFunction), "UpdateSub", typeof(TickDeaths), prefix: nameof(Poison_Prefix), postfix: nameof(Poison_Postfix));
            Hooks.Patch(h, typeof(PoisonConditionFunction), "ActEndUpdateSub", typeof(TickDeaths), prefix: nameof(Poison_Prefix), postfix: nameof(Poison_Postfix));
            Hooks.Patch(h, typeof(SlipConditionFunction), "Update", typeof(TickDeaths), prefix: nameof(Sap_Prefix), postfix: nameof(Sap_Postfix));
        }

        static int Hp(BattleUnitData u) => u?.BattleUnitDataInfo?.Parameter?.CurrentHP ?? 0;

        public static void Poison_Prefix(PoisonConditionFunction __instance) => hpBefore = Before(__instance?.BattleUnitData);
        public static void Poison_Postfix(PoisonConditionFunction __instance) => After(__instance?.BattleUnitData, "Poison");
        public static void Sap_Prefix(SlipConditionFunction __instance) => hpBefore = Before(__instance?.BattleUnitData);
        public static void Sap_Postfix(SlipConditionFunction __instance) => After(__instance?.BattleUnitData, "Sap");

        static int Before(BattleUnitData u)
        {
            try { return Enabled && u != null && u.TryCast<BattleEnemyData>() != null ? Hp(u) : 0; }
            catch (Exception e) { Hooks.WarnOnce("TickDeaths.before", e); return 0; }
        }

        static void After(BattleUnitData u, string what)
        {
            if (hpBefore <= 0 || u == null) return;
            try
            {
                if (Hp(u) > 0) return;
                var ed = u.TryCast<BattleEnemyData>();
                if (ed == null) return;
                if (!done.Add(ed.UniqueId)) return;
                var c = u.BattleUnitDataInfo?.ActSelectController?.TryCast<ActSelectEnemyController>();
                if (c == null) return;
                bool was = c.IsReadyActFinalAttack();
                if (!was) c.ActSelectFinalAttack(new Il2CppReferenceArray<BattleUnitData>(0), u, global::Last.Systems.HitType.Hit, null, null);
                Forced++;
                var id = ed.MonsterMaster?.Id ?? -1;
                Last = $"monster {id} (unique {ed.UniqueId}) died to {what} (HP {hpBefore} -> 0): final attack "
                     + (was ? "already raised" : c.IsReadyActFinalAttack() ? "raised" : "NOT raised (a status blocks it)");
                PrKit.Kit.Info("[audit] tick death: " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("TickDeaths.after", e); }
            finally { hpBefore = 0; }
        }
    }
}
