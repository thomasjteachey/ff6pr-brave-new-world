using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Last.Battle;
using Last.Battle.Function;
using Last.Data;
using Last.Data.Master;
using Last.Data.User;
using Last.Management;
using Last.Systems;
using UnityEngine;
using Harness = PrKit.Harness;
using Hooks = PrKit.Hooks;
using MissKind = PrKit.Ff6.SnesInformativeMiss.Kind;
using SnesInformativeMiss = PrKit.Ff6.SnesInformativeMiss;
using SnesInformativeMissMod = PrKit.Ff6.SnesInformativeMissMod;

namespace BraveNewWorld.PR
{
    // BNW's informative misses (c2.asm "Informative Miss", Bropedio's inform-miss-3.asm) are the kit's
    // SnesInformativeMiss (PrKit/Ff6; the code that was here, moved there 2026-11-21): a status attack
    // that changes nothing shows
    //   "Null"  when the target is immune to every status it would set (BNW's MaybeNull; the
    //           instant-death and fractional immunities are statuses the PR's monsters resist too),
    //   "Fail"  when the target was open to a status but evaded it (BNW's Stamina evasion, which
    //           CommandRules' Spirit evasion makes a forced miss: StamEvdChk / StatusHelp; the kit's
    //           default counts every forced miss but the PR's own special refusals, Imp / Control /
    //           WeakMaker),
    //   "Miss"  otherwise (hit roll, status already there, the PR's own special refusals).
    // The kit sorts each status miss in the game's status calc (Fail before Null before Miss, per
    // target) and draws FAIL / NULL into that target's miss popup in the Miss sprite's style; English
    // only. Here: BNW's Config gate ([Battle] InformativeMiss), BNW's self-tests of it (bnwui miss /
    // show / popup) and BNW's other battle UI tests (the Rage list, ATB colours, turns).
    static class BattleUi
    {
        internal static bool Enabled = true;

        public static void Apply(Harmony h, ConfigFile config)
        {
            Enabled = config.Bind("Battle", "InformativeMiss", true,
                "BNW's informative misses: a status attack shows Null on an immune target and Fail when the status was evaded (English).").Value;
            // the kit's postfix on CheckForceMissCondition runs last (Priority.Last): after CommandRules'
            // Spirit evasion and AuditRules' Imp miss, as BattleUi's own did by coming after them in Plugin.cs
            SnesInformativeMiss.Apply(h, new SnesInformativeMissMod { Name = "Brave New World", Tag = "[battle ui]", Enabled = () => Enabled });
            Harness.Register("bnwui", Command, "bnwui miss | show <enemy> <fail|null|miss> | popup <fail|null|miss> [enemy] | gau | rages | " +
                "status <slow|haste|stop> <party slot>: BNW's battle UI self-tests (Fail/Null, Rage list, ATB colours)");
            Harness.Register("gauturn", (a, tick) => Turn(Gau, tick), "gauturn: in battle, only Gau's gauge fills until his command menu opens");
            Harness.Register("turn", (a, tick) => Turn(a.Length > 0 && int.TryParse(a[0], out var c) ? c : Gau, tick),
                "turn <character status id>: in battle, only that character's gauge fills until its command menu opens");
        }

        // --- harness self-tests ---------------------------------------------------------------------

        static string Command(string[] a)
        {
            var what = a.Length > 0 ? a[0] : "";
            try
            {
                switch (what)
                {
                    case "miss": return MissSelfTest();
                    case "popup": return Popup(a.Length > 1 ? a[1] : "fail", a.Length > 2 && int.TryParse(a[2], out var n) ? n : 0);
                    case "show": return Show(a.Length > 1 && int.TryParse(a[1], out var e) ? e : 0, a.Length > 2 ? a[2] : "null");
                    case "gau": return GauJoin();
                    case "join": return Join(a.Length > 1 && int.TryParse(a[1], out var j) ? j : Gau, a.Length > 2 && int.TryParse(a[2], out var sl) ? sl : -1);
                    case "rages": return Rages();
                    case "bars": return WaitGauge.Bars();
                    case "status": return Status(a.Length > 1 ? a[1] : "haste", a.Length > 2 && int.TryParse(a[2], out var s) ? s : 0);
                    default: return "bnwui miss | show <enemy> <fail|null|miss> | popup <fail|null|miss> [enemy] | gau | rages | status <slow|haste|stop> <slot>";
                }
            }
            catch (Exception e) { return $"error: {e.GetType().Name}: {e.Message}"; }
        }

