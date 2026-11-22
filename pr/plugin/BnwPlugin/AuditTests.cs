using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Last.Battle;
using Last.Battle.Function;
using Last.Data.Master;
using Last.Defaine;
using Last.Defaine.User;
using Last.Management;
using Last.Systems;

namespace BraveNewWorld.PR
{
    // AuditRules' checks in the probe battle (FormulaProbe.AddSelfTest "audit ..."), through the game's own act flow
    // (ParameterActFunctionManagment.GetFunction(ability), each function's Init, FixedTarget, HitCalc; nothing applied),
    // as EngineTests does. Each returns null until a battle with a party member and a monster standing, then one
    // "[audit] ... ok/FAILED" line. Whatever a check changes (Vitality, a condition in the list) is put back.
    // Not checkable here (logged in real fights instead): counters on misses (the counter module runs after real
    // acts), Morph taking Imp off, a monster dying at 0 MP.
    static class AuditTests
    {
        internal static void Register()
        {
            Add("stamina", Stamina);
            Add("boss fractional", BossFractional);
            Add("imp", Imp);
            Add("back row", BackRow);
            Add("0 MP flag", Mp0);
        }

        static void Add(string name, Func<string> test) =>
            FormulaProbe.AddSelfTest("audit " + name, () => !FormulaProbe.Running || GameHarness.Active ? null : test());

        sealed class Ctx
        {
            public List<BattleUnitData> Players = new List<BattleUnitData>(), Enemies = new List<BattleUnitData>();
            public BattleUnitData P0 => Players[0];
            public BattleUnitData E0 => Enemies[0];
        }

        static Ctx Battle()
        {
            var b = BattlePlugManager.Instance();
            if (b == null || !b.IsBattle()) return null;
            var c = new Ctx();
            foreach (var u in b.GetPlayerUnits())
                if (u?.BattleUnitDataInfo?.Parameter != null && u.BattleUnitDataInfo.Parameter.CurrentHP > 0) c.Players.Add(u);
            foreach (var u in b.GetEnemyUnits())
                if (u?.BattleUnitDataInfo?.Parameter != null && u.BattleUnitDataInfo.Parameter.CurrentHP > 0) c.Enemies.Add(u);
            return c.Players.Count == 0 || c.Enemies.Count == 0 ? null : c;
        }

        static Command Cmd(int id) => MasterManager.Instance?.GetData<Command>(id);
        static Ability Ab(int id) => MasterManager.Instance?.GetData<Ability>(id);
        static string Ok(bool ok) => ok ? "ok" : "FAILED";

        // one act of `ab` under `cmd` by `attacker` on `target`: the target's hit type and value
        static (HitType Type, int Value) Run(BattleUnitData attacker, BattleUnitData target, Command cmd, Ability ab)
        {
            var abilities = new Il2CppSystem.Collections.Generic.List<Ability>();
            abilities.Add(ab);
            var targets = new Il2CppReferenceArray<BattleUnitData>(1);
            targets[0] = target;
            var act = new BattleActData(attacker, targets, cmd, abilities, BattleActData.ActType.Normal,
                (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
            var fns = ParameterActFunctionManagment.GetFunction(ab);
            var res = (HitType.Non, 0);
            if (fns == null) return res;
            var results = new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>();
            var attacks = new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>();
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
                    res = (r.GetHitType(), r.GetValue(false));
                }
            }
            return res;
        }

        static List<(HitType Type, int Value)> Many(int n, Func<(HitType, int)> one)
        {
            var l = new List<(HitType, int)>();
            for (int i = 0; i < n; i++) l.Add(one());
            return l;
        }

        // 1. Demi on a party member: Stamina 127 evades every time, Stamina 0 nearly never (1 in 128)
        static string Stamina()
        {
            var c = Battle();
            var demi = Ab(62);
            var magic = Cmd(7);
            if (c == null || demi == null || magic == null) return null;
            var p = c.P0.BattleUnitDataInfo.Parameter;
            int was = p.BaseVitality;
            int rolls0 = AuditRules.StaminaRolls;
            List<(HitType Type, int Value)> high, low;
            try
            {
                p.BaseVitality = 127;
                high = Many(30, () => Run(c.E0, c.P0, magic, demi));
                p.BaseVitality = 0;
                low = Many(30, () => Run(c.E0, c.P0, magic, demi));
            }
            finally { p.BaseVitality = was; }
            int highMiss = high.Count(x => x.Type == HitType.Miss), lowMiss = low.Count(x => x.Type == HitType.Miss);
            int lowDamage = low.Count(x => x.Type != HitType.Miss && x.Value > 0);
            bool ok = highMiss == 30 && lowMiss <= 3 && lowDamage >= 25;
            return $"[audit] Stamina evasion self-test {Ok(ok)}: Demi on {p.ConfirmedMaxHp()}-HP party member: Stamina 127 missed {highMiss}/30, " +
                   $"Stamina 0 missed {lowMiss}/30 (damage in {lowDamage}); rolls {AuditRules.StaminaRolls - rolls0}";
        }

