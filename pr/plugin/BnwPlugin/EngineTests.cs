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
using Last.Defaine;
using Last.Defaine.User;
using Last.Management;
using Last.Systems;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // The battle-engine checks of 2026-11-20 (bnw-engine), run in the probe's battle through the game's
    // own act flow (ParameterActFunctionManagment.GetFunction(ability), then each function's Init,
    // FixedTarget, HitCalc; no UpdateParameter, so nothing is applied): the hit in the back, BNW's
    // elemental share, Mug, Blind on Jump and Steal, dual wield and Offering, Shock's cost, Sketch's
    // Magic, Mimic, the back-row True Knight cover, the PR's accessory lists, and the save's EL block.
    // Each registers with FormulaProbe.AddSelfTest: null until it can run, then "[engine] ... ok/FAILED".
    static class EngineTests
    {
        static bool forceSide;

        internal static void Register(Harmony h)
        {
            // tests only: a Fight worked out as a hit in the back (CalcInfo.isSideAttack)
            Hooks.Patch(h, typeof(CalcControllerProvider), "GetFightStatus", typeof(EngineTests), prefix: nameof(Fight_Prefix));
            Add("back hit", BackHit);
            Add("elements", Elements);
            Add("mug", Mug);
            Add("blind", Blind);
            Add("dual wield", DualWield);
            Add("offering", Offering);
            Add("grip", Grip);
            Add("chainsaw", Chainsaw);
            Add("mug second strike", MugSecondStrike);
            Add("x-magic counter", XMagicCounter);
            Add("noise blaster", NoiseBlaster);
            // tests only: acts the game would queue (CreateContiniousActData's extra strikes) are caught instead
            Hooks.Patch(h, typeof(BattleActExection), "SetBattleActData", typeof(EngineTests), prefix: nameof(Queue_Prefix),
                args: new[] { typeof(BattleActData) });
            Add("shock cost", ShockCost);
            Add("sketch", Sketch);
            Add("mimic", Mimic);
            Add("rage statuses", RageStatuses);
            Add("back-row cover", BackRowCover);
            Add("accessory lists", AccessoryLists);
            Add("EL in the save", ElSave);
        }

        static void Add(string name, Func<string> test) =>
            FormulaProbe.AddSelfTest("engine " + name, () => !FormulaProbe.Running || GameHarness.Active ? null : test());

        public static void Fight_Prefix(CalcControllerProvider __instance)
        {
            if (!forceSide || __instance == null) return;
            var ci = __instance.calcInfo;
            if (ci == null) return;
            ci.isSideAttack = true;
            __instance.calcInfo = ci;
        }

        // --- helpers -------------------------------------------------------------------------------

        sealed class Ctx
        {
            public BattlePlugManager Battle;
            public List<BattleUnitData> Players = new List<BattleUnitData>(), Enemies = new List<BattleUnitData>();
            public BattleUnitData P0 => Players[0];
            public BattleUnitData E0 => Enemies[0];
        }

        static Ctx Battle()
        {
            var b = BattlePlugManager.Instance();
            if (b == null || !b.IsBattle()) return null;
            var c = new Ctx { Battle = b };
            foreach (var u in b.GetPlayerUnits())
                if (u?.BattleUnitDataInfo?.Parameter != null && u.BattleUnitDataInfo.Parameter.CurrentHP > 0) c.Players.Add(u);
            foreach (var u in b.GetEnemyUnits())
                if (u?.BattleUnitDataInfo?.Parameter != null && u.BattleUnitDataInfo.Parameter.CurrentHP > 0) c.Enemies.Add(u);
            return c.Players.Count == 0 || c.Enemies.Count == 0 ? null : c;
        }

        static Command Cmd(int id) => MasterManager.Instance?.GetData<Command>(id);
        static Ability Ab(int id) => MasterManager.Instance?.GetData<Ability>(id);

        struct Hit
        {
            public HitType Type;
            public int Value, Game;
            public double Factor;
            public bool Struck => Type == HitType.Hit || Type == HitType.Critical;
        }

        // one act of `ab` under `cmd` by `attacker` on `target`, every function of the ability
        static Hit Run(BattleUnitData attacker, BattleUnitData target, Command cmd, Ability ab)
        {
            var h = new Hit { Type = HitType.Non };
            var abilities = new Il2CppSystem.Collections.Generic.List<Ability>();
            abilities.Add(ab);
            var targets = new Il2CppReferenceArray<BattleUnitData>(1);
            targets[0] = target;
            var act = new BattleActData(attacker, targets, cmd, abilities, BattleActData.ActType.Normal,
                (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
            var fns = ParameterActFunctionManagment.GetFunction(ab);
            if (fns == null) return h;
            var results = new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>();
            var attacks = new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>();
            BattleFormulas.LastGameValue = 0;
            BattleFormulas.LastFactor = 1.0;
            foreach (var f in fns)
            {
                if (f == null) continue;
                f.Init(act, results, attacks);
                f.FixedTarget();
                f.HitCalc();
                var dic = f.ICalcResultDic ?? results;
                if (dic != null && dic.ContainsKey(target))
                {
                    var r = dic[target];
                    h.Type = r.GetHitType();
                    h.Value = r.GetValue(false);
                }
            }
            h.Game = BattleFormulas.LastGameValue;
            h.Factor = BattleFormulas.LastFactor;
            return h;
        }

        static List<Hit> Many(int n, Func<Hit> one)
        {
            var list = new List<Hit>();
            for (int i = 0; i < n; i++) list.Add(one());
            return list;
        }

        static double Mean(IEnumerable<int> xs) { var l = xs.ToList(); return l.Count == 0 ? 0 : l.Average(); }
        static string Ok(bool ok) => ok ? "ok" : "FAILED";
        static string Name(BattleUnitData u) => u?.TryCast<BattlePlayerData>()?.ownedCharacterData?.Name
                                                 ?? $"monster {u?.TryCast<BattleEnemyData>()?.GetMonster()?.Id}";
        static Last.Data.User.OwnedCharacterData OwnedOf(BattleUnitData u) => u?.TryCast<BattlePlayerData>()?.ownedCharacterData;

        // --- the hit in the back (c2.asm: "Back-attack damage increment is removed") ---------------

        static string BackHit()
        {
            var c = Battle();
            var fight = Cmd(1);
            var attack = Ab(1);
            if (c == null || fight == null || attack == null) return null;
            List<Hit> plain, side;
            try
            {
                forceSide = false;
                plain = Many(40, () => Run(c.P0, c.E0, fight, attack));
                forceSide = true;
                side = Many(40, () => Run(c.P0, c.E0, fight, attack));
            }
            finally { forceSide = false; }
            var p = plain.Where(x => x.Type == HitType.Hit && x.Game > 0).ToList();
            var s = side.Where(x => x.Type == HitType.Hit && x.Game > 0).ToList();
            double game = Mean(s.Select(x => x.Game)) / Math.Max(1, Mean(p.Select(x => x.Game)));
            double final = Mean(s.Select(x => x.Value)) / Math.Max(1, Mean(p.Select(x => x.Value)));
            bool ok = p.Count >= 10 && s.Count >= 10 && game > 1.3 && game < 1.7 && final > 0.85 && final < 1.15;
            return $"[engine] back-hit self-test {Ok(ok)}: Fight by {Name(c.P0)} on {Name(c.E0)}, 40 plain / 40 as a hit in the back " +
                   $"(CalcInfo.isSideAttack): the game's damage x{game:0.00} (its x1.5), BNW's x{final:0.00} (BNW: x1); hits {p.Count}/{s.Count}, " +
                   $"misses {plain.Count(x => x.Type == HitType.Miss)}/{side.Count(x => x.Type == HitType.Miss)} (the back hit can't miss)";
        }

        // --- BNW's elemental share ----------------------------------------------------------------

        static string Elements()
        {
            var c = Battle();
            var provider = BattleControllerManagement.Instance()?.GetCurrentProvider()?.TryCast<CalcControllerProvider>();
            if (c == null || provider == null) return null;
            var mm = MasterManager.Instance;
            var tp = c.E0.BattleUnitDataInfo.Parameter;
            var magic = Cmd(7);
            bool wasOn = EngineRules.Elements;
            try
            {
                // spells first (ability type 1), then anything else with elements
                var all = new List<Ability>();
                foreach (var a in mm.GetList<Ability>().Values)
                    if (a != null && a.StandardValue > 0 && a.AttributeGroupId != 0) all.Add(a);
                all = all.OrderBy(a => a.TypeId == 1 ? 0 : 1).ThenBy(a => a.Id).ToList();
                foreach (var ab in all)
                {
                    var elems = EngineRules.ElementsOf(provider, ab).Where(e => e > 0).Distinct().ToArray();
                    if (elems.Length < 2) continue;
                    EngineRules.TestTarget = tp.Pointer;
                    EngineRules.Elements = true;
                    EngineRules.TestTable = new Dictionary<int, int>();
                    var plain = Many(8, () => Run(c.P0, c.E0, magic, ab));
                    if (plain.Count(x => x.Struck && x.Value > 0) < 3) continue;   // not a damage spell here
                    // weak to one, immune to another: BNW 4 + 0 (+ 2 per other element) = normal damage; the game: none
                    EngineRules.TestTable = new Dictionary<int, int> { [elems[0]] = BattleMath.ElemWeak, [elems[1]] = BattleMath.ElemNull };
                    int views = EngineRules.ViewsApplied;
                    var bnw = Many(8, () => Run(c.P0, c.E0, magic, ab));
                    views = EngineRules.ViewsApplied - views;
                    EngineRules.Elements = false;
                    var game = Many(8, () => Run(c.P0, c.E0, magic, ab));
                    var ph = plain.Where(x => x.Struck && x.Value > 0).ToList();
                    var bh = bnw.Where(x => x.Struck && x.Value > 0).ToList();
                    int gh = game.Count(x => x.Struck && x.Value > 0);
                    double ratio = Mean(bh.Select(x => x.Value)) / Math.Max(1, Mean(ph.Select(x => x.Value)));
                    bool ok = bh.Count >= 3 && ratio > 0.75 && ratio < 1.33 && gh == 0 && views > 0;
                    return $"[engine] elements self-test {Ok(ok)}: ability {ab.Id} (elements {string.Join("+", elems)}) by {Name(c.P0)} on " +
                           $"{Name(c.E0)} made weak to {elems[0]} and immune to {elems[1]}: BNW's share {bh.Count}/8 hits at x{ratio:0.00} " +
                           $"of the all-normal damage (BNW: x1), the game's own rule {gh}/8 hits (vanilla: none); tables read BNW's way {views}";
                }
                return "[engine] elements self-test FAILED: no multi-element damage ability hit the monster";
            }
            finally
            {
                EngineRules.Elements = wasOn;
                EngineRules.TestTable = null;
                EngineRules.TestTarget = IntPtr.Zero;
            }
        }

        // --- Mug ----------------------------------------------------------------------------------

        static string Mug()
        {
            var c = Battle();
            var mug = Cmd(11);
            var ab = Ab(9);
            if (c == null || mug == null || ab == null) return null;
            List<Hit> failed, stolen;
            int misses = EngineRules.MugMisses;
            try
            {
                CommandRules.ForceSteal = false;
                failed = Many(20, () => Run(c.P0, c.E0, mug, ab));
                misses = EngineRules.MugMisses - misses;
                CommandRules.ForceSteal = true;
                stolen = Many(20, () => Run(c.P0, c.E0, mug, ab));
            }
            finally { CommandRules.ForceSteal = null; }
            int fh = failed.Count(x => x.Struck), sh = stolen.Count(x => x.Struck);
            // BNW's Mug hits with its Fight formula: the same factor on the game's value as an Attack's
            var fightCmd = Cmd(1);
            var attackAb = Ab(1);
            var fights = Many(10, () => Run(c.P0, c.E0, fightCmd, attackAb)).Where(x => x.Type == HitType.Hit && x.Game > 0).ToList();
            var mugs = stolen.Where(x => x.Type == HitType.Hit && x.Game > 0).ToList();
            double fFight = fights.Count > 0 ? fights.Average(x => x.Factor) : 0, fMug = mugs.Count > 0 ? mugs.Average(x => x.Factor) : 0;
            bool formula = fFight > 0 && fMug > 0 && Math.Abs(fMug / fFight - 1) < 0.02;
            bool ok = fh == 0 && misses > 0 && sh > 0 && formula;
            return $"[engine] mug self-test {Ok(ok)}: 20 Mugs by {Name(c.P0)} on {Name(c.E0)} with the steal failing: {fh} strikes landed, " +
                   $"{misses} turned to misses (BNW: none land); 20 with the steal working: {sh} landed; BNW's Fight formula on Mug: " +
                   $"factor x{fMug:0.###} vs Attack's x{fFight:0.###}";
        }

        // --- Blind on Jump and Steal (BNW BlindHelp / CoinHelp: hit rate 50) ------------------------

        static string Blind()
        {
            var c = Battle();
            var jump = Cmd(30);
            var landing = Ab(22);
            var steal = Cmd(10);
            var stealAb = Ab(8);
            var gil = Cmd(21);
            var gilAb = Ab(15);
            var cc = c?.Battle.BattleConditionController;
            if (c == null || jump == null || landing == null || steal == null || stealAb == null || gil == null || gilAb == null || cc == null) return null;
            // a member Blind can land on (gear can block it)
            BattleUnitData who = null;
            foreach (var cand in c.Players)
            {
                cc.InterruptAddCondition(cand, ConditionType.Blind);
                bool took = cand.BattleUnitDataInfo.GetConfirmedConditionType()?.Contains(ConditionType.Blind) ?? false;
                cc.InterruptRemoveCondition(cand, ConditionType.Blind);
                if (took) { who = cand; break; }
            }
            if (who == null) return "[engine] blind self-test skipped: Blind lands on nobody in the party";
            List<Hit> jp, jb, sp, sb, gp = new List<Hit>(), gb = new List<Hit>();
            var udm = UserDataManager.Instance();
            int gilWas = udm?.OwendGil ?? 0;
            var items = new Last.Management.OwnedItemClient();
            // GP Rain spends gil in its HitCalc: topped up before each throw (the first run ran dry after 34)
            Hit Throw()
            {
                if (udm != null && udm.OwendGil < 100000) items.AddGil(100000);
                return Run(who, c.E0, gil, gilAb);
            }
            try
            {
                jp = Many(40, () => Run(who, c.E0, jump, landing));
                sp = Many(40, () => Run(who, c.E0, steal, stealAb));
                for (int i = 0; i < 40; i++) gp.Add(Throw());
                cc.InterruptAddCondition(who, ConditionType.Blind);
                jb = Many(40, () => Run(who, c.E0, jump, landing));
                sb = Many(40, () => Run(who, c.E0, steal, stealAb));
                for (int i = 0; i < 40; i++) gb.Add(Throw());
            }
            finally
            {
                cc.InterruptRemoveCondition(who, ConditionType.Blind);
                try { if (udm != null && udm.OwendGil != gilWas) items.AddGil(gilWas - udm.OwendGil); } catch (Exception) { }
            }
            int jph = jp.Count(x => x.Struck), jbh = jb.Count(x => x.Struck);
            int spm = sp.Count(x => x.Type == HitType.Miss), sbm = sb.Count(x => x.Type == HitType.Miss);
            int gph = gp.Count(x => x.Struck), gbh = gb.Count(x => x.Struck);
            bool gpOk = gph < 30 || gbh <= 0.75 * gph;   // no gil to throw: nothing to see
            bool ok = jph >= 30 && jbh <= 0.75 * jph && sbm >= 8 && sbm > spm && gpOk;
            return $"[engine] blind self-test {Ok(ok)}: {Name(who)} on {Name(c.E0)}, 40 each: Jump's landing hit {jph} plain, {jbh} blinded " +
                   $"(BNW: about half); Steal missed {spm} plain, {sbm} blinded (BNW: hit rate 50, then Evade); GP Rain hit {gph} plain, " +
                   $"{gbh} blinded (BNW: about half{(gph < 30 ? "; too few plain hits to judge" : "")})";
        }

        // --- dual wield and Offering ----------------------------------------------------------------

        static string DualWield()
        {
            var c = Battle();
            var fight = Cmd(1);
            var attack = Ab(1);
            if (c == null || fight == null || attack == null) return null;
            foreach (var u in c.Players)
            {
                var oc = OwnedOf(u);
                var eq = oc?.EquipmentData;
                if (eq == null || !eq.ContainsKey(EquipSlotType.Slot1) || !eq.ContainsKey(EquipSlotType.Slot2)) continue;
                var right = eq[EquipSlotType.Slot1];
                if (right?.Weapon == null || right.Weapon.TypeId == 2) continue;
                var left = eq[EquipSlotType.Slot2];
                if (left?.Weapon != null && left.Weapon.TypeId != 2) continue;   // already two weapons
                var util = BattleProviderManager.Instance?.GetSerialBattleUtility();
                List<Hit> one, two;
                bool before, during;
                float corrOne, corrTwo;
                try
                {
                    before = util.IsSecondlyAttack(oc);
                    corrOne = CalcExecuteCorrection.GetAccessoryPhysicalCorrectionValue(CalcExecuteCorrection.PhysicalType.GenchisBasho, oc);
                    one = Many(30, () => Run(u, c.E0, fight, attack));
                    eq[EquipSlotType.Slot2] = right;   // the same weapon in both hands
                    during = util.IsSecondlyAttack(oc);
                    corrTwo = CalcExecuteCorrection.GetAccessoryPhysicalCorrectionValue(CalcExecuteCorrection.PhysicalType.GenchisBasho, oc);
                    two = Many(30, () => Run(u, c.E0, fight, attack));
                }
                finally { eq[EquipSlotType.Slot2] = left; }
                var o = one.Where(x => x.Type == HitType.Hit).ToList();
                var t = two.Where(x => x.Type == HitType.Hit).ToList();
                double ratio = Mean(t.Select(x => x.Value)) / Math.Max(1, Mean(o.Select(x => x.Value)));
                bool ok = !before && during && corrOne <= 0 && Math.Abs(corrTwo - 0.75f) < 0.001 && o.Count >= 8 && t.Count >= 8 && ratio > 0.65 && ratio < 0.85;
                return $"[engine] dual-wield self-test {Ok(ok)}: {oc.Name} with {right.Weapon.Id} in one hand: two strikes {before}, Genji " +
                       $"correction {corrOne}; the same weapon in both: two strikes {during}, correction {corrTwo} (BNW: -25%); " +
                       $"Fight damage x{ratio:0.00} ({o.Count}/{t.Count} hits)";
            }
            return "[engine] dual-wield self-test skipped: nobody in the party holds a weapon";
        }

        const int OfferingAbility = 253;

        static string Offering()
        {
            var c = Battle();
            var fight = Cmd(1);
            var attack = Ab(1);
            if (c == null || fight == null || attack == null) return null;
            var u = c.P0;
            var oc = OwnedOf(u);
            var abilities = oc?.EquipmentAbilitys;
            var util = BattleProviderManager.Instance?.GetSerialBattleUtility();
            if (abilities == null || util == null) return "[engine] offering self-test skipped: no equipment abilities";
            bool had = abilities.Contains(OfferingAbility);
            List<Hit> plain, offering;
            float corr;
            bool shoot;
            int count0 = -1, count1 = -1;
            try
            {
                if (had) abilities.Remove(OfferingAbility);
                plain = Many(30, () => Run(u, c.E0, fight, attack));
                abilities.Add(OfferingAbility);
                corr = CalcExecuteCorrection.GetAccessoryPhysicalCorrectionValue(CalcExecuteCorrection.PhysicalType.PilgrimsPassport, oc);
                shoot = util.IsDisorderlyShootAttack(u);
                offering = Many(30, () => Run(u, c.E0, fight, attack));
                // the strikes: the game's own builder (RelicRules: 4 -> 2)
                var abl = new Il2CppSystem.Collections.Generic.List<Ability>();
                abl.Add(attack);
                var targets = new Il2CppReferenceArray<BattleUnitData>(1);
                targets[0] = c.E0;
                var act = new BattleActData(u, targets, fight, abl, BattleActData.ActType.Normal,
                    (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
                // (CreateContinuousActData itself queues the extra strikes as real acts: not called here)
                act.continuousCount = count0 = 4;   // the game's Offering count
                RelicRules.Continuous_Prefix(act, true);
                count1 = act.continuousCount;
            }
            finally
            {
                abilities.Remove(OfferingAbility);
                if (had) abilities.Add(OfferingAbility);
            }
            var p = plain.Where(x => x.Type == HitType.Hit).ToList();
            var o = offering.Where(x => x.Type == HitType.Hit).ToList();
            double ratio = Mean(o.Select(x => x.Value)) / Math.Max(1, Mean(p.Select(x => x.Value)));
            bool ok = Math.Abs(corr - 0.75f) < 0.001 && shoot && p.Count >= 8 && o.Count >= 8 && ratio > 0.65 && ratio < 0.85 && count1 <= 2;
            return $"[engine] offering self-test {Ok(ok)}: {oc.Name} given the Offering property (ability 253, BNW's Kagenui / Daryl's Soul): " +
                   $"correction {corr} (BNW: -25%, not the PR's x0.5), an Offering attack {shoot}, strikes {count0} -> {count1} (BNW: 2), " +
                   $"Fight damage x{ratio:0.00} ({p.Count}/{o.Count} hits)";
        }

        // --- tests' act queue catcher -------------------------------------------------------------------

        static List<BattleActData> caught;

        public static bool Queue_Prefix(BattleActData battleActData)
        {
            if (caught == null) return true;
            caught.Add(battleActData);
            return false;
        }

        // --- Chainsaw on a death-immune target --------------------------------------------------------

        static string Chainsaw()
        {
            var c = Battle();
            var kill = Ab(EngineRules.ChainsawKill);
            var tools = Cmd(14);
            if (c == null || kill == null || tools == null) return null;
            var targets = new Il2CppReferenceArray<BattleUnitData>(1);
            targets[0] = c.E0;
            var abl = new Il2CppSystem.Collections.Generic.List<Ability>();
            abl.Add(kill);
            var act = new BattleActData(c.P0, targets, tools, abl, BattleActData.ActType.Normal,
                (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
            var f = new BattleFunction46();
            f.Init(act, new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>(),
                new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>());
            bool realImmune = EngineRules.DeathImmune(c.E0);
            int Made()
            {
                var made = f.CreateAbility(kill);
                return made?.abilityList != null && made.abilityList.Count > 0 ? made.abilityList[0].Id : -1;
            }
            int plain, immune;
            try
            {
                EngineRules.TestImmune = IntPtr.Zero;
                plain = Made();
                EngineRules.TestImmune = c.E0.BattleUnitDataInfo.Parameter.Pointer;
                immune = Made();
            }
            finally { EngineRules.TestImmune = IntPtr.Zero; }
            bool ok = immune == EngineRules.ChainsawCut && (realImmune ? plain == EngineRules.ChainsawCut : plain == EngineRules.ChainsawKill);
            return $"[engine] chainsaw self-test {Ok(ok)}: the game's Chainsaw kill (ability {EngineRules.ChainsawKill}) built through " +
                   $"BattleFunction46.CreateAbility on {Name(c.E0)}: {(realImmune ? "death-immune" : "not immune")} -> ability {plain}; " +
                   $"made death-immune -> ability {immune} (BNW: the cut, {EngineRules.ChainsawCut})";
        }

        // --- a dual-wield Mug's second strike ---------------------------------------------------------

        static string MugSecondStrike()
        {
            var c = Battle();
            var mug = Cmd(11);
            var mugAb = Ab(9);
            var fight = Cmd(1);
            var attack = Ab(1);
            var util = BattleProviderManager.Instance?.GetSerialBattleUtility();
            if (c == null || mug == null || mugAb == null || fight == null || attack == null || util == null) return null;
            string Second(Command cmd, Ability ab)
            {
                var targets = new Il2CppReferenceArray<BattleUnitData>(1);
                targets[0] = c.E0;
                var abl = new Il2CppSystem.Collections.Generic.List<Ability>();
                abl.Add(ab);
                var act = new BattleActData(c.P0, targets, cmd, abl, BattleActData.ActType.Normal,
                    (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
                caught = new List<BattleActData>();
                util.CreateContiniousActData(act, cmd, ab, default, true, false, false, true);
                var got = caught;
                caught = null;
                if (got.Count == 0) return "none";
                var x = got[0];
                return $"{x.Command?.Id}/{(x.abilityList != null && x.abilityList.Count > 0 ? x.abilityList[0].Id : -1)}";
            }
            string m, f;
            try { m = Second(mug, mugAb); f = Second(fight, attack); }
            finally { caught = null; }
            bool ok = m == "1/1" && f == "1/1";
            return $"[engine] mug-second-strike self-test {Ok(ok)}: the game's dual-wield second strike (CreateContiniousActData) for a Mug " +
                   $"is command/ability {m} (BNW: a plain Attack, 1/1), for a Fight {f}";
        }

        // --- X-Magic: a monster that counters the first spell doesn't counter the second ------------------

        static string XMagicCounter()
        {
            var c = Battle();
            var cm = c?.Battle.BattleActExection?.counterModule ?? UnityEngine.Object.FindObjectOfType<CounterModule>();
            var magic = Cmd(7);
            var mm = MasterManager.Instance;
            if (c == null || magic == null || mm == null) return null;
            if (cm == null) return "[engine] x-magic self-test skipped: this battle has no counter module (BattleActExection.counterModule)";
            Ability spell = null;
            foreach (var a in mm.GetList<Ability>().Values)
                if (a != null && a.TypeId == 1 && a.StandardValue > 0 && a.AttributeId != 0 && (spell == null || a.Id < spell.Id)) spell = a;
            if (spell == null) return "[engine] x-magic self-test FAILED: no damage spell";
            BattleActData Act(BattleUnitData target, BattleActData.ActAbilityType kind)
            {
                var targets = new Il2CppReferenceArray<BattleUnitData>(1);
                targets[0] = target;
                var abl = new Il2CppSystem.Collections.Generic.List<Ability>();
                abl.Add(spell);
                return new BattleActData(c.P0, targets, magic, abl, BattleActData.ActType.Normal,
                    (BattleActData.TargetType)0, BattleActData.ArmType.Right, kind, 1, false);
            }
            (int First, int Second) Counters(BattleUnitData target, bool rule)
            {
                var list = cm.calcResults;
                int n0 = list.Count;
                bool was = SpecialCommands.Enabled;
                SpecialCommands.Enabled = rule;
                try
                {
                    var a1 = Act(target, BattleActData.ActAbilityType.ContinuousMagicFirst);
                    var g1 = new FunctionGroupInfo(a1);
                    g1.HitCalc();
                    cm.InitCounterAI(a1, g1);
                    int n1 = list.Count;
                    var a2 = Act(target, BattleActData.ActAbilityType.ContinuousMagicSecond);
                    var g2 = new FunctionGroupInfo(a2);
                    g2.HitCalc();
                    cm.InitCounterAI(a2, g2);
                    int n2 = list.Count;
                    return (n1 - n0, n2 - n1);
                }
                finally
                {
                    SpecialCommands.Enabled = was;
                    if (list.Count > n0) list.RemoveRange(n0, list.Count - n0);   // nothing left to run
                }
            }
            foreach (var e in c.Enemies)
            {
                var game = Counters(e, false);
                if (game.First == 0) continue;   // this one doesn't counter that spell
                var bnw = Counters(e, true);
                bool ok = game.Second > 0 && bnw.First > 0 && bnw.Second == 0;
                return $"[engine] x-magic self-test {Ok(ok)}: {Name(c.P0)}'s X-Magic ({spell.Id} twice) on {Name(e)}, counters set for the " +
                       $"first/second spell: the game's rules {game.First}/{game.Second}, BNW's {bnw.First}/{bnw.Second} (BNW: one per X-Magic)";
            }
            return $"[engine] x-magic self-test skipped: no monster in this fight counters spell {spell.Id}";
        }

        // --- Noise Blaster's Muddle evaded by Spirit -------------------------------------------------------

        static string NoiseBlaster()
        {
            var c = Battle();
            var tools = Cmd(14);
            var udm = UserDataManager.Instance();
            if (c == null || tools == null || udm == null) return null;
            Last.Data.User.OwnedItemData nb = null;
            foreach (var it in udm.GetAllOwnedItemsClone())
                if (it?.Item != null && it.Item.Id == CommandRules.NoiseBlasterItem) { nb = it; break; }
            if (nb == null)
            {
                // none owned (a new game): one given for this run (test runs never save)
                foreach (var ct in MasterManager.Instance.GetList<Content>().Values)
                    if (ct != null && ct.TypeId == 1 && ct.TypeValue == CommandRules.NoiseBlasterItem) { new OwnedItemClient().AddOwnedItem(ct.Id, 1); break; }
                foreach (var it in udm.GetAllOwnedItemsClone())
                    if (it?.Item != null && it.Item.Id == CommandRules.NoiseBlasterItem) { nb = it; break; }
            }
            if (nb == null) return "[engine] noise-blaster self-test skipped: no Noise Blaster owned, and none could be given";
            // the act carries the item's own ability (ActUtility.GetUseItemAbility, as the game's item acts do):
            // FunctionUtility.GetAbilityFunction reads the act's first ability for its function group
            // (a tool's item gives none on some saves: QA 2026-11-20 14:43 and 2026-11-21 06:34): then the Tools command's
            // own ability (command.csv 14: ability 24), as the Tools menu's act carries
            int useId = ActUtility.GetUseItemAbility(nb);
            var useAbility = MasterManager.Instance.GetData<Ability>(useId) ?? MasterManager.Instance.GetData<Ability>(tools.AbilityId);
            if (useAbility == null) return $"[engine] noise-blaster self-test FAILED: no ability for item {CommandRules.NoiseBlasterItem} " +
                                           $"(ActUtility.GetUseItemAbility gave {useId}; the Tools command's ability {tools.AbilityId})";
            int evades = CommandRules.NoiseEvades, landed = 0, results = 0;
            const int N = 400;
            for (int i = 0; i < N; i++)
            {
                var targets = new Il2CppReferenceArray<BattleUnitData>(1);
                targets[0] = c.E0;
                var items = new Il2CppSystem.Collections.Generic.List<Last.Data.User.OwnedItemData>();
                items.Add(nb);
                var abilities = new Il2CppSystem.Collections.Generic.List<Ability>();
                abilities.Add(useAbility);
                var act = new BattleActData(c.P0, targets, tools, abilities, items, BattleActData.ActType.Normal,
                    (BattleActData.TargetType)0, BattleActData.UseType.Item, BattleActData.ArmType.Right);
                FunctionGroupInfo g;
                try { g = new FunctionGroupInfo(act); }
                catch (Exception e)
                {
                    // QA bnw-atb3: "Function Group is not set" for this act (ability 24 has no function group): the test
                    // can't build a tool's act here; the live rule (CommandRules.ForceMiss_Postfix) is unverified, not failed
                    return $"[engine] noise-blaster self-test skipped: the game builds no function group for a tool act " +
                           $"with ability {useAbility.Id} ({e.GetType().Name}); Noise Blaster's Spirit evasion unverified";
                }
                g.HitCalc();
                var dic = g.GetICalcResult();
                if (dic == null || !dic.ContainsKey(c.E0)) continue;
                results++;
                if (dic[c.E0].GetHitType() != HitType.Miss) landed++;
            }
            evades = CommandRules.NoiseEvades - evades;
            int sta = BattleFormulas.MonsterStamina(c.E0.BattleUnitDataInfo.Parameter.ConfirmedMaxHp());
            bool ok = results > 0 && evades > 0;
            return $"[engine] noise-blaster self-test {Ok(ok)}: {N} Noise Blasters (ability {useAbility.Id}) by {Name(c.P0)} on {Name(c.E0)} (Stamina {sta}: " +
                   $"{(sta + 1) * 100 / 128}% to evade): Spirit evaded {evades}, results {results}, not missed {landed}";
        }

        // --- the two-handed grip: the game's x2 attack becomes BNW's x1.5 ----------------------------

        const int GripAbility = 251;

        static string Grip()
        {
            var c = Battle();
            if (c == null) return null;
            foreach (var u in c.Players)
            {
                var oc = OwnedOf(u);
                var abilities = oc?.EquipmentAbilitys;
                var p = oc?.Parameter;
                if (abilities == null || p == null) continue;
                bool had = abilities.Contains(GripAbility);
                bool grips;
                int ours, game, without;
                bool was = RelicRules.Enabled;
                try
                {
                    if (had) abilities.Remove(GripAbility);
                    without = p.ConfirmedAttack();
                    abilities.Add(GripAbility);
                    grips = RelicRules.Gripping(oc);
                    // both hands full (the game's grip needs one weapon and a free hand): the game's own check
                    // answers yes for the test, as it would for a grip weapon held alone
                    if (!grips) RelicRules.TestGrip = true;
                    ours = p.ConfirmedAttack();
                    RelicRules.Enabled = false;
                    game = p.ConfirmedAttack();
                }
                finally
                {
                    RelicRules.Enabled = was;
                    RelicRules.TestGrip = false;
                    abilities.Remove(GripAbility);
                    if (had) abilities.Add(GripAbility);
                }
                bool ok = game == 2 * without && ours == BattleMath.Grip(without);
                return $"[engine] grip self-test {Ok(ok)}: {oc.Name} given the grip (ability 251){(grips ? " with a free hand" : ", the game's grip check forced (no free hand)")}: attack {without} -> " +
                       $"the game's {game} (x2), BNW's {ours} (x1.5 = {BattleMath.Grip(without)})";
            }
            return "[engine] grip self-test skipped: nobody in the party has a free hand";
        }

        // --- Shock's cost: max HP / 8 to the user, a frame after its hits ---------------------------

        static int shockStage;
        static int shockMaxHp;

        static string ShockCost()
        {
            var c = Battle();
            var shock = Cmd(28);
            var ab = Ab(19);
            if (c == null || shock == null || ab == null) return null;
            if (shockStage == 0)
            {
                BattleFormulas.LastShockCost = null;
                var hit = Run(c.P0, c.E0, shock, ab);
                shockMaxHp = c.P0.BattleUnitDataInfo.Parameter.ConfirmedMaxHp();
                shockStage = 1;
                return null;   // the cost lands a frame later (BattleFormulas.Track)
            }
            var cost = BattleFormulas.LastShockCost;
            if (cost == null) return shockStage++ > 20 ? "[engine] shock-cost self-test FAILED: no cost charged within 10 s" : null;
            var (was, now, charged) = cost.Value;
            // the max HP when the cost lands may differ from the cast's (QA bnw-atb6: the probe's 9999-HP party): either one
            int maxNow = c.P0.BattleUnitDataInfo.Parameter.ConfirmedMaxHp();
            bool ok = (charged == BattleMath.ShockCost(shockMaxHp) || charged == BattleMath.ShockCost(maxNow)) && now == Math.Max(0, was - charged);
            if (maxNow != shockMaxHp) shockMaxHp = maxNow;
            return $"[engine] shock-cost self-test {Ok(ok)}: Shock by {Name(c.P0)} (max HP {shockMaxHp}): HP {was} -> {now}, cost {charged} " +
                   $"(BNW: max HP / 8 = {BattleMath.ShockCost(shockMaxHp)})";
        }

        // --- Sketch: Relm's Magic for Vigor in physical hits ----------------------------------------

        static string Sketch()
        {
            var c = Battle();
            int sketchId = EsperHooks.D?.SketchCommandId ?? 0;
            var sketch = sketchId > 0 ? Cmd(sketchId) : null;
            var fight = Cmd(1);
            var attack = Ab(1);
            if (c == null || sketch == null || fight == null || attack == null) return null;
            var a = c.P0.BattleUnitDataInfo.Parameter;
            var plain = Many(10, () => Run(c.P0, c.E0, fight, attack)).Where(x => x.Struck && x.Game > 0).ToList();
            var sk = Many(10, () => Run(c.P0, c.E0, sketch, attack)).Where(x => x.Struck && x.Game > 0).ToList();
            if (plain.Count == 0 || sk.Count == 0) return "[engine] sketch self-test FAILED: no hits";
            int lv = a.ConfirmedLevel(), vig = a.ConfirmedPower(), mag = a.ConfirmedMagic();
            int bp = a.TryCast<PlayerCharacterParameter>()?.GetArmConfirmedAttack(BattleActData.ArmType.Right) ?? a.ConfirmedAttack();
            double want = BattleFormulas.BnwFight(lv, bp, BattleMath.SketchVigor(mag)) / BattleFormulas.BnwFight(lv, bp, vig);
            double got = sk.Average(x => x.Factor) / plain.Average(x => x.Factor);
            bool ok = Math.Abs(got / want - 1) < 0.05;
            return $"[engine] sketch self-test {Ok(ok)}: Attack by {Name(c.P0)} (Lv {lv}, Vigor {vig}, Magic {mag}) under Sketch vs Fight: " +
                   $"BNW factor x{got:0.000} (Magic {mag} doubled as Vigor {BattleMath.SketchVigor(mag)}: x{want:0.000})";
        }

        // --- Mimic: a Mimic's own act isn't copied again --------------------------------------------

        static string Mimic()
        {
            var c = Battle();
            var magic = Cmd(7);
            var attackAb = Ab(1);
            if (c == null || magic == null || attackAb == null) return null;
            var targets = new Il2CppReferenceArray<BattleUnitData>(1);
            targets[0] = c.E0;
            var abl = new Il2CppSystem.Collections.Generic.List<Ability>();
            abl.Add(attackAb);
            var copied = new BattleActData(c.P0, targets, magic, abl, BattleActData.ActType.Normal,
                (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
            copied.isCopiedAct = true;
            var made = BattleImpersonationFunctionFF6.CreateActDataFromOtherData(c.P0, copied, 0);
            int cmd = made?.Command?.Id ?? -1;
            bool ok = made != null && cmd == 1;
            return $"[engine] mimic self-test {Ok(ok)}: Mimic of an act that was itself a Mimic's (isCopiedAct): the game made command {cmd} " +
                   $"(BNW: Gogo fights, command 1)";
        }

        // --- Rage: statuses the rage's monster blocks come off the rager ----------------------------

        static string RageStatuses()
        {
            var c = Battle();
            var cc = c?.Battle.BattleConditionController;
            var mm = MasterManager.Instance;
            if (c == null || cc == null || mm == null) return null;
            int poisonId = 0, blindId = 0;
            foreach (var cond in mm.GetList<Condition>().Values)
            {
                if (cond == null) continue;
                if (cond.ConditionType == (int)ConditionType.Poison && poisonId == 0) poisonId = cond.Id;
                if (cond.ConditionType == (int)ConditionType.Blind && blindId == 0) blindId = cond.Id;
            }
            if (poisonId == 0 || blindId == 0) return "[engine] rage-statuses self-test FAILED: no Poison/Blind condition";
            BattleUnitData u = null;
            bool Has(ConditionType t) => u.BattleUnitDataInfo.GetConfirmedConditionType()?.Contains(t) ?? false;
            // a member that takes both (gear can block them)
            foreach (var cand in c.Players)
            {
                u = cand;
                cc.InterruptAddCondition(u, ConditionType.Poison);
                cc.InterruptAddCondition(u, ConditionType.Blind);
                if (Has(ConditionType.Poison) && Has(ConditionType.Blind)) break;
                cc.InterruptRemoveCondition(u, ConditionType.Poison);
                cc.InterruptRemoveCondition(u, ConditionType.Blind);
                u = null;
            }
            if (u == null) return "[engine] rage-statuses self-test skipped: nobody in the party takes Poison and Blind";
            try
            {
                bool set = Has(ConditionType.Poison) && Has(ConditionType.Blind);
                // the rage blocks Poison (new) and Blind (the rager's gear blocked it already)
                EngineRules.CureNewlyBlocked(u, new HashSet<int> { blindId }, new HashSet<int> { poisonId, blindId });
                bool poison = Has(ConditionType.Poison), blind = Has(ConditionType.Blind);
                bool ok = set && !poison && blind;
                return $"[engine] rage-statuses self-test {Ok(ok)}: {Name(u)} Poisoned and Blind, raging as a monster that blocks both (Blind " +
                       $"already blocked by the rager's gear): Poison {(poison ? "kept" : "cured")}, Blind {(blind ? "kept" : "cured")} " +
                       $"(BNW: Poison cured, Blind kept); cured: {string.Join(", ", EngineRules.RageCured)}";
            }
            finally
            {
                cc.InterruptRemoveCondition(u, ConditionType.Poison);
                cc.InterruptRemoveCondition(u, ConditionType.Blind);
            }
        }

        // --- the back-row True Knight cover: chance Stamina / 192 per knight ------------------------

        static string BackRowCover()
        {
            var c = Battle();
            var fight = Cmd(1);
            var attack = Ab(1);
            var cc = c?.Battle.BattleConditionController;
            if (c == null || fight == null || attack == null || cc == null) return null;
            if (!CoverRules.Enabled) return "[engine] back-row cover self-test: off";
            BattleUnitData guard = null, target = null;
            foreach (var u in c.Players)
            {
                var row = BattleUtility.GetCorpsId(u);
                if (row == CorpsId.Back) target ??= u;
                else guard ??= u;
            }
            // nobody in the front row: one back-row member stands there as the knight (BattlePlayerData.corps)
            Last.Data.User.Corps guardMoved = null;
            if (guard == null && target != null)
                foreach (var u in c.Players)
                {
                    var corps = u.TryCast<BattlePlayerData>()?.corps;
                    if (u.Pointer == target.Pointer || corps == null) continue;
                    corps.Id = CorpsId.Front;
                    if (BattleUtility.GetCorpsId(u) != CorpsId.Back) { guardMoved = corps; guard = u; }
                    else corps.Id = CorpsId.Back;
                    break;
                }
            // nobody in the back row: one front-row member stands there for the test (BattlePlayerData.corps)
            Last.Data.User.Corps moved = null;
            if (target == null && guard != null)
                foreach (var u in c.Players)
                {
                    var corps = u.TryCast<BattlePlayerData>()?.corps;
                    if (u.Pointer == guard.Pointer || corps == null) continue;
                    corps.Id = CorpsId.Back;
                    if (BattleUtility.GetCorpsId(u) == CorpsId.Back) { moved = corps; target = u; }
                    else corps.Id = CorpsId.Front;
                    break;
                }
            if (guard == null || target == null)
            {
                if (moved != null) moved.Id = CorpsId.Front;
                if (guardMoved != null) guardMoved.Id = CorpsId.Back;
                return "[engine] back-row cover self-test skipped: the party has nobody in the " + (target == null ? "back row" : "front row");
            }
            int stamina = Math.Clamp(guard.BattleUnitDataInfo.Parameter.ConfirmedVitality(), 0, 255);
            const int N = 200;
            int covered = 0;
            // a healthy knight for the run (QA bnw-atb 2026-11-21: Edgar covered 0 of 200 on the PC save; a Near Fatal or
            // blocked knight never covers a back-row ally): full HP, no Near Fatal; his other statuses go in the line
            var gp = guard.BattleUnitDataInfo.Parameter;
            int guardHpWas = gp.CurrentHP;
            var gconds = guard.BattleUnitDataInfo.GetConfirmedConditionType();
            bool guardDyingWas = gconds?.Contains(ConditionType.Dying) ?? false;
            var condNames = new List<string>();
            if (gconds != null) foreach (var t in gconds) condNames.Add(t.ToString());
            string guardState = $"HP {guardHpWas}/{gp.ConfirmedMaxHp()}, statuses [{string.Join(" ", condNames)}]";
            // statuses that bar a guard (QA bnw-atb3: Berserk left by an earlier self-test) are lifted for the run
            var lifted = new List<ConditionType>();
            string stuck = null;
            if (gconds != null)
                foreach (var t in CoverRules.GuardBlocked)
                    if (gconds.Contains(t)) lifted.Add(t);
            try
            {
                gp.CurrentHP = gp.ConfirmedMaxHp();
                if (guardDyingWas) cc.InterruptRemoveCondition(guard, ConditionType.Dying);
                foreach (var t in lifted) cc.InterruptRemoveCondition(guard, t);
                cc.InterruptAddCondition(guard, ConditionType.TipsForKnights);
                // a blocking status that stays (QA bnw-atb6: Edgar's Berserk, likely BNW's Hyper Wrist): BNW rightly
                // refuses the cover, so the chance can't be measured with this knight
                var nowConds = guard.BattleUnitDataInfo.GetConfirmedConditionType();
                foreach (var t in CoverRules.GuardBlocked)
                    if (nowConds != null && nowConds.Contains(t)) { stuck = t.ToString(); break; }
                for (int i = 0; i < N && stuck == null; i++)
                {
                    var abl = new Il2CppSystem.Collections.Generic.List<Ability>();
                    abl.Add(attack);
                    var targets = new Il2CppReferenceArray<BattleUnitData>(1);
                    targets[0] = target;
                    var act = new BattleActData(c.E0, targets, fight, abl, BattleActData.ActType.Normal,
                        (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
                    var r = TargetFunction.ProtectExtractTarget(act);
                    var now = r?.TargetUnitDatas;
                    if (now != null && now.Length > 0 && now[0].Pointer == guard.Pointer) covered++;
                }
            }
            finally
            {
                cc.InterruptRemoveCondition(guard, ConditionType.TipsForKnights);
                gp.CurrentHP = guardHpWas;
                if (guardDyingWas) cc.InterruptAddCondition(guard, ConditionType.Dying);
                foreach (var t in lifted) cc.InterruptAddCondition(guard, t);
                if (moved != null) moved.Id = CorpsId.Front;
                if (guardMoved != null) guardMoved.Id = CorpsId.Back;
            }
            if (stuck != null)
                return $"[engine] back-row cover self-test skipped: knight {Name(guard)} keeps {stuck} after it's lifted (his equipment's, " +
                       $"so BNW rightly won't cover with him; before the test {guardState}); measured ok on other saves (2026-11-20 14:43, 16:28)";
            double want = Math.Min(1.0, stamina / 192.0), got = covered / (double)N;
            double sd = Math.Sqrt(Math.Max(want * (1 - want), 0.0025) / N);
            bool ok = Math.Abs(got - want) <= 4 * sd + 0.02;
            return $"[engine] back-row cover self-test {Ok(ok)}: knight {Name(guard)} (front row{(guardMoved != null ? ", moved there for the test" : "")}, Stamina {stamina}; before the test {guardState}) for {Name(target)} (back row, " +
                   $"healthy{(moved != null ? ", moved there for the test" : "")}): covered {covered} of {N} Attacks ({got:P0}; BNW: Stamina / 192 = {want:P0})";
        }

        // --- the PR's accessory lists for GetAccessoryPhysicalCorrectionValue ------------------------

        static string AccessoryLists()
        {
            if (Battle() == null) return null;
            var prop = typeof(CalcExecuteCorrection).GetProperty("PhysicalCorrectionAccessoryIds",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var dict = prop?.GetValue(null) as Il2CppSystem.Collections.Generic.Dictionary<int, Il2CppStructArray<int>>;
            if (dict == null) return "[engine] accessory-lists self-test FAILED: the game's list not found";
            string Ids(int k) => dict.ContainsKey(k) ? "[" + string.Join(", ", dict[k].ToArray()) + "]" : "-";
            bool ok = Ids(0) == "[131]" && Ids(1) == "[]" && Ids(3) == "[]" && Ids(2).Contains("99") && !Ids(2).Contains("104");
            return $"[engine] accessory-lists self-test {Ok(ok)}: the game's physical-correction accessories now 0 Offering {Ids(0)} " +
                   $"(BNW: Daryl's Soul 131), 1 Genji {Ids(1)} (BNW: none), 2 Atlas {Ids(2)}, 3 Gauntlet {Ids(3)} (BNW: none)";
        }

        // --- EL data in the save: the game's own save JSON carries the BNW1 block --------------------

        const int Marker = 0x424E5731;

        static string ElSave()
        {
            if (Battle() == null) return null;
            var udm = UserDataManager.Instance();
            var D = EsperHooks.D;
            if (udm == null || D == null) return null;
            int sid = 0;
            foreach (var o in udm.OwnedCharacterList)
                if (o != null && o.characterStatusId >= 1 && o.characterStatusId <= D.Rules.MaxElCharacterStatusId) { sid = o.characterStatusId; break; }
            if (sid == 0) return "[engine] EL-save self-test skipped: no esper user owned";
            var was = EsperStore.Get(sid);
            var probe = was.Clone();
            probe.Ep = 12345;
            probe.El = Math.Min(D.Rules.MaxEl, 7);
            string json;
            try
            {
                EsperStore.Put(sid, probe);
                json = udm.ToJSON(false);   // what the game writes into a save file
            }
            finally { EsperStore.Put(sid, was); EsperStore.Invalidate(); }
            // "ownedKeyWaordList":"{\"target\":[...]}" (the list, JSON inside the JSON)
            int at = json?.IndexOf("ownedKeyWaordList", StringComparison.Ordinal) ?? -1;
            if (at < 0) return "[engine] EL-save self-test FAILED: no ownedKeyWaordList in the save JSON";
            int open = json.IndexOf('[', at), close = open < 0 ? -1 : json.IndexOf(']', open);
            if (open < 0 || close < 0) return "[engine] EL-save self-test FAILED: no list in ownedKeyWaordList";
            var ints = json.Substring(open + 1, close - open - 1).Split(',').Select(s => int.TryParse(s.Trim(), out var v) ? v : int.MinValue).ToList();
            int m = ints.IndexOf(Marker);
            if (m < 0) return $"[engine] EL-save self-test FAILED: the BNW1 block isn't in the save JSON ({ints.Count} words)";
            int w = m + 1 + 3 * (sid - 1);
            var back = w + 2 < ints.Count ? EsperProgress.Unpack(ints[w], ints[w + 1], ints[w + 2]) : EsperProgress.Empty;
            bool ok = back.Ep == probe.Ep && back.El == probe.El && back.ElBank == probe.ElBank && back.SpBank == probe.SpBank;
            return $"[engine] EL-save self-test {Ok(ok)}: UserDataManager.ToJSON (the save's text) holds the BNW1 block; character " +
                   $"{sid} read back from it: EL {back.El}, EP {back.Ep}, bank {back.ElBank}, SP {back.SpBank} (set EL {probe.El}, EP {probe.Ep}); " +
                   $"loading clears the list and reads it from the save (UserDataManager.FromJsonAsync, decompiled)";
        }
    }
}
