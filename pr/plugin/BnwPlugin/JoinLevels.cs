using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Data.Master;
using Last.Data.User;
using Last.Management;
using BraveNewWorld.Rules;
using Hooks = PrKit.Hooks;
using SysCall = Last.Interpreter.Instructions.SystemCall.Current;

namespace BraveNewWorld.PR
{
    // BNW's join levels (Rules/JoinRules.cs has the rule and the SNES/BNW references). The PR levels a joiner in
    // SystemCall.Current.JoinCharacter -> CorrectLevel(characterStatusId, isFirstTimeJoin) (decompiled
    // tools/decomp/events): when the character isn't in the roster (GetOwnedCharactersClone(false)), the average
    // AddtionalLevel of the other available characters (statusId < 15; 1 when none), plus
    // correctLevelAtFirstTime[id - 1] on a first join, clamped 1-99, raise only: the exp of that level
    // (ExpTable.TotalExp, less CharacterStatusExp on a rejoin) into AdditionalExp, the stat-up provider's Execution,
    // OwnedCharacterClient.SetOwnedCharacter. This prefix takes over: BNW's first-join offsets, no averaging on World of
    // Balance rejoins (but Shadow's and Setzer's, which stay the PR's own), and BNW's level floors on World of Ruin
    // recruitments; the raise itself is the PR's own steps above.
    static class JoinLevels
    {
        internal static bool Enabled = true;
        internal static string LastJoin;   // tests
        const int CollapseFlag = 41;       // ScenarioFlag2 41: the world has collapsed (Schematics.cs, WC)
        static readonly string[] Names = { "", "Terra", "Locke", "Cyan", "Shadow", "Edgar", "Sabin", "Celes", "Strago", "Relm",
                                           "Setzer", "Mog", "Gau", "Gogo", "Umaro", "Banon", "Leo", "Ghost", "Ghost" };
        // EsperStore.Flags bit (Schematics has bit 0): Banon's level is BNW's (he joined under JoinRules' +2, or a Banon
        // from an older build was raised once by Repair)
        const int FlagBanonLevel = 2;

        public static void Apply(Harmony h)
        {
            Hooks.Patch(h, typeof(SysCall), "CorrectLevel", typeof(JoinLevels), prefix: nameof(CorrectLevel_Prefix));
            PrKit.Harness.Register("joinlv", a => SelfTest(), "joinlv: BNW's join levels through the game's own JoinCharacter " +
                "(World of Balance rejoin keeps its level, World of Ruin rejoin gets BNW's floor, Shadow/Setzer average, a first join's offset); " +
                "changes the roster and levels for this run only (test runs never save)");
        }

        static string Name(int id) => id > 0 && id < Names.Length ? Names[id] : "character " + id;

        static bool WorldOfRuin => Last.Interpreter.DataStorage.Instance?.Get(Last.Interpreter.DataStorage.Category.kScenarioFlag2, CollapseFlag) == 1;

        public static bool CorrectLevel_Prefix(int characterStatusId, bool isFirstTimeJoin)
        {
            if (!Enabled) return true;
            try
            {
                int id = characterStatusId;
                bool wor = WorldOfRuin;
                int average = Average(id);
                int target = JoinRules.Target(id, isFirstTimeJoin, wor, average);
                int before = Level(id);
                string what = $"{Name(id)} {(isFirstTimeJoin ? "first joins" : "rejoins")} in the World of {(wor ? "Ruin" : "Balance")}";
                if (target < 0)
                {
                    LastJoin = $"{what}: the PR's averaging (BNW keeps it), average {average}, level {before}";
                    Plugin.L.LogInfo("[joins] " + LastJoin);
                    return true;
                }
                if (target > before) Raise(id, target, isFirstTimeJoin);
                int after = Level(id);
                if (id == JoinRules.Banon && isFirstTimeJoin) MarkBanon();
                bool party = id >= 1 && id <= 14;   // the guests have no World of Ruin floor
                string rule = isFirstTimeJoin ? $"average {average} {JoinRules.OffsetOf(id):+0;-0;+0}" + (wor && party ? $", floor {JoinRules.RejoinLevel[id - 1]}" : "")
                    : wor && party ? $"floor {JoinRules.RejoinLevel[id - 1]}" : "no averaging";
                LastJoin = $"{what}: level {before} -> {after} (BNW: {rule}; the PR's average {average})";
                Plugin.L.LogInfo("[joins] " + LastJoin);
                return false;
            }
            catch (Exception e) { Hooks.WarnOnce("JoinLevels.CorrectLevel", e); return true; }
        }

