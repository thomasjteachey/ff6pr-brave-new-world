using System;
using Last.Defaine;
using PrKit.Ff6;   // SnesHitRates: the PR's hit-rate status lists

namespace BraveNewWorld.PR
{
    // BNW's physical hit rate against statuses (Synchysi's evade.asm; c2.asm C2235E, C22372).
    // Vanilla raises a physical attack's hit rate x1.25 against a target with Blind, Zombie,
    // Muddle, Slow or Rerise (C2/235B), and lowers it x0.75 against Poison, Near Fatal, Seizure or
    // Haste (C2/2372). BNW keeps the first list without Rerise ("PEA $0004": Slow only in that byte
    // pair) and skips the second ("BRA HitCalc"). The PR has the same two lists (plus Charm in the
    // first, which the SNES doesn't have): the kit's SnesHitRates takes Rerise and Charm out of the
    // first and empties the second. Config: [Battle] DamageRules.
    static class HitRules
    {
        internal static bool Enabled = true;

        internal static string Fix()
        {
            if (!Enabled) return null;
            var minus = new[] { ConditionType.Poison, ConditionType.Dying, ConditionType.Slip, ConditionType.Heist };
            var plus = new[] { ConditionType.Reraise, ConditionType.Temptation };
            // everything the PR has in the x0.75 list goes (its four, and any other)
            var all = CalcExecuteFF6Minus();
            return SnesHitRates.Fix("BNW", plus, all ?? minus);
        }

        static ConditionType[] CalcExecuteFF6Minus()
        {
            var a = Last.Battle.CalcExecuteFF6.HitRateMinusConditions;
            if (a == null) return null;
            var r = new ConditionType[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i];
            return r;
        }
    }
}
