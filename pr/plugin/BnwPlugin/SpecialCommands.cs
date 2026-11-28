using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Last.Battle;
using Last.Battle.Function;
using Last.Data.Master;
using Last.Data.User;
using Last.Defaine;
using UnityEngine;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW's limits on Gau's, Gogo's and the X-Magic users' commands (c2.asm), config [Battle]
    // CommandLimits:
    //  * Leap (PR command 24, ability 16, type 615 "Dive"): only in a leapable fight (C2/542A
    //    ChkMenu_Leap: the formation's own Veldt flag, $F5900 + 4f byte 3 bit 1 clear, wherever it
    //    is fought: readme "Leap Anywhere") and not once Gau knows every rage of the fight (C2/6535
    //    LeapDis, Bropedio). The PR's battle menu asks CommandAvailableData.IsAvailable (it greys out
    //    Morph that way); Leap answers no unless the fight's party is on the loaded Veldt table
    //    (port_veldt.py lists every PR party of each of the mod's Veldt formations) and one of its
    //    rages is still unknown. Gau alone keeps Leap on the menu: BNW's Leap then misses with
    //    "Cannot Leap" (C23B71), as the PR's own Dive does ("Can't leap!", MSG_SYSTEM_006).
    //  * Mimic (BattleImpersonationFunctionFF6, decompiled tools/decomp/leap_mimic): BNW wipes
    //    Mimic's memory after each Mimic (C2/0224 ResetMimic), so Gogo can't mimic his own Mimic
    //    (chains with Palidor). The PR walks its act log back to the last act it may copy; when
    //    that act is itself a copy (BattleActData.isCopiedAct), Gogo fights instead, the game's
    //    own fallback (CreateFightActData) for nothing to copy.
    //  * X-Magic: one counterattack per monster per X-Magic (C20084 MayReset keeps the "reaction
    //    script ran" flags between the two spells). The PR gives each spell (ContinuousMagicFirst /
    //    Second acts) its own counters (CounterModule.SetCounter per target, per act): a unit that
    //    counters the first spell doesn't counter the second.
    //  * Scan draws no counterattack (BNW readme "Scan Improvements", Seibaby): SetCounter skips
    //    an act whose ability is Scan.
    static class SpecialCommands
    {
        internal static bool Enabled = true;
        const int LeapCommandId = 24;    // MSG_CMD_NAME_18 "Leap"
        const int ScanAbility = 64;      // Scan (CommandRules: its free turn)

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            Hooks.Patch(h, typeof(Serial.FF6.Management.CommandAvailableData), "IsAvailable", typeof(SpecialCommands),
                postfix: nameof(Available_Postfix));
            Hooks.Patch(h, typeof(BattleImpersonationFunctionFF6), "CreateActDataFromOtherData", typeof(SpecialCommands),
                prefix: nameof(Copy_Prefix));
            Hooks.Patch(h, typeof(CounterModule), "SetCounter", typeof(SpecialCommands),
                prefix: nameof(SetCounter_Prefix), postfix: nameof(SetCounter_Postfix));
            var create = AccessTools.Method(typeof(InstantiateManager), "Create");
            if (create != null) h.Patch(create, postfix: new HarmonyMethod(typeof(SpecialCommands), nameof(Create_Postfix)));
            PrKit.Harness.Register("leapcheck", a => LeapCheck(a), "leapcheck [teach|forget]: in battle, the game's own command check for " +
                "Gau's Leap (BNW's limits on); teach / forget: Gau learns / un-learns this fight's BNW rages first (memory only)");
        }

        static string LeapCheck(string[] a)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var gau = battle.GetPlayerUnits().FirstOrDefault(u => u?.ownedCharacterData?.characterStatusId == GauRages.Gau)?.ownedCharacterData;
            var p = FightParty(battle);
            if (gau == null || p == null) return "error: no Gau or no monster party";
            var rages = FightRages(p).Where(GauRages.Bnw.Contains).ToList();
            string did = "";
            if (a.Contains("teach")) { foreach (int id in rages) PrKit.Ff6.StartSkills.Teach(gau, id); did = $"taught [{string.Join(" ", rages)}]; "; }
            if (a.Contains("forget")) { var f = PrKit.Ff6.StartSkills.Forget(gau, rages.Contains); did = $"forgot [{string.Join(" ", f)}]; "; }
            return "leapcheck: " + did + (BattleCheck() ?? "no check yet");
        }

        // --- Leap ----------------------------------------------------------------------------

        // The fight's monster party. BattlePlugManager.MonsterParty is unset in some battles (harness and event
        // battles, 2026-11-28: "party unknown" in a harness encount 2009), so the party InstantiateManager.Create made
        // the battle from stands in (as in the kit's SnesAiHost).
        static int createdParty;
        public static void Create_Postfix(int __0) => createdParty = __0;

        internal static int FightPartyId(BattlePlugManager battle) =>
            battle?.MonsterParty?.Id is int id && id > 0 ? id : createdParty;

        static MonsterParty FightParty(BattlePlugManager battle)
        {
            var p = battle?.MonsterParty;
            if (p != null && p.Id > 0) return p;
            return createdParty > 0 ? MasterManager.Instance?.GetData<MonsterParty>(createdParty) : null;
        }

        static List<int> FightRages(MonsterParty p) =>
            PrKit.Ff6.SnesRagesRules.Rages(new[] { p.Monster1, p.Monster2, p.Monster3, p.Monster4, p.Monster5, p.Monster6 }.Where(id => id > 0));

        static readonly HashSet<int> leapParties = new HashSet<int>();
        static object leapTable;

        // the parties of the Veldt's formations, from the table the game loaded
        static bool Leapable(int partyId)
        {
            var table = global::Misc.MiscAssetDescAccessor.Desc?.beast_field_encount_table;
            if (table == null) return true;   // no table: leave Leap to the game
            if (!ReferenceEquals(table, leapTable))
            {
                leapParties.Clear();
                foreach (var g in table)
                {
                    if (g?.party_table == null) continue;
                    foreach (var e in g.party_table)
                    {
                        if (e == null) continue;
                        leapParties.Add(e.monster_party_id);
                        leapParties.Add(e.encount_target_party_id);
                        if (e.encount_target_other_ids != null)
                            foreach (var id in e.encount_target_other_ids) leapParties.Add(id);
                    }
                }
                leapTable = table;
            }
            return leapParties.Contains(partyId);
        }

        // BNW's LeapDis (c2.asm C2/6535, Bropedio): Leap is off when every monster of the formation is in the rage list
        // ($1D2C). A monster with no BNW rage has nothing to teach, so it counts as learned (on the SNES one Leap would
        // set its bit; the port un-learns rages outside BNW's 64, GauRages.Sync).
        internal static bool AllRagesKnown(BattlePlugManager battle, OwnedCharacterData c)
        {
            var p = FightParty(battle);
            if (p == null || c == null) return false;
            return RagesKnown(FightRages(p), GauRages.Bnw.Contains, id => PrKit.Ff6.StartSkills.Knows(c, id));
        }

        // the rule itself: no rage left to learn among the fight's rages
        internal static bool RagesKnown(IEnumerable<int> rages, Func<int, bool> isRage, Func<int, bool> knows) =>
            rages.All(id => !isRage(id) || knows(id));

        static readonly ConditionType[] Gone =
        {
            ConditionType.UnableFight, ConditionType.Mineralization, ConditionType.Zombie, ConditionType.Dive,
            ConditionType.Escape, ConditionType.Hide, ConditionType.Withdraw, ConditionType.Withdrawal,
        };

        // party members present and alive (SNES $3A76)
        internal static int Standing(BattlePlugManager battle)
        {
            int n = 0;
            foreach (var u in battle.GetPlayerUnits())
            {
                var info = u?.BattleUnitDataInfo;
                if (info?.Parameter == null || info.Parameter.CurrentHP <= 0) continue;
                var conditions = info.GetConfirmedConditionType();
                bool gone = false;
                if (conditions != null)
                    foreach (var c in Gone)
                        if (conditions.Contains(c)) { gone = true; break; }
                if (!gone) n++;
            }
            return n;
        }

        public static void Available_Postfix(OwnedCharacterData targetData, Command command, ref bool __result)
        {
            if (!Enabled || !__result || command == null || command.Id != LeapCommandId) return;
            try
            {
                var battle = BattlePlugManager.Instance();
                if (battle == null || !battle.IsBattle()) return;
                int party = FightPartyId(battle);
                string why = party > 0 && !Leapable(party) ? $"party {party} is no Veldt formation"
                    : AllRagesKnown(battle, targetData) ? $"{targetData?.Name} knows every rage in party {party}" : null;
                if (why == null) return;
                __result = false;
                Hooks.NoteOnce("leap-" + (why.StartsWith("party") ? "formation" : "known"),
                    () => $"[commands] Leap off for {targetData?.Name}: {why} (BNW)");
            }
            catch (Exception e) { Hooks.WarnOnce("SpecialCommands.Leap", e); }
        }

        // --- Mimic ---------------------------------------------------------------------------

        public static bool Copy_Prefix(BattleUnitData attackUnitData, BattleActData targetActData, ref BattleActData __result)
        {
            if (!Enabled || targetActData == null || !targetActData.isCopiedAct) return true;
            try
            {
                __result = BattleImpersonationFunctionFF6.CreateFightActData(attackUnitData);
                Hooks.NoteOnce("mimic-copy", () => "[commands] Mimic: the last act was a Mimic's own: Gogo fights (BNW)");
                return false;
            }
            catch (Exception e) { Hooks.WarnOnce("SpecialCommands.Mimic", e); return true; }
        }

        // --- X-Magic -------------------------------------------------------------------------

        sealed class XMagic { public IntPtr FirstAct; public float At; public readonly HashSet<IntPtr> Countered = new HashSet<IntPtr>(); }
        static readonly Dictionary<IntPtr, XMagic> xmagic = new Dictionary<IntPtr, XMagic>();   // by caster

        public static bool SetCounter_Prefix(CounterModule __instance, BattleUnitData battleUnitData, BattleActData actData, out int __state)
        {
            __state = -1;
            if (!Enabled || actData == null || battleUnitData == null) return true;
            try
            {
                var caster = actData.AttackUnitData;
                if (caster == null) return true;
                // BNW: Scan can't draw a counterattack (Seibaby's fix, readme "Scan Improvements")
                var abilities = actData.abilityList;
                if (abilities != null && abilities.Count > 0 && abilities[0]?.Id == ScanAbility)
                {
                    Hooks.NoteOnce("scan-counter", () => "[commands] Scan: no counterattack (BNW)");
                    return false;
                }
                var type = actData.UseActAbilityType;
                if (type == BattleActData.ActAbilityType.ContinuousMagicFirst)
                {
                    if (!xmagic.TryGetValue(caster.Pointer, out var x) || x.FirstAct != actData.Pointer)
                        xmagic[caster.Pointer] = new XMagic { FirstAct = actData.Pointer, At = Time.unscaledTime };
                    __state = __instance.calcResults?.Count ?? -1;
                    return true;
                }
                if (type == BattleActData.ActAbilityType.ContinuousMagicSecond
                    && xmagic.TryGetValue(caster.Pointer, out var first) && Time.unscaledTime - first.At < 60f
                    && first.Countered.Contains(battleUnitData.Pointer))
                {
                    Hooks.NoteOnce("xmagic-counter", () => "[commands] X-Magic: a unit that countered the first spell doesn't counter the second (BNW)");
                    return false;
                }
            }
            catch (Exception e) { Hooks.WarnOnce("SpecialCommands.XMagic", e); }
            return true;
        }

        public static void SetCounter_Postfix(CounterModule __instance, BattleUnitData battleUnitData, BattleActData actData, int __state)
        {
            if (__state < 0) return;
            try
            {
                if ((__instance.calcResults?.Count ?? -1) <= __state) return;   // no counter set
                if (xmagic.TryGetValue(actData.AttackUnitData.Pointer, out var x) && x.FirstAct == actData.Pointer)
                    x.Countered.Add(battleUnitData.Pointer);
            }
            catch (Exception e) { Hooks.WarnOnce("SpecialCommands.XMagicCount", e); }
        }


        // In a battle (once): the game's own command check for Leap, with these limits on (null
        // until a battle runs).
        internal static string BattleCheck()
        {
            if (!Enabled) return "[commands] Leap check: off";
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return null;
            var units = battle.GetPlayerUnits();
            if (units == null || units.Count == 0) return null;
            var c = (units.FirstOrDefault(u => u?.ownedCharacterData?.characterStatusId == GauRages.Gau) ?? units[0])
                ?.TryCast<BattlePlayerData>()?.ownedCharacterData;
            var data = Serial.ProviderManager.Instance?.CommandAvailableProvider?.serialData
                ?.TryCast<Serial.FF6.Management.CommandAvailableData>();
            var leap = MasterManager.Instance?.GetData<Command>(LeapCommandId);
            if (c == null || data == null || leap == null) return null;
            int party = FightPartyId(battle);
            bool ok = data.IsAvailable(c, leap, null, null);
            return $"[commands] Leap check in battle: party {(party > 0 ? party.ToString() : "unknown")} {(party <= 0 ? "(no formation rule)" : Leapable(party) ? "is a Veldt party" : "isn't a Veldt party")}, " +
                   $"{(AllRagesKnown(battle, c) ? "all its rages known" : "a rage to learn")}, {Standing(battle)} standing -> Leap {(ok ? "on" : "off")}";
        }

        // Startup check: the PR's Mimic exclusions (they come from static data, unseen in the
        // decompile) and the Veldt table's party count.
        internal static string Check()
        {
            if (!Enabled) return "[commands] limits: off";
            var cmds = BattleImpersonationFunctionFF6.ExcludeCommandIds;
            var abilities = BattleImpersonationFunctionFF6.ExcludeAbilityIds;
            if (cmds == null || abilities == null) return null;
            Leapable(0);
            return $"[commands] limits on: Leap in {leapParties.Count} Veldt parties; Mimic skips commands [{string.Join(",", cmds)}], " +
                   $"abilities [{string.Join(",", abilities)}]";
        }
    }
}