        static OwnedCharacterData Find(Il2CppSystem.Collections.Generic.List<OwnedCharacterData> list, int id)
        {
            if (list != null) foreach (var c in list) if (c != null && c.characterStatusId == id) return c;
            return null;
        }

        static int Level(int id) => Find(UserDataManager.Instance()?.GetOwnedCharactersClone(true), id)?.Parameter?.AddtionalLevel ?? 0;

        static bool Available(int id) => Find(UserDataManager.Instance()?.GetOwnedCharactersClone(false), id) != null;

        // the PR's own average (CorrectLevel): the other available characters, statusId < 15; 1 when there are none
        static int Average(int id)
        {
            int sum = 0, n = 0;
            var list = UserDataManager.Instance()?.GetOwnedCharactersClone(false);
            if (list != null)
                foreach (var c in list)
                {
                    if (c == null || c.characterStatusId == id || c.characterStatusId >= 15 || c.Parameter == null) continue;
                    sum += c.Parameter.AddtionalLevel;
                    n++;
                }
            return n == 0 ? 1 : sum / n;
        }

        static void MarkBanon()
        {
            if (EsperStore.Available) EsperStore.Flags |= FlagBanonLevel;
        }

        // Saves from builds before Banon's +2 (BnwRunner, every few seconds): a Banon who already joined at the PR's -3 is
        // raised once (JoinRules.BanonRepair), on the field, outside battle. A Banon who hasn't joined yet waits for
        // CorrectLevel, which marks the save; one who has left stays as he is.
        internal static string LastRepair;   // tests
        internal static void Sync()
        {
            if (!Enabled || !EsperStore.Available || (EsperStore.Flags & FlagBanonLevel) != 0) return;
            try
            {
                var bm = Last.Battle.BattlePlugManager.Instance();
                if (bm != null && bm.IsBattle()) return;
                if (UnityEngine.Object.FindObjectOfType<global::FieldMap>()?.fieldController == null) return;
                Repair();
            }
            catch (Exception e) { Hooks.WarnOnce("JoinLevels.Sync", e); }
        }

        static void Repair()
        {
            int id = JoinRules.Banon, before = Level(id);
            bool here = Available(id);
            if (!here && before <= 1) return;   // he hasn't joined in this save: CorrectLevel will do it
            if (here)
            {
                int average = Average(id), want = JoinRules.BanonRepair(before, average);
                if (want > 0) Raise(id, want, false);
                LastRepair = $"Banon (joined before BNW's +2): level {before} -> {Level(id)} (average {average} +2, at most +5)";
            }
            else LastRepair = $"Banon has left the party: level {before} kept";
            MarkBanon();
            Plugin.L.LogInfo("[joins] " + LastRepair);
        }

        // CorrectLevel's own raise: the level's total exp, the stat-up provider, the owned-character client
        static void Raise(int id, int target, bool firstJoin)
        {
            var owned = Find(UserDataManager.Instance()?.GetOwnedCharactersClone(true), id);
            if (owned?.Parameter == null || target <= owned.Parameter.AddtionalLevel) return;
            var row = MasterManager.Instance?.GetData<ExpTable>(target);
            if (row == null) return;
            int exp = row.TotalExp, status = owned.CharacterStatusExp;
            if (status > 0 && !firstJoin) exp -= status;
            owned.AdditionalExp = exp;
            var current = StatusUpProviderInstanceManager.Instance()?.GetCurrentProvider();
            if (current == null) return;
            var provider = current.TryCast<Last.Systems.StatusUpProvider>();
            var result = provider != null ? provider.Execution(owned, DateTime.Now.Millisecond, null) : current.Execution(owned, DateTime.Now.Millisecond, null);
            if (result != null) new OwnedCharacterClient().SetOwnedCharacter(result);
        }

