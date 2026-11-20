using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx.Configuration;
using BraveNewWorld.Rules;
using HarmonyLib;
using Last.Management;
using PrKit.Ff6;   // SnesEncounters: the SNES's danger counter on the PR's steps; SnesEncounterRules: its numbers

namespace BraveNewWorld.PR
{
    // BNW's random battles as BNW counts them on the SNES (the user, 2026-11-20: "is BNW mod using the
    // SNES encounter rate?"; it used the PR's own counter, ~38 steps a battle everywhere). The kit's
    // SnesEncounters runs the SNES's danger counter on the PR's steps (as for Worlds Collide); this is BNW's
    // part: bnw_data.json's encounters (the porter's tools/port_encounter_rates.py, read from BNW's ROM):
    //   * its rate codes: other maps by PR monster set (13 not normal: 6 low, 4 high, 3 where BNW turned
    //     random battles off), world maps by zone and the ground's terrain group (BNW rates most of the
    //     World of Ruin high, and many World of Balance terrains have none);
    //   * its battle roll, 4 + random 0-232 (C0/C48C) against the danger's high byte, so a battle needs
    //     the counter past 4/256: at least 12 steps in a normal dungeon (mean 38; vanilla 3 and 31), 7 on
    //     a normal world map (mean 27; vanilla 2 and 23), its "minimum of 10 steps between encounters";
    //   * its party odds 5/16, 5/16, 3/16, 3/16: the port's set weights (80/80/48/48), which the kit draws on;
    //   * its encounter relics: the half rate on the Mystery Egg, none with Dirty Undies (bnw_data
    //     hard_items EncountDownItemId / NonEncountItemId; the PR's own checks still name vanilla's slots,
    //     the Black Belt's and the old Moogle Charm's, HardItems.cs), when the ItemEffects option is on;
    //   * madsiur's Encounters On/Off (BNW's source asm/encounter.asm: a setting in the config menu, bit
    //     $20 of $1D4E, that skips the field's battle check; an optional patch, not in BNW 3.0's own ROM;
    //     the user, 2026-11-20, chose to offer it): a Config row of the kit's (ConfigRows), Off = no danger
    //     added and no battles, as on the SNES.
    static class BnwEncounters
    {
        internal static bool Enabled = true;
        internal static SnesEncounterRules Rules;
        internal static ConfigEntry<bool> On;   // the Config row's setting (BepInEx config Field/RandomEncounters)
        const int RowType = 7201;               // the row's command type (Battle Options' are 7101/7102)

        sealed class Bnw : SnesEncounterMod
        {
            public override SnesEncounterRules Rules => BnwEncounters.Rules;
            public override bool Active => Enabled && Rules != null;
            public override string Tag => "bnw";
            public override bool? HalfRate() { Relics(); return half; }
            public override bool NoEncounters() { Relics(); return none || (On != null && !On.Value); }
        }

        public static void Apply(Harmony h, string dataPath, ConfigEntry<bool> on)
        {
            On = on;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(dataPath));
                if (!doc.RootElement.TryGetProperty("encounters", out var e) || e.ValueKind != JsonValueKind.Object)
                {
                    Plugin.L.LogInfo("[bnw] encounters: no encounters in bnw_data.json (the game's own step counter)");
                    return;
                }
                Rules = SnesEncounterRules.Parse(e);
            }
            catch (Exception ex)
            {
                Plugin.L.LogWarning($"[bnw] encounters: can't read {dataPath}: {ex.Message} (the game's own step counter)");
                return;
            }
            SnesEncounters.Patch(h, new Bnw());
            PrKit.ConfigRows.Add(h, new PrKit.ConfigRow
            {
                Type = RowType, Key = "Encounters", Name = "Encounters",
                Help = "Random battles on the field. Off: no random battles (event battles still happen).",
                Get = () => On.Value,
                Set = v =>
                {
                    if (On.Value == v) return;
                    if (PrKit.TestRun.Running || FormulaProbe.Running) On.ConfigFile.SaveOnConfigSet = false;   // a test run saves nothing of the player's
                    On.Value = v;
                    Plugin.L.LogInfo($"[bnw] encounters: random battles {(v ? "on" : "off")}");
                },
            });
            if (!On.Value) Plugin.L.LogInfo("[bnw] encounters: random battles off (Config)");
        }

        // the party's encounter relics, looked at twice a second at most (a step asks)
        static bool? half;
        static bool none;
        static float relicsAt = -1;

        static HardList ListOf(string member) =>
            EsperHooks.D?.HardItems.FirstOrDefault(l => l.Member == member && l.Type.EndsWith("SpcialFlagData"));

        static void Relics()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (relicsAt >= 0 && now >= relicsAt && now - relicsAt < 0.5f) return;
            relicsAt = now;
            half = null;
            none = false;
            var down = ListOf("EncountDownItemId");
            var stop = ListOf("NonEncountItemId");
            if (!HardItems.Enabled || (down == null && stop == null)) return;   // the game's own checks
            bool h = false, n = false;
            var party = UserDataManager.Instance()?.GetPartyMemberData();
            if (party != null)
                foreach (var c in party)
                {
                    if (down != null && HardItems.Wears(c, down.Bnw)) h = true;
                    if (stop != null && HardItems.Wears(c, stop.Bnw)) n = true;
                }
            half = h;
            none = n;
        }
    }
}
