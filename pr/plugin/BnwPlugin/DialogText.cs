using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using Last.Management;

namespace BraveNewWorld.PR
{
    // Brave New World's dialogue in the PR's story lines, or its alternate script, Vanilla New World
    // (the user, 2026-11-20: "BNW also ships with an optional alternate translation, and i want to
    // include that as a config"). bnw_dialog.json (tools/bnw_dialog.py) holds BNW's own text per PR line
    // key, as pages: {"bnw": {key: [page, ...]}, "vnw": {...}}; the lines are placed on the SNES
    // dialogues through the Japanese (tools/dialog_map.py). Nothing of the PR's text is shipped: each
    // line keeps the PR's own timing tags (<WAITnn><AUTO_P>, <PRESS>) at its end, and its pages are
    // joined the way that line joins its own (<P>, <WAITnn><AUTO_P> or <PRESS> before the line break).
    // English only (BNW's scripts are English): another language keeps the PR's text. Applied whenever
    // the game's text (re)loads, and again when the choice changes in the Config menu.
    static class DialogText
    {
        static Dictionary<string, string[]> bnw, vnw, names, addedBnw, addedVnw;
        static ConfigEntry<bool> enabled, alternate;
        static readonly Dictionary<string, string> original = new Dictionary<string, string>();
        static bool dirty;   // the choice changed: apply again
        static string appliedScript;
        internal static int Applied, Renamed, Added;
        const string Marker = "BNW_DIALOG_SCRIPT";
        const int English = 2;   // MessageManager.currentLanguage (Language.En)
        const string Probe = "E0001_03_020_a_05";   // a story line every language has: the story text has loaded

        public static void Apply(Harmony h, string dir, ConfigFile config)
        {
            enabled = config.Bind("Dialogue", "BnwScript", true,
                "Show Brave New World's own dialogue in the story's lines (English). Off: the game's own text.");
            alternate = config.Bind("Dialogue", "VanillaNewWorld", false,
                "Use BNW's alternate script, Vanilla New World: BNW's script with no fourth-wall breaks and less profanity.");
            var path = Path.Combine(dir, "bnw_dialog.json");
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                bnw = Read(doc.RootElement, "bnw");
                vnw = Read(doc.RootElement, "vnw");
                names = Read(doc.RootElement, "names");
                if (doc.RootElement.TryGetProperty("added", out var added))
                {
                    addedBnw = Read(added, "bnw");
                    addedVnw = Read(added, "vnw");
                }
                Plugin.L.LogInfo($"[dialogue] {bnw.Count} lines of BNW's script, {vnw.Count} of Vanilla New World's, {names.Count} lines naming an item or esper");
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning($"[dialogue] no BNW dialogue ({path}: {e.Message}): the game's own text stays");
                return;
            }
            alternate.SettingChanged += (_, __) => dirty = true;
            enabled.SettingChanged += (_, __) => dirty = true;
            PrKit.ConfigRows.Add(h, new PrKit.ConfigRow
            {
                Type = 7301,
                Key = "BNW_DIALOG_VNW",
                Name = "Vanilla New World",
                Help = "BNW's alternate script: no fourth-wall breaks, less profanity.",
                Get = () => alternate.Value,
                Set = v =>
                {
                    if (alternate.Value == v) return;
                    if (PrKit.TestRun.Running || FormulaProbe.Running) alternate.ConfigFile.SaveOnConfigSet = false;   // a test run saves nothing of the player's
                    alternate.Value = v;
                },
                Offered = () => enabled.Value,
            });
        }

        static Dictionary<string, string[]> Read(JsonElement root, string name)
        {
            var d = new Dictionary<string, string[]>();
            if (!root.TryGetProperty(name, out var set)) return d;
            foreach (var p in set.EnumerateObject())
            {
                var pages = new List<string>();
                foreach (var x in p.Value.EnumerateArray()) pages.Add(x.GetString() ?? "");
                d[p.Name] = pages.ToArray();
            }
            return d;
        }

