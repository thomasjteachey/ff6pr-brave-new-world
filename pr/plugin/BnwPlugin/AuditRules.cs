using System;
using System.Collections.Generic;
using System.Linq;
using BraveNewWorld.Rules;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Last.Battle;
using Last.Battle.Function;
using Last.Data;
using Last.Data.Master;
using Last.Data.User;
using Last.Defaine;
using Last.Defaine.User;
using Last.Management;
using Last.Systems;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // The readme audit's batch B, battle formulas and evasion (docs/bnw.md "Gaps found by the readme audit"; asm read
    // 2026-11-21; the PR's side decompiled into tools/decomp/audit_evade, audit_imp, audit_counter):
    //  1. Stamina, the only evasion (c0.asm StamPhase, from C2/22A8): an attack Stamina can block, not "can't dodge",
    //     that is fractional or MP damage (or North Cross's effect) rolls Stamina before it hits: it misses when
    //     random(0..127) <= Stamina (StamEvdChk, C2/23B2), and nothing else evades it (BNW gives them hit rate 255, so
    //     the PR's MBlock roll never runs). bnw_data spirit.stamina_first_abilities (13: Demi, Quartr, Rasp, Leech,
    //     Cave In, Cyclonic, N.Cross, Launcher, Grav Bomb, Thriller, Purge, Air Blast, Lode Stone). The PR's per-target
    //     forced miss is unreachable for Demi (its IsFixedMiss is a jump thunk): each function's Calc carries the roll. HP drain is NOT in
    //     StamPhase (the readme says it is; the asm wins): Drain keeps the normal evasion.
    //  2. Fractional damage on bosses (C2/50F4 Fractional, special effect $30): against a target with the boss flag
    //     ($3C80 bit 2, which is also "immune to instant death/fractional") the fractional flag is dropped and the spell
    //     deals its normal damage from its own power (Demi 8, Quartr 12, Cave In 12, Cyclonic 14, Grav Bomb 15, Air
    //     Blast 8, Lode Stone 10). The PR's BaseValueFixedHPMagicFunction.Calc makes it a miss on an IsBoss() or
    //     KO-resistant enemy; here that miss becomes the provider's magic damage at BNW's power (GetMagicStatus(0,
    //     power): defense, variance, elements and BattleFormulas' BNW rules as for any spell).
    //  3. Imp (C2/320D skips "mute all magic but Imp"; C2/5xxx disables only Morph for Imps; Morph's on-set clears Imp;
    //     OvercastFix: a Morphed character can't be Imped): BattleUtility.IsSelectableCommand blocks by condition
    //     groups (spells: group 400 {Silence, Imp}; special commands: 302 {Imp}); an Imp's block is lifted unless the
    //     command is Morph. Imp on a Trance (Morph) unit misses (AddConditionFunctionSerial.CheckForceMissCondition) or
    //     isn't added (BattleConditionController.InterruptAddCondition); Trance's start takes Imp off (a frame later).
    //  4. Parry & Counter (C2/343C ParryCounter): a missed attack still arms its target's counter (vanilla: hits only),
    //     every kind of counter (the Black Belt relic, Retort, monsters' counter scripts). CounterModule.IsCounter
    //     answers no for a Miss before its other checks; here a Miss goes on through them (IsActable, < 2 queued).
    //  5. The back row (C2/3392 GetRowFlag; readme "Back Row Nerfs"): BNW's physical skills with "respect row" (spell
    //     byte 7 bit 4: Bushido 85/88/91, Pummel, Suplex, Bum Rush; spirit.respect_row_abilities) do half damage from
    //     the back row; the others don't. The PR halves every Bushido (type 601) from the back row unless
    //     CalcExecuteCorrection.IsIgnoreRearguardAttackAbility, and never a Blitz (type 602 sets isIgnoreCorps):
    //     GetFightStatus's result x (BNW's / the PR's).
    //  6. Dies at 0 MP (monster byte $12 bit 0; readme: every undead, bosses too): the PR kills a monster whose MP an
    //     MP attack takes to 0 when its CharacterParameterBase.isUnableMP is set (MonsterFragType.UnableMP, read at its
    //     creation; BattleConditionController's MP check, InterruptAddCondition UnableFight: the game's own death).
    //     Each enemy's flag is set to BNW's (bnw_data die_at_zero_mp: 47 SNES monsters, 62 PR ids with the Coliseum's
    //     copies) once a battle frame. NOT done here: BNW's Mindblow itself (Bushido 86, MP damage, power 10) is still
    //     the PR's Retort stance (ability 125, power 0) in the port: the porter's work.
    // Gated on [Battle] DamageRules (1, 2, 3, 5, 6) and TurnRules (4). Probe self-tests: "audit ..." (FormulaProbe).
    static class AuditRules
    {
        internal static bool Damage = true, Turns = true;
        static BnwData D => EsperHooks.D;
        static readonly Random Rng = new Random();
        const int ImpConditionId = 13, MorphCommand = 8;
        const int KappaType = 601, TranceType = 621, PermanentTranceType = 684;
        const int BushidoType = 601, BlitzType = 602;

        // test counters
        internal static int StaminaRolls, StaminaEvades, BossHits, ImpLifts, ImpRefusals, ImpCleared, CounterFlips, RowScaled, Mp0Set;
        internal static double LastRowFactor = 1;

        public static void Apply(Harmony h, bool damage, bool turns)
        {
            Damage = damage;
            Turns = turns;
            int n = 0, of = 0;
            void P(bool ok) { of++; if (ok) n++; }
            // 1 and 2: each per-target function's Calc (the fractional, MP-damage and MP-drain functions; 2026-11-21, QA:
            // BattleBasicFunction.IsFixedMiss never ran for Demi, whose function class overrides it with a jump thunk too
            // small to detour): a prefix makes the ability never miss by the PR's MBlock roll (hit rate 255, as BNW's
            // data: StamPhase replaces EvadeChk), the postfix rolls Stamina on its result (then the boss rule)
            var calcArgs = new[] { typeof(CharacterParameterBase), typeof(CharacterParameterBase), typeof(Ability), typeof(OwnedItemData) };
            P(Hooks.Patch(h, typeof(BaseValueFixedHPMagicFunction), "Calc", typeof(AuditRules), prefix: nameof(Accuracy_Prefix), postfix: nameof(Fractional_Postfix), args: calcArgs));
            P(Hooks.Patch(h, typeof(MagicFunction), "Calc", typeof(AuditRules), prefix: nameof(Accuracy_Prefix), postfix: nameof(Stamina_Postfix), args: calcArgs));
            P(Hooks.Patch(h, typeof(MagicAbsorptionFunction), "Calc", typeof(AuditRules), prefix: nameof(Accuracy_Prefix), postfix: nameof(Stamina_Postfix), args: calcArgs));
            // 3
            P(Hooks.Patch(h, typeof(BattleUtility), "IsSelectableCommand", typeof(AuditRules), postfix: nameof(Selectable_Postfix)));
            P(Hooks.Patch(h, typeof(Serial.FF6.Battle.AddConditionFunctionSerial), "CheckForceMissCondition", typeof(AuditRules),
                          postfix: nameof(ImpMiss_Postfix)));
            P(Hooks.Patch(h, typeof(BattleConditionController), "InterruptAddCondition", typeof(AuditRules), prefix: nameof(AddByType_Prefix),
                          args: new[] { typeof(BattleUnitData), typeof(ConditionType) }));
            P(Hooks.Patch(h, typeof(BattleConditionController), "InterruptAddCondition", typeof(AuditRules), prefix: nameof(AddById_Prefix),
                          args: new[] { typeof(BattleUnitData), typeof(int) }));
            P(Hooks.Patch(h, typeof(TranceConditionFunction), "Start", typeof(AuditRules), postfix: nameof(Trance_Postfix)));
            // 4
            P(Hooks.Patch(h, typeof(CounterModule), "IsCounter", typeof(AuditRules), postfix: nameof(Counter_Postfix),
                          args: new[] { typeof(BattleUnitData), typeof(BattleActData), typeof(FunctionGroupInfo) }));
            // 5
            P(Hooks.Patch(h, typeof(CalcControllerProvider), "GetFightStatus", typeof(AuditRules), postfix: nameof(Row_Postfix)));
            // 6 (and 3's deferred Imp removal)
            P(Hooks.Patch(h, typeof(BattleProgressATBFF6), "Update", typeof(AuditRules), postfix: nameof(Frame_Postfix)));
            Plugin.L.LogInfo($"[audit] {n} of {of} hooks: Stamina-only evasion {D?.StaminaFirstAbilities.Count} abilities, fractional on bosses " +
                             $"{D?.BossFractional.Count}, Imp casts, counters on misses, back row {D?.RespectRowAbilities.Count} skills, " +
                             $"0 MP deaths {D?.DieAtZeroMp.Count} monsters (damage rules {damage}, turn rules {turns})");
            AuditTests.Register();
        }

        static Ability FirstAbility(BattleActData act)
        {
            var list = act?.abilityList;
            return list != null && list.Count > 0 ? list[0] : null;
        }

        internal static int Stamina(CharacterParameterBase p) =>
            p == null ? 0 : p.TryCast<PlayerCharacterParameter>() != null ? Math.Clamp(p.ConfirmedVitality(), 0, 255)
                                                                          : BattleFormulas.MonsterStamina(p.ConfirmedMaxHp());

        static bool Has(CharacterParameterBase p, params int[] types)
        {
            var list = p?.CurrentConditionList;
            if (list == null) return false;
            foreach (var c in list)
                if (c != null && Array.IndexOf(types, c.ConditionType) >= 0) return true;
            return false;
        }

        // --- 1. Stamina, the only evasion ----------------------------------------------------------------------

        // the PR's own hit roll off for these (BNW: hit rate 255 and StamPhase in place of EvadeChk): the master row's
        // accuracy, once (MagicExecution: >= 255 never rolls MBlock)
        public static void Accuracy_Prefix(Ability __2)
        {
            if (!Damage || D == null || __2 == null || !D.StaminaFirstAbilities.Contains(__2.Id) || __2.AccuracyRate >= 255) return;
            try
            {
                int was = __2.AccuracyRate, id = __2.Id;
                __2.AccuracyRate = 255;
                Hooks.NoteOnce("audit-acc-" + id, () => $"[audit] ability {id}: no MBlock roll (accuracy {was} -> 255; Stamina is its only evasion)");
            }
            catch (Exception e) { Hooks.WarnOnce("AuditRules.Accuracy", e); }
        }

        // a hit (or MP hit) by a Stamina-first ability becomes a miss when rand(0..127) <= Stamina (StamEvdChk C2/23B2)
        static bool StaminaMiss(FunctionBase fn, CharacterParameterBase target, Ability ab)
        {
            if (!Damage || D == null || fn == null || target == null || ab == null || !D.StaminaFirstAbilities.Contains(ab.Id)) return false;
            var r = fn.calcResult;
            var ht = r?.GetHitType() ?? HitType.Non;
            if (ht == HitType.Miss || ht == HitType.Non) return false;
            int sta = Stamina(target);
            bool evaded = Rng.Next(128) <= sta;
            StaminaRolls++;
            if (!evaded) return false;
            var miss = BattleControllerManagement.Instance()?.GetCurrentProvider()?.GetFixedStatus(HitType.Miss);
            if (miss == null) return false;
            fn.calcResult = miss;
            StaminaEvades++;
            int id = ab.Id;
            Hooks.NoteOnce("audit-stamina", () => $"[audit] Stamina evasion: ability {id} missed by Stamina {sta} (the only evasion for fractional / MP attacks)");
            return true;
        }

        public static void Stamina_Postfix(FunctionBase __instance, CharacterParameterBase __1, Ability __2)
        {
            try { StaminaMiss(__instance, __1, __2); }
            catch (Exception e) { Hooks.WarnOnce("AuditRules.Stamina", e); }
        }

        // --- 2. fractional damage on bosses --------------------------------------------------------------------

        public static void Fractional_Postfix(BaseValueFixedHPMagicFunction __instance, CharacterParameterBase __1, Ability __2)
        {
            try { if (StaminaMiss(__instance, __1, __2)) return; }   // Stamina first (BNW: before the hit), then the boss rule
            catch (Exception e) { Hooks.WarnOnce("AuditRules.Stamina", e); }
            if (!Damage || D == null || __instance == null || __2 == null || !D.BossFractional.TryGetValue(__2.Id, out int power)) return;
            try
            {
                var r = __instance.calcResult;
                if (r == null || r.GetHitType() != HitType.Miss || __1?.TryCast<EnemyCharacterParameter>() == null) return;
                // the PR's miss here: an IsBoss() or KO-resistant enemy (BNW's $3C80 bit 2)
                bool boss = __1.IsBoss();
                var provider = BattleControllerManagement.Instance()?.GetCurrentProvider();
                if (provider == null) return;
                var hit = provider.GetMagicStatus(0, power, (MagicFunction.DamageFactor)0);
                if (hit == null) return;
                __instance.calcResult = hit;
                BossHits++;
                int id = __2.Id, dmg = hit.GetValue(false);
                var kind = hit.GetHitType();
                Hooks.NoteOnce("audit-boss-frac", () => $"[audit] fractional ability {id} on a {(boss ? "boss" : "KO-resistant enemy")}: " +
                                                         $"BNW's normal damage at power {power} ({kind} {dmg}) instead of a miss");
            }
            catch (Exception e) { Hooks.WarnOnce("AuditRules.Fractional", e); }
        }

        // --- 3. Imp --------------------------------------------------------------------------------------------

        static Dictionary<int, HashSet<int>> groups;

        static bool InGroup(int group, int conditionId)
        {
            if (group <= 0) return false;
            if (groups == null)
            {
                var g = new Dictionary<int, HashSet<int>>();
                var list = MasterManager.Instance?.GetList<ConditionGroup>();
                if (list == null) return false;
                foreach (var row in list.Values)
                {
                    if (row == null) continue;
                    if (!g.TryGetValue(row.GroupId, out var set)) g[row.GroupId] = set = new HashSet<int>();
                    set.Add(row.ConditionId);
                }
                groups = g;
            }
            return groups.TryGetValue(group, out var s) && s.Contains(conditionId);
        }

        // the PR blocks by the unit's conditions in the ability's and the command's condition groups; BNW's Imp blocks Morph only
        public static void Selectable_Postfix(CharacterParameterBase __0, Ability __1, Command __2, ref bool __result)
        {
            if (__result || !Damage || __0 == null) return;
            try
            {
                var list = __0.CurrentConditionList;
                if (list == null || !Has(__0, KappaType)) return;
                bool morph = __2 != null && __2.Id == MorphCommand;
                int ag = __1?.ConditionGroupId ?? 0, cg = __2?.ConditionGroupId ?? 0;
                foreach (var c in list)
                {
                    if (c == null) continue;
                    if ((c.ConditionType == KappaType || c.Id == ImpConditionId) && !morph) continue;
                    if (InGroup(ag, c.Id) || InGroup(cg, c.Id)) return;   // something else blocks it (Silence...)
                }
                __result = true;
                ImpLifts++;
                int ab = __1?.Id ?? 0, cmd = __2?.Id ?? 0;
                Hooks.NoteOnce("audit-imp", () => $"[audit] Imp: ability {ab} / command {cmd} usable while Imped (BNW blocks only Morph)");
            }
            catch (Exception e) { Hooks.WarnOnce("AuditRules.Imp", e); }
        }

        static bool Morphed(CharacterParameterBase p) => Has(p, TranceType, PermanentTranceType);

        public static void ImpMiss_Postfix(CharacterParameterBase __1, Condition __2, ref bool __result)
        {
            if (__result || !Damage || __2 == null || __2.ConditionType != KappaType || !Morphed(__1)) return;
            __result = true;
            ImpRefusals++;
            Hooks.NoteOnce("audit-imp-morph", () => "[audit] Imp misses a Morphed character");
        }

        public static bool AddByType_Prefix(BattleUnitData __0, ConditionType __1)
        {
            if (!Damage || (int)__1 != KappaType || !Morphed(__0?.BattleUnitDataInfo?.Parameter)) return true;
            ImpRefusals++;
            return false;
        }

        public static bool AddById_Prefix(BattleUnitData __0, int __1)
        {
            if (!Damage || __1 != ImpConditionId || !Morphed(__0?.BattleUnitDataInfo?.Parameter)) return true;
            ImpRefusals++;
            return false;
        }

        // Morph's on-set clears Imp (C2/46E6 ClearImp); taken off from the next battle frame (Start runs inside the
        // condition controller's own pass)
        static readonly List<BattleUnitData> clearImp = new List<BattleUnitData>();

        public static void Trance_Postfix(TranceConditionFunction __instance)
        {
            if (!Damage || __instance == null) return;
            try
            {
                var u = __instance.BattleUnitData;
                if (u != null && Has(u.BattleUnitDataInfo?.Parameter, KappaType)) clearImp.Add(u);
            }
            catch (Exception e) { Hooks.WarnOnce("AuditRules.Trance", e); }
        }

        // --- 4. counters on misses -----------------------------------------------------------------------------

        public static void Counter_Postfix(CounterModule __instance, BattleUnitData __0, FunctionGroupInfo __2, ref bool __result)
        {
            if (__result || !Turns || __instance == null || __0 == null || __2 == null) return;
            try
            {
                var dic = __2.GetICalcResult();
                if (dic == null || !dic.ContainsKey(__0)) return;
                var r = dic[__0];
                if (r == null || r.GetHitType() != HitType.Miss) return;
                if (!__0.IsActable()) return;                 // the game's own checks, which it skips for a miss
                int queued = 0;
                var list = __instance.calcResults;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                        if (list[i].targetedUnit != null && list[i].targetedUnit.Pointer == __0.Pointer) queued++;
                if (queued >= 2) return;
                __result = true;
                CounterFlips++;
                if (CounterFlips <= 3)
                    Plugin.L.LogInfo($"[audit] counter armed on a miss ({(__0.TryCast<BattleEnemyData>() != null ? "monster " + __0.TryCast<BattleEnemyData>().GetMonster()?.Id : "party member")})");
            }
            catch (Exception e) { Hooks.WarnOnce("AuditRules.Counter", e); }
        }

        // --- 5. the back row -----------------------------------------------------------------------------------

        public static void Row_Postfix(CalcControllerProvider __instance, ICalcResult __result)
        {
            if (!Damage || D == null || __instance == null || __result == null) return;
            try
            {
                var ab = __instance.ability;
                if (ab == null || __instance.item != null || __instance.userCharaData == null) return;
                int type = ab.TypeId;
                if (type != BushidoType && type != BlitzType) return;
                if (__instance.userCorpsId != CorpsId.Back) { LastRowFactor = 1; return; }
                bool bnw = D.RespectRowAbilities.Contains(ab.Id);
                bool pr = type == BushidoType && !CalcExecuteCorrection.IsIgnoreRearguardAttackAbility(ab.Id);
                double f = (bnw ? 0.5 : 1.0) / (pr ? 0.5 : 1.0);
                LastRowFactor = f;
                var ht = __result.GetHitType();
                if (f == 1.0 || (ht != HitType.Hit && ht != HitType.Critical)) return;
                int was = __result.GetValue(false);
                int now = Math.Max(1, (int)Math.Round(was * f));
                __result.SetValue(now);
                RowScaled++;
                int id = ab.Id;
                Hooks.NoteOnce("audit-row-" + id, () => $"[audit] back row: {(type == BlitzType ? "Blitz" : "Bushido")} {id} {was} -> {now} " +
                                                        $"(BNW {(bnw ? "halves" : "doesn't halve")} it, the PR {(pr ? "did" : "didn't")})");
            }
            catch (Exception e) { Hooks.WarnOnce("AuditRules.Row", e); }
        }

        // --- 6. dies at 0 MP, and 3's Imp removal: once a battle frame -----------------------------------------

        static int frame;

        public static void Frame_Postfix()
        {
            try
            {
                if (clearImp.Count > 0)
                {
                    var cc = BattlePlugManager.Instance()?.BattleConditionController;
                    foreach (var u in clearImp)
                        if (u != null && cc != null && Has(u.BattleUnitDataInfo?.Parameter, KappaType))
                        {
                            cc.InterruptRemoveCondition(u, (ConditionType)KappaType);
                            ImpCleared++;
                            Hooks.NoteOnce("audit-imp-cleared", () => "[audit] Morph took Imp off");
                        }
                    clearImp.Clear();
                }
                if (!Damage || D == null || ++frame % 20 != 1) return;
                var battle = BattlePlugManager.Instance();
                if (battle == null || !battle.IsBattle()) return;
                foreach (var u in battle.GetEnemyUnits())
                {
                    var p = u?.BattleUnitDataInfo?.Parameter;
                    int id = u?.GetMonster()?.Id ?? 0;
                    if (p == null || id == 0) continue;
                    bool want = D.DieAtZeroMp.Contains(id);
                    if (p.isUnableMP == want) continue;
                    p.isUnableMP = want;
                    Mp0Set++;
                    Hooks.NoteOnce("audit-mp0-" + want, () => $"[audit] monster {id} {(want ? "dies" : "no longer dies")} at 0 MP (BNW's flag)");
                }
            }
            catch (Exception e) { Hooks.WarnOnce("AuditRules.Frame", e); }
        }
    }
}
