using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BraveNewWorld.Rules;
using HarmonyLib;
using Last.Data.Master;
using Last.Management;
using PrKit;
using UnityEngine;

namespace BraveNewWorld.PR
{
    [BepInPlugin(Guid, "Brave New World (Pixel Remaster)", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "ff6mod.bravenewworld";
        public const string Version = "0.26.19";
        internal static ManualLogSource L;

        public override void Load()
        {
            L = Log;
            Kit.Init(Log, "Brave New World");   // the modding kit (plugin/PrKit): hooks, harness, test runs
            var restrict = Config.Bind("Espers", "RestrictEquip", true, "Each esper can only be equipped by its BNW users.");
            var levels = Config.Bind("Espers", "EsperLevels", true, "EP from battles raises Esper Levels; each EL adds the equipped esper's bonus.");
            var equip = Config.Bind("Espers", "EquipBonuses", true, "Equipped espers give BNW's equip bonus (stats, elements, statuses, HP/MP %).");
            var noVanilla = Config.Bind("Espers", "DisableLevelUpBonus", true, "Turn off the vanilla esper level-up stat bonus (BNW replaces it with ELs).");
            var autoSpend = Config.Bind("Espers", "AutoSpendEL", false,
                "Spend each new EL at once on the equipped esper's bonus instead of banking it for the esper menu.");
            var spendKey = Config.Bind("Espers", "SpendKey", "None",
                "ELs are spent by moving the cursor down onto the esper's bonus row and pressing Confirm. " +
                "Optionally also a game key that spends from anywhere on that screen: None, Start, Select, PageUp or PageDown.");
            var bossExp = Config.Bind("Battle", "BossExp", true,
                "Every defeated monster pays its EXP, bosses included, as in BNW (the Pixel Remaster " +
                "leaves boss EXP out of the battle total; vanilla bosses give none, many BNW bosses do).");
            var procs = Config.Bind("Battle", "ProcRules", true,
                "Weapon spell procs follow BNW: 1 in 4 per hit, 1 in 2 with a High Proc Rate relic; brush-odds " +
                "chances (Soul Sabre, brushes) 1 in 2 or 3 in 4. Which spell each weapon casts is game data either way.");
            var hardItems = Config.Bind("Battle", "ItemEffects", true,
                "Move effects the game ties to specific items (encounter relics, physical/magic boost relics, MP-crit, " +
                "healing and Defense-ignoring weapons, Dragon Horn, weapons that allow Runic/Bushido, Sketch only with a brush...) to the items BNW gives them, " +
                "and drop ones BNW removed.");
            var relicRules = Config.Bind("Battle", "RelicRules", true,
                "BNW's numbers for relic effects: two-handed grip x1.5, Black Belt counter (Stamina+32)/129, Dragon Horn " +
                "25% for one extra bounce, MP criticals cost Level/2, +25% magic relics don't stack, esper +25% boosts count; " +
                "True Knight cover as BNW has it (also a back-row ally, by Stamina), the covering knight's Evade halved and " +
                "Interceptor never blocking a hit Shadow takes for someone else.");
            var waitTimes = Config.Bind("Battle", "WaitTimes", true,
                "BNW's command wait times: Bushido, Tools, Blitz, Control, Slot, GP Rain x2, Summon x4, Morph waits, " +
                "Jump/Pray/Shock halved, Steal, Mug, Runic, Leap, Mimic, Row, Defend, Possess, Magitek act at once.");
            var waitGauge = Config.Bind("Battle", "WaitGauge", true,
                "BNW's Wait Gauge: after a command is chosen the ATB bar shows the wait left; the bar turns red under " +
                "Stop, yellow under Slow, green under Haste.");
            var spiritAbilities = Config.Bind("Battle", "SpiritAbilities", true,
                "BNW's Spirit-based abilities (Restore, Regen, Rerise, Aurabolt, many Lores, Rages and Dances) use the " +
                "caster's Spirit (Stamina) instead of Magic.");
            var damageRules = Config.Bind("Battle", "DamageRules", true,
                "BNW's damage changes on top of the game's formulas: Fight formula, defensive variance by Vigor/Spirit, " +
                "back row x0.75, Defend halves magic, BNW's monster special attack multipliers, Imp (equipment still counts, " +
                "an imped attacker does half damage), Blind (a blinded attacker hits 50% of the time, even with can't-miss attacks), " +
                "hit rate x1.25 against Blind/Zombie/Muddle/Slow targets but not Rerise, and no harder to hit with Poison/Near Fatal/Seizure/Haste.");
            var statusTicks = Config.Bind("Battle", "StatusTicks", true,
                "BNW's Regen/Poison/Sap: amounts from MaxHP and Stamina, Poison growing up to x16.5, ticks twice as often.");
            var stealRules = Config.Bind("Battle", "StealRules", true,
                "BNW's Steal: success (Speed*2+112)/256, the rare item with chance Speed/256.");
            var turnRules = Config.Bind("Battle", "TurnRules", true,
                "BNW's turns: half a turn after Defend or Row, a free turn after a successful Steal/Mug or a Scan; " +
                "Morph has no gauge (always available, lasts until Revert or the battle's end); Umaro's attack odds, and a thrown Mog " +
                "always a critical hit.");
            var reflectRules = Config.Bind("Battle", "ReflectRules", true,
                "BNW's Reflect: no timer, but 1 in 3 to break each time it reflects (not from equipment).");
            var commandLimits = Config.Bind("Battle", "CommandLimits", true,
                "BNW's command limits: Leap in any Veldt formation wherever it's fought, greyed out once Gau knows all its rages;" +
                "Gogo can't Mimic a Mimic (he fights instead); one counterattack per monster per X-Magic.");
            var danceRules = Config.Bind("Battle", "DanceRules", true,
                "BNW's Dance: one dance per command, the dancer stays under control (step odds and no stumbling are game data).");
            var spiritEvade = Config.Bind("Battle", "SpiritEvasion", true,
                "BNW's Spirit evasion: Spirit-evadable spells and monster specials miss a status with chance (Spirit+1)/128.");
            var atbFormula = Config.Bind("Battle", "AtbFormula", true,
                "BNW's Speed Balance: ATB gauges fill at BNW's SNES rates, characters 75 x (Speed + 51) (Haste 90, Slow 60), " +
                "monsters 112 x (Speed + 20) (Haste 135, Slow 90), with no battle-speed setting (vanilla: 96 x (Speed + 20)).");
            var quickfill = Config.Bind("Battle", "Quickfill", true,
                "BNW's Quickfill: battle time runs twice as fast while no party member's turn is up (ATB gauges, waits " +
                "and status timers; time stands still anyway while an action plays).");
            var slotRules = Config.Bind("Battle", "SlotRules", true,
                "Setzer's Slot as in BNW: BNW's reels (no Bahamut) and no rigging (reels stop where you stop them).");
            // a new key: the old "nATB" (default off, and it never stopped time) stays in players' config files as false
            var natb = Config.Bind("Battle", "nATBOnly", true,
                "BNW's nATB: time stops while an action plays out (ATB gauges, waits, status timers) and in the battle " +
                "menus. BNW hides Battle Speed and, without Battle Options, Battle Mode. With Battle Options, Battle Mode offers Wait/Pause. Battles " +
                "run in Wait (your saved settings stay as they are for other games and mods; BNW's ATB fill ignores the battle speed, its waits are timed through it).");
            var encounterRates = Config.Bind("Field", "EncounterRates", true,
                "Random battles come at BNW's SNES rates: its danger counter per map and world zone, at least ~10 steps " +
                "between battles in most places, its party odds, the Mystery Egg halving them and Dirty Undies stopping them " +
                "(off: the Pixel Remaster's own step counter, ~38 steps a battle everywhere).");
            var randomEncounters = Config.Bind("Field", "RandomEncounters", true,
                "Random battles on (the in-game Config menu's Encounters row, madsiur's BNW option; with EncounterRates on).");
            var damageLog = Config.Bind("Testing", "DamageLog", true,
                "Write every damage/heal result with its inputs to BepInEx/bnw_damage.csv (for porting BNW's formulas).");
            var testKit = Config.Bind("Testing", "TestKit", false,
                "F8 on a loaded game: every BNW esper, EL 10 with 10 ELs to spend and the next EL one EP away, " +
                "20 SP banked, 500,000 gil (save to a spare slot to keep it). F9 on the field: start the next " +
                "boss fight that runs BNW's AI. F10 in battle: defeat every enemy (counts as a win). " +
                "Controller: click both sticks (L3+R3): tap in battle = F10, tap on the field = F9, hold 1.5 s = F8.");

            // BNW's rules need BNW's data: its Magicite folder in Magicite's import directory (an
            // install without it would put BNW's rules on the game's own data)
            if (!MagiciteDir.Has("BraveNewWorld"))
            {
                L.LogWarning($"Brave New World's data isn't installed (no BraveNewWorld folder in {MagiciteDir.ImportDir}); BNW rules are off.");
                return;
            }
            var dataPath = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "bnw_data.json");
            try
            {
                EsperHooks.D = BnwData.Load(dataPath);
                BnwMonsterImages.Ensure(dataPath);   // BNW's monster images, made once from this game (the kit's PrKit/MonsterImages.cs)
            }
            catch (Exception e)
            {
                L.LogError($"can't read {dataPath}: {e.Message}. Esper rules are off.");
                AddComponent<BnwRunner>();
                return;
            }
            EsperHooks.Restrict = restrict.Value;
            EsperHooks.Levels = levels.Value;
            EsperHooks.EquipBonuses = equip.Value;
            EsperHooks.NoVanillaBonus = noVanilla.Value;
            EsperHooks.AutoSpend = autoSpend.Value;
            EsperMenu.SpendKeyName = spendKey.Value.Trim();
            TestKitHotkey.Enabled = testKit.Value;
            BattleRewards.Enabled = bossExp.Value;
            WeaponProcs.Enabled = procs.Value;
            HardItems.Enabled = hardItems.Value;
            RelicRules.Enabled = relicRules.Value;
            WaitGauge.WaitTimes = waitTimes.Value;
            WaitGauge.Gauge = waitGauge.Value;
            BattleFormulas.Spirit = spiritAbilities.Value;
            AtbRules.SpeedBalance = atbFormula.Value;
            AtbRules.Quickfill = quickfill.Value;
            AtbRules.Natb = natb.Value;
            BattleFormulas.Damage = damageRules.Value;
            StatusTicks.Enabled = statusTicks.Value;
            CommandRules.Steal = stealRules.Value;
            CommandRules.Turns = turnRules.Value;
            CommandRules.Reflect = reflectRules.Value;
            CommandRules.SpiritEvade = spiritEvade.Value;
            CommandRules.Dance = danceRules.Value;
            DamageLog.Enabled = damageLog.Value;
            var harmony = new Harmony(Guid);
            // BNW's own saves, apart from vanilla's and WC's (the user, 2026-11-19), synced by Steam Cloud (PrKit/ModSaves.cs)
            ModSaves.IsTestRun = () => FormulaProbe.Running || TestRun.Running;
            ModSaves.Apply(harmony, "BraveNewWorld");
            EsperHooks.Apply(harmony);
            BattleRewards.Apply(harmony);
            BattleExtras.Apply(harmony, Config);   // audit C: spear/dual-wield pairing in equip lists; NoGainz (Config "Exp. Gain") + Veldt no EXP / x2 gil
            WeaponProcs.Apply(harmony);
            HardItems.Apply(harmony);
            RelicRules.Apply(harmony);
            CoverRules.Enabled = relicRules.Value;
            CoverRules.Apply(harmony);
            WaitGauge.Apply(harmony);
            BattleFormulas.Apply(harmony);
            StatusTicks.Apply(harmony);
            ImpRules.Enabled = damageRules.Value;
            ImpRules.Apply(harmony);
            BlindRules.Enabled = damageRules.Value;
            BlindRules.Apply(harmony);
            HitRules.Enabled = damageRules.Value;
            MorphRules.Enabled = turnRules.Value;
            MorphRules.Apply(harmony);
            CommandWeapons.Enabled = hardItems.Value;
            CommandWeapons.Patch(harmony);
            SlotRules.Enabled = slotRules.Value;
            SlotRules.Apply(harmony);
            UmaroRules.Enabled = turnRules.Value;
            UmaroRules.Apply(harmony);
            Coliseum.Apply(harmony);   // BNW's Coliseum matches (the kit's SnesColiseum)
            // no "can't be suplexed" rule: BNW's Suplex (SNES 5E) has no special effect (spell byte 9 = FF; vanilla's
            // $30 kept Suplex off monsters with byte $13 bit 2, and BNW's per-strike $30 is a no-op), and bit 2 is its
            // boss flag for fractional damage: Suplex hits everything, the Soul Train too (2026-11-27)
            ReviveRules.Enabled = damageRules.Value;
            ReviveRules.Apply(harmony);   // BNW's revive amounts: Phoenix Down 1 HP, Phoenix Tear 3/4, Life 250-500
            ItemHealRules.Enabled = damageRules.Value;
            ItemHealRules.Apply(harmony);   // BNW's fractional healing items: Tonic 1/2, Potion / X-Potion 3/4, Ethers 3/4 MP
            ChakraGpRules.Enabled = damageRules.Value;
            ChakraGpRules.Apply(harmony);   // Chakra restores (Stamina + Level)/2 MP; GP Rain costs Stamina x 10
            WeaponEffects.Enabled = damageRules.Value;
            WeaponEffects.Apply(harmony);   // Anti-Air x2/x3 on Fight; Zantetsuken/Ichimonji crit vs death-immune, Mug, Jump
            FirstStrike.Enabled = turnRules.Value;
            FirstStrike.Apply(harmony);   // BNW's starting ATB (Speed-weighted; back / preemptive / side cases)
            AuditRules.Apply(harmony, damageRules.Value, turnRules.Value);   // audit batch B: Stamina evasion, fractional on bosses, Imp, counters on misses, back row, 0 MP
            BattleItems.Apply(harmony, hardItems.Value, damageRules.Value);   // v0.25.0: stars spread, Golem, MP-crit procs (Palidor: profile_bnw)
            AtbRules.Apply(harmony);   // Speed Balance, Quickfill, nATB (the kit's SnesAtb, SnesQuickfill, SnesActionFreeze, BattleSettingsLock)
            CommandRules.Apply(harmony);
            HotswapRules.Enabled = turnRules.Value;
            HotswapRules.Apply(harmony);   // Hotswap Delay: a weapon/shield swap in battle ends the turn as Defend (half turn)
            SpecialCommands.Enabled = commandLimits.Value;
            SpecialCommands.Apply(harmony);
            ShopCalls.Apply(harmony);   // the mod's added merchants (SysCall "BNW:shop <group>")
            MenuRules.Apply(harmony);   // audit C: Ultros's EL respec SysCall, Umaro's equip menu, his airship unequip
            DamageLog.Apply(harmony);
            EsperMenu.Apply(harmony);
            EsperList.Apply(harmony);
            EsperLevelDisplay.Apply(harmony);
            BnwEnemyAi.Apply(harmony, dataPath);   // no switch: the port gives these monsters only the plugin's shim
            if (encounterRates.Value) BnwEncounters.Apply(harmony, dataPath, randomEncounters);
            DialogText.Apply(harmony, Path.GetDirectoryName(dataPath), Config);   // BNW's script / Vanilla New World's on the story lines
            BattleUi.Apply(harmony, Config);   // "Fail" / "Null" in place of Miss (the kit's SnesInformativeMiss, after CommandRules' forced misses), Rage list tests
            EngineRules.Apply(harmony, damageRules.Value, stealRules.Value, turnRules.Value);   // elemental share, Mug, Rage statuses + engine self-tests (after EsperHooks, CommandRules)
            EsperResultRows.Apply(harmony);   // "<name> gained an EL" on the victory screen
            HarnessItems.Register();   // harness: bnwgive <item> [n], exp
            PrKit.Ff6.HarnessBattle.Register();   // harness: units, unithp, cond, act (battle probes)
            PartyEvents.Register();   // harness: bnwgear [strip <ids>] (audit C party events QA)
            ReviveHealRules.Enabled = damageRules.Value;
            ReviveHealRules.Apply(harmony);   // Kirin / Heal Force / Jackpot heal the living and revive the dead
            TickDeaths.Enabled = damageRules.Value;
            TickDeaths.Apply(harmony);   // a Poison/Sap death still runs a monster's death script (BNW's DeathCounterFix)
            ExploderRule.Enabled = damageRules.Value;
            ExploderRule.Apply(harmony);   // a party member's Exploder: 250% of the user's current HP
            MindblowRule.Enabled = damageRules.Value;
            MindblowRule.Apply(harmony);   // Bushido Mindblow: 500 MP (BNW's BlowFish effect on an MP hit)
            Schematics.Enabled = hardItems.Value;
            Schematics.Apply(harmony);   // Julian's Schematics in Figaro (World of Ruin): Auto Crossbow 225, never misses
            JoinLevels.Apply(harmony);   // BNW's join levels: no averaging on rejoins, World of Ruin floors, first-join offsets
            GauRages.Reset = Config.Bind("Gau", "ForgetVanillaStartRages", false,
                "One time: Gau forgets the 9 starting Rages the port gave him before 0.26.13 (Brawler, Whisper, Were-Rat, " +
                "Rhinotaur, Lobo, Hornet, Trilobiter, Exocite, M-TekArmor) and learns BNW's Soldier, the next time you're " +
                "on the field. Once per save: the save remembers it once you save (loading a state you didn't save does it " +
                "again). Rages he learned another way stay. Save afterwards; turn it off when your saves are done.");
            GauRages.Apply(harmony);     // Gau's start rages: BNW's Soldier, not the PR's vanilla 9; none of the rages BNW cut
            VeldtReturn.Apply(harmony);  // Gau back from a Leap: log lines + harness gaureturn (the fix itself is port_bnw_party_events data)
            FormulaProbe.Init(harmony);  // only when BepInEx/bnw_probe.request exists
            AddComponent<BnwRunner>();
            L.LogInfo($"Brave New World PR plugin loaded: {EsperHooks.D.Espers.Count} espers, " +
                      $"restrict={restrict.Value} levels={levels.Value} equip={equip.Value} noVanillaBonus={noVanilla.Value} " +
                      $"autoSpend={autoSpend.Value} spendKey={EsperMenu.SpendKeyName} bossExp={bossExp.Value} testKit={testKit.Value}");
        }
    }