        // One act of `ab` by the first party member on `enemy`, through the game's functions (nothing
        // applied): the game's hit type there, and the kit's sort of a miss ("FAIL", "NULL", "Miss"),
        // taken from it with the kit's KindFor (as the game's miss popup takes it).
        static string RunStatusAct(Ability ab, BattleUnitData caster, BattleUnitData enemy)
        {
            try { return RunStatusActOnce(ab, caster, enemy); }
            catch (Exception e)
            {
                Hooks.NoteOnce("ui-act-" + ab.Id, () => $"[battle ui] ability {ab.Id} not run: {e.Message}");
                return "none";
            }
        }

        static string RunStatusActOnce(Ability ab, BattleUnitData caster, BattleUnitData enemy)
        {
            var fns = ParameterActFunctionManagment.GetFunction(ab);
            if (fns == null || fns.Count == 0) return "none";
            var mm = MasterManager.Instance;
            var abilities = new Il2CppSystem.Collections.Generic.List<Ability>();
            abilities.Add(ab);
            var targets = new Il2CppReferenceArray<BattleUnitData>(1);
            targets[0] = enemy;
            var act = new BattleActData(caster, targets, mm.GetData<Command>(7), abilities, BattleActData.ActType.Normal,
                (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
            var results = new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>();
            var attacks = new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>();
            SnesInformativeMiss.KindFor(enemy);   // an earlier try's sort goes (one frame: the kit would sort them together)
            string got = "none";
            foreach (var f in fns)
            {
                if (f == null) continue;
                f.Init(act, results, attacks);
                f.FixedTarget();
                f.HitCalc();
                var dic = f.ICalcResultDic ?? results;
                if (dic.ContainsKey(enemy)) got = dic[enemy].GetHitType().ToString();
            }
            var k = SnesInformativeMiss.KindFor(enemy);   // null: no sort, or a status landed there
            if (got == "Miss" && k != null && k != MissKind.Miss) got = k.Value.ToString().ToUpper();
            return got;
        }

        static IEnumerable<Ability> StatusOnly()
        {
            foreach (var ab in MasterManager.Instance.GetList<Ability>().Values)
                if (ab != null && ab.Id >= 1 && ab.Id <= 160 && ab.StandardValue == 0 && ab.BattleFunctionGroupId != 0 && ab.ConditionGroupId != 0)
                    yield return ab;
        }

        // The game's popup for a real status miss on a monster: an ability whose act there the game
        // misses and the kit sorts as `want` (up to 60 tries each), then the game's own miss popup
        // (BattleUtility.CreateDamageView, as BattleBasicFunction makes it), drawn from that sort (the
        // one the kit's KindFor took for that monster, handed to the popup as the kit's Forced).
        static string Show(int index, string want)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var players = battle.GetPlayerUnits();
            var enemies = battle.GetEnemyUnits();
            if (players == null || players.Count == 0 || enemies == null || enemies.Count == 0) return "error: no party or monster";
            var enemy = enemies[Math.Clamp(index, 0, enemies.Count - 1)];
            string target = want == "fail" ? "FAIL" : want == "null" ? "NULL" : "Miss";
            foreach (var ab in StatusOnly())
            {
                for (int i = 0; i < 60; i++)
                {
                    var got = RunStatusAct(ab, players[0], enemy);
                    if (got == "none" || got == "Hit" || got == "Critical") break;   // not a status miss here
                    if (got != target) continue;
                    int before = SnesInformativeMiss.Shown;
                    SnesInformativeMiss.Forced = got == "FAIL" ? MissKind.Fail : got == "NULL" ? MissKind.Null : MissKind.Miss;
                    try { BattleUtility.CreateDamageView(enemy, 0, false, true); }
                    finally { SnesInformativeMiss.Forced = null; }
                    string name = MessageManager.Instance.GetMessage(Content(ab.Id)?.MesIdName ?? "", false);
                    return $"show {want} on monster {index} ({enemy.TryCast<BattleEnemyData>()?.GetMonster()?.Id}): ability {ab.Id} " +
                           $"{name?.Replace("<IC_", "<")} missed as {got} on try {i + 1}; popup drawn as " +
                           $"{(SnesInformativeMiss.Shown > before ? target : "the game's Miss")}";
                }
            }
            return $"show {want} on monster {index}: no ability missed that way";
        }

        // In a battle: status-only abilities run on the first monster the way the game runs an
        // act's functions (GetFunction, Init, FixedTarget, HitCalc; nothing applied), each 40
        // times: what the game made of each try and how the kit sorted its misses.
        static string MissSelfTest()
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var players = battle.GetPlayerUnits();
            var enemies = battle.GetEnemyUnits();
            if (players == null || players.Count == 0 || enemies == null || enemies.Count == 0) return "error: no party or monster";
            var parts = new List<string>();
            int nullSeen = 0, failSeen = 0;
            for (int n = 0; n < enemies.Count && n < 4; n++)
            {
                var enemy = enemies[n];
                var res = enemy.BattleUnitDataInfo?.Parameter?.ConfirmedResistanceCondtionList();
                var resisted = new List<int>();
                if (res != null) foreach (var kv in res) if (kv.Value == 1) resisted.Add(kv.Key);
                resisted.Sort();
                var rows = new List<string>();
                int abilities = 0;
                foreach (var ab in StatusOnly())
                {
                    var c = new Dictionary<string, int>();
                    for (int i = 0; i < 40; i++)
                    {
                        var got = RunStatusAct(ab, players[0], enemy);
                        c[got] = c.TryGetValue(got, out var x) ? x + 1 : 1;
                        if (got == "none") break;
                    }
                    if (c.ContainsKey("none")) continue;
                    abilities++;
                    if (c.ContainsKey("FAIL")) failSeen++;
                    if (c.ContainsKey("NULL")) nullSeen++;
                    if (c.ContainsKey("FAIL") || c.ContainsKey("NULL"))
                        rows.Add($"{ab.Id}: " + string.Join(" ", c.Select(kv => $"{kv.Key} {kv.Value}")));
                }
                parts.Add($"monster {n} ({enemy.TryCast<BattleEnemyData>()?.GetMonster()?.Id}, resists [{string.Join(" ", resisted)}]): " +
                          $"{abilities} status abilities, {rows.Count} with FAIL/NULL: {string.Join(", ", rows.Take(12))}");
            }
            return $"[battle ui] miss self-test {(nullSeen > 0 && failSeen > 0 ? "ok" : "INCOMPLETE")}: abilities with NULL {nullSeen}, " +
                   $"with FAIL {failSeen} | " + string.Join(" | ", parts);
        }

