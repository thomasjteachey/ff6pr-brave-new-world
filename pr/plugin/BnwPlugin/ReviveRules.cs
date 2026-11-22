using System;
using HarmonyLib;
using Last.Data;
using Last.Data.Master;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW's revive amounts (readme "Life Hack" / "Fractional Items"; its item and spell descriptions):
    // Phoenix Down revives to 1 HP, Phoenix Tear to 3/4 of max HP, Life (and Edgar's Defibrator, which
    // casts Life) to a random 250-500 (never past max), Life 2 to full (the PR's own, unchanged). The PR
    // revives through function 34, ResultValueFixedHPRecoveryFunction: its Calc(attacker, target, ability,
    // item) takes the amount from BaseValueFixedHPFunction.Calc (value2 4 = percent of max, 5 = max / value3:
    // Phoenix Down and Raise 1/8; decompiled tools/decomp/revive), which has no flat or random mode. So the
    // revive's Calc notes what is being used, and the base Calc's result is BNW's while it runs.
    // Phoenix Tear sits on vanilla's Magicite slot (PR item 18): the porter gives it Phoenix Down's revive
    // (profile_bnw table_overrides), and this sets its 3/4. Config: [Battle] DamageRules.
    static class ReviveRules
    {
        internal static bool Enabled = true;
        internal const int PhoenixDown = 9, PhoenixTear = 18, Defibrator = 34;   // PR item ids
        internal const int Life = 34;                                             // PR ability (the PR's Raise)
        static readonly Random Rng = new Random();

        [ThreadStatic] static int pendingItem, pendingAbility;
        [ThreadStatic] static bool pending;
        internal static int Applied;
        internal static string Last = "";

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            // both types have more than one Calc: name the overloads (an unnamed one is "Ambiguous match", nothing patched)
            Hooks.Patch(h, typeof(Last.Battle.Function.ResultValueFixedHPRecoveryFunction), "Calc", typeof(ReviveRules),
                prefix: nameof(Revive_Prefix), postfix: nameof(Revive_Postfix),
                args: new[] { typeof(CharacterParameterBase), typeof(CharacterParameterBase), typeof(Ability), typeof(Last.Data.User.OwnedItemData) });
            Hooks.Patch(h, typeof(Last.Battle.Function.BaseValueFixedHPFunction), "Calc", typeof(ReviveRules),
                postfix: nameof(Amount_Postfix), args: new[] { typeof(CharacterParameterBase), typeof(CharacterParameterBase) });
        }

        static int ItemId(Last.Data.User.OwnedItemData item)
        {
            if (item == null) return 0;
            var c = MasterManager.Instance?.GetData<Content>(item.ContentId);
            return c != null && c.TypeId == 1 ? c.TypeValue : 0;
        }

        public static void Revive_Prefix(Ability ability, Last.Data.User.OwnedItemData item)
        {
            try
            {
                pendingItem = ItemId(item);
                pendingAbility = pendingItem == 0 ? ability?.Id ?? 0 : 0;
                pending = true;
            }
            catch (Exception e) { pending = false; Hooks.WarnOnce("ReviveRules.prefix", e); }
        }

        public static void Revive_Postfix() => pending = false;

        // BNW's amount for what's being used, or -1 (the PR's own)
        internal static int Amount(int item, int ability, int maxHp, Func<int, int, int> rand)
        {
            if (item == PhoenixDown) return 1;
            if (item == PhoenixTear) return Math.Max(1, maxHp * 3 / 4);
            if (item == Defibrator || (item == 0 && ability == Life)) return Math.Max(1, Math.Min(maxHp, rand(250, 501)));
            return -1;
        }

        public static void Amount_Postfix(CharacterParameterBase targetParameter, ref int __result)
        {
            if (!pending || !Enabled) return;
            try
            {
                int max = targetParameter?.ConfirmedMaxHp() ?? 0;
                if (max <= 0) return;
                int bnw = Amount(pendingItem, pendingAbility, max, (lo, hi) => Rng.Next(lo, hi));
                if (bnw < 0) return;
                Last = $"revive: item {pendingItem} ability {pendingAbility}: the PR's {__result} -> BNW's {bnw} (max {max})";
                if (Applied++ < 5) Plugin.L.LogInfo("[revive] " + Last);
                __result = bnw;
            }
            catch (Exception e) { Hooks.WarnOnce("ReviveRules.amount", e); }
        }
    }
}
