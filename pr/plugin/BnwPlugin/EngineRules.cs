using System;
using System.Collections.Generic;
using System.Linq;
using BraveNewWorld.Rules;
using HarmonyLib;
using Last.Battle;
using Last.Battle.Function;
using Last.Data;
using Last.Data.Master;
using Last.Defaine;
using Last.Systems;
using Hooks = PrKit.Hooks;
using Il2CppIntMap = Il2CppSystem.Collections.Generic.Dictionary<int, int>;

namespace BraveNewWorld.PR
{
    // BNW battle-engine rules from the research list's last open boxes (2026-11-20, BNW asm refs):
    //  * Elemental Share (c2.asm C20BD3, c0.asm EleModLoop): an attack with several elements is
    //    rated per element on the target (immune 0, resistant 1, normal 2, weak 4 points); no points
    //    = no damage, points / 2 against the number of elements = normal, more = double, fewer = half
    //    (vanilla and the PR: the strongest single response wins, so Weak + Immune = 0 damage where
    //    BNW does normal damage). The PR rates elements in CalcExecuteFF6.DamageAppliedResistanceAttribute
    //    (called by MagicExecution; it returns a ValueTuple, so it isn't patched): while the game works
    //    out a multi-element spell (CalcControllerProvider.GetMagicStatus), the target's element table
    //    (ConfirmedResistanceAttributeList, vtable slot 33, the one that method reads) reads with every
    //    attack element normal but one carrying BNW's outcome (BattleMath.ElementView), so the game's
    //    own rule gives BNW's. 16 BNW spells have 2+ elements (Storm, Merton, Tritoch, Blizzard...).
    //  * Mug (Bropedio's "Mug Better", c0.asm MugHelper): the strike misses unless something was
    //    stolen. The PR's Mug is BattleFunction23 (ability 9, "StealAttack"): HitCalc has
    //    UniqueUpdateParameterProvider work out the hit (GetFightStatus) and, unless it missed, the
    //    steal (GetStealStatus, CommandRules' BNW roll); after HitCalc a target whose steal came back
    //    empty gets a Miss (no damage, no weapon effect).
    //  * Rage (c2.asm C22E17 GauRageStatuses): raging as a monster that blocks a status cures it on
    //    Gau (statuses of SNES bytes 1-2: Blind, Zombie, Poison, Vanish, Imp, Petrify, Doom, Image,
    //    Mute, Berserk, Muddle, Sap, Sleep), unless Gau's own gear blocked it already. BNW's other Rage
    //    change, the rage's innate statuses cleared when Rage ends (RageClear), is the PR's own:
    //    RampageConditionFunction.Remove takes the start conditions back off (decompiled
    //    tools/decomp/bnw_engine).
    static class EngineRules
    {
        internal static bool Elements = true, Mug = true, Rage = true;
        static readonly Dictionary<int, int[]> abilityElements = new Dictionary<int, int[]>();

        public static void Apply(Harmony h, bool damageRules, bool stealRules, bool turnRules)
        {
            Elements = damageRules;
            Mug = stealRules;
            Rage = turnRules;
            var calc = typeof(CalcControllerProvider);
            Hooks.Patch(h, calc, "GetMagicStatus", typeof(EngineRules), prefix: nameof(Magic_Prefix), postfix: nameof(Magic_Postfix));
            // after EsperHooks' postfix on the same method (its equip halves come first)
            Hooks.Patch(h, typeof(PlayerCharacterParameter), "ConfirmedResistanceAttributeList", typeof(EngineRules), postfix: nameof(Resist_Postfix));
            Hooks.Patch(h, typeof(EnemyCharacterParameter), "ConfirmedResistanceAttributeList", typeof(EngineRules), postfix: nameof(Resist_Postfix));
            Hooks.Patch(h, typeof(BattleFunction23), "HitCalc", typeof(EngineRules), prefix: nameof(Mug_Prefix), postfix: nameof(Mug_Postfix));
            Hooks.Patch(h, typeof(RampageConditionFunction), "Start", typeof(EngineRules), prefix: nameof(Rage_Prefix), postfix: nameof(Rage_Postfix));
            Tools = damageRules;
            Hooks.Patch(h, typeof(BattleFunction46), "CreateAbility", typeof(EngineRules), prefix: nameof(Chainsaw_Prefix));
            Hooks.Patch(h, typeof(BattleFunction2), "HitCalc", typeof(EngineRules), postfix: nameof(Landing_Postfix));
            Hooks.Patch(h, typeof(Serial.FF6.Management.SerialBattleUtility), "CreateContiniousActData", typeof(EngineRules), prefix: nameof(Strikes_Prefix));
            EngineTests.Register(h);
        }

