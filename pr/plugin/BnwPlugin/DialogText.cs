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
        static Dictionary<string, string[]> bnw, vnw;
        static ConfigEntry<bool> enabled, alternate;
        static readonly Dictionary<string, string> original = new Dictionary<string, string>();
        static bool dirty;   // the choice changed: apply again
        static string appliedScript;
        internal static int Applied;
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
                Plugin.L.LogInfo($"[dialogue] {bnw.Count} lines of BNW's script, {vnw.Count} of Vanilla New World's");
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
                if (!fresh && !dirty) return;
                if (fresh) original.Clear();
                dirty = false;
                if ((int)msg.currentLanguage != English)
                {
                    PrKit.Hooks.NoteOnce("dialogue-language", () => $"[dialogue] the game's language is {msg.currentLanguage}: its own text (BNW's scripts are English)");
                    return;
                }
                string script = !enabled.Value ? "pr" : alternate.Value ? "vnw" : "bnw";
                SetAll(dict, script);
                dict[Marker] = script;
                appliedScript = script;
                Plugin.L.LogInfo($"[dialogue] {(script == "pr" ? "the game's own text" : script == "vnw" ? "Vanilla New World" : "Brave New World")}: {Applied} story lines; " +
                                 $"E0001_03_020_a_05 = '{msg.GetMessage("E0001_03_020_a_05", false)}'");
            }
            catch (Exception e) { PrKit.Hooks.WarnOnce("dialogue", e); }
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

        static readonly Regex Tail = new Regex(@"((?:<WAIT[0-9a-fA-F]+>|<AUTO_P>|<PRESS>|<P>)+)\s*$");
        static readonly Regex Break = new Regex(@"((?:<WAIT[0-9a-fA-F]+>)?(?:<AUTO_P>|<PRESS>|<P>))(\\n|\n)");

        // BNW's pages in the PR line's own form: its end tags kept, its page break between pages
        internal static string Compose(string pr, string[] pages)
        {
            var tail = Tail.Match(pr);
            var brk = Break.Match(pr);
            string nl = pr.Contains("\\n") ? "\\n" : "\n";
            string sep = brk.Success ? brk.Groups[1].Value + brk.Groups[2].Value : "<P>" + nl;
            return string.Join(sep, pages) + (tail.Success ? tail.Groups[1].Value : "");
        }
    }
}