        // once per frame (BnwRunner): the game's text was (re)loaded, or the choice changed
        public static void Update()
        {
            if (bnw == null) return;
            try
            {
                var msg = MessageManager.Instance;
                var dict = msg?.messageDictionary;
                if (dict == null || !dict.ContainsKey(Probe)) return;   // the story's lines aren't in yet (the system's load first)
                bool fresh = !dict.ContainsKey(Marker);   // the game (re)loaded its text: our marker is gone
                if (!fresh && !dirty)
                {
                    if (UnityEngine.Time.frameCount % 30 == 0) Mikhail(dict);
                    return;
                }
                if (fresh) original.Clear();
                dirty = false;
                // BNW's own lines for scenes the port adds: in every language and script choice (the PR
                // has no text of its own for them; English rather than an empty box)
                AddLines(msg, dict, enabled.Value && alternate.Value ? addedVnw : addedBnw);
                if ((int)msg.currentLanguage != English)
                {
                    PrKit.Hooks.NoteOnce("dialogue-language", () => $"[dialogue] the game's language is {msg.currentLanguage}: its own text (BNW's scripts are English), {Added} lines of BNW's own");
                    dict[Marker] = "pr";   // done until the text reloads
                    return;
                }
                string script = !enabled.Value ? "pr" : alternate.Value ? "vnw" : "bnw";
                SetAll(dict, script);
                SetNames(dict, msg);
                dict[Marker] = script;
                appliedScript = script;
                mikhailWelcome = script == "pr" ? null : (dict.ContainsKey(MikhailKey) ? dict[MikhailKey] : null);
                mikhailHint = null;
                Mikhail(dict);
                Plugin.L.LogInfo($"[dialogue] {(script == "pr" ? "the game's own text" : script == "vnw" ? "Vanilla New World" : "Brave New World")}: {Applied} story lines, {Renamed} item/esper names, {Added} lines of its own; " +
                                 $"E0001_03_020_a_05 = '{msg.GetMessage("E0001_03_020_a_05", false)}'; the opening's first lines = " +
                                 $"'{msg.GetMessage("E0001_00_020_a_01", false)?.Replace("\n", "|")}'");
            }
            catch (Exception e) { PrKit.Hooks.WarnOnce("dialogue", e); }
        }

        // Mikhail, Figaro's chancellor (the PR's sc_e_1071, line E1036_00_031_a_01 "Welcome back, King Edgar!"):
        // with Edgar in the party BNW has him say "Edgar, have you spoken with Julian? He said he had something
        // for you." (dialogue 0x675, BNW_D0675) unless the Schematics are fetched and the world hasn't collapsed
        // (BNW CB/5201: "Welcome back" only with event bits 1DC set and 0A4 clear; Schematics.cs keeps 1DC,
        // the collapse is ScenarioFlag2 41). The PR's script can't read the plugin's flag, so the line's text
        // follows it here (BNW's and VNW's scripts only).
        const string MikhailKey = "E1036_00_031_a_01", JulianHintKey = "BNW_D0675";
        const int CollapseFlag = 41;
        static string mikhailWelcome;
        static bool? mikhailHint;

        static void Mikhail(Il2CppSystem.Collections.Generic.Dictionary<string, string> dict)
        {
            if (mikhailWelcome == null || !dict.ContainsKey(MikhailKey) || !dict.ContainsKey(JulianHintKey)) return;
            var ds = Last.Interpreter.DataStorage.Instance;
            bool collapsed = ds != null && ds.Get(Last.Interpreter.DataStorage.Category.kScenarioFlag2, CollapseFlag) == 1;
            bool hint = !Schematics.Have || collapsed;
            if (hint == mikhailHint) return;
            dict[MikhailKey] = hint ? dict[JulianHintKey] : mikhailWelcome;
            mikhailHint = hint;
            PrKit.Hooks.NoteOnce("mikhail-" + hint, () => $"[dialogue] Mikhail {(hint ? "points Edgar to Julian" : "welcomes Edgar back")}");
        }

