using System;
using HarmonyLib;
using Last.Battle;
using Last.Systems;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW's fractional healing items (readme "Fractional Items"; its descriptions): Tonic cures 1/2 of max HP,
    // Potion 3/4, X-Potion 3/4 (party), Hi-Ether 3/4 of max MP, X-Ether 3/4 (party); Ether stays 50 MP, Elixir
    // and Megalixir full (their status cures are data: profile_bnw function_overrides). The PR heals a fixed
    // amount (the item's standard_value: 50 / 250 / 9999, 150 / 999) through its recovery calc; the result is
    // set to BNW's fraction of the target's max. PR item ids (the porter keeps vanilla's slots). Config:
    // [Battle] DamageRules. In battle; the menu's own item use is checked separately (QA).
    static class ItemHealRules
    {
        internal static bool Enabled = true;
        internal static int Applied;
        internal static string Last = "";

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            var calc = typeof(CalcControllerProvider);
            foreach (var m in new[] { "GetRecoveryStatus", "GetFixedBaseValueRecoveryStatus" })
                Hooks.Patch(h, calc, m, typeof(ItemHealRules), postfix: nameof(Heal_Postfix));
        }

        // (numerator, denominator, of MP?) for BNW's fractional items, or null
        internal static (int Num, int Den, bool Mp)? Fraction(int item) => item switch
        {
            1 => (1, 2, false),   // Tonic (the PR's Potion)
            2 => (3, 4, false),   // Potion (Hi-Potion)
            3 => (3, 4, false),   // X-Potion
            5 => (3, 4, true),    // Hi-Ether
            6 => (3, 4, true),    // X-Ether
            _ => null,
        };

        public static void Heal_Postfix(CalcControllerProvider __instance, ICalcResult __result)
        {
            if (!Enabled || __instance == null || __result == null) return;
            try
            {
                var item = __instance.item?.Item;
                if (item == null) return;
                var f = Fraction(item.Id);
                if (f == null) return;
                if (__result.GetHitType() != HitType.Recovery) return;
                var target = __instance.target;
                if (target == null) return;
                int max = f.Value.Mp ? target.ConfirmedMaxMp() : target.ConfirmedMaxHp();
                if (max <= 0) return;
                int want = Math.Max(1, max * f.Value.Num / f.Value.Den);
                int was = __result.GetValue(false);
                __result.SetValue(want);
                Last = $"item {item.Id}: the PR's {was} -> BNW's {want} ({f.Value.Num}/{f.Value.Den} of max {(f.Value.Mp ? "MP" : "HP")} {max})";
                if (Applied++ < 5) Plugin.L.LogInfo("[items] " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("ItemHealRules", e); }
        }
    }
}
