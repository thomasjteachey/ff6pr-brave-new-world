using System;
using HarmonyLib;
using Last.Data;
using Last.Data.Master;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW's "Gau Explod" (readme 2037-2041, Seibaby; BNW's own rage text: "dmg = caster's HP * 2.5"): Exploder deals
    // 250% of the user's current HP when a party member uses it (Bomb's rage, Sketch); a monster's stays its HP.
    // BNW's Exploder is SNES spell 132, the port's ability 6132 (profile_bnw spell_relocations). The PR computes it with
    // function 33 (BaseValueFixedHPMagicFunction, modes 1/1: the user's current HP) and then KOs the user (function 38):
    // its Calc(attacker, target, ability, item) takes the amount from BaseValueFixedHPFunction.Calc, so while it runs
    // for a party member's Exploder that amount is x2.5 (at most 9999). Config: [Battle] DamageRules.
    static class ExploderRule
    {
        internal static bool Enabled = true;
        internal const int Exploder = 6132;
        [ThreadStatic] static bool pending;
        internal static int Applied;
        internal static string Last = "";

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            Hooks.Patch(h, typeof(global::Last.Battle.Function.BaseValueFixedHPMagicFunction), "Calc", typeof(ExploderRule),
                prefix: nameof(Calc_Prefix), postfix: nameof(Calc_Postfix),
                args: new[] { typeof(CharacterParameterBase), typeof(CharacterParameterBase), typeof(Ability), typeof(global::Last.Data.User.OwnedItemData) });
            Hooks.Patch(h, typeof(global::Last.Battle.Function.BaseValueFixedHPFunction), "Calc", typeof(ExploderRule),
                postfix: nameof(Amount_Postfix), args: new[] { typeof(CharacterParameterBase), typeof(CharacterParameterBase) });
        }

        public static void Calc_Prefix(CharacterParameterBase attackParameter, Ability ability)
        {
            try { pending = Enabled && ability?.Id == Exploder && attackParameter?.TryCast<PlayerCharacterParameter>() != null; }
            catch (Exception e) { pending = false; Hooks.WarnOnce("ExploderRule.prefix", e); }
        }

        public static void Calc_Postfix() => pending = false;

        public static void Amount_Postfix(CharacterParameterBase attackParameter, ref int __result)
        {
            if (!pending || __result <= 0) return;
            try
            {
                int was = __result;
                __result = (int)Math.Min(9999, was * 5L / 2);
                Applied++;
                Last = $"exploder: {attackParameter?.ConfirmedMaxHp()} max HP, the PR's {was} -> BNW's {__result} (x2.5, a party member's)";
                if (Applied <= 3) Plugin.L.LogInfo("[skills] " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("ExploderRule.amount", e); }
        }
    }

    // BNW's Mindblow (Bushido #2, SNES spell 86, PR ability 125 since profile_bnw's Bushido overrides): BlowFish's effect
    // $1A on an MP hit, Spell Power (10) x 50 = 500 MP (c2.asm BlowFish: no defense, no split), and undead with "dies
    // at 0 MP" fall to it (AuditRules). The port gives 125 Rasp's MP damage function (3, value 2); its amount is set
    // here, after BattleFormulas' magic scaling. Config: [Battle] DamageRules.
    static class MindblowRule
    {
        internal static bool Enabled = true;
        internal const int Mindblow = 125, Amount = 500;
        internal static int Applied;
        internal static string Last = "";

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            Hooks.Patch(h, typeof(global::Last.Systems.CalcControllerProvider), "GetMagicStatus", typeof(MindblowRule), postfix: nameof(Magic_Postfix));
        }

        public static void Magic_Postfix(global::Last.Systems.CalcControllerProvider __instance, global::Last.Systems.ICalcResult __result)
        {
            if (!Enabled || __instance == null || __result == null) return;
            try
            {
                if ((__instance.ability?.Id ?? 0) != Mindblow || __instance.item != null) return;
                var hit = __result.GetHitType();
                // an MP hit reports MPHit (5), not Hit
                if (hit != global::Last.Systems.HitType.MPHit && hit != global::Last.Systems.HitType.Hit && hit != global::Last.Systems.HitType.Critical) return;
                int was = __result.GetValue(false);
                if (was == Amount) return;
                __result.SetValue(Amount);
                Applied++;
                Last = $"mindblow: the PR's {was} -> BNW's {Amount} MP";
                if (Applied <= 3) Plugin.L.LogInfo("[skills] " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("MindblowRule", e); }
        }
    }
}
