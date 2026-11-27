using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Last.Data.User;
using Last.Management;
using Hooks = PrKit.Hooks;
using StartSkills = PrKit.Ff6.StartSkills;
using SysCall = Last.Interpreter.Instructions.SystemCall.Current;

namespace BraveNewWorld.PR
{
    // Gau's Rages as BNW has them. BNW cut his list to 64 rages (readme: "the 64 remaining rages"; the ones with a
    // description in its Rage table at C4/A820, tools/port_bnw_descriptions.py) and starts him with one: the initial
    // rage bits at C4/7AA0 hold only Soldier (SNES monster 1), where vanilla's hold 9 (Brawler, Whisper, Were-Rat,
    // Rhinotaur, Lobo, Hornet, Trilobiter, Exocite, M-TekArmor). The PR gives a first-time Gau vanilla's 9 in
    // JoinCharacter, three of them rages BNW removed (they show the PR's "75%: ...; 25%: Attack" text, not BNW's).
    //  * Gau's first join (CorrectLevel's isFirstTimeJoin, JoinLevels): after JoinCharacter he knows Soldier alone;
    //  * any save (Sync, on the field): a rage outside BNW's 64 is un-learned; once a save, Soldier is taught (a Gau
    //    who joined before this fix had vanilla's 9 and no Soldier; the other six are BNW rages and stay, since a
    //    Leap could have taught them too).
    // Rage abilities are 800 + SNES monster id (StartSkills.Sets).
    static class GauRages
    {
        internal const int Gau = 12;
        internal static readonly int[] Start = { 801 };   // Soldier
        internal static readonly HashSet<int> Bnw = new HashSet<int>
        {
            801, 803, 805, 808, 811, 812, 814, 815, 819, 823, 824, 829, 831, 832, 833, 834, 839, 840, 846, 852, 857, 861,
            865, 866, 870, 871, 872, 874, 879, 885, 888, 891, 892, 893, 897, 898, 899, 902, 907, 912, 914, 917, 923, 930,
            935, 936, 937, 942, 945, 947, 950, 1006, 1008, 1010, 1012, 1021, 1023, 1033, 1038, 1041, 1042, 1047, 1048, 1054,
        };
        const int FirstRage = 800, LastRage = 1054;
        const int FlagStartRages = 4;   // EsperStore.Flags (Schematics 1, JoinLevels' Banon 2): Soldier taught to an older save's Gau
        internal static string LastFix = "";   // tests
        static bool firstJoin;

        public static void Apply(Harmony h)
        {
            Hooks.Patch(h, typeof(SysCall), "JoinCharacter", typeof(GauRages), postfix: nameof(Join_Postfix));
            PrKit.Harness.Register("gaurages", a => SelfTest(), "gaurages: Gau's first join through the game's own JoinCharacter " +
                "knows BNW's start rages alone (Soldier), and a rage BNW removed is un-learned on the field (test runs never save)");
        }

        // JoinLevels.CorrectLevel_Prefix: the PR's own first-join test, inside JoinCharacter
        internal static void CorrectLevel(int characterStatusId, bool isFirstTimeJoin)
        {
            if (characterStatusId == Gau && isFirstTimeJoin) firstJoin = true;
        }

        public static void Join_Postfix(int characterStatusId)
        {
            if (characterStatusId != Gau || !firstJoin) return;
            firstJoin = false;
            try
            {
                var gau = Find();
                if (gau == null) return;
                var dropped = Unlearn(gau, id => Array.IndexOf(Start, id) < 0);
                int taught = Start.Count(id => !StartSkills.Knows(gau, id) && StartSkills.Teach(gau, id));
                if (EsperStore.Available) EsperStore.Flags |= FlagStartRages;
                LastFix = $"Gau's first join: the PR's {dropped.Count} start rages un-learned ({string.Join(" ", dropped)}), " +
                          $"BNW's {string.Join(" ", Start)} taught ({taught} new); knows [{string.Join(" ", Known(gau))}]";
                Plugin.L.LogInfo("[rages] " + LastFix);
            }
            catch (Exception e) { Hooks.WarnOnce("GauRages.Join", e); }
        }

