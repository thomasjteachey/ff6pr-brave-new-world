using System;
using System.Collections.Generic;
using BraveNewWorld.Rules;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Last.Battle;
using Last.Battle.Function;
using Last.Defaine;
using Last.Defaine.User;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // True Knight cover as BNW has it (c2.asm C2123B TrueKnight / EvalKnight / SmartCover; the
    // choice itself is BattleMath.CoverGuard, offline-tested). The PR (decompiled
    // tools/decomp/cover, cover2): TargetFunction.ProtectExtractTarget picks a single target's
    // guard: ProtectTarget for jobs with innate cover (IsInitialProtectDying: none in FF6),
    // otherwise its own second path, which covers a Near Fatal ally from a wearer of the knight's
    // effect (condition type 606 TipsForKnights: the port's wear condition 29 for relic bit 12.6),
    // both through act.SetProtectData(guard, covered, ...). A postfix there redoes the choice by
    // BNW's rules whenever a knight stands among the target's allies (and the game's own cover,
    // if any, was a knight's), through the same SetProtectData:
    //  * a Near Fatal target is covered; a healthy one only in the back row, with chance
    //    Stamina / 192 per knight (Stamina / 96 while the knight defends), by a knight who isn't
    //    Near Fatal; knights stand in the front row; the knight with the most HP covers;
    //  * no cover from a party member's own attack unless it's confused, a zombie or charmed;
    //    none of a dead, petrified, invisible, zombie, Magitek or Image target; no monster guards;
    //  * a knight can't cover while dead, petrified, invisible, a zombie, in Magitek armor,
    //    blinded, asleep, confused, berserk, with Image, dancing, stopped, frozen or hidden.
    // BNW also drops the knight's Defend when it covers (done here); its Love Token relic has no
    // PR counterpart.
    // The covered hit itself (c2.asm HalveEvasion, SkipDogBlock: "is target a bodyguard"): the
    // guard's Evade counts half (floor), and Shadow's Interceptor never blocks a hit he takes for
    // someone else. The PR applies cover in BattleFunction2.FixedTarget (the only caller of
    // ProtectExtractTarget) and computes that act's hits in BattleFunction2.HitCalc, one call for
    // all targets (decompiled tools/decomp/cover_hit): CalcControllerProvider.GetFightStatus rolls
    // Interceptor there (WildFangConditionFunction.CheckDefendInvoke, 50% vs physical attacks) and
    // PhysicalExecution reads the target's ConfirmedEvasionRate (Block = (128 - Evade) * 2). So
    // while a covered act's HitCalc runs, the guard's evasion reads halved and the roll says no.
    // Config: [Battle] RelicRules.
    static class CoverRules
    {
        internal static bool Enabled = true;
        static readonly Random Rng = new Random();
        // during a covered act's BattleFunction2.HitCalc: the guard (its unit and its parameters:
        // the battle unit's and its OwnedCharacterData's, either of which the calc may read)
        [ThreadStatic] static IntPtr hitGuard, hitGuardParam, hitGuardParam2;

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            Hooks.Patch(h, typeof(TargetFunction), "ProtectExtractTarget", typeof(CoverRules), postfix: nameof(Extract_Postfix));
            Hooks.Patch(h, typeof(BattleFunction2), "HitCalc", typeof(CoverRules), prefix: nameof(HitCalc_Prefix), postfix: nameof(HitCalc_Postfix));
            Hooks.Patch(h, typeof(Last.Data.PlayerCharacterParameter), "ConfirmedEvasionRate", typeof(CoverRules), postfix: nameof(Evasion_Postfix));
            Hooks.Patch(h, typeof(WildFangConditionFunction), "CheckDefendInvoke", typeof(CoverRules), prefix: nameof(DefendInvoke_Prefix));
        }

        // --- the covered hit ------------------------------------------------------------------

        public static void HitCalc_Prefix(BattleFunction2 __instance)
        {
            hitGuard = hitGuardParam = hitGuardParam2 = IntPtr.Zero;
            if (!Enabled || __instance == null) return;
            try
            {
                var act = __instance.battleActData;
                var covered = act?.ProtectedTarget;
                var targets = act?.TargetUnitDatas;
                if (covered == null || targets == null || targets.Length != 1) return;
                var guard = targets[0];
                if (guard == null || guard.Pointer == covered.Pointer) return;
                hitGuard = guard.Pointer;
                hitGuardParam = guard.BattleUnitDataInfo?.Parameter?.Pointer ?? IntPtr.Zero;
                hitGuardParam2 = guard.TryCast<BattlePlayerData>()?.ownedCharacterData?.Parameter?.Pointer ?? IntPtr.Zero;
                Hooks.NoteOnce("cover-hit", () => $"[cover] a covered hit: {guard.TryCast<BattlePlayerData>()?.ownedCharacterData?.Name} " +
                                                  $"takes it for {covered.TryCast<BattlePlayerData>()?.ownedCharacterData?.Name} (Evade halved, no Interceptor)");
            }
            catch (Exception e) { Hooks.WarnOnce("CoverRules.HitCalc", e); }
        }

        public static void HitCalc_Postfix() => hitGuard = hitGuardParam = hitGuardParam2 = IntPtr.Zero;

        // BNW: Evade / 2 for the guard (255 - Block = Evade * 2 - 1, INC, LSR, LSR)
        public static void Evasion_Postfix(Last.Data.PlayerCharacterParameter __instance, ref int __result)
        {
            if (hitGuard == IntPtr.Zero || __instance == null) return;
            var p = __instance.Pointer;
            if (p == IntPtr.Zero || (p != hitGuardParam && p != hitGuardParam2)) return;
            int was = __result;
            __result = HalvedEvade(was);
            EvadeReads++;
            LastEvade = (was, __result);
            Hooks.NoteOnce("cover-evade", () => $"[cover] the covering knight's Evade {was} -> {HalvedEvade(was)} (BNW halves it)");
        }

        // for the self-test: how often each hook acted, the last Evade it changed
        internal static int EvadeReads, InterceptorSkips;
        internal static (int Was, int Now) LastEvade;

        internal static int HalvedEvade(int evade) => BattleMath.CoverEvade(evade);

        // BNW's SkipDogBlock: no Interceptor block on a hit the guard takes for someone else (the
        // act has the one target, the guard, so any roll here is the guard's)
        public static bool DefendInvoke_Prefix(WildFangConditionFunction __instance, ref bool __result)
        {
            if (hitGuard == IntPtr.Zero || __instance == null) return true;
            try { __instance.isDefendInvoke = false; }
            catch (Exception e) { Hooks.WarnOnce("CoverRules.DefendInvoke", e); }
            __result = false;
            InterceptorSkips++;
            Hooks.NoteOnce("cover-interceptor", () => "[cover] Interceptor doesn't block a covered hit (BNW)");
            return false;
        }

        // an attack run the way the game runs an act's functions (the kit's SnesUmaro.RunAct)
        internal static (BattleUnitData Took, Last.Systems.HitType Hit, string Error) RunAttack(
            BattleUnitData attacker, BattleUnitData target, Last.Data.Master.Command command, Last.Data.Master.Ability ability) =>
            PrKit.Ff6.SnesUmaro.RunAct(attacker, target, command, ability);

        // Probe battles: the covered hit through the game's own act flow. The guard is a knight and
        // has Interceptor (WildFang) on; the target is Near Fatal, so BNW's rules cover it. Each act
        // must land on the guard with its Evade read halved and Interceptor never rolling; the same
        // attack on the guard directly (no cover) must leave both alone (Interceptor blocks ~half).
        static string CoveredHitTest(BattleUnitData guard, BattleUnitData target, BattleUnitData enemy,
            BattleConditionController cc, Last.Data.Master.Command fight, Last.Data.Master.Ability attack)
        {
            const int N = 40;
            var tp = target.BattleUnitDataInfo.Parameter;
            int hpWas = tp.CurrentHP;
            bool dogWas = Has(Conditions(guard), ConditionType.WildFang);
            int onGuard = 0, misses = 0, errors = 0, evade0 = EvadeReads, skip0 = InterceptorSkips;
            string firstError = null;
            int dMisses = 0, dErrors = 0, dEvade, dSkip;
            try
            {
                if (!dogWas) cc.InterruptAddCondition(guard, ConditionType.WildFang);
                bool dog = Has(Conditions(guard), ConditionType.WildFang);
                tp.CurrentHP = Math.Max(1, tp.ConfirmedMaxHp() / 16);
                cc.InterruptAddCondition(target, ConditionType.Dying);
                for (int i = 0; i < N; i++)
                {
                    tp.CurrentHP = Math.Max(1, tp.ConfirmedMaxHp() / 16);
                    var r = RunAttack(enemy, target, fight, attack);
                    if (r.Error != null) { errors++; firstError ??= r.Error; continue; }
                    if (r.Took != null && r.Took.Pointer == guard.Pointer) onGuard++;
                    if (r.Hit == Last.Systems.HitType.Miss) misses++;
                }
                int evadeReads = EvadeReads - evade0, skips = InterceptorSkips - skip0;
                var lastEvade = LastEvade;
                cc.InterruptRemoveCondition(target, ConditionType.Dying);
                tp.CurrentHP = hpWas;
                // the control: the guard attacked directly, nobody covering
                int e1 = EvadeReads, s1 = InterceptorSkips;
                for (int i = 0; i < N; i++)
                {
                    var r = RunAttack(enemy, guard, fight, attack);
                    if (r.Error != null) { dErrors++; firstError ??= r.Error; continue; }
                    if (r.Hit == Last.Systems.HitType.Miss) dMisses++;
                }
                dEvade = EvadeReads - e1;
                dSkip = InterceptorSkips - s1;
                bool ok = errors == 0 && onGuard == N && evadeReads >= N && skips == (dog ? N : 0) && dEvade == 0 && dSkip == 0;
                return $"[cover] covered-hit self-test {(ok ? "ok" : "FAILED")}: {N} monster Attacks on the Near Fatal ally (knight " +
                       $"{(dog ? "with" : "WITHOUT")} Interceptor): {onGuard} landed on the knight, {misses} missed, Evade read halved " +
                       $"{evadeReads} times (last {lastEvade.Was} -> {lastEvade.Now}), Interceptor rolls skipped {skips}, errors {errors}; " +
                       $"{N} on the knight directly: {dMisses} missed (Interceptor rolls as usual), Evade halved {dEvade}, skipped {dSkip}, " +
                       $"errors {dErrors}{(firstError != null ? $" (first: {firstError})" : "")}";
            }
            finally
            {
                tp.CurrentHP = hpWas;
                cc.InterruptRemoveCondition(target, ConditionType.Dying);
                if (!dogWas) cc.InterruptRemoveCondition(guard, ConditionType.WildFang);
            }
        }

        // --- the guard ------------------------------------------------------------------------

        static readonly ConditionType[] TargetBlocked =
        {
            ConditionType.UnableFight, ConditionType.Mineralization, ConditionType.Transparent, ConditionType.Zombie,
            ConditionType.Madou, ConditionType.Blink,
        };
        internal static readonly ConditionType[] GuardBlocked =
        {
            ConditionType.UnableFight, ConditionType.Mineralization, ConditionType.Transparent, ConditionType.Zombie,
            ConditionType.Madou, ConditionType.Blind, ConditionType.Sleep, ConditionType.Confusion, ConditionType.Bersak,
            ConditionType.Blink, ConditionType.Dance, ConditionType.Stop, ConditionType.Freeze, ConditionType.Hide,
        };
        static readonly ConditionType[] Uncontrolled = { ConditionType.Confusion, ConditionType.Zombie, ConditionType.Temptation };

        static Il2CppSystem.Collections.Generic.List<ConditionType> Conditions(BattleUnitData u) =>
            u?.BattleUnitDataInfo?.GetConfirmedConditionType();

        static bool Has(Il2CppSystem.Collections.Generic.List<ConditionType> list, params ConditionType[] types)
        {
            if (list == null) return false;
            foreach (var t in types)
                if (list.Contains(t)) return true;
            return false;
        }

        static bool Knight(BattleUnitData u) => Has(Conditions(u), ConditionType.TipsForKnights);

        // BNW's guard for an attack on a party member, or null; knightNear: a knight stands among
        // the target's allies at all (else the game's choice stays)
        static BattleUnitData Choose(BattleActData act, BattleUnitData target, out bool knightNear, out string why)
        {
            knightNear = false;
            why = null;
            var battle = BattlePlugManager.Instance();
            var players = battle?.GetPlayerUnits();
            if (players == null) return null;
            var attacker = act.AttackUnitData;
            var units = new List<BattleUnitData>();
            var infos = new List<BattleMath.CoverGuardInfo>();
            for (int i = players.Count - 1; i >= 0; i--)   // BNW checks from the last slot down
            {
                BattleUnitData g = players[i];
                if (g == null || g.Pointer == target.Pointer || (attacker != null && g.Pointer == attacker.Pointer)) continue;
                var gc = Conditions(g);
                var p = g.BattleUnitDataInfo?.Parameter;
                bool knight = Has(gc, ConditionType.TipsForKnights);
                knightNear |= knight;
                infos.Add(new BattleMath.CoverGuardInfo
                {
                    Knight = knight,
                    BackRow = BattleUtility.GetCorpsId(g) == CorpsId.Back,
                    Dying = Has(gc, ConditionType.Dying),
                    Defending = Has(gc, ConditionType.Defend),
                    Blocked = p == null || p.CurrentHP <= 0 || Has(gc, GuardBlocked),
                    Stamina = p == null ? 0 : Math.Clamp(p.ConfirmedVitality(), 0, 255),
                    Hp = p?.CurrentHP ?? 0,
                });
                units.Add(g);
            }
            if (!knightNear) return null;
            if (attacker != null && attacker.TryCast<BattlePlayerData>() != null && !Has(Conditions(attacker), Uncontrolled)) return null;
            var tc = Conditions(target);
            if (Has(tc, TargetBlocked)) return null;
            bool dying = Has(tc, ConditionType.Dying);
            int pick = BattleMath.CoverGuard(dying, BattleUtility.GetCorpsId(target) == CorpsId.Back, infos, n => Rng.Next(n));
            if (pick < 0) return null;
            why = dying ? "Near Fatal ally" : $"back-row ally, Stamina {infos[pick].Stamina}{(infos[pick].Defending ? " (defending)" : "")}";
            return units[pick];
        }

        public static void Extract_Postfix(ref BattleActData __result)
        {
            if (!Enabled || __result == null) return;
            try
            {
                var act = __result;
                var targets = act.TargetUnitDatas;
                BattleUnitData covered = act.ProtectedTarget;
                BattleUnitData target = covered ?? (targets != null && targets.Length == 1 ? targets[0] : null);
                if (target == null || target.TryCast<BattlePlayerData>() == null) return;   // several targets / a monster: the game's
                BattleUnitData gameGuard = covered != null && targets != null && targets.Length == 1 && targets[0].Pointer != covered.Pointer
                    ? targets[0] : null;
                if (gameGuard != null && !Knight(gameGuard)) return;   // some other cover: the game's
                var guard = Choose(act, target, out bool knightNear, out string why);
                if (!knightNear) return;
                if (guard != null && gameGuard != null && guard.Pointer == gameGuard.Pointer) return;   // the game chose the same
                if (guard == null)
                {
                    if (covered == null) return;
                    Uncover(act, target);
                    Hooks.NoteOnce("cover-undone", () => "[cover] the game's knight cover undone: BNW's rules say no");
                    return;
                }
                Cover(act, guard, target);
                if (Has(Conditions(guard), ConditionType.Defend))
                    BattlePlugManager.Instance()?.BattleConditionController?.InterruptRemoveCondition(guard, ConditionType.Defend);
                Hooks.NoteOnce("cover-" + (why.StartsWith("Near") ? "dying" : "stamina"), () => $"[cover] a True Knight covers a {why} (BNW)");
            }
            catch (Exception e) { Hooks.WarnOnce("CoverRules", e); }
        }

        // the game's own cover: what SetProtectData leaves on the act (the probe's self-test logs
        // it); both directions keep the act's targets in step with it
        internal static bool SwapsTargets = true;

        static void Cover(BattleActData act, BattleUnitData guard, BattleUnitData target)
        {
            act.SetProtectData(guard, target, null);
            if (SwapsTargets)
            {
                var t = new Il2CppReferenceArray<BattleUnitData>(1);
                t[0] = guard;
                act.TargetUnitDatas = t;
            }
        }

        static void Uncover(BattleActData act, BattleUnitData target)
        {
            act.ProtectedTarget = null;
            act.ProtectUnitOthers = null;
            var t = new Il2CppReferenceArray<BattleUnitData>(1);
            t[0] = target;
            act.TargetUnitDatas = t;
        }

        // Probe runs only (FormulaProbe.Running: a throwaway battle the game then quits): a
        // front-row member made a knight, another member Near Fatal and then healthy, a monster's
        // Attack on it through the game's TargetFunction.ProtectExtractTarget, with the game's own
        // rules (this postfix off) and BNW's. Null until such a battle.
        internal static string SelfTest()
        {
            if (!Enabled) return "[cover] self-test: off";
            if (!FormulaProbe.Running || GameHarness.Active) return null;   // the harness plays real battles
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return null;
            var players = battle.GetPlayerUnits();
            var enemies = battle.GetEnemyUnits();
            var cc = battle.BattleConditionController;
            var mm = Last.Data.Master.MasterManager.Instance;
            var fight = mm?.GetData<Last.Data.Master.Command>(1);
            var attack = mm?.GetData<Last.Data.Master.Ability>(1);
            if (players == null || players.Count < 2 || enemies == null || enemies.Count == 0 || cc == null || fight == null || attack == null)
                return null;
            int gi = 1;
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && BattleUtility.GetCorpsId(players[i]) != CorpsId.Back) { gi = i; break; }
            int ti = gi == 0 ? 1 : 0;
            BattleUnitData target = players[ti], guard = players[gi], enemy = enemies[0];
            var tp = target?.BattleUnitDataInfo?.Parameter;
            if (tp == null || guard == null) return null;

            string Run(bool bnw)
            {
                var abilities = new Il2CppSystem.Collections.Generic.List<Last.Data.Master.Ability>();
                abilities.Add(attack);
                var targets = new Il2CppReferenceArray<BattleUnitData>(1);
                targets[0] = target;
                var act = new BattleActData(enemy, targets, fight, abilities, BattleActData.ActType.Normal,
                    (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
                bool was = Enabled;
                Enabled = bnw;
                BattleActData result;
                try { result = TargetFunction.ProtectExtractTarget(act); }
                finally { Enabled = was; }
                var now = result?.TargetUnitDatas;
                string to = now == null || now.Length == 0 ? "none" : now[0].Pointer == guard.Pointer ? "knight"
                    : now[0].Pointer == target.Pointer ? "target" : "other";
                return $"{(result?.ProtectedTarget != null ? "covered" : "not covered")}, act targets {to}";
            }

            int hpWas = tp.CurrentHP;
            try
            {
                cc.InterruptAddCondition(guard, ConditionType.TipsForKnights);
                bool knight = Knight(guard);
                tp.CurrentHP = Math.Max(1, tp.ConfirmedMaxHp() / 16);
                cc.InterruptAddCondition(target, ConditionType.Dying);
                bool dyingSet = Has(Conditions(target), ConditionType.Dying);
                bool guardBack = BattleUtility.GetCorpsId(guard) == CorpsId.Back;
                bool targetBack = BattleUtility.GetCorpsId(target) == CorpsId.Back;
                bool blocked = Has(Conditions(guard), GuardBlocked);
                string gameDying = Run(false), bnwDying = Run(true);
                bool expected = knight && dyingSet && !guardBack && !blocked;
                cc.InterruptRemoveCondition(target, ConditionType.Dying);
                tp.CurrentHP = hpWas;
                string gameHealthy = Run(false), bnwHealthy = Run(true);
                bool ok = bnwDying.StartsWith(expected ? "covered" : "not covered")
                          && (targetBack || bnwHealthy.StartsWith("not covered"));
                string hit = expected ? CoveredHitTest(guard, target, enemy, cc, fight, attack)
                                      : "[cover] covered-hit self-test skipped: BNW wouldn't cover here";
                return hit + System.Environment.NewLine + $"[cover] self-test {(ok ? "ok" : "FAILED")}: knight set {knight}, Near Fatal set {dyingSet}, knight " +
                       $"{(guardBack ? "back" : "front")} row{(blocked ? ", blocked" : "")}, target {(targetBack ? "back" : "front")} row; " +
                       $"Near Fatal target: the game's rules {gameDying}, BNW's {bnwDying} (BNW: {(expected ? "covered" : "not covered")}); " +
                       $"healthy target: the game's {gameHealthy}, BNW's {bnwHealthy}";
            }
            finally
            {
                tp.CurrentHP = hpWas;
                cc.InterruptRemoveCondition(target, ConditionType.Dying);
                cc.InterruptRemoveCondition(guard, ConditionType.TipsForKnights);
            }
        }
    }
}
