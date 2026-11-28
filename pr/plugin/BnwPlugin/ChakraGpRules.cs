using System;
using HarmonyLib;
using Last.Battle;
using Last.Data;
using Last.Systems;
using Last.Battle.Function;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // Two BNW command rules the audit found (2026-11-21; plan from c2.asm):
    //  * Chakra (Sabin's Blitz, PR ability 137, Spiraler's old slot): restores MP = (Stamina + Level) / 2 to each ally but
    //    Sabin, no HP (c2.asm 3285-3293, 3343-3348). The porter gives 137 an MP recovery (profile_bnw: process_prog None,
    //    function 1 (1,1,3,0) as an Ether's); here the amount, and the caster's own result is a miss.
    //  * GP Rain (Setzer's gil toss) costs Stamina x 10 GP, at most what the party has (c2.asm 5336-5385); the PR charges
    //    Level x 30 through BattleAddExpPlug.AddPlayerSideLostGil (UniqueUpdateParameterProvider.HitCalcGilThrow, inside
    //    BattleFunction23.HitCalc). That method's native code is 4 shared bytes (`add [rcx+1Ch], edx; ret`: playerLostGil
    //    += lostGil), which can't be hooked (QA log 2026-11-21), so the field is read before and after the HitCalc and
    //    what it gained becomes BNW's cost. Its damage formula is CommandRules' (Stamina x 10 x Level / (2 x (targets + 1))).
    // Config: [Battle] DamageRules.
    static class ChakraGpRules
    {
        internal static bool Enabled = true;
        internal const int Chakra = 137;
        [ThreadStatic] static int gpThrowerStamina;   // > 0 while a character's GP Rain is computed
        [ThreadStatic] static int lostBefore;          // BattleAddExpPlug.playerLostGil before that HitCalc
        internal static string Last = "";

        // Every recovery calc the PR has: 137's function 1 (1,1,3,0) isn't known to go through the two ItemHealRules
        // hooks (the user 2026-11-28: Gau's Scrapper rage Chakra healed everyone for 1 = 137's standard_value, so
        // the rule never ran). The postfix is idempotent if one of these calls another.
        static readonly (string Name, Type[] Args)[] RecoveryCalcs =
        {
            ("GetRecoveryStatus", Type.EmptyTypes),
            ("GetFixedRecoveryStatus", Type.EmptyTypes),
            ("GetFixedRecoveryStatus", new[] { typeof(int) }),
            ("GetFixedBaseValueRecoveryStatus", new[] { typeof(int) }),
            ("GetFixedResultValueRecoveryStatus", new[] { typeof(int) }),
        };

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            var calc = typeof(CalcControllerProvider);
            foreach (var (m, args) in RecoveryCalcs)
                Hooks.Patch(h, calc, m, typeof(ChakraGpRules), postfix: nameof(Chakra_Postfix), args: args);
            Hooks.Patch(h, typeof(BattleFunction23), "HitCalc", typeof(ChakraGpRules), prefix: nameof(Unique_Prefix), postfix: nameof(Unique_Postfix));
            PrKit.Harness.Register("chakra", _ => Trace.Count == 0 ? "chakra: no recovery calc seen" : "chakra: " + string.Join(" | ", Trace),
                "chakra: the last recovery calcs ChakraGpRules saw (ability, item, hit, the PR's value -> BNW's)");
        }

        // the last recovery results seen (harness "chakra"): what reaches the hook, Chakra or not
        static readonly System.Collections.Generic.List<string> Trace = new System.Collections.Generic.List<string>();
        static void Note(string s) { lock (Trace) { Trace.Add(s); if (Trace.Count > 12) Trace.RemoveAt(0); } }

        static int Stamina(CharacterParameterBase p) =>
            p.TryCast<PlayerCharacterParameter>() != null ? Math.Clamp(p.ConfirmedVitality(), 0, 255) : BattleFormulas.MonsterStamina(p.ConfirmedMaxHp());

        public static void Chakra_Postfix(CalcControllerProvider __instance, ICalcResult __result)
        {
            if (!Enabled || __instance == null || __result == null) return;
            try
            {
                int ab = __instance.ability?.Id ?? 0;
                var hit = __result.GetHitType();
                string seen = $"ability {ab} item {__instance.item?.Id ?? 0} {hit} {__result.GetValue(false)}";
                if (ab != Chakra || __instance.item != null) { Note(seen); return; }
                if (hit != HitType.Recovery) { Note(seen + " (not a recovery)"); return; }
                var a = __instance.attack;
                var t = __instance.target;
                if (a == null || t == null) { Note(seen + " (no attacker/target)"); return; }
                if (a.Pointer == t.Pointer)
                {
                    __result.SetStatus(0, HitType.Miss, __result.GetHitCount(), __result.GetConditions(), false);
                    Note(seen + " -> caster: miss");
                    return;
                }
                int mp = Math.Clamp((Stamina(a) + a.ConfirmedLevel()) / 2, 1, 999);
                int was = __result.GetValue(false);
                __result.SetValue(mp);
                Last = $"chakra: the PR's {was} -> BNW's {mp} MP (Stamina {Stamina(a)}, Lv {a.ConfirmedLevel()})";
                Note(seen + " -> " + mp);
                Hooks.NoteOnce("bnw-chakra", () => "[commands] " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("ChakraGpRules.Chakra", e); }
        }

        public static void Unique_Prefix(BattleFunction23 __instance)
        {
            gpThrowerStamina = 0;
            if (!Enabled || __instance == null) return;
            try
            {
                var act = __instance.battleActData;
                var abilities = act?.abilityList;
                if (abilities == null || abilities.Count == 0 || abilities[0]?.ProcessProg != "GIL_THROW") return;
                var thrower = act.AttackUnitData;
                if (thrower?.TryCast<BattlePlayerData>() == null) return;
                var p = thrower.BattleUnitDataInfo?.Parameter;
                var plug = BattlePlugManager.Instance()?.BattleAddExpPlug;
                if (p == null || plug == null) return;
                gpThrowerStamina = Math.Max(1, Stamina(p));
                lostBefore = plug.playerLostGil;
            }
            catch (Exception e) { gpThrowerStamina = 0; Hooks.WarnOnce("ChakraGpRules.Unique", e); }
        }

        // the PR added min(Level x 30, gil) to playerLostGil; BNW's cost is min(Stamina x 10, gil)
        public static void Unique_Postfix()
        {
            int sta = gpThrowerStamina;
            gpThrowerStamina = 0;
            if (!Enabled || sta <= 0) return;
            try
            {
                var plug = BattlePlugManager.Instance()?.BattleAddExpPlug;
                if (plug == null) return;
                int added = plug.playerLostGil - lostBefore;
                if (added <= 0) return;
                int gil = global::Last.Management.UserDataManager.Instance()?.OwendGil ?? int.MaxValue;
                int want = Math.Min(sta * 10, Math.Max(0, gil - lostBefore));
                plug.playerLostGil = lostBefore + want;
                Last = $"gp rain: the PR's cost {added} -> BNW's {want} (Stamina {sta}, gil {gil})";
                Hooks.NoteOnce("bnw-gp-cost", () => "[commands] " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("ChakraGpRules.LostGil", e); }
        }
    }
}