        // older saves and anything else: on the field, outside battle (BnwRunner, every few seconds)
        internal static void Sync()
        {
            try
            {
                var bm = Last.Battle.BattlePlugManager.Instance();
                if (bm != null && bm.IsBattle()) return;
                if (UnityEngine.Object.FindObjectOfType<global::FieldMap>()?.fieldController == null) return;
                var gau = Find();
                if (gau == null) return;
                var notes = new List<string>();
                var dropped = Unlearn(gau, id => !Bnw.Contains(id));
                if (dropped.Count > 0) notes.Add($"un-learned {string.Join(" ", dropped)} (not BNW rages)");
                if (EsperStore.Available && (EsperStore.Flags & FlagStartRages) == 0)
                {
                    int taught = Start.Count(id => !StartSkills.Knows(gau, id) && StartSkills.Teach(gau, id));
                    EsperStore.Flags |= FlagStartRages;
                    if (taught > 0) notes.Add($"taught BNW's start rage {string.Join(" ", Start)}");
                }
                if (notes.Count == 0) return;
                LastFix = "Gau (older save): " + string.Join("; ", notes);
                Plugin.L.LogInfo("[rages] " + LastFix);
            }
            catch (Exception e) { Hooks.WarnOnce("GauRages.Sync", e); }
        }

        static OwnedCharacterData Find()
        {
            var list = UserDataManager.Instance()?.OwnedCharacterList;
            if (list != null) foreach (var c in list) if (c != null && c.characterStatusId == Gau) return c;
            return null;
        }

        static List<int> Unlearn(OwnedCharacterData c, Func<int, bool> drop)
        {
            var dropped = new List<int>();
            if (c?.OwnedAbilityList == null) return dropped;
            foreach (var a in c.OwnedAbilityList)
            {
                if (a?.Ability == null || a.SkillLevel <= 0) continue;
                int id = a.Ability.Id;
                if (id < FirstRage || id > LastRage || !drop(id)) continue;
                a.SkillLevel = 0;
                dropped.Add(id);
            }
            return dropped;
        }

        static List<int> Known(OwnedCharacterData c)
        {
            var known = new List<int>();
            if (c?.OwnedAbilityList != null)
                foreach (var a in c.OwnedAbilityList)
                    if (a?.Ability != null && a.Ability.Id >= FirstRage && a.Ability.Id <= LastRage && a.SkillLevel >= 100) known.Add(a.Ability.Id);
            known.Sort();
            return known;
        }

        // harness gaurages: a New Game's Gau joins (the game's own JoinCharacter), then an older save's state is faked
        static string SelfTest()
        {
            var lines = new List<string>();
            bool ok = true;
            void Check(bool pass, string line) { ok &= pass; lines.Add((pass ? "ok: " : "FAILED: ") + line); }
            try
            {
                var gau = Find();
                if (gau == null)
                {
                    SysCall.JoinCharacter(Gau, -1, (Last.Defaine.User.CorpsId)1, false, false, false);
                    gau = Find();
                    if (gau == null) return "gaurages: FAILED: Gau didn't join";
                    var known = Known(gau);
                    Check(known.SequenceEqual(Start), $"first join: knows [{string.Join(" ", known)}], want [{string.Join(" ", Start)}] ({LastFix})");
                }
                else lines.Add("skipped first join: Gau has joined in this save");

                // an older save: vanilla's 9 known, no Soldier, the flag clear
                int[] vanilla = { 811, 814, 819, 821, 825, 846, 854, 857, 866 };
                Unlearn(gau, id => true);
                foreach (int id in vanilla) StartSkills.Teach(gau, id);
                if (EsperStore.Available) EsperStore.Flags &= ~FlagStartRages;
                Sync();
                var after = Known(gau);
                var want = vanilla.Where(Bnw.Contains).Concat(EsperStore.Available ? Start : new int[0]).OrderBy(x => x).ToList();
                Check(after.SequenceEqual(want), $"older save: knows [{string.Join(" ", after)}], want [{string.Join(" ", want)}] ({LastFix})");
                Sync();
                Check(Known(gau).SequenceEqual(after), "the repair is stable (a second Sync changes nothing)");
            }
            catch (Exception e) { return $"gaurages: FAILED: {e.GetType().Name}: {e.Message}"; }
            return $"gaurages: {(ok ? "ok" : "FAILED")}\n" + string.Join("\n", lines);
        }
    }
}
