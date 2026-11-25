using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Last.Battle;
using Last.Battle.Function;
using Last.Data;
using Last.Defaine;
using Last.Systems;
using PrKit;
using FunctionProvider = Serial.FF6.Calc.FunctionProvider;

namespace BraveNewWorld.PR
{
    // BNW's Revive And Heal (readme 2076-2078, Bropedio): the Kirin summon, the 7-7-7 Slots spin and Heal Force heal
    // living allies AND revive dead ones (vanilla: a command targets the living or the dead, never both). The PR does
    // the same as vanilla (probes 2026-11-25, build/batches/bnw-probes.json, the kit's act/units harness): Heal Force
    // aimed at a KO'd Terra alone was retargeted to the living Locke (10 -> 186 HP), Terra stayed KO'd; Jackpot (216)
    // revived Terra (0 -> 112 HP) and left Locke at 10.
    // Here, for a party member's act of one of these abilities (FunctionProvider.CreateActFunction): the party's HP
    // is noted when the act starts; when it ends, the largest HP gain it gave an ally (or a recovery value its calcs
    // reported, CalcControllerProvider.GetRecoveryStatus, if larger: a full-HP ally hides its gain) goes to each party
    // member the act didn't reach: a KO'd one is revived (UnableFight lifted) with that much HP, a hurt one healed by
    // it (capped at max HP). Revive-and-heal is the spell's one effect on every ally, so the amount is the same.
    // Petrified and Zombie allies are left alone (the PR's own functions decide those). Config [Battle] DamageRules.
    static class ReviveHealRules
    {
        internal static bool Enabled = true;
        // PR ability ids: Kirin (esper summon), Heal Force, Jackpot (the 7-7-7 party spin; port_slots SEVENS_PARTY)
        internal static readonly HashSet<int> Abilities = new HashSet<int> { 89, 302, 216 };
        sealed class Pending
        {
            public int Ability;
            public IntPtr Act;
            public bool Seen;           // staged once (the act ran)
            public BattleUnitData Caster;
            public readonly Dictionary<IntPtr, int> Reached = new Dictionary<IntPtr, int>();   // unit -> recovery value
            public readonly Dictionary<IntPtr, int> Before = new Dictionary<IntPtr, int>();    // unit -> HP at the start
        }
        static readonly Dictionary<IntPtr, Pending> acts = new Dictionary<IntPtr, Pending>();   // caster -> its act
        internal static int Revived, Healed;   // tests and the log

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            Hooks.Patch(h, typeof(FunctionProvider), "CreateActFunction", typeof(ReviveHealRules), prefix: nameof(Act_Prefix));
            Hooks.Patch(h, typeof(CalcControllerProvider), "GetRecoveryStatus", typeof(ReviveHealRules), postfix: nameof(Recovery_Postfix));
            Hooks.Patch(h, typeof(BattleProgressATB), "ActExectionEndDelegate", typeof(ReviveHealRules), postfix: nameof(ActEnd_Postfix));
            // an act that isn't staged any more has ended, counters and interrupts too (ActExectionEndDelegate skips
            // those: the probes' acts). Not BattleActExection.EndFinishingState: a detour there froze battles in their
            // opening (2026-11-25).
            Hooks.Patch(h, AccessTools.DeclaredMethod(typeof(BattleProgressATBFF6), "Update"), typeof(ReviveHealRules), postfix: nameof(Frame_Postfix));
        }

        public static void Act_Prefix(BattleActData battleActData)
        {
            try
            {
                var who = battleActData?.AttackUnitData;
                var abilities = battleActData?.abilityList;
                if (who == null || abilities == null || abilities.Count == 0 || who.TryCast<BattlePlayerData>() == null) return;
                int id = abilities[0]?.Id ?? 0;
                if (!Abilities.Contains(id)) return;
                var p = new Pending { Ability = id, Act = battleActData.Pointer, Caster = who };
                foreach (var u in BattlePlugManager.Instance()?.GetPlayerUnits() ?? Enumerable.Empty<BattleUnitData>())
                {
                    var par = u?.BattleUnitDataInfo?.Parameter;
                    if (par != null) p.Before[u.Pointer] = Has(u, ConditionType.UnableFight) ? -1 : par.CurrentHP;
                }
                acts[who.Pointer] = p;
            }
            catch (Exception e) { Hooks.WarnOnce("ReviveHeal.Act", e); }
        }

