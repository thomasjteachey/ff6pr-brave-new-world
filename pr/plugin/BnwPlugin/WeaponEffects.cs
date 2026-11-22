using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Battle;
using Last.Data;
using Last.Data.User;
using Last.Systems;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW's weapon effects the readme audit found missing (2026-11-21; plan docs/bnw_audit_2026-11-20/plan_weapons.md):
    //  * Anti-Air (Kunai, Ninjato, Orochi, Full Moon, Boomerang, Rising Sun, Wing Edge: weapon process HAWK_EYE;
    //    c2.asm HawkEye 2440-2452): on Fight only, x2 vs a grounded target (the PR's x1.5) and x3 vs a floating one
    //    (the PR's own); nothing on other commands. The PR answers CalcExecuteCorrection.GetHawkEyeCorrectionValue.
    //  * Zantetsuken / Ichimonji (process SWORD_THAT_CUTS_IRON; c2.asm Zantetsuken 5305 + Undead_Killer 5317): 25% per
    //    swing; against a death-immune target, or on Mug or a Jump landing, it's a x2 critical instead of the cut (the PR
    //    skips immune targets and cuts only on Fight). A swing that's already a critical stays as it is.
    //  * Undead-Slayer (Demonsbane, Tarot: BNW effect F, the PR's weapons with effective_species 1 = undead; c2.asm
    //    Cleave 3084 -> KillZombie 111 -> Undead_Killer 5317): against an undead target, 50% per hit: a death-immune
    //    target, or Mug / a Jump landing, takes a x2 critical; else the hit also kills (KO added to its conditions).
    //    Its x2 against undead is the data's (effective_species).
    //  * Atma Weapon (PR weapon 30; c2.asm AtmaWpn 740, AtmaStat 6281): BNW's Fight with Stamina for Vigor, ignoring
    //    Defense, x (HP/256 + 1) / (MaxHP/256 + 1), no Level term. The PR keeps vanilla's: the Fight value x Level x
    //    the HP ratio / 64 + 1 (CalcExecuteFF6.PhysicalExecution, tools/decomp/physical; WcChallenges -saw) and
    //    BattleFormulas has already scaled that Fight to BNW's with Vigor, so this applies
    //    BnwFight(Stamina) / BnwFight(Vigor) x 64 / Level (logged: QA checks the Level term against a few HP ratios).
    //  * Dice (DICE / CHEAT_DICE): the PR already rolls BNW's dice (2, Fixed Dice 3, fair, the all-equal bonus x the
    //    value: c2.asm DiceDamage 3166 = CalcExecuteCorrection.IsDiceWeapon); BNW dropped the Berserk +50% the PR
    //    applies right after it (PhysicalExecution: CheckHaveCondition(user, 0x19a Bersak) -> x1.5), so a Berserk
    //    roller's dice value is divided by 1.5 first.
    //  * Chainsaw: no change needed. BNW cuts and, 25%, also kills a target that isn't death-immune; the PR picks the
    //    KO (ability 205: KO inflict, accuracy 255) 25% or the cut 75%, and EngineRules makes it the cut on immune
    //    targets: the same outcome (only the animation differs).
    // Fight-type strikes only (ability types 4 Attack, 15/16 Jump and its landing, 608 Mug): GetFightStatus also runs
    // for Blitzes and other physical skills, where the weapon's effect doesn't apply.
    // Still to do: the MP-crit procs (Punisher's crit spell, Quartrstaff all enemies, Doomstick X-Zone; plan 2.7-2.8).
    // Config: [Battle] DamageRules.
    static class WeaponEffects
    {
        internal static bool Enabled = true;
        const string Cleave = "SWORD_THAT_CUTS_IRON";
        const int AttackType = 4, JumpType = 15, JumpLandingType = 16, MugType = 608;
        const int UndeadSpecies = 1, AtmaWeapon = 30;
        static readonly HashSet<int> StrikeTypes = new HashSet<int> { AttackType, JumpType, JumpLandingType, MugType };
        static readonly Random Rng = new Random();
        internal static int Crits, Kills, Atmas, DiceBerserk;
        internal static string Last = "";

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            Hooks.Patch(h, typeof(CalcExecuteCorrection), "GetHawkEyeCorrectionValue", typeof(WeaponEffects), postfix: nameof(HawkEye_Postfix));
            Hooks.Patch(h, typeof(CalcControllerProvider), "GetFightStatus", typeof(WeaponEffects), postfix: nameof(Fight_Postfix));
            Hooks.Patch(h, typeof(CalcExecuteCorrection), "IsDiceWeapon", typeof(WeaponEffects), postfix: nameof(Dice_Postfix));
        }

        public static void HawkEye_Postfix(BattleActData battleActData, ref float __result)
        {
            if (!Enabled || battleActData == null) return;
            try
            {
                if (battleActData.GetHawkEyeAttackType() == BattleActData.HawkEyeAttackType.None) return;
                bool fight = battleActData.Command?.Id == 1;
                float was = __result;
                if (!fight) __result = 1f;
                else if (Math.Abs(__result - 1.5f) < 0.01f) __result = 2f;   // grounded: BNW's x2 (floating x3 is the PR's)
                float now = __result;
                if (now != was)
                    Hooks.NoteOnce("bnw-antiair-" + (fight ? "fight" : "other"), () => $"[weapons] anti-air: the PR's x{was} -> BNW's x{now} ({(fight ? "Fight" : "not Fight")})");
            }
            catch (Exception e) { Hooks.WarnOnce("WeaponEffects.HawkEye", e); }
        }

        static bool DeathImmune(CharacterParameterBase p)
        {
            var m = p?.ConfirmedResistanceCondtionList();
            return m != null && m.ContainsKey(EngineRules.KoCondition) && m[EngineRules.KoCondition] == 1;
        }

        static void ToCritical(ICalcResult r, int v)
        {
            r.SetStatus(Math.Min(9999, v * 2), HitType.Critical, r.GetHitCount(), r.GetConditions(), false);
        }

        public static void Fight_Postfix(CalcControllerProvider __instance, ICalcResult __result)
        {
            if (!Enabled || __instance == null || __result == null) return;
            try
            {
                var weapon = __instance.GetCalcInfo()?.userWeapon;
                if (weapon == null) return;
                int type = __instance.ability?.TypeId ?? 0;
                var hit = __result.GetHitType();
                // once per special weapon: what the effects saw (QA bnw-weapons 2026-11-21: Demonsbane and Zantetsuken
                // left no line, Atma did)
                if (weapon.ProcessProg == Cleave || weapon.EffectiveSpecies == UndeadSpecies)
                {
                    var t0 = __instance.target;
                    int wid = weapon.Id;
                    string prog = weapon.ProcessProg;
                    int sp = weapon.EffectiveSpecies;
                    bool undead0 = t0 != null && t0.IsUndead(), immune0 = DeathImmune(t0);
                    Hooks.NoteOnce($"bnw-weapon-seen-{wid}", () => $"[weapons] seen: weapon {wid} ({prog}, species {sp}): ability type {type}, " +
                                   $"hit {hit}, target undead {undead0}, death-immune {immune0}");
                }
                if (!StrikeTypes.Contains(type)) return;
                if (hit != HitType.Hit && hit != HitType.Critical) return;
                bool mugOrJump = type == MugType || type == JumpType || type == JumpLandingType;
                if (weapon.Id == AtmaWeapon && __instance.userCharaData != null) Atma(__instance, __result);
                if (hit != HitType.Hit) return;                         // already a critical: the effects below don't stack
                if (weapon.ProcessProg == Cleave) CleaveCrit(__instance, __result, weapon.Id, mugOrJump);
                else if (weapon.EffectiveSpecies == UndeadSpecies) UndeadSlayer(__instance, __result, weapon.Id, mugOrJump);
            }
            catch (Exception e) { Hooks.WarnOnce("WeaponEffects.Fight", e); }
        }

        static void CleaveCrit(CalcControllerProvider c, ICalcResult r, int weaponId, bool mugOrJump)
        {
            bool immune = DeathImmune(c.target);
            if (!immune && !mugOrJump) return;                     // the PR's own 25% cut handles the rest
            if (Rng.Next(256) >= 64) return;                       // 1 in 4
            int v = r.GetValue(false);
            ToCritical(r, v);
            Crits++;
            Last = $"cleave crit: weapon {weaponId}, {(immune ? "death-immune target" : "Mug/Jump")}: {v} -> {r.GetValue(false)}";
            if (Crits <= 5) Plugin.L.LogInfo("[weapons] " + Last);
        }

        static void UndeadSlayer(CalcControllerProvider c, ICalcResult r, int weaponId, bool mugOrJump)
        {
            var t = c.target;
            if (t == null || !t.IsUndead()) return;
            if (Rng.Next(256) >= 128) return;                      // 50% (KillZombie: random < $80)
            int v = r.GetValue(false);
            if (DeathImmune(t) || mugOrJump)
            {
                ToCritical(r, v);
                Crits++;
                Last = $"undead-slayer crit: weapon {weaponId}, {(mugOrJump ? "Mug/Jump" : "death-immune undead")}: {v} -> {r.GetValue(false)}";
            }
            else
            {
                var ko = Last_Master_Condition(EngineRules.KoCondition);
                if (ko == null) return;
                var conds = r.GetConditions() ?? new Il2CppSystem.Collections.Generic.List<global::Last.Data.Master.Condition>();
                conds.Add(ko);
                r.SetStatus(v, HitType.Hit, r.GetHitCount(), conds, false);
                Kills++;
                Last = $"undead-slayer kill: weapon {weaponId}, {v} damage + KO";
            }
            if (Crits + Kills <= 6) Plugin.L.LogInfo("[weapons] " + Last);
        }

        static global::Last.Data.Master.Condition Last_Master_Condition(int id) =>
            global::Last.Data.Master.MasterManager.Instance?.GetData<global::Last.Data.Master.Condition>(id);

        static void Atma(CalcControllerProvider c, ICalcResult r)
        {
            var a = c.attack;
            if (a == null) return;
            int lv = Math.Max(1, a.ConfirmedLevel());
            int vig = a.ConfirmedPower(), sta = Math.Clamp(a.ConfirmedVitality(), 0, 255);
            int bp = BattleFormulas.StrikeBattlePower(c, a.TryCast<PlayerCharacterParameter>(), a);
            double withVig = BattleFormulas.BnwFight(lv, bp, vig), withSta = BattleFormulas.BnwFight(lv, bp, sta);
            if (withVig <= 0) return;
            double f = withSta / withVig * 64.0 / lv;
            int v = r.GetValue(false);
            int now = (int)Math.Min(9999, Math.Round(v * f));
            r.SetValue(now);
            Atmas++;
            Last = $"atma: Lv {lv} BP {bp} Vigor {vig} Stamina {sta} HP {a.CurrentHP}/{a.ConfirmedMaxHp()}: the PR's {v} x{f:0.###} -> {now}";
            if (Atmas <= 5) Plugin.L.LogInfo("[weapons] " + Last);
        }

        static BattleUnitData UnitOf(OwnedCharacterData o)
        {
            var units = BattlePlugManager.Instance()?.GetPlayerUnits();
            if (units == null || o == null) return null;
            foreach (var u in units)
            {
                var p = u?.TryCast<BattlePlayerData>();
                if (p?.ownedCharacterData != null && p.ownedCharacterData.Pointer == o.Pointer) return u;
            }
            return null;
        }

        // the PR multiplies a Berserk roller's dice by 1.5 right after this; BNW doesn't: the value it will scale is
        // 2/3 of the roll, so the damage comes out as rolled (within 1)
        public static void Dice_Postfix(OwnedCharacterData ownedCharacterData, ref int damageValue, bool __result)
        {
            if (!Enabled || !__result || damageValue <= 0) return;
            try
            {
                var u = UnitOf(ownedCharacterData);
                if (u == null || !BattleUtility.CheckHaveConditions(u, global::Last.Defaine.ConditionType.Bersak)) return;
                int was = damageValue;
                damageValue = (2 * was + 1) / 3;
                DiceBerserk++;
                Last = $"dice: {ownedCharacterData.Name} is Berserk: {was} -> {damageValue} before the PR's x1.5 (BNW: no Berserk bonus)";
                Hooks.NoteOnce("bnw-dice-berserk", () => "[weapons] " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("WeaponEffects.Dice", e); }
        }
    }
}
