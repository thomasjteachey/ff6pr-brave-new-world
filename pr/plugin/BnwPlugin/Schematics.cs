using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Management;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW's Schematics (Synchysi's events, ca_cb_cc.asm "Helpers for Edgar's Schematics quest"; c2.asm
    // Autocrossbow): in the World of Ruin, Figaro's engineer Julian (SNES map 55, NPC 9, which BNW gave
    // the new event CB520D) says caption 1667 and, the first time Edgar is in the party, caption 1654:
    // he has improved Edgar's crossbow design, "Got [Schematics]" (event bit 1DC). From then on Auto
    // Crossbow hits with Battle Power 225 and never misses (BNW item text: "Power +50%, Accuracy = 100%").
    // The PR's man is the Figaro soldier on Map_20370 (map id 247, Figaro Castle in the World of Ruin;
    // entity 329, a plain talk NPC with message_key N001_C00_021_10_01, the World of Balance one is
    // Map_20050's). While that map is up after the world's collapse his talk key reads one of the mod's own keys (BNW_JULIAN, or
    // BNW_JULIAN_SCHEMATICS when he has them for Edgar), whose text this file gives in
    // MessageManager.GetMessage; the Schematics are handed over when the game fetches that text to show
    // it. Kept in the save in BNW's own block (EsperStore flags, bit 0). Auto Crossbow (item 37) reads
    // BNW's 180 / 100 or, with the Schematics, 225 / 255 in its master data. English only (BNW's text).
    static class Schematics
    {
        internal static bool Enabled = true;
        const int FigaroWoR = 247;           // Map_20370
        const int SoldierObject = 329, SoldierEntity = 261;
        const string SoldierKey = "N001_C00_021_10_01";
        internal const string KeyJulian = "BNW_JULIAN", KeyGift = "BNW_JULIAN_SCHEMATICS";
        const int EdgarStatusId = 5;
        const int AutoCrossbow = 37, UpgradedPower = 225, UpgradedAccuracy = 255;
        const int FlagSchematics = 1;
        static int basePower = -1, baseAccuracy = -1;
        static string cachedGift;
        static float cachedAt;
        internal static int Given;   // tests
        internal static bool TestEdgar;   // tests: Edgar counts as in the party

        public static void Apply(Harmony h)
        {
            Hooks.Patch(h, typeof(MessageManager), "GetMessage", typeof(Schematics), postfix: nameof(GetMessage_Postfix),
                args: new[] { typeof(string), typeof(bool) });
            PrKit.Harness.Register("julian", a =>
            {
                if (a.Length > 0 && a[0] == "wor")   // test runs never save: the World of Ruin for this run only
                    Last.Interpreter.DataStorage.Instance?.Set(Last.Interpreter.DataStorage.Category.kScenarioFlag2, CollapseFlag, 1);
                return Report();
            }, "julian [wor]: Figaro's World of Ruin soldier (map 247) as BNW's Julian: his talk key, the Schematics, Auto Crossbow " +
               "(wor: count this run as the World of Ruin first)");
            FormulaProbe.AddSelfTest("schematics", SelfTest);
        }

        internal static bool Have => (EsperStore.Flags & FlagSchematics) != 0;

        // the world has collapsed (ScenarioFlag2 41, as WC reads it): Julian is BNW's World of Ruin man (event bit 0A4)
        const int CollapseFlag = 41;
        static bool WorldOfRuin => Last.Interpreter.DataStorage.Instance?.Get(Last.Interpreter.DataStorage.Category.kScenarioFlag2, CollapseFlag) == 1;

        static Last.Data.User.OwnedCharacterData Edgar(bool inParty)
        {
            var udm = UserDataManager.Instance();
            if (udm == null) return null;
            if (inParty && TestEdgar) return Edgar(false);
            if (inParty)
            {
                var party = udm.GetPartyMemberData();
                if (party != null)
                    foreach (var c in party)
                        if (c != null && c.characterStatusId == EdgarStatusId) return c;
                return null;
            }
            foreach (var c in udm.OwnedCharacterList)
                if (c != null && c.characterStatusId == EdgarStatusId) return c;
            return null;
        }

        // every second or so (BnwRunner): the soldier's key on Figaro's World of Ruin map, Auto Crossbow's numbers
        internal static void Sync()
        {
            if (!Enabled) return;
            try
            {
                SyncCrossbow();
                var udm = UserDataManager.Instance();
                if (udm == null || udm.CurrentMapId != FigaroWoR || !WorldOfRuin) return;
                var fc = UnityEngine.Object.FindObjectOfType<FieldMap>()?.fieldController;
                if (fc == null) return;
                global::PropertyTalk talk = null;
                foreach (int id in new[] { SoldierObject, SoldierEntity })
                {
                    Last.Entity.Field.FieldEntity e = null;
                    try { e = fc.GetFieldEntity(id); } catch (Exception) { }
                    var p = e?.Property?.TryCast<global::PropertyTalk>();
                    if (p != null && (p.MessageKey == SoldierKey || p.MessageKey == KeyJulian || p.MessageKey == KeyGift)) { talk = p; break; }
                }
                if (talk == null) { Hooks.NoteOnce("schematics-npc", () => "[schematics] Figaro's World of Ruin map is up but its soldier isn't found"); return; }
                string want = !Have && Edgar(true) != null ? KeyGift : KeyJulian;
                if (talk.MessageKey != want)
                {
                    talk.MessageKey = want;
                    Hooks.NoteOnce("schematics-key-" + want, () => $"[schematics] Figaro's soldier (World of Ruin) speaks as Julian: {want}");
                }
            }
            catch (Exception e) { Hooks.WarnOnce("Schematics.Sync", e); }
        }

        static void SyncCrossbow()
        {
            var item = Last.Data.Master.MasterManager.Instance?.GetData<Last.Data.Master.Item>(AutoCrossbow);
            if (item == null) return;
            if (basePower < 0)
            {
                basePower = item.StandardValue;
                baseAccuracy = item.AccuracyRate;
            }
            int power = Have ? UpgradedPower : basePower, accuracy = Have ? UpgradedAccuracy : baseAccuracy;
            if (item.StandardValue == power && item.AccuracyRate == accuracy) return;
            item.StandardValue = power;
            item.AccuracyRate = accuracy;
            Plugin.L.LogInfo($"[schematics] Auto Crossbow: power {power}, accuracy {accuracy}{(Have ? " (the Schematics, BNW)" : "")}");
        }

        public static void GetMessage_Postfix(string key, bool isReplace, ref string __result)
        {
            if (key == null || key.Length < 10 || !key.StartsWith("BNW_JULIAN")) return;
            try
            {
                if (key == KeyJulian) { __result = Text(false, isReplace); return; }
                if (key != KeyGift) return;
                if (cachedGift != null && UnityEngine.Time.unscaledTime - cachedAt < 10f) { __result = cachedGift; return; }
                bool give = Enabled && !Have && (TestEdgar || Edgar(true) != null) && (WorldOfRuin || TestEdgar);
                __result = Text(give, isReplace);
                if (!give) return;
                EsperStore.Flags |= FlagSchematics;
                Given++;
                cachedGift = __result;
                cachedAt = UnityEngine.Time.unscaledTime;
                SyncCrossbow();
                Plugin.L.LogInfo("[schematics] Julian gave Edgar the Schematics: Auto Crossbow power 225, never misses (BNW)");
            }
            catch (Exception e) { Hooks.WarnOnce("Schematics.Message", e); }
        }

        static string Report()
        {
            var udm = UserDataManager.Instance();
            var fc = UnityEngine.Object.FindObjectOfType<FieldMap>()?.fieldController;
            string key = "-";
            foreach (int id in new[] { SoldierObject, SoldierEntity })
            {
                Last.Entity.Field.FieldEntity e = null;
                try { e = fc?.GetFieldEntity(id); } catch (Exception) { }
                var p = e?.Property?.TryCast<global::PropertyTalk>();
                if (p != null) { key = $"entity {id}: {p.MessageKey}"; break; }
            }
            var item = Last.Data.Master.MasterManager.Instance?.GetData<Last.Data.Master.Item>(AutoCrossbow);
            return $"julian: map {udm?.CurrentMapId}; World of Ruin {WorldOfRuin}; soldier {key}; Edgar in the party {Edgar(true) != null}; Schematics {Have}; " +
                   $"Auto Crossbow power {item?.StandardValue}, accuracy {item?.AccuracyRate}";
        }

        // probe battle: the gift through the game's own MessageManager.GetMessage, with Edgar counted in the party,
        // then everything put back (the save's flags word, Auto Crossbow's numbers)
        static string SelfTest()
        {
            if (!FormulaProbe.Running || GameHarness.Active) return null;
            var msg = MessageManager.Instance;
            var item = Last.Data.Master.MasterManager.Instance?.GetData<Last.Data.Master.Item>(AutoCrossbow);
            if (msg == null || item == null || UserDataManager.Instance() == null || !EsperStore.Available) return null;
            int flagsWas = EsperStore.Flags;
            try
            {
                SyncCrossbow();
                int p0 = item.StandardValue, a0 = item.AccuracyRate;
                EsperStore.Flags = flagsWas & ~FlagSchematics;
                cachedGift = null;
                string julian = msg.GetMessage(KeyJulian, true);
                bool edgarHere = Edgar(true) != null;
                string noEdgar = msg.GetMessage(KeyGift, true);
                bool afterNoEdgar = Have;
                // the save's own party: given only with Edgar there in the World of Ruin (Julian's there then); a World of
                // Balance save with Edgar keeps Julian's own line (QA bnw-atb 2026-11-21: Locke/Edgar save, WoB)
                bool wantGiven = edgarHere && WorldOfRuin;
                bool partyOk = wantGiven ? afterNoEdgar : !afterNoEdgar && !noEdgar.Contains("Schematics");
                EsperStore.Flags = flagsWas & ~FlagSchematics;
                TestEdgar = true;
                cachedGift = null;
                string gift = msg.GetMessage(KeyGift, true);
                bool given = Have;
                int p1 = item.StandardValue, a1 = item.AccuracyRate;
                bool ok = julian != null && julian.StartsWith("Julian:") && !julian.Contains("Schematics")
                          && partyOk
                          && gift != null && gift.Contains("Schematics") && given && p1 == UpgradedPower && a1 == UpgradedAccuracy
                          && p0 != UpgradedPower;
                return $"[schematics] self-test {Ok(ok)}: Julian's line \"{julian?.Split('<')[0]}\"; the party {(edgarHere ? "with" : "without")} Edgar " +
                       $"({(WorldOfRuin ? "World of Ruin" : "World of Balance")}, BNW: {(wantGiven ? "given" : "not given")}): " +
                       $"{(afterNoEdgar ? "given" : "not given")}; with Edgar: {(given ? "given" : "not given")}, the text ends " +
                       $"\"{gift?.Substring(Math.Max(0, (gift?.Length ?? 0) - 19))}\"; Auto Crossbow {p0}/{a0} -> {p1}/{a1} (BNW: 225 power, never misses)";
            }
            finally
            {
                TestEdgar = false;
                cachedGift = null;
                EsperStore.Flags = flagsWas;
                SyncCrossbow();
            }
        }

        static string Ok(bool ok) => ok ? "ok" : "FAILED";

        // BNW's captions 1667 (always) and 1654 (the Schematics), as pages
        internal static string Text(bool gift, bool names)
        {
            string edgar = names ? Edgar(false)?.Name ?? "Edgar" : "(EDGAR)";
            var pages = new List<string>
            {
                "Julian: Right, I'm Julian. Right, I'm a genius.",
                $"Julian: In fact, a rat escaped out of here again. But that's not a pet. It's a genius rat that King {edgar} asked me to make.",
            };
            if (gift)
            {
                pages.Add($"{edgar}: I see you've gone quite mad from spending a whole year underground.");
                pages.Add("Julian: Yes! Also, I've modified your crossbow design to increase its efficiency by 50%!");
                pages.Add("Got the Schematics!");
            }
            return string.Join("<P>\n", pages);
        }
    }
}