        // --- Elemental Share ----------------------------------------------------------------------

        [ThreadStatic] static int[] calcElements;
        [ThreadStatic] static IntPtr calcTarget;
        internal static int ViewsApplied;          // tests: tables read BNW's way
        internal static Dictionary<int, int> TestTable;   // tests: the target's table before the rule
        internal static IntPtr TestTarget;

        internal static int[] ElementsOf(CalcControllerProvider c, Ability ability)
        {
            if (ability == null) return Array.Empty<int>();
            if (abilityElements.TryGetValue(ability.Id, out var known)) return known;
            int[] list;
            try
            {
                var arr = c.GetAbilityAttributeArray(ability);
                list = arr == null ? Array.Empty<int>() : arr.ToArray();
            }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.Elements", e); list = Array.Empty<int>(); }
            abilityElements[ability.Id] = list;
            return list;
        }

        public static void Magic_Prefix(CalcControllerProvider __instance, out bool __state)
        {
            __state = false;
            if (!Elements || calcElements != null || __instance == null || !Hooks.IsA<CalcControllerProvider>(__instance)) return;
            try
            {
                var target = __instance.target;
                if (target == null) return;
                var elems = ElementsOf(__instance, __instance.ability);
                if (elems.Length < 2) return;
                calcElements = elems;
                calcTarget = target.Pointer;
                __state = true;
            }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.MagicPrefix", e); }
        }

        public static void Magic_Postfix(bool __state)
        {
            if (!__state) return;
            calcElements = null;
            calcTarget = IntPtr.Zero;
        }

        public static void Resist_Postfix(CharacterParameterBase __instance, ref Il2CppIntMap __result)
        {
            if (__instance == null) return;
            var ptr = __instance.Pointer;
            if (TestTable != null && ptr == TestTarget) __result = ToIl2Cpp(TestTable);
            if (calcElements == null || ptr != calcTarget) return;
            try
            {
                var table = new Dictionary<int, int>();
                if (__result != null)
                    foreach (var kv in __result) table[kv.Key] = kv.Value;
                var view = BattleMath.ElementView(table, calcElements);
                if (view == null) return;
                __result = ToIl2Cpp(view);
                ViewsApplied++;
                var elems = string.Join("+", calcElements);
                Hooks.NoteOnce("element-share", () => $"[elements] multi-element attack ({elems}): BNW's share on the target's table");
            }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.Resist", e); }
        }

        static Il2CppIntMap ToIl2Cpp(Dictionary<int, int> d)
        {
            var m = new Il2CppIntMap();
            foreach (var kv in d) m.Add(kv.Key, kv.Value);
            return m;
        }

        // --- Mug ----------------------------------------------------------------------------------

        // target parameter -> something stolen, for the Mug being worked out (CommandRules records it)
        static readonly Dictionary<IntPtr, bool> mugSteals = new Dictionary<IntPtr, bool>();
        static bool mugActive;
        internal static int MugMisses;   // tests

        internal static void NoteSteal(CharacterParameterBase target, bool stolen)
        {
            if (mugActive && target != null) mugSteals[target.Pointer] = stolen;
        }