    // Per-frame driver: refreshes the cached esper progress (so loading a save is
    // picked up) and logs a one-time check that the Magicite data port applied.
    public class BnwRunner : MonoBehaviour
    {
        public BnwRunner(IntPtr ptr) : base(ptr) { }
        bool verified, textVerified, slotChecked, runicChecked, sketchChecked, veldtChecked, gripChecked, limitsChecked, leapChecked, coverChecked, tossChecked, coliseumChecked;
        int tries, frame;

        // The Veldt's table as the game loaded it (null until the misc asset is in). The mod's
        // copy (tools/port_veldt.py) has 27 groups, 74 formations, first party 2001; vanilla's
        // 55 groups, 371 formations, first party 6.
        static string VeldtCheck()
        {
            var table = global::Misc.MiscAssetDescAccessor.Desc?.beast_field_encount_table;
            if (table == null) return null;
            int entries = 0, first = -1;
            foreach (var g in table)
            {
                if (g?.party_table == null) continue;
                entries += g.party_table.Length;
                if (first < 0 && g.party_table.Length > 0) first = g.party_table[0].monster_party_id;
            }
            return $"[veldt] {table.Length} groups, {entries} formations, first party {first}";
        }

        // after the field's sprite animations: the recovery save points' sparkle frames swapped for their red copies
        void LateUpdate() => RecoveryPoints.LateUpdate();