        static void SetAll(Il2CppSystem.Collections.Generic.Dictionary<string, string> dict, string script)
        {
            Applied = 0;
            // back to the PR's text first (a switch from one script to the other, or off)
            foreach (var kv in original) dict[kv.Key] = kv.Value;
            if (script == "pr") return;
            var lines = script == "vnw" ? vnw : bnw;
            foreach (var kv in lines)
            {
                if (!dict.ContainsKey(kv.Key)) continue;
                if (!original.TryGetValue(kv.Key, out var pr))
                {
                    pr = dict[kv.Key];
                    original[kv.Key] = pr;
                }
                dict[kv.Key] = Compose(pr, kv.Value);
                Applied++;
            }
        }

        // "Obtained <name>!" lines name the item (or esper) their script gives by the PR's name (the Gold
        // Hairpin), which BNW renamed (Goggles): each gets the game's own current name for its key
        // (bnw_dialog.json "names": a name key per "Obtained" in the line, tools/bnw_dialog.py
        // obtained_names). In every script choice, the game's own text too: the name is the item's.
        static readonly Regex Obtained = new Regex(@"Obtained (.+?)( magicite)?!");
        static readonly Regex Tags = new Regex(@"<[^>]*>");

        // bnw_dialog.json "added": {script: {key: [speaker, page, ...]}} (tools/bnw_dialog.py ADDED_LINES):
        // the line under its own key (BNW_<event>_<nn>) and its speaker's name ("" = no name box)
        static void AddLines(MessageManager msg, Il2CppSystem.Collections.Generic.Dictionary<string, string> dict,
            Dictionary<string, string[]> lines)
        {
            Added = 0;
            if (lines == null) return;
            var speakers = msg.speakerDictionary;
            foreach (var kv in lines)
            {
                if (kv.Value.Length < 2) continue;
                var pages = new string[kv.Value.Length - 1];
                Array.Copy(kv.Value, 1, pages, 0, pages.Length);
                dict[kv.Key] = Compose("", pages);
                if (speakers != null) speakers[kv.Key] = kv.Value[0];
                Added++;
            }
        }

        static void SetNames(Il2CppSystem.Collections.Generic.Dictionary<string, string> dict, MessageManager msg)
        {
            Renamed = 0;
            if (names == null) return;
            foreach (var kv in names)
            {
                if (!dict.ContainsKey(kv.Key)) continue;
                var line = dict[kv.Key];
                var keys = kv.Value;
                int i = 0;
                var now = Obtained.Replace(line, m =>
                {
                    var key = i < keys.Length ? keys[i] : "";
                    i++;
                    if (string.IsNullOrEmpty(key)) return m.Value;
                    var name = Tags.Replace(msg.GetMessage(key, false) ?? "", "").Trim();
                    return name.Length == 0 ? m.Value : "Obtained " + name + m.Groups[2].Value + "!";
                });
                if (now == line) continue;
                if (!original.ContainsKey(kv.Key)) original[kv.Key] = line;
                dict[kv.Key] = now;
                Renamed++;
            }
        }

        static readonly Regex Tail = new Regex(@"((?:<WAIT[0-9a-fA-F]+>|<AUTO_P>|<PRESS>|<P>)+)\s*$");
        static readonly Regex Break = new Regex(@"((?:<WAIT[0-9a-fA-F]+>)?(?:<AUTO_P>|<PRESS>|<P>))(\\n|\n)");

        // BNW's pages in the PR line's own form: its end tags kept, its page break between pages
        internal static string Compose(string pr, string[] pages)
        {
            var tail = Tail.Match(pr);
            var brk = Break.Match(pr);
            string nl = pr.Contains("\\n") ? "\\n" : "\n";
            string sep = brk.Success ? brk.Groups[1].Value + brk.Groups[2].Value : "<P>" + nl;
            // a page's own line breaks (centered narration, letters, the opera's lyrics) in the line's
            // form; a centered line (<CT>) centers each of them
            bool centered = pr.StartsWith("<CT>");
            var parts = new string[pages.Length];
            for (int i = 0; i < pages.Length; i++)
            {
                var rows = pages[i].Split('\n');
                if (centered)
                    for (int r = 0; r < rows.Length; r++) rows[r] = "<CT>" + rows[r];
                parts[i] = string.Join(nl, rows);
            }
            return string.Join(sep, parts) + (tail.Success ? tail.Groups[1].Value : "");
        }
    }
}