        static Content Content(int abilityId)
        {
            foreach (var c in MasterManager.Instance.GetList<Content>().Values)
                if (c != null && c.TypeId == 4 && c.TypeValue == abilityId) return c;
            return null;
        }

        // The game's own miss popup on a monster, as a FAIL / NULL / plain miss.
        static string Popup(string kind, int index)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var enemies = battle.GetEnemyUnits();
            if (enemies == null || enemies.Count == 0) return "error: no monster";
            var unit = enemies[Math.Clamp(index, 0, enemies.Count - 1)];
            SnesInformativeMiss.Forced = kind == "null" ? MissKind.Null : kind == "fail" ? MissKind.Fail : MissKind.Miss;
            int before = SnesInformativeMiss.Shown;
            try { BattleUtility.CreateDamageView(unit, 0, false, true); }
            finally { SnesInformativeMiss.Forced = null; }
            return $"popup {kind} on monster {index}: {(SnesInformativeMiss.Shown > before ? "drawn as " + kind.ToUpper() : "the game's Miss")}";
        }

        // A tick handler: in battle, every other party member's gauge held empty and Gau's full until
        // his command menu opens (the KeyInput BattleMenuController's selected character).
        static string Turn(int who, int tick)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return tick > 600 ? "error: no battle" : null;
            var menu = UnityEngine.Object.FindObjectOfType<Last.UI.KeyInput.BattleMenuController>();
            var sel = menu?.SelectedTarget;
            if (sel?.ownedCharacterData?.characterStatusId == who && menu.IsShow) return $"{sel.ownedCharacterData.Name}'s menu is open (tick {tick})";
            var atb = battle.BattleProgress?.TryCast<BattleProgressATB>();
            var players = battle.GetPlayerUnits();
            if (atb == null || players == null) return null;
            bool there = false;
            foreach (var p in players)
            {
                bool mine = p.TryCast<BattlePlayerData>()?.ownedCharacterData?.characterStatusId == who;
                there |= mine;
                atb.ChangeATBGaugeByUnitData(p, mine ? 100f : 0f);
            }
            return there ? null : $"error: character {who} isn't in the battle";
        }

        static string Status(string which, int slot)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var players = battle.GetPlayerUnits();
            if (players == null || slot < 0 || slot >= players.Count) return "error: no such party member";
            var type = which == "slow" ? Last.Defaine.ConditionType.Slow : which == "stop" ? Last.Defaine.ConditionType.Stop : Last.Defaine.ConditionType.Heist;
            battle.BattleConditionController?.InterruptAddCondition(players[slot], type);
            var now = players[slot].BattleUnitDataInfo?.GetConfirmedConditionType();
            return $"{which} on party member {slot}: {(now != null && now.Contains(type) ? "set" : "NOT set")}";
        }

        // A character joins (outside a battle) into party slot `slot` (-1: a free one, else the last)
        static string Join(int who, int slot)
        {
            var udm = UserDataManager.Instance();
            if (udm == null) return "error: no user data";
            OwnedCharacterData c = null;
            if (udm.OwnedCharacterList != null) foreach (var x in udm.OwnedCharacterList) if (x?.characterStatusId == who) c = x;
            if (c == null) Last.Interpreter.Instructions.SystemCall.Current.JoinCharacter(who, -1, (Last.Defaine.User.CorpsId)1, true, false, false);
            if (udm.OwnedCharacterList != null) foreach (var x in udm.OwnedCharacterList) if (x?.characterStatusId == who) c = x;
            if (c == null) return $"error: character {who} didn't join";
            var party = udm.GetPartyMemberData();
            int members = 0;
            bool inParty = false;
            if (party != null) foreach (var x in party) { members++; if (x.characterStatusId == who) inParty = true; }
            if (!inParty)   // a test run saves nothing
                Last.Management.CorpsClient.Instance().Set(slot >= 0 ? slot : Math.Min(members, 3), c.Id, (Last.Defaine.User.CorpsId)1);
            var names = new List<string>();
            party = udm.GetPartyMemberData();
            if (party != null) foreach (var x in party) names.Add(x.Name);
            return $"party [{string.Join(", ", names)}]";
        }

        // Gau joins (outside a battle) and learns every Rage the mod has (for the Rage list).
        const int Gau = 12;

        static string GauJoin()
        {
            var udm = UserDataManager.Instance();
            if (udm == null) return "error: no user data";
            bool inParty = false;
            var party = udm.GetPartyMemberData();
            if (party != null) foreach (var c in party) if (c.characterStatusId == Gau) inParty = true;
            OwnedCharacterData gau = null;
            if (udm.OwnedCharacterList != null) foreach (var c in udm.OwnedCharacterList) if (c?.characterStatusId == Gau) gau = c;
            if (gau == null) Last.Interpreter.Instructions.SystemCall.Current.JoinCharacter(Gau, -1, (Last.Defaine.User.CorpsId)1, true, false, false);
            if (udm.OwnedCharacterList != null) foreach (var c in udm.OwnedCharacterList) if (c?.characterStatusId == Gau) gau = c;
            if (gau == null) return "error: Gau didn't join";
            party = udm.GetPartyMemberData();
            inParty = false;
            int members = 0;
            if (party != null) foreach (var c in party) { members++; if (c.characterStatusId == Gau) inParty = true; }
            if (!inParty)   // a test run saves nothing: the party's last slot (or a free one) is Gau's
                Last.Management.CorpsClient.Instance().Set(Math.Min(members, 3), gau.Id, (Last.Defaine.User.CorpsId)1);
            var names = new List<string>();
            party = udm.GetPartyMemberData();
            if (party != null) foreach (var c in party) names.Add(c.Name);
            return $"party [{string.Join(", ", names)}]; " + Rages();
        }

        static string Rages()
        {
            var udm = UserDataManager.Instance();
            OwnedCharacterData gau = null;
            if (udm?.OwnedCharacterList != null) foreach (var c in udm.OwnedCharacterList) if (c?.characterStatusId == Gau) gau = c;
            if (gau == null) return "error: Gau hasn't joined (bnwui gau)";
            var mm = MasterManager.Instance;
            // the PR keeps every rage in Gau's list, learned at skill level 100 (unlearned below)
            var have = new Dictionary<int, OwnedAbility>();
            foreach (var o in gau.OwnedAbilityList) if (o?.Ability != null) have[o.Ability.Id] = o;
            var contents = new Dictionary<int, Content>();
            foreach (var c in mm.GetList<Content>().Values) if (c != null && c.TypeId == 4) contents[c.TypeValue] = c;
            int added = 0, described = 0;
            string sample = null;
            foreach (var ab in mm.GetList<Ability>().Values)
            {
                if (ab == null || ab.TypeId != 625 || !contents.TryGetValue(ab.Id, out var content)) continue;
                var desc = content.MesIdDescription;
                if (!string.IsNullOrEmpty(desc) && desc != "None")
                {
                    described++;
                    sample ??= $"{MessageManager.Instance.GetMessage(content.MesIdName, false)}: {MessageManager.Instance.GetMessage(desc, false)}";
                }
                // the rages BNW lets Gau learn: the ones with BNW's own description (2/3: ...; 1/3: ...)
                var text = string.IsNullOrEmpty(desc) || desc == "None" ? "" : MessageManager.Instance.GetMessage(desc, false) ?? "";
                if (!text.StartsWith("2/3:")) continue;
                if (have.TryGetValue(ab.Id, out var owned))
                {
                    if (owned.SkillLevel >= 100) continue;
                    owned.SkillLevel = 100;
                }
                else gau.OwnedAbilityList.Add(new OwnedAbility(ab, content, 100));
                added++;
            }
            return $"Gau (lv {gau.Parameter?.ConfirmedLevel()}): {added} rages learned, {gau.OwnedAbilityList.Count} abilities; " +
                   $"{described} rages with a description, e.g. {sample}";
        }
    }
}