        void Update()
        {
            if (++frame % 30 == 0) EsperStore.Invalidate();
            if (frame % 120 == 0) EsperHooks.UnequipDisallowed();   // old saves: espers BNW forbids that character
            if (frame % 60 == 30) Schematics.Sync();   // Figaro's World of Ruin soldier as Julian; Auto Crossbow's numbers
            if (frame % 120 == 90) JoinLevels.Sync();   // older saves: a Banon who joined at the PR's -3 raised once
            if (frame % 120 == 100) GauRages.Sync();    // older saves: Gau's rages BNW cut un-learned, Soldier once
            if (frame % 10 == 0) { BattleRewards.Track(); WaitGauge.Track(); StatusTicks.Track(); TickDeaths.Track(); VeldtReturn.Track(); }
            if (frame % 300 == 200) MenuRules.Track();   // one-time log self-test (Umaro's menu)
            if (frame % 10 == 5) BnwMonsterImages.Track();   // the final battle tower: a statue part that dies leaves the picture
            BattleFormulas.Track();  // Shock's cost to its user, after its hits
            if (verified && frame % 300 == 0) { HardItems.Fix(); RelicRules.SetConstants(); }
            TestKitHotkey.Update();
            FormulaProbe.BattleChainDone = tossChecked;   // the chain below ends with toss; new checks: FormulaProbe.AddSelfTest
            BnwEnemyAi.Update();
            RecoveryPoints.Update();   // BNW's full-recovery save points: red sparkle; the heal on stepping onto one
            DialogText.Update();
            FormulaProbe.Update();
            if (verified && !veldtChecked && frame % 60 == 0)
            {
                try
                {
                    var line = VeldtCheck();
                    if (line != null) { veldtChecked = true; Plugin.L.LogInfo(line); }
                }
                catch (Exception e) { veldtChecked = true; Plugin.L.LogWarning($"[veldt] check: {e}"); }
            }
            if (verified && !gripChecked && frame % 60 == 0)
            {
                try
                {
                    var line = RelicRules.GripCheck();
                    if (line != null) { gripChecked = true; Plugin.L.LogInfo(line); }
                }
                catch (Exception e) { gripChecked = true; Plugin.L.LogWarning($"[grip] check: {e}"); }
            }
            if (verified && veldtChecked && !limitsChecked && frame % 60 == 0)
            {
                try
                {
                    var line = SpecialCommands.Check();
                    if (line != null) { limitsChecked = true; Plugin.L.LogInfo(line); }
                }
                catch (Exception e) { limitsChecked = true; Plugin.L.LogWarning($"[commands] limits check: {e}"); }
            }
            if (limitsChecked && !leapChecked && frame % 60 == 0)
            {
                try
                {
                    var line = SpecialCommands.BattleCheck();
                    if (line != null) { leapChecked = true; Plugin.L.LogInfo(line); }
                }
                catch (Exception e) { leapChecked = true; Plugin.L.LogWarning($"[commands] Leap check: {e}"); }
            }
            if (leapChecked && !coverChecked && frame % 60 == 0)
            {
                try
                {
                    var line = CoverRules.SelfTest();   // probe battles only
                    if (line != null) { coverChecked = true; Plugin.L.LogInfo(line); }
                }
                catch (Exception e) { coverChecked = true; Plugin.L.LogWarning($"[cover] self-test: {e}"); }
            }
            if (coverChecked && !tossChecked && frame % 60 == 0)
            {
                try
                {
                    var line = UmaroRules.TossSelfTest();   // probe battles only
                    if (line != null) { tossChecked = true; Plugin.L.LogInfo(line); }
                }
                catch (Exception e) { tossChecked = true; Plugin.L.LogWarning($"[umaro] toss self-test: {e}"); }
            }
            if (verified && !runicChecked && frame % 30 == 0)
            {
                try
                {
                    var line = CommandWeapons.Apply();
                    if (line != null) { runicChecked = true; Plugin.L.LogInfo(line); }
                }
                catch (Exception e) { runicChecked = true; Plugin.L.LogWarning($"[weapons] {e}"); }
            }
            if (verified && !sketchChecked && frame % 60 == 0)
            {
                try
                {
                    var line = CommandWeapons.SketchCheck();
                    if (line != null) { sketchChecked = true; Plugin.L.LogInfo(line); }
                }
                catch (Exception e) { sketchChecked = true; Plugin.L.LogWarning($"[sketch] check: {e}"); }
            }
            if (verified && !slotChecked && frame % 30 == 0)
            {
                try
                {
                    var line = SlotRules.Check();
                    if (line != null) { slotChecked = true; Plugin.L.LogInfo(line); Plugin.L.LogInfo(UmaroRules.Check()); }
                }
                catch (Exception e) { slotChecked = true; Plugin.L.LogWarning($"[slot] check: {e}"); }
            }
            if (verified && !coliseumChecked && frame % 30 == 0)
            {
                coliseumChecked = true;
                try { Plugin.L.LogInfo(Coliseum.Check()); }
                catch (Exception e) { Plugin.L.LogWarning($"[coliseum] check: {e}"); }
            }
            if (textVerified) return;
            if (verified)
            {
                var msg = MessageManager.Instance;
                var name = msg?.GetMessage("MSG_MAGIC_NAME_13", false);
                if (string.IsNullOrEmpty(name)) return;
                textVerified = true;
                Plugin.L.LogInfo($"[verify] text: ability43='{name}' bonus00='{msg.GetMessage("MSG_BNW_ESPER_BONUS_00", false)}' " +
                                 $"battle line BNW_BTL_018_01='{msg.GetMessage("BNW_BTL_018_01", false)}' " +
                                 $"game story E0055_02_343_b_13='{msg.GetMessage("E0055_02_343_b_13", false)}'");
                return;
            }
            var mm = MasterManager.Instance;
            if (mm == null || !mm.isInitialized) return;
            try
            {
                var guard = mm.GetData<Monster>(1);
                var terra = mm.GetData<CharacterStatus>(1);
                if (guard == null || terra == null) { if (++tries % 300 == 0) Plugin.L.LogInfo("[verify] waiting for master data"); return; }
                verified = true;
                Plugin.L.LogInfo($"[verify] Guard HP={guard.Hp} | Terra Vigor={terra.Strength} Magic={terra.Magic}");
                try
                {
                    var hit = HitRules.Fix();
                    if (hit != null) Plugin.L.LogInfo(hit);
                }
                catch (Exception e) { Plugin.L.LogWarning($"[hit] {e}"); }
                Plugin.L.LogInfo(EsperHooks.GuardSelfTest(mm));
            }
            catch (Exception e)
            {
                verified = textVerified = true;
                Plugin.L.LogWarning($"[verify] {e}");
            }
        }
    }
}
