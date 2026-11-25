using System;
using System.Collections.Generic;
using System.Linq;
using BraveNewWorld.Rules;
using HarmonyLib;
using Last.Battle;
using Last.Management;
using PrKit;
using PrKit.Ff6;
using UnityEngine;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // The check of BNW's battle clock (AtbRules), in the probe battle (FormulaProbe.AddSelfTest "atb", QA's
    // "selftests") or any battle (harness "atbtest": answers when done, up to RecordSeconds + a little):
    //  1. the rules: SnesAtbRules carries BNW's numbers (character Speed 30: 75 x 81 / 16 = 379 a tick; monster
    //     Speed 30: 112 x 50 / 16 = 350), and the game's own ATBCalc per second for a party member and a monster at
    //     Speeds 1-128 is that rate x this frame's Quickfill factor (0 while time is held);
    //  2. the lock: battles in Wait (SystemConfig.ATBBattleType), with the saved config's values left alone (logged);
    //  3. the real clock, recorded frame by frame for RecordSeconds with the party on auto (nobody answers a menu in
    //     a headless run) and everyone's HP topped up below half: every gauge from 0; frames where an action holds
    //     time (SnesActionFreeze) move no gauge and no chant; the gauges' rise over the other frames is BNW's rate x
    //     the frame's Quickfill factor (x2 while no one's turn is up), for the party and the monsters; the AI
    //     scripts' battle timer (SnesAiHost, BNW's fights) rises with the battle clock (SnesBattleClock); each party
    //     member's wait (a chant that starts and ends inside the recording) lasts BNW's wait on the battle clock.
    // Logged, not judged: the PR's own elapsed battle time while held, frames in the game's own pause.
    static class AtbTests
    {
        const float RecordSeconds = 12f;
        static bool recording, probeStarted, probeAnswered;
        static float recordStart;
        static string staticLine, result;
        static bool autoWas;

        struct Frame
        {
            public float Dt;
            public bool Held, Acting, Paused;   // Paused: the game's own pause (Wait's sub-menu, a forced wait)
            public int Factor, Waiting, Chants;
            public float Elapsed, ChantSum;
            public double AiTimer;              // the AI scripts' battle timer (SnesAiHost; -1: no BNW AI fight)
            public Dictionary<IntPtr, (float Gauge, double Rate)> Units;
            public HashSet<IntPtr> ChantActs;    // the acts waiting (chantingInfo) after this frame
        }
        static readonly List<Frame> frames = new List<Frame>();
        static readonly Dictionary<IntPtr, bool> monsters = new Dictionary<IntPtr, bool>();

        public static void Register(Harmony h)
        {
            Hooks.Patch(h, typeof(BattleProgressATBFF6), "Update", typeof(AtbTests), postfix: nameof(Update_Postfix));
            FormulaProbe.AddSelfTest("atb", () => !FormulaProbe.Running || GameHarness.Active ? null : Probe());
            Harness.Register("atbtest", (args, tick) =>
            {
                if (tick == 0) return Begin();   // null: started, answered when the recording ends
                return recording ? null : result ?? "atb: no result (the battle ended?)";
            }, $"atbtest: in a battle, BNW's ATB rules and {RecordSeconds:0} s of its battle clock recorded (party on auto); answers the result");
            Harness.Register("configcheck", ConfigCheck, "configcheck [open]: the Config menu's battle rows (Wait/Pause visible with Battle Options), " +
                             "Battle Speed hidden, and whether the cursor is on a live row ('open': the field menu's Config first)");
        }

        // nATB hides Battle Speed, and Battle Mode too unless Battle Options owns its Wait/Pause row.
        // "ok" = hidden rows absent and inactive, with the cursor on a live row.
        static string ConfigCheck(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "open")
                {
                    Last.UI.KeyInput.MainMenuController menu = null;
                    foreach (var m in Resources.FindObjectsOfTypeAll<Last.UI.KeyInput.MainMenuController>())
                        if (m != null && m.gameObject.scene.IsValid()) { menu = m; break; }   // hidden until opened
                    if (menu == null) return "configcheck: no field menu (on the field first)";
                    menu.ShowBySpecifiedCommandId(Last.Defaine.MenuCommandId.Config, null);
                    return "configcheck: the field menu's Config opened (configcheck again once it's drawn)";
                }
                var pages = Resources.FindObjectsOfTypeAll<Last.UI.KeyInput.ConfigActualDetailsControllerBase>();
                var lines = new List<string>();
                bool ok = true, any = false;
                foreach (var m in pages)
                {
                    if (m == null || !m.gameObject.scene.IsValid() || !m.gameObject.activeInHierarchy) continue;
                    var list = m.commandList;
                    if (list == null) continue;
                    any = true;
                    var types = new List<int>();
                    for (int i = 0; i < list.Count; i++) types.Add(TypeOf(list[i]));
                    int sel = TypeOf(m.SelectedCommand);
                    bool selLive = m.SelectedCommand != null && m.SelectedCommand.gameObject.activeInHierarchy;
                    bool hideMode = AtbRules.Natb && !BattleSettingsLock.BattleOptionsPresent();
                    bool hiddenLeft = AtbRules.Natb && ((hideMode && types.Contains(1)) || types.Contains(2));
                    // the hidden rows' objects (out of the list, under the same parent): inactive
                    int liveHidden = 0;
                    var parent = list.Count > 0 ? list[0]?.transform.parent : null;
                    if (parent != null && AtbRules.Natb)
                        foreach (var r in parent.GetComponentsInChildren<Last.UI.KeyInput.ConfigCommandController>(true))
                            if (r != null && ((hideMode && TypeOf(r) == 1) || TypeOf(r) == 2) && r.gameObject.activeSelf) liveHidden++;
                    bool pageOk = !hiddenLeft && liveHidden == 0 && (m.SelectedCommand == null || (selLive && (!hideMode || sel != 1) && sel != 2));
                    ok &= pageOk;
                    lines.Add($"page '{m.name}': rows [{string.Join(" ", types)}], selected type {sel} (live {selLive}, index {m.SelectedContentIndex}), " +
                              $"hidden rows still active {liveHidden}{(pageOk ? "" : " WRONG")}");
                }
                if (!any) return $"configcheck: no Config page open (rows hidden so far {PrKit.ConfigRows.RowsHidden})";
                return $"configcheck {(ok ? "ok" : "FAILED")}: {string.Join("; ", lines)} (rows hidden so far {PrKit.ConfigRows.RowsHidden})";
            }
            catch (Exception e) { return "configcheck: " + e.GetType().Name + ": " + e.Message; }
        }

        static int TypeOf(Last.UI.KeyInput.ConfigCommandController r)
        {
            try { var d = r == null ? null : r.commandsData; return d == null ? 0 : (int)d.configCommandType; }
            catch (Exception) { return 0; }
        }

        static BattleProgressATBFF6 Progress() => BattlePlugManager.Instance()?.BattleProgress?.TryCast<BattleProgressATBFF6>();

        static IEnumerable<BattleUnitData> All(BattlePlugManager battle)
        {
            foreach (var u in battle.GetPlayerUnits()) if (u != null) yield return u;
            foreach (var u in battle.GetEnemyUnits()) if (u != null) yield return u;
        }

        // BNW's rate for a unit as it stands (its Speed, Haste, Slow), per second of battle clock
        static double BaseRate(BattleUnitData u)
        {
            var p = u.BattleUnitDataInfo?.Parameter;
            if (p == null) return 0;
            bool haste = false, slow = false;
            var list = p.CurrentConditionList;
            if (list != null)
                foreach (var c in list)
                {
                    if (c == null) continue;
                    if (c.ConditionType == SnesAtbRules.HasteType) haste = true;
                    else if (c.ConditionType == SnesAtbRules.SlowType) slow = true;
                }
            return SnesAtbRules.Rate(p.ConfirmedAgility(), u.TryCast<BattleEnemyData>() != null, SnesAtb.SnesBattleSpeed(), haste, slow);
        }

        // the probe's poll (twice a second): start once the battle has both sides, answer once done
        static string Probe()
        {
            if (probeAnswered) return null;
            if (!probeStarted)
            {
                var battle = BattlePlugManager.Instance();
                if (battle == null || !battle.IsBattle() || Progress() == null ||
                    battle.GetPlayerUnits()?.Length == 0 || battle.GetEnemyUnits()?.Length == 0) return null;
                probeStarted = true;
                string err = Begin();
                if (err != null) { probeAnswered = true; return "[atb] clock self-test FAILED: " + err; }
                return null;
            }
            if (recording || result == null) return null;
            probeAnswered = true;
            return result;
        }

        // null: started; else why it can't
        static string Begin()
        {
            if (recording) return "atb: already recording";
            var battle = BattlePlugManager.Instance();
            var prog = Progress();
            if (battle == null || !battle.IsBattle() || prog == null) return "atb: not in a battle";
            if (!All(battle).Any(u => u.TryCast<BattleEnemyData>() != null)) return "atb: no monster in this battle";
            result = null;
            sweepPending = false; sweepOk = false; sweepLine = null;
            staticLine = StaticChecks(battle, prog);
            Plugin.L.LogInfo(staticLine);
            var ui = Last.UI.BattleUIManager.Instance;
            autoWas = ui != null && ui.IsAuto();
            if (ui != null) ui.SetIsAuto(true);   // nobody answers a menu in a headless run: Wait would hold it open
            frames.Clear();
            monsters.Clear();
            foreach (var u in All(battle))
            {
                prog.ChangeATBGaugeByUnitData(u, 0);
                monsters[u.Pointer] = u.TryCast<BattleEnemyData>() != null;
            }
            recordStart = Time.time;
            recording = true;
            Plugin.L.LogInfo($"[atb] recording the battle clock for {RecordSeconds:0} s: {monsters.Count} units, gauges from 0, auto on");
            return null;
        }

        static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol * Math.Max(1e-6, Math.Abs(b));

        static bool sweepPending, sweepOk;
        static string sweepLine;

        // ATBCalc per second (every postfix: SnesAtb's rate, Quickfill, the freeze) for a party member and a monster at
        // Speeds 1-128 against BNW's rate x this frame's factor; not ok if any differs or every rate is 0
        static string SweepRates(BattlePlugManager battle, BattleProgressATBFF6 prog, out bool allOk)
        {
            var fails = new List<string>();
            var notes = new List<string>();
            bool anyRate = false;
            var calc = AccessTools.Method(typeof(BattleProgressATBFF6), "ATBCalc");
            var hero = battle.GetPlayerUnits().FirstOrDefault(u => u != null && u.BattleUnitDataInfo?.Parameter?.CurrentHP > 0);
            var foe = battle.GetEnemyUnits().FirstOrDefault(u => u != null && u.BattleUnitDataInfo?.Parameter?.CurrentHP > 0);
            var hp = hero?.BattleUnitDataInfo?.Parameter;
            var fp = foe?.BattleUnitDataInfo?.Parameter;
            if (calc == null || hp == null || fp == null) fails.Add($"no ATBCalc ({calc != null}) or no party member / monster standing");
            else
            {
                int factor = SnesActionFreeze.Held() ? 0 : SnesQuickfill.Factor();
                float dt = Mathf.Max(1e-6f, Time.deltaTime);
                int heroAgi = hp.BaseAgility, foeAgi = fp.BaseAgility;
                foreach (var (unit, p, monster) in new[] { ((BattleUnitData)hero, hp, false), (foe, fp, true) })
                {
                    var got = new List<string>();
                    bool ok = true;
                    foreach (int s in new[] { 1, 10, 30, 50, 100, 128 })
                    {
                        p.BaseAgility = s;
                        double want = BaseRate(unit) * factor;
                        double r = (float)calc.Invoke(prog, new object[] { unit }) / dt;
                        got.Add($"{p.ConfirmedAgility()}:{r:0.000}/{want:0.000}");
                        ok &= Near(r, want, 0.002) || (want == 0 && Math.Abs(r) < 1e-6);
                        anyRate |= r > 0;
                    }
                    (ok ? notes : fails).Add($"{(monster ? "monster" : "party")} ATBCalc/s by Speed (game/BNW x{factor}) {string.Join(" ", got)}");
                }
                hp.BaseAgility = heroAgi;
                fp.BaseAgility = foeAgi;
                int sp = hp.ConfirmedAgility();
                notes.Add($"BNW waits at Speed {sp}: Fight {BnwAtb.Seconds(16, sp, false, false):0.00} s, Magic {BnwAtb.Seconds(32, sp, false, false):0.00}, " +
                          $"Summon {BnwAtb.Seconds(64, sp, false, false):0.00}, Jump's air {BnwAtb.Seconds(112, sp, false, false):0.00}");
            }
            if (!anyRate) fails.Add("every ATBCalc rate is 0");
            allOk = fails.Count == 0;
            return $"ATBCalc sweep {(allOk ? "ok" : "FAILED: " + string.Join("; ", fails))} ({string.Join("; ", notes)})";
        }

        // 1 and 2: the rules, ATBCalc at known Speeds, the settings lock
        static string StaticChecks(BattlePlugManager battle, BattleProgressATBFF6 prog)
        {
            var fails = new List<string>();
            var notes = new List<string>();
            // the numbers BNW gave the kit
            int c30 = SnesAtbRules.AtbAdd(30, false, 0, false, false), m30 = SnesAtbRules.AtbAdd(30, true, 0, false, false);
            if (c30 != 379 || m30 != 350) fails.Add($"rules: character Speed 30 adds {c30} (BNW 379), monster {m30} (BNW 350)");
            notes.Add($"adds: character Spd 30 {c30}, monster Spd 30 {m30}");
            // the game's own ATBCalc at a sweep of Speeds: on a frame where time isn't held (QA 2026-11-21: at the battle's
            // start an act stages, the freeze answers 0 and "x0" matched itself); deferred to the first free frame
            if (SnesActionFreeze.Held()) { sweepPending = true; notes.Add("ATBCalc sweep: time held now, run on the first free battle frame"); }
            else
            {
                sweepLine = SweepRates(battle, prog, out sweepOk);
                (sweepOk ? notes : fails).Add(sweepLine);
            }
            // the lock: Wait, x1 at every setting, the saved config untouched
            var sys = SystemConfig.Instance();
            var cfg = UserDataManager.Instance()?.Config;
            if (AtbRules.Natb)
            {
                if (sys == null || sys.ATBBattleType != ATBBattleType.Wait) fails.Add($"battle mode {sys?.ATBBattleType} (BNW: Wait)");
                // the battle speed isn't pinned (2026-11-21: the kit's detour on its reader was unsafe): the hidden row's
                // saved value stays; BNW's fill ignores it and its waits are timed through it (WaitGauge)
                float speed = BattleUtility.GetBattleSpeedByConfigValue(cfg?.BattleSpeed ?? 2);
                notes.Add($"battle mode {sys?.ATBBattleType}, battle speed x{speed:0.##}, " +
                          $"saved config mode {cfg?.BattleModeIndex} speed {cfg?.BattleSpeed} (left as they were)");
            }
            return $"[atb] rules: {(fails.Count == 0 ? "ok" : "FAILED: " + string.Join("; ", fails))} ({string.Join("; ", notes)})";
        }

        // once a battle frame, after the game's update (and every hook in it) ran
        public static void Update_Postfix()
        {
            if (!recording) return;
            try
            {
                var battle = BattlePlugManager.Instance();
                var prog = Progress();
                if (battle == null || prog == null || !battle.IsBattle()) { Finish(true); return; }
                var f = new Frame
                {
                    Dt = Time.deltaTime,
                    Held = SnesActionFreeze.Held(),
                    Acting = battle.BattleActExection?.GetStatingAct() ?? false,
                    Paused = SnesBattleClock.Paused(),
                    Factor = SnesQuickfill.Factor(),
                    Waiting = SnesQuickfill.Waiting(),
                    AiTimer = SnesAiHost.BattleTimerSeconds,
                    Units = new Dictionary<IntPtr, (float, double)>(),
                    ChantActs = new HashSet<IntPtr>(),
                };
                try { f.Elapsed = battle.elapsedTime; } catch (Exception) { f.Elapsed = -1; }
                var chants = prog.chantingInfo;
                if (chants != null)
                    for (int i = 0; i < chants.Count; i++)
                    {
                        f.ChantSum += chants[i].time;
                        f.Chants++;
                        var act = chants[i].actData;
                        if (act != null) f.ChantActs.Add(act.Pointer);
                    }
                foreach (var u in All(battle))
                {
                    if (!monsters.ContainsKey(u.Pointer)) continue;
                    f.Units[u.Pointer] = (prog.GetAtbGaugeByUnitData(u), BaseRate(u));
                    var p = u.BattleUnitDataInfo?.Parameter;
                    // keep the battle going: max HP 9999 first, so one hit can't end it between two frames (QA bnw-atb5: monster 24's 50 HP)
                    if (p != null && p.CurrentHP > 0 && p.BaseMaxHp < 9999) p.BaseMaxHp = 9999;
                    if (p != null && p.CurrentHP > 0 && p.CurrentHP < p.ConfirmedMaxHp() / 2) p.CurrentHP = p.ConfirmedMaxHp();
                }
                frames.Add(f);
                if (sweepPending && !f.Held)
                {
                    sweepPending = false;
                    sweepLine = SweepRates(battle, prog, out sweepOk);
                    Plugin.L.LogInfo("[atb] " + sweepLine);
                }
                if (Time.time - recordStart >= RecordSeconds) Finish(false);
            }
            catch (Exception e) { Hooks.WarnOnce("atb.test", e); Finish(true); }
        }

        static void Finish(bool cut)
        {
            recording = false;
            try
            {
                var ui = Last.UI.BattleUIManager.Instance;
                if (ui != null) ui.SetIsAuto(autoWas);
            }
            catch (Exception) { }
            try { result = staticLine + "\n" + Analyse(cut); }
            catch (Exception e) { result = staticLine + "\n[atb] clock self-test FAILED: " + e.GetType().Name + ": " + e.Message; }
            Plugin.L.LogInfo(result.Substring(result.IndexOf('\n') + 1));
        }

        // 3: the recording, judged
        static string Analyse(bool cut)
        {
            var fails = new List<string>();
            var notes = new List<string>();
            int heldFrames = 0, heldRises = 0, heldChantMoves = 0, waitFrames = 0, pausedFrames = 0;
            float heldTime = 0, elapsedInHeld = 0, quickTime = 0;
            // per side and factor: the gauges' rise against BNW's rate x the frame's factor
            var rise = new Dictionary<(bool Monster, int Factor), (double Got, double Want)>();
            // the AI scripts' battle timer against the battle clock (0 held or paused, else dt x factor); SnesAiHost
            // ticks from BnwRunner's frame, so a held stretch may see one frame's tick at its edge
            double aiGot = 0, aiWant = 0, aiHeld = 0;
            int aiResets = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                var a = frames[i - 1];
                var b = frames[i];
                if (b.Paused) pausedFrames++;
                if (b.Waiting > 0) waitFrames++;
                if (b.Held)
                {
                    heldFrames++;
                    heldTime += b.Dt;
                    if (a.Elapsed >= 0 && b.Elapsed >= 0) elapsedInHeld += b.Elapsed - a.Elapsed;
                    if (b.Chants == a.Chants && Math.Abs(b.ChantSum - a.ChantSum) > 1e-5) heldChantMoves++;
                }
                else if (b.Factor == 2 && !b.Paused) quickTime += b.Dt;
                if (a.AiTimer >= 0 && b.AiTimer >= 0)
                {
                    double d = b.AiTimer - a.AiTimer;
                    if (d < 0) aiResets++;   // a script's FB 04
                    else
                    {
                        aiGot += d;
                        aiWant += b.Held || b.Paused ? 0 : b.Dt * b.Factor;
                        if (b.Held) aiHeld += d;
                    }
                }
                foreach (var kv in b.Units)
                {
                    if (!a.Units.TryGetValue(kv.Key, out var was)) continue;
                    float d = kv.Value.Gauge - was.Gauge;
                    if (b.Held) { if (d > 1e-4f) heldRises++; continue; }
                    // full, reset (its turn), not filling (Stop, a turn up, the game's pause): left out
                    if (was.Gauge >= 99.9f || kv.Value.Gauge >= 99.9f || d <= 0 || kv.Value.Rate <= 0) continue;
                    var key = (monsters[kv.Key], b.Factor);
                    rise.TryGetValue(key, out var acc);
                    rise[key] = (acc.Got + d, acc.Want + kv.Value.Rate * b.Dt * b.Factor);
                }
            }
            float total = frames.Count > 1 ? frames.Skip(1).Sum(x => x.Dt) : 0;
            notes.Add($"{frames.Count} frames, {total:0.0} s{(cut ? " (the battle ended first)" : "")}; held {heldFrames} frames ({heldTime:0.00} s), " +
                      $"Quickfill x2 {quickTime:0.00} s, a turn up in {waitFrames} frames, the game's own pause in {pausedFrames}");
            if (heldFrames < 10) fails.Add($"no action held time ({heldFrames} held frames in {total:0.0} s)");
            if (sweepPending) fails.Add("ATBCalc sweep never ran (time held all along)");
            else if (sweepLine != null && !sweepOk) fails.Add(sweepLine);
            if (heldRises > 0) fails.Add($"{heldRises} gauge rises while time was held");
            if (heldChantMoves > 0) fails.Add($"{heldChantMoves} chant countdowns moved while time was held");
            notes.Add($"the PR's elapsed battle time ran {elapsedInHeld:0.00} s of the {heldTime:0.00} s held");
            notes.Add($"kit counters: held gauges {SnesActionFreeze.GaugesHeld}, chants {SnesActionFreeze.ChantsHeld}, status updates " +
                      $"{SnesActionFreeze.ConditionsHeld}, holds {SnesActionFreeze.Hold.Holds} (last {SnesActionFreeze.Hold.LastHeld:0.00} s); doubled gauges " +
                      $"{SnesQuickfill.GaugesDoubled}, chants {SnesQuickfill.ChantsDoubled}, status updates {SnesQuickfill.ConditionsDoubled}; " +
                      $"Wait forced {BattleSettingsLock.Forced}, Config rows hidden {ConfigRows.RowsHidden}");
            foreach (var kv in rise.OrderBy(k => k.Key.Monster).ThenBy(k => k.Key.Factor))
            {
                double ratio = kv.Value.Want > 0 ? kv.Value.Got / kv.Value.Want : 0;
                string what = $"{(kv.Key.Monster ? "monsters" : "party")} x{kv.Key.Factor}: rose {kv.Value.Got:0.0} of BNW's {kv.Value.Want:0.0} ({ratio:0.000})";
                bool enough = kv.Value.Want >= 10;   // at least a tenth of a gauge to judge
                if (enough && !Near(ratio, 1, 0.05)) fails.Add(what); else notes.Add(what + (enough ? "" : " (too little to judge)"));
            }
            if (AtbRules.Quickfill && (!rise.TryGetValue((true, 2), out var q) || q.Want < 10))
                fails.Add("no Quickfill (x2) stretch long enough to judge the monsters' fill");
            // the party's waits: each chant that started and ended inside the recording, timed on the battle clock
            // (0 held or paused, else dt x factor) against BNW's wait (WaitGauge.AbsoluteWait); a frame's edge either way
            var seen = new Dictionary<IntPtr, int>();
            var waits = new List<string>();
            int waitsOk = 0, waitsBad = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                foreach (var act in frames[i].ChantActs)
                    if (!frames[i - 1].ChantActs.Contains(act) && !seen.ContainsKey(act)) seen[act] = i;
                foreach (var act in frames[i - 1].ChantActs)
                {
                    if (frames[i].ChantActs.Contains(act) || !seen.TryGetValue(act, out int from) || from < 1) continue;
                    if (!WaitGauge.Timed.TryGetValue(act, out var want) || want.Seconds < 0.2f) continue;
                    double clock = 0;
                    for (int k = from + 1; k <= i; k++)
                        if (!frames[k].Held && !frames[k].Paused) clock += frames[k].Dt * frames[k].Factor;
                    // each end of the window may count or miss one frame's countdown (the chant starts / ends mid-frame):
                    // + both edge frames' battle clock (at x2 and ~35 fps, ~0.057 s each)
                    double edges = frames[from].Dt * frames[from].Factor + frames[i].Dt * frames[i].Factor;
                    bool ok = Math.Abs(clock - want.Seconds) <= Math.Max(0.1, 0.1 * want.Seconds) + edges;
                    if (ok) waitsOk++; else waitsBad++;
                    if (waits.Count < 8) waits.Add($"command {want.Command} wait {want.Wait:X2}: {clock:0.00} s of battle clock (BNW {want.Seconds:0.00}){(ok ? "" : " WRONG")}");
                }
            }
            if (AtbRules.SpeedBalance && WaitGauge.WaitTimes)
            {
                string w = $"waits: {waitsOk} as BNW's, {waitsBad} not ({string.Join("; ", waits)})";
                if (waitsBad > 0 || waitsOk == 0) fails.Add(w); else notes.Add(w);
            }
            if (aiWant >= 1)
            {
                string ai = $"AI battle timer rose {aiGot:0.00} s of the battle clock's {aiWant:0.00} ({aiGot / aiWant:0.000}; {aiHeld:0.00} s inside held frames, {aiResets} resets)";
                if (Near(aiGot / aiWant, 1, 0.05)) notes.Add(ai); else fails.Add(ai);
            }
            else notes.Add("no BNW AI fight on the host (its battle timer not checked)");
            return $"[atb] clock self-test {(fails.Count == 0 ? "ok" : "FAILED: " + string.Join("; ", fails))} ({string.Join("; ", notes)})";
        }
    }
}