        // 2. Demi on the first monster if the game takes it for a boss or it resists KO (the PR's miss there): BNW's
        //    normal damage at power 8. Most probe monsters are neither: then skipped (seen in a boss fight instead).
        static string BossFractional()
        {
            var c = Battle();
            var demi = Ab(62);
            var magic = Cmd(7);
            if (c == null || demi == null || magic == null) return null;
            int hits0 = AuditRules.BossHits;
            var runs = Many(10, () => Run(c.P0, c.E0, magic, demi));
            var p = c.E0.BattleUnitDataInfo.Parameter;
            string id = $"monster {c.E0.TryCast<BattleEnemyData>()?.GetMonster()?.Id} ({p.CurrentHP} HP, boss {p.IsBoss()})";
            string got = string.Join(" ", runs.Select(x => $"{x.Type}:{x.Value}"));
            if (AuditRules.BossHits == hits0)
                return $"[audit] fractional-on-boss self-test skipped: {id} gets Demi's own damage ({got}), not the PR's boss miss";
            int hits = runs.Count(x => (x.Type == HitType.Hit || x.Type == HitType.Critical) && x.Value > 0);
            bool ok = hits >= 6;   // the rest: Stamina evasions (a monster's Stamina <= 32)
            return $"[audit] fractional-on-boss self-test {Ok(ok)}: Demi on {id}: {got} (BNW's damage at power 8 instead of a miss)";
        }

        // 3. an Imped party member: a spell in condition group 400 is selectable, Morph isn't
        static string Imp()
        {
            var c = Battle();
            var kappa = MasterManager.Instance?.GetData<Condition>(13);
            var magic = Cmd(7);
            var morph = Cmd(8);
            if (c == null || kappa == null || magic == null || morph == null) return null;
            Ability spell = null;
            foreach (var a in MasterManager.Instance.GetList<Ability>().Values)
                if (a != null && a.ConditionGroupId == 400) { spell = a; break; }
            if (spell == null) return "[audit] Imp self-test FAILED: no spell in condition group 400";
            var p = c.P0.BattleUnitDataInfo.Parameter;
            var list = p.CurrentConditionList;
            bool before = BattleUtility.IsSelectableCommand(p, spell, magic);
            bool spellImp, morphImp;
            list.Add(kappa);
            try
            {
                spellImp = BattleUtility.IsSelectableCommand(p, spell, magic);
                morphImp = BattleUtility.IsSelectableCommand(p, null, morph);
            }
            finally { list.Remove(kappa); }
            bool ok = before && spellImp && !morphImp;
            return $"[audit] Imp self-test {Ok(ok)}: spell {spell.Id} selectable {before}, while Imped {spellImp} (BNW: yes); Morph while Imped {morphImp} (BNW: no)";
        }

        // 5. the back row: Pummel (BNW respects row) and Bushido 85 (PR 124) from the first party member's row
        static string BackRow()
        {
            var c = Battle();
            var fight = Cmd(1);
            if (c == null || fight == null) return null;
            var lines = new List<string>();
            bool ok = true;
            var b = BattlePlugManager.Instance();
            foreach (int id in new[] { 132, 124, 126 })
            {
                var ab = Ab(id);
                if (ab == null) continue;
                AuditRules.LastRowFactor = double.NaN;
                Run(c.P0, c.E0, fight, ab);
                bool pr = ab.TypeId == 601 && !CalcExecuteCorrection.IsIgnoreRearguardAttackAbility(id);
                bool bnw = EsperHooks.D.RespectRowAbilities.Contains(id);
                lines.Add($"ability {id} (type {ab.TypeId}): x{AuditRules.LastRowFactor:0.##} (BNW halves {bnw}, the PR {pr})");
                if (!double.IsNaN(AuditRules.LastRowFactor) && AuditRules.LastRowFactor != 1)
                    ok &= Math.Abs(AuditRules.LastRowFactor - (bnw ? 0.5 : 1) / (pr ? 0.5 : 1)) < 1e-9;
            }
            return $"[audit] back-row self-test {Ok(ok)}: {string.Join("; ", lines)} (x1 = a front-row user, or nothing to change)";
        }

        // 6. every monster's 0-MP death flag is BNW's
        static string Mp0()
        {
            var c = Battle();
            if (c == null) return null;
            var lines = new List<string>();
            bool ok = true;
            foreach (var u in c.Enemies)
            {
                int id = u.TryCast<BattleEnemyData>()?.GetMonster()?.Id ?? 0;
                bool want = EsperHooks.D.DieAtZeroMp.Contains(id), got = u.BattleUnitDataInfo.Parameter.isUnableMP;
                ok &= want == got;
                lines.Add($"monster {id}: {got} (BNW {want})");
            }
            return $"[audit] 0-MP flag self-test {Ok(ok)}: {string.Join("; ", lines)}";
        }
    }
}