        public static void Mug_Prefix()
        {
            mugSteals.Clear();
            mugActive = true;
        }

        public static void Mug_Postfix(BattleFunction23 __instance)
        {
            mugActive = false;
            if (__instance == null) return;
            try { GpRainBlind(__instance); }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.GpRain", e); }
            if (!Mug || mugSteals.Count == 0) return;
            try
            {
                var act = __instance.battleActData;
                // a character's Mug (the PR's monsters steal with ability 693, not Mug)
                if (act?.AttackUnitData?.TryCast<BattlePlayerData>() == null) return;
                var dic = __instance.ICalcResultDic;
                if (dic == null) return;
                var keys = new List<BattleUnitData>();
                foreach (var k in dic.Keys) keys.Add(k);
                foreach (var unit in keys)
                {
                    var p = unit?.BattleUnitDataInfo?.Parameter;
                    if (p == null || !mugSteals.TryGetValue(p.Pointer, out bool stolen) || stolen) continue;
                    var r = dic[unit];
                    var hit = r.GetHitType();
                    if (hit != HitType.Hit && hit != HitType.Critical) continue;
                    r.SetStatus(0, HitType.Miss, r.GetHitCount(), new Il2CppSystem.Collections.Generic.List<Condition>(), false);
                    MugMisses++;
                    Hooks.NoteOnce("mug-miss", () => "[commands] Mug: nothing stolen, the strike misses (BNW)");
                }
            }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.Mug", e); }
            finally { mugSteals.Clear(); }
        }

        // GP Rain (ability 15, "GIL_THROW": BattleFunction23 too). The PR's (UniqueUpdateParameterProvider
        // .HitCalcGilThrow) never misses; BNW sends it through hit determination like Steal (c2.asm
        // C21908 CoinHelp): hit rate 255, 50 from a blinded thrower (BlindHelp), against Evade.
        static readonly Random Rng = new Random();
        internal static int GpRainMisses;   // tests

        static void GpRainBlind(BattleFunction23 f)
        {
            if (!Mug) return;
            var act = f.battleActData;
            var abilities = act?.abilityList;
            if (abilities == null || abilities.Count == 0 || abilities[0]?.ProcessProg != "GIL_THROW") return;
            var thrower = act.AttackUnitData;
            var status = thrower?.BattleUnitDataInfo?.GetConfirmedConditionType();
            if (thrower?.TryCast<BattlePlayerData>() == null || status == null || !status.Contains(ConditionType.Blind)) return;
            var dic = f.ICalcResultDic;
            if (dic == null) return;
            var keys = new List<BattleUnitData>();
            foreach (var k in dic.Keys) keys.Add(k);
            foreach (var unit in keys)
            {
                var r = dic[unit];
                var hit = r.GetHitType();
                if (hit != HitType.Hit && hit != HitType.Critical) continue;
                int evade = Math.Clamp(unit?.BattleUnitDataInfo?.Parameter?.ConfirmedEvasionRate() ?? 0, 0, 128);
                int chance = 50 * Math.Max(0, 256 - evade * 2) / 256;
                if (chance > Rng.Next(100)) continue;
                r.SetStatus(0, HitType.Miss, r.GetHitCount(), new Il2CppSystem.Collections.Generic.List<Condition>(), false);
                GpRainMisses++;
                Hooks.NoteOnce("gprain-blind", () => $"[commands] GP Rain from a blinded thrower missed (BNW: hit rate 50, Evade {evade})");
            }
        }

        // --- Blind on Jump (dn's "Blind Jump Fix", c3.asm C3_BlindJump2) -----------------------------------
        // BNW's landing can't miss unless the jumper is blinded; then it goes through hit determination with
        // hit rate 50 (BlindHelp) against the target's Evade. The PR's landing (ability 22, type 16) has
        // content flag 1 ("can't miss"), so Blind never touched it (self-test: 40 of 40 blinded landings hit).
        const int JumpLandingType = 16;
        internal static int LandingMisses;   // tests