        public static void Recovery_Postfix(CalcControllerProvider __instance, ICalcResult __result)
        {
            try
            {
                if (acts.Count == 0 || __instance == null || __result == null) return;
                int id = __instance.ability?.Id ?? 0;
                if (!Abilities.Contains(id)) return;
                var p = acts.Values.FirstOrDefault(x => x.Ability == id);
                var target = __instance.GetUnitData(__instance.target);
                if (p == null || target == null) return;
                int v = __result.GetValue(false);
                if (!p.Reached.TryGetValue(target.Pointer, out int had) || v > had) p.Reached[target.Pointer] = v;
            }
            catch (Exception e) { Hooks.WarnOnce("ReviveHeal.Recovery", e); }
        }

        static bool Has(BattleUnitData u, ConditionType t)
        {
            var l = u?.BattleUnitDataInfo?.GetConfirmedConditionType();
            return l != null && l.Contains(t);
        }

        public static void Frame_Postfix()
        {
            if (acts.Count == 0) return;
            try
            {
                var staged = BattlePlugManager.Instance()?.BattleActExection?.InStagingBattleActData;
                foreach (var key in acts.Keys.ToList())
                {
                    var p = acts[key];
                    if (staged != null && staged.Pointer == p.Act) { p.Seen = true; continue; }
                    if (p.Seen) ActEnd_Postfix(p.Caster);
                }
            }
            catch (Exception e) { Hooks.WarnOnce("ReviveHeal.Frame", e); }
        }

        public static void ActEnd_Postfix(BattleUnitData battleUnitData)
        {
            try
            {
                if (battleUnitData == null || !acts.TryGetValue(battleUnitData.Pointer, out var p)) return;
                acts.Remove(battleUnitData.Pointer);
                var bm = BattlePlugManager.Instance();
                var cc = bm?.BattleConditionController;
                if (bm == null || cc == null) return;
                // the act's own gains: an ally whose HP rose (a KO'd one counts from 0) was reached
                foreach (var u in bm.GetPlayerUnits())
                {
                    var par = u?.BattleUnitDataInfo?.Parameter;
                    if (par == null || !p.Before.TryGetValue(u.Pointer, out int was)) continue;
                    int gain = par.CurrentHP - Math.Max(0, was);
                    bool up = was < 0 ? !Has(u, ConditionType.UnableFight) : gain > 0;
                    if (up && (!p.Reached.TryGetValue(u.Pointer, out int r) || gain > r)) p.Reached[u.Pointer] = gain;
                }
                int amount = p.Reached.Count == 0 ? 0 : p.Reached.Values.Max();
                int reached = p.Reached.Count, ab = p.Ability;
                Hooks.NoteOnce("revive-heal-" + ab, () => $"[revive-heal] ability {ab}: the act reached {reached} ({amount} HP)");
                if (amount <= 0) return;
                var done = new List<string>();
                foreach (var u in bm.GetPlayerUnits())
                {
                    var par = u?.BattleUnitDataInfo?.Parameter;
                    if (par == null || p.Reached.ContainsKey(u.Pointer) || !p.Before.ContainsKey(u.Pointer)) continue;
                    if (Has(u, ConditionType.Mineralization) || Has(u, ConditionType.Zombie)) continue;
                    int max = par.ConfirmedMaxHp();
                    bool dead = par.CurrentHP <= 0 || Has(u, ConditionType.UnableFight);
                    if (dead)
                    {
                        cc.InterruptRemoveCondition(u, ConditionType.UnableFight);
                        par.CurrentHP = Math.Min(amount, max);
                        Revived++;
                        done.Add($"{HarnessName(u)} revived with {par.CurrentHP}");
                    }
                    else if (par.CurrentHP < max)
                    {
                        par.CurrentHP = Math.Min(par.CurrentHP + amount, max);
                        Healed++;
                        done.Add($"{HarnessName(u)} healed to {par.CurrentHP}");
                    }
                }
                if (done.Count > 0)
                    Kit.Info($"[revive-heal] ability {p.Ability} ({amount} HP): {string.Join(", ", done)}");
            }
            catch (Exception e) { Hooks.WarnOnce("ReviveHeal.ActEnd", e); }
        }

        static string HarnessName(BattleUnitData u) => PrKit.Ff6.HarnessBattle.Name(u);
    }
}
