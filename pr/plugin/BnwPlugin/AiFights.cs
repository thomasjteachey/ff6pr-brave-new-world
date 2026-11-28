using System;
using System.Collections.Generic;
using System.Linq;
using Last.Battle;
using Last.Management;
using UnityEngine;
using Harness = PrKit.Harness;
using Host = PrKit.Ff6.SnesAiHost;

namespace BraveNewWorld.PR
{
    // Harness commands for playing BNW's monster AI fights headless, one after another in one run (the
    // enemy-AI session, 2026-11-20: "make slow tests fast": a run loads a save with a real party once,
    // then tools/ai_fights.py starts each formation with the kit's "battle <party>" and drives it with these):
    //   aikeep on|off   while in battle: Active mode, the party on auto, its HP kept at 9999 (the AI test's way)
    //   ko [PR monster] the first enemy standing (or that monster) to 0 HP through the game's death check
    //                   (SnesAiHost.KnockOut: a BNW boss is held there for its script)
    //   aistat          the battle's phase, the enemies' HP, the host's counts and its last turn/counter/death/entry
    //   endfight        every enemy still standing dies the game's way (a fight that doesn't end by itself)
    //   flag C I [V]    a script variable (DataStorage, e.g. ScenarioFlag2 221): a story fight's preconditions for a test
    static class AiFights
    {
        static bool keep;

        public static void Register()
        {
            Harness.Register("aikeep", a =>
            {
                keep = a.Length > 0 && a[0] == "on";
                return $"aikeep {(keep ? "on" : "off")}";
            }, "aikeep on|off: in battle, Active mode, the party on auto at 9999 HP");
            Harness.Register("ko", a => Ko(a), "ko [PR monster id]: an enemy to 0 HP through the game's death check");
            Harness.Register("aistat", _ => Stat(), "aistat: the battle's phase, enemies, the AI host's counts and last acts");
            Harness.Register("endfight", _ => Host.KillAllForTest("endfight"), "endfight: every enemy left dies the game's way");
            Harness.Register("aipummel", _ => Pummel(), "aipummel: queue Sabin's Pummel in the solo phase of the Vargas story fight");
            Harness.Register("aiact", a => Act(a), "aiact <command> <ability> [character] [PR monster]: queue a party member's act " +
                "(the first one, or that character status id) on an enemy (the first standing, or that monster), e.g. aiact 10 8 = Steal");
            Harness.Register("flag", a => Flag(a), "flag <category> <index> [value]: read (or set) a script variable, e.g. flag ScenarioFlag2 221 1");
        }

        // Queue the same battle action as a selected Pummel. Keep the story script and damage pipeline active.
        static string Pummel()
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var players = battle.GetPlayerUnits().Where(p => p != null).ToArray();
            var vargas = battle.GetEnemyUnits().FirstOrDefault(e => e?.GetMonster()?.Id == 260);
            if (players.Length != 1 || players[0].ownedCharacterData?.characterStatusId != 6 || vargas == null)
                return "error: Vargas is not fighting Sabin alone";
            var sabin = players[0];

            var command = PrKit.Master.Get<Last.Data.Master.Command>(15);
            var ability = PrKit.Master.Get<Last.Data.Master.Ability>(132);
            var abilities = new Il2CppSystem.Collections.Generic.List<Last.Data.Master.Ability>();
            abilities.Add(ability);
            var targets = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<BattleUnitData>(1);
            targets[0] = vargas;
            var act = new BattleActData(sabin, targets, command, abilities, BattleActData.ActType.Normal,
                BattleActData.TargetType.SingleEnemy, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
            battle.BattleActExection.SetBattleActData(act);
            return $"queued Sabin's Pummel against Vargas at {Harness.Hp(vargas)} (Sabin corps index {sabin.corpsIndex})";
        }