        // ---- test (harness joinlv): the game's own JoinCharacter, each case's expected level by BNW's rule ----
        static string SelfTest()
        {
            var udm = UserDataManager.Instance();
            var store = Last.Interpreter.DataStorage.Instance;
            if (udm == null || store == null) return "joinlv: no user data (load a game first)";
            int wasRuin = store.Get(Last.Interpreter.DataStorage.Category.kScenarioFlag2, CollapseFlag);
            var lines = new List<string>();
            bool ok = true;
            void Check(bool pass, string line) { ok &= pass; lines.Add((pass ? "ok: " : "FAILED: ") + line); }
            void World(bool ruin) => store.Set(Last.Interpreter.DataStorage.Category.kScenarioFlag2, CollapseFlag, ruin ? 1 : 0);
            void Join(int id) => SysCall.JoinCharacter(id, -1, (Last.Defaine.User.CorpsId)1, false, false, false);
            void Rejoin(int id)
            {
                SysCall.SecedeCharacter(id, false);
                Join(id);
            }
            List<int> Roster()
            {
                var r = new List<int>();
                foreach (var o in udm.GetOwnedCharactersClone(false))
                    if (o != null && o.characterStatusId < 15 && o.Parameter != null) r.Add(o.characterStatusId);
                return r;
            }
            // a first join through the game's JoinCharacter, checked against BNW's offset (vanilla's in the line)
            void FirstJoin(int id, bool ruin)
            {
                World(ruin);
                int a = Average(id), want = JoinRules.Target(id, true, ruin, a);
                Join(id);
                Check(Available(id) && Level(id) == want, $"first join: {Name(id)} level {Level(id)}, want {want} (average {a} " +
                    $"{JoinRules.OffsetOf(id):+0;-0;+0}; vanilla's offset {JoinRules.VanillaOffsetOf(id):+0;-0;+0})");
            }
            try
            {
                // the party leader stays put (the field's player); a New Game's roster is Terra alone (Biggs and
                // Wedge are guests), so Locke joins for this run (a first join: the average, BNW's offset 0, vanilla's +2)
                int leader = 0;
                var party = udm.GetPartyMemberData();
                if (party != null) foreach (var m in party) if (m != null) { leader = m.characterStatusId; break; }
                bool Candidate(int id) => id != leader && id != JoinRules.Shadow && id != JoinRules.Setzer;
                bool any = false;
                foreach (int id in Roster()) any |= Candidate(id);
                if (!any)
                    foreach (int id in new[] { 2, 3, 5, 6, 7 })   // Locke, Cyan, Edgar, Sabin, Celes: the first who never joined
                        if (Level(id) <= 1 && !Available(id)) { FirstJoin(id, false); break; }

                // the lowest-level character in the roster who isn't the leader, Shadow or Setzer
                int c = 0, lc = 999;
                var roster = Roster();
                foreach (int id in roster)
                    if (Candidate(id) && Level(id) < lc) { c = id; lc = Level(id); }
                if (c == 0 || roster.Count < 2) return $"joinlv: self-test FAILED: no one to rejoin (roster {roster.Count}, leader {Name(leader)}); " + string.Join("; ", lines);

                // World of Balance rejoin: the others raised past him first, so the PR's averaging would show
                if (lc < 89)
                    foreach (int o in roster) if (o != c && Level(o) < lc + 10) Raise(o, lc + 10, false);
                World(false);
                int avg = Average(c);
                Rejoin(c);
                Check(!Available(c) ? false : Level(c) == lc,
                    $"World of Balance rejoin: {Name(c)} level {lc} -> {Level(c)} (BNW keeps it; the PR's average {avg}){(Available(c) ? "" : " NOT back in the roster")}");

                // World of Ruin rejoin: BNW's floor, not the average
                World(true);
                int floor = JoinRules.RejoinLevel[c - 1], want = Math.Max(lc, floor);
                avg = Average(c);
                Rejoin(c);
                Check(Level(c) == want, $"World of Ruin rejoin: {Name(c)} level {lc} -> {Level(c)}, want {want} (floor {floor}; the PR's average {avg})");

                // Shadow / Setzer: still the PR's averaging in the World of Balance (Shadow joins first if he never has)
                World(false);
                int s = Level(JoinRules.Shadow) > 1 ? JoinRules.Shadow : Level(JoinRules.Setzer) > 1 ? JoinRules.Setzer : 0;
                if (s == 0) { FirstJoin(JoinRules.Shadow, false); s = Level(JoinRules.Shadow) > 1 ? JoinRules.Shadow : 0; }
                if (s != 0 && s != leader)
                {
                    int ls = Level(s);
                    if (ls < 89)   // the others past him, so the averaging shows
                        foreach (int o in Roster()) if (o != s && Level(o) < ls + 10) Raise(o, ls + 10, false);
                    if (Available(s)) SysCall.SecedeCharacter(s, false);
                    avg = Average(s);
                    Join(s);
                    Check(Level(s) == Math.Max(ls, avg) && Level(s) > ls,
                        $"World of Balance rejoin: {Name(s)} level {ls} -> {Level(s)}, want {Math.Max(ls, avg)} (averaged, as BNW keeps it)");
                }
                else lines.Add("skipped: no Shadow or Setzer to rejoin");

                // a first join: BNW's offset (Gogo -3, Umaro +5; vanilla Gogo +2)
                int f = Level(JoinRules.Gogo) <= 1 ? JoinRules.Gogo : Level(JoinRules.Umaro) <= 1 ? JoinRules.Umaro : 0;
                if (f != 0) FirstJoin(f, false);
                else lines.Add("skipped: Gogo and Umaro have both joined");

                // a guest's first join: Banon at the average + BNW's +2 (the PR's own table: -3); it marks the save
                World(false);
                int b = JoinRules.Banon;
                if (Level(b) <= 1 && !Available(b))
                {
                    FirstJoin(b, false);
                    Check((EsperStore.Flags & FlagBanonLevel) != 0, "Banon's first join marks the save");
                }
                else lines.Add("skipped: Banon has joined (no first join to check)");

                // a save from an older build (unmarked, Banon behind the party): raised once, at most 5 levels
                if (Available(b) && EsperStore.Available)
                {
                    int lb = Level(b);
                    if (lb < 89) foreach (int o in Roster()) if (Level(o) < lb + 10) Raise(o, lb + 10, false);
                    int avgB = Average(b), wantB = Math.Max(lb, Math.Min(avgB + 2, lb + 5));
                    EsperStore.Flags &= ~FlagBanonLevel;
                    Sync();
                    Check(Level(b) == wantB && (EsperStore.Flags & FlagBanonLevel) != 0,
                        $"older save: {LastRepair}, want {wantB} (the PR's average {avgB})");
                    int again = Level(b);
                    Sync();
                    Check(Level(b) == again, $"the repair runs once a save: Banon stays at {Level(b)}");
                }
                else lines.Add("skipped: Banon isn't in the roster (no repair to check)");
            }
            catch (Exception e) { ok = false; lines.Add("FAILED: " + e.GetType().Name + ": " + e.Message); }
            finally { World(wasRuin == 1); }
            try   // the PR's own first-join table (Current.correctLevelAtFirstTime), against vanilla's C0/A228 offsets
            {
                var table = SysCall.correctLevelAtFirstTime;
                var pr = new List<int>();
                if (table != null) for (int i = 0; i < table.Length; i++) pr.Add(table[i]);
                lines.Add($"the PR's first-join offsets [{string.Join(",", pr)}], vanilla's [{string.Join(",", JoinRules.VanillaFirstJoinOffset)}]");
            }
            catch (Exception e) { lines.Add("the PR's first-join offsets unread: " + e.Message); }
            return $"joinlv: self-test {(ok ? "ok" : "FAILED")}; " + string.Join("; ", lines);
        }
    }
}