        public static void Landing_Postfix(BattleFunction2 __instance)
        {
            if (!Elements || __instance == null) return;   // with the damage rules (BlindRules' switch)
            try
            {
                var act = __instance.battleActData;
                var abilities = act?.abilityList;
                if (abilities == null || abilities.Count == 0 || abilities[0] == null || abilities[0].TypeId != JumpLandingType) return;
                var status = act.AttackUnitData?.BattleUnitDataInfo?.GetConfirmedConditionType();
                if (status == null || !status.Contains(ConditionType.Blind)) return;
                var dic = __instance.ICalcResultDic;
                if (dic == null) return;
                var keys = new List<BattleUnitData>();
                foreach (var k in dic.Keys) keys.Add(k);
                foreach (var unit in keys)
                {
                    var r = dic[unit];
                    var hit = r.GetHitType();
                    if (hit != HitType.Hit && hit != HitType.Critical) continue;
                    int evade = Math.Clamp(unit?.BattleUnitDataInfo?.Parameter?.ConfirmedEvasionRate() ?? 0, 0, 128);
                    int chance = 50 * Math.Max(0, 256 - evade * 2) / 256;
                    if (chance > Rng.Next(100)) continue;
                    r.SetStatus(0, HitType.Miss, r.GetHitCount(), new Il2CppSystem.Collections.Generic.List<Condition>(), false);
                    LandingMisses++;
                    Hooks.NoteOnce("jump-blind", () => $"[commands] a blinded jumper's landing missed (BNW: hit rate 50, Evade {evade})");
                }
            }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.Landing", e); }
        }

        // --- Chainsaw: a death-immune target takes the cut (c2.asm ChainInit / Chainsaw2) -------------
        // The PR's Chainsaw (item 33: function 46, a random ability of group 10) picks ability 205 (the
        // KO status, no damage) 25% and 206 (the cut) 75% in BattleFunction46.SetRandomAbility, then
        // builds that act with CreateAbility (decompiled tools/decomp/chainsaw), as vanilla: a kill on an
        // immune target does nothing. BNW: the 25% kill only where it can land, else the cut.
        internal static bool Tools = true;
        internal const int ChainsawKill = 205, ChainsawCut = 206, KoCondition = 5;   // 205: function 9, condition 5
        internal static IntPtr TestImmune;   // tests: this parameter counts as death-immune
        internal static int ChainsawSwaps;

        internal static bool DeathImmune(BattleUnitData u)
        {
            var p = u?.BattleUnitDataInfo?.Parameter;
            if (p == null) return false;
            if (TestImmune != IntPtr.Zero && p.Pointer == TestImmune) return true;
            var m = p.ConfirmedResistanceCondtionList();
            return m != null && m.ContainsKey(KoCondition) && m[KoCondition] == 1;
        }

        public static void Chainsaw_Prefix(BattleFunction46 __instance, ref Ability nextAbility)
        {
            if (!Tools || nextAbility == null || nextAbility.Id != ChainsawKill || __instance == null) return;
            try
            {
                var targets = __instance.battleActData?.TargetUnitDatas;
                if (targets == null || targets.Length == 0 || !DeathImmune(targets[0])) return;
                var cut = MasterManager.Instance?.GetData<Ability>(ChainsawCut);
                if (cut == null) return;
                nextAbility = cut;
                ChainsawSwaps++;
                Hooks.NoteOnce("chainsaw-immune", () => "[tools] Chainsaw's kill on a death-immune target: the cut instead (BNW)");
            }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.Chainsaw", e); }
        }

        // --- dual-wield Mug: only the first strike mugs (Bropedio's "Mug Better", c2.asm C231AE) ---------
        // The PR's second strike (SerialBattleUtility.CreateContiniousActData, secondlyAttack) copies the
        // act's own command and ability (decompiled tools/decomp/dualwield), so a dual-wield Mug would mug
        // twice; BNW resets the backup command to Fight: the second strike is a plain Attack.
        internal static int MugSecondStrikes;