        // Queue any command + ability as if the party member had picked it (story fights whose scene a command starts:
        // Locke's Steal on the Merchant / Trooper in South Figaro, QA 2026-11-28)
        static string Act(string[] a)
        {
            if (a.Length < 2 || !int.TryParse(a[0], out int cmdId) || !int.TryParse(a[1], out int abId))
                return "error: aiact <command> <ability> [character] [PR monster]";
            int who = a.Length > 2 && int.TryParse(a[2], out int w) ? w : 0, mon = a.Length > 3 && int.TryParse(a[3], out int m) ? m : 0;
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var user = battle.GetPlayerUnits().FirstOrDefault(p => p != null && p.BattleUnitDataInfo?.Parameter?.CurrentHP > 0 &&
                (who == 0 || p.ownedCharacterData?.characterStatusId == who));
            var target = battle.GetEnemyUnits().FirstOrDefault(e => e != null && e.BattleUnitDataInfo?.Parameter?.CurrentHP > 0 &&
                (mon == 0 || e.GetMonster()?.Id == mon));
            if (user == null || target == null) return $"error: no {(user == null ? $"party member {who}" : $"enemy {mon}")} standing";
            var command = PrKit.Master.Get<Last.Data.Master.Command>(cmdId);
            var ability = PrKit.Master.Get<Last.Data.Master.Ability>(abId);
            if (command == null || ability == null) return $"error: no command {cmdId} / ability {abId}";
            var abilities = new Il2CppSystem.Collections.Generic.List<Last.Data.Master.Ability>();
            abilities.Add(ability);
            var targets = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<BattleUnitData>(1);
            targets[0] = target;
            var act = new BattleActData(user, targets, command, abilities, BattleActData.ActType.Normal,
                BattleActData.TargetType.SingleEnemy, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
            battle.BattleActExection.SetBattleActData(act);
            return $"queued command {cmdId} ability {abId} by character {user.ownedCharacterData?.characterStatusId} on monster {target.GetMonster()?.Id} at {Harness.Hp(target)}";
        }

        // the event scripts' variables (DataStorage: ScenarioFlag1-4, TreasureFlag1-4, ScenarioVariable...), by the
        // category names the scripts use: story fights' preconditions set up for a test (nothing is saved)
        static string Flag(string[] a)
        {
            if (a.Length < 2 || !int.TryParse(a[1], out int index)) return "error: flag <category> <index> [value]";
            var ds = Last.Interpreter.DataStorage.Instance;
            if (ds == null) return "error: no DataStorage";
            if (a.Length > 2 && int.TryParse(a[2], out int value)) ds.Set(a[0], index, value);
            return $"{a[0]} {index} = {ds.Get(a[0], index)}";
        }

        public static void Update()
        {
            if (!keep) return;
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return;
            try
            {
                var cfg = UserDataManager.Instance()?.Config;
                if (cfg != null && cfg.BattleModeIndex != 0) cfg.BattleModeIndex = 0;
                var ui = Last.UI.BattleUIManager.Instance;
                if (ui != null && !ui.IsAuto()) ui.SetIsAuto(true);
                foreach (var pu in battle.GetPlayerUnits())
                {
                    var pr = pu?.BattleUnitDataInfo?.Parameter;
                    if (pr != null && pr.CurrentHP > 0 && pr.CurrentHP < 9999) { if (pr.BaseMaxHp < 9999) pr.BaseMaxHp = 9999; pr.CurrentHP = 9999; }
                }
            }
            catch (Exception e) { PrKit.Hooks.WarnOnce("aifights-keep", e); }
        }

        static string Ko(string[] a)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in battle";
            int id = a.Length > 0 && int.TryParse(a[0], out var v) ? v : 0;
            var target = battle.GetEnemyUnits().FirstOrDefault(u => u?.BattleUnitDataInfo?.Parameter?.CurrentHP > 0 && (id == 0 || u.GetMonster()?.Id == id));
            if (target == null) return "no enemy standing" + (id != 0 ? $" with id {id}" : "");
            return $"ko monster {target.GetMonster()?.Id}: " + Host.KnockOut(target);
        }

        static string Stat()
        {
            var battle = BattlePlugManager.Instance();
            bool on = battle != null && battle.IsBattle();
            string phase;
            try { phase = UnityEngine.Object.FindObjectOfType<BattleController>()?.stateMachine?.Current.ToString() ?? "-"; } catch { phase = "?"; }
            var enemies = on ? string.Join(", ", battle.GetEnemyUnits().Select(u =>
                $"{u?.GetMonster()?.Id}#{u?.UniqueId} {u?.BattleUnitDataInfo?.Parameter?.CurrentHP}/{u?.BattleUnitDataInfo?.Parameter?.ConfirmedMaxHp()}")) : "";
            return $"battle {on} phase {phase}; enemies [{enemies}]; host: {Host.Turns} turns, {Host.Counters} counters, {Host.Finals} deaths, " +
                   $"{Host.Entries} entries, {Host.LinesShown}/{Host.LinesQueued} lines; last turn [{Host.LastTurn}]; last counter [{Host.LastCounter}]; " +
                   $"last death [{Host.LastFinal}]; last entry [{Host.LastEntry}]";
        }
    }
}
