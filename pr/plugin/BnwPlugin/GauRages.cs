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
        const int FlagVanillaReset = 8; // ... and ForgetVanillaStartRages done in this save (a first join: nothing to forget)
        internal static string LastFix = "";   // tests
        static bool firstJoin;
        // Config Gau.ForgetVanillaStartRages: a player whose Gau joined before 0.26.13 (the PR's 9) and who knows none of
        // the six BNW ones came from a Leap drops them all, once per save (Sync): the save's BNW flags remember it
        // (FlagVanillaReset), so a load of a state not saved since repeats it. It used to turn the switch off at once;
        // on the Deck (2026-11-27) the player loaded again before saving, and the reloaded Gau kept vanilla's 9.
        internal static BepInEx.Configuration.ConfigEntry<bool> Reset;
        internal static readonly int[] Vanilla = { 811, 814, 819, 821, 825, 846, 854, 857, 866 };

        public static void Apply(Harmony h)
        {
            Hooks.Patch(h, typeof(SysCall), "JoinCharacter", typeof(GauRages), postfix: nameof(Join_Postfix));
            StartSkills.RegisterHarness();   // skillcopies: the kit's both-copy Forget / Teach on a loaded save
            PrKit.Harness.Register("gaustate", a =>
            {
                if (a.Length > 1 && a[0] == "switch" && Reset != null) Reset.Value = a[1] == "on";   // a test copy's config
                return State();
            }, "gaustate [switch on|off]: Gau's known rages in each copy, the save's flags (switch: ForgetVanillaStartRages)");
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
                int taught = Start.Count(id => Learn(gau, id));
                if (EsperStore.Available) EsperStore.Flags |= FlagStartRages | FlagVanillaReset;
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
                var before = Known(gau);
                var dropped = Unlearn(gau, id => !Bnw.Contains(id));
                if (dropped.Count > 0) notes.Add($"un-learned {string.Join(" ", dropped)} (not BNW rages)");
                if (Reset != null && Reset.Value && (!EsperStore.Available || (EsperStore.Flags & FlagVanillaReset) == 0))
                {
                    var forgot = Unlearn(gau, id => Array.IndexOf(Vanilla, id) >= 0);
                    int taught = Start.Count(id => Learn(gau, id));
                    string done;
                    if (EsperStore.Available)
                    {
                        EsperStore.Flags |= FlagStartRages | FlagVanillaReset;
                        done = "marked in this save (save to keep it)";
                    }
                    else { Reset.Value = false; done = "the switch is off again (no BNW flags in this save)"; }
                    notes.Add($"ForgetVanillaStartRages: forgot {string.Join(" ", forgot)}, taught {string.Join(" ", Start)} ({taught} new); " +
                              $"knows [{string.Join(" ", Known(gau))}]; {done}");
                }
                if (EsperStore.Available && (EsperStore.Flags & FlagStartRages) == 0)
                {
                    int taught = Start.Count(id => Learn(gau, id));
                    EsperStore.Flags |= FlagStartRages;
                    if (taught > 0) notes.Add($"taught BNW's start rage {string.Join(" ", Start)}");
                }
                if (notes.Count == 0) return;
                notes.Add($"list [{string.Join(" ", Known(gau, true, false))}], menu [{string.Join(" ", Known(gau, false, true))}]");
                LastFix = $"Gau (older save, knew [{string.Join(" ", before)}]): " + string.Join("; ", notes);
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

        // A character's abilities are held twice: OwnedAbilityList, and AbilityDictionary (by command), which the
        // Abilities menu reads. The two can hold different OwnedAbility objects for one ability (the Deck, 2026-11-27:
        // skill level 0 on the list's copies left the menu's rages known), and the game's AddAbility adds to the list
        // only. The kit's StartSkills (Forget, Teach, Knows, Entries) reads and writes both.
        static List<int> Unlearn(OwnedCharacterData c, Func<int, bool> drop) =>
            StartSkills.Forget(c, id => id >= FirstRage && id <= LastRage && drop(id));

        // one rage learned in both copies (Teach puts the list's object in the dictionary's Rage group when it lacks
        // one); true if it was new
        static bool Learn(OwnedCharacterData c, int id)
        {
            bool had = StartSkills.Knows(c, id);
            if (!had) StartSkills.Teach(c, id);
            return !had;
        }

        static List<int> Known(OwnedCharacterData c) => Known(c, true, true);

        // harness gaustate: what each copy holds now, and how many Gau entries the owned list has (read only)
        static string State()
        {
            var list = UserDataManager.Instance()?.OwnedCharacterList;
            int gaus = 0;
            if (list != null) foreach (var c in list) if (c != null && c.characterStatusId == Gau) gaus++;
            var gau = Find();
            if (gau == null) return $"gaustate: no Gau ({gaus} entries)";
            return $"gaustate: {gaus} Gau entries; list [{string.Join(" ", Known(gau, true, false))}], " +
                   $"menu [{string.Join(" ", Known(gau, false, true))}]; flags {(EsperStore.Available ? EsperStore.Flags : -1)}; " +
                   $"switch {(Reset?.Value == true ? "on" : "off")}";
        }

        // known in either copy (menu: the AbilityDictionary alone, which the Abilities menu shows)
        static List<int> Known(OwnedCharacterData c, bool list, bool menu)
        {
            var known = new SortedSet<int>();
            foreach (var a in StartSkills.Entries(c, list, menu))
                if (a.Ability.Id >= FirstRage && a.Ability.Id <= LastRage && a.SkillLevel >= 100) known.Add(a.Ability.Id);
            return known.ToList();
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
                foreach (int id in vanilla) Learn(gau, id);
                if (EsperStore.Available) EsperStore.Flags &= ~FlagStartRages;
                Sync();
                var after = Known(gau);
                var want = vanilla.Where(Bnw.Contains).Concat(EsperStore.Available ? Start : new int[0]).OrderBy(x => x).ToList();
                Check(after.SequenceEqual(want), $"older save: knows [{string.Join(" ", after)}], want [{string.Join(" ", want)}] ({LastFix})");
                var menu = Known(gau, false, true);
                // a loaded save has the dictionary filled; a character who just joined in this session may have none yet
                Check(menu.Count == 0 || menu.SequenceEqual(want), $"the Abilities menu's copy: [{string.Join(" ", menu)}], want [{string.Join(" ", want)}]" +
                    (menu.Count == 0 ? " (not built yet: a join in this session)" : ""));
                Sync();
                Check(Known(gau).SequenceEqual(after), "the repair is stable (a second Sync changes nothing)");
            }
            catch (Exception e) { return $"gaurages: FAILED: {e.GetType().Name}: {e.Message}"; }
            return $"gaurages: {(ok ? "ok" : "FAILED")}\n" + string.Join("\n", lines);
        }
    }
}