        public static void Strikes_Prefix(ref Command command, ref Ability ability, bool secondlyAttack, bool randomAttack)
        {
            if (!Mug || !secondlyAttack || randomAttack || command == null || command.Id != MugCommand) return;
            try
            {
                var mm = MasterManager.Instance;
                var fight = mm?.GetData<Command>(1);
                var attack = mm?.GetData<Ability>(1);
                if (fight == null || attack == null) return;
                command = fight;
                ability = attack;
                MugSecondStrikes++;
                Hooks.NoteOnce("mug-second", () => "[commands] dual-wield Mug: the second strike is a plain Attack (BNW)");
            }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.MugStrikes", e); }
        }

        const int MugCommand = 11;

        // --- Rage ---------------------------------------------------------------------------------

        // SNES status bytes 1-2 as PR condition types (Magitek, Death and Near Fatal left out: no monster blocks them)
        static readonly HashSet<ConditionType> RageCurable = new HashSet<ConditionType>
        {
            ConditionType.Blind, ConditionType.Zombie, ConditionType.Poison, ConditionType.Vanish, ConditionType.Transparent,
            ConditionType.Kappa, ConditionType.Mineralization, ConditionType.Doom, ConditionType.DoomEx, ConditionType.Blink,
            ConditionType.Silence, ConditionType.Mute, ConditionType.Bersak, ConditionType.Confusion, ConditionType.Slip,
            ConditionType.Sleep,
        };
        internal static readonly List<string> RageCured = new List<string>();   // tests: what the last rage cured

        static HashSet<int> Blocked(BattleUnitData u)
        {
            var set = new HashSet<int>();
            var m = u?.BattleUnitDataInfo?.Parameter?.ConfirmedResistanceCondtionList();
            if (m != null)
                foreach (var kv in m) if (kv.Value == 1) set.Add(kv.Key);
            return set;
        }

        public static void Rage_Prefix(RampageConditionFunction __instance, out HashSet<int> __state)
        {
            __state = null;
            if (!Rage || __instance == null) return;
            try { __state = Blocked(__instance.BattleUnitData); }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.RagePrefix", e); }
        }

        public static void Rage_Postfix(RampageConditionFunction __instance, HashSet<int> __state)
        {
            if (!Rage || __state == null || __instance == null) return;
            try
            {
                CureNewlyBlocked(__instance.BattleUnitData, __state, Blocked(__instance.BattleUnitData));
            }
            catch (Exception e) { Hooks.WarnOnce("EngineRules.Rage", e); }
        }

        // the statuses `unit` has that the rage blocks (`now`) and its own gear didn't (`before`) come off
        internal static void CureNewlyBlocked(BattleUnitData unit, HashSet<int> before, HashSet<int> now)
        {
            RageCured.Clear();
            var have = unit?.BattleUnitDataInfo?.GetConfirmedConditionType();
            if (have == null || have.Count == 0) return;
            var mm = MasterManager.Instance;
            var controller = BattlePlugManager.Instance()?.BattleConditionController;
            foreach (int id in now)
            {
                if (before.Contains(id)) continue;   // the rager's own gear blocked it already
                var cond = mm?.GetData<Condition>(id);
                if (cond == null) continue;
                var type = (ConditionType)cond.ConditionType;
                if (!RageCurable.Contains(type) || !have.Contains(type)) continue;
                controller?.InterruptRemoveCondition(unit, type);
                RageCured.Add(type.ToString());
            }
            if (RageCured.Count > 0)
            {
                var cured = string.Join(", ", RageCured);
                Hooks.NoteOnce("rage-cure", () => $"[commands] Rage: the monster blocks {cured}, cured on the rager (BNW)");
            }
        }
    }
}
