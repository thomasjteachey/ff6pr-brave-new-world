using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Last.Battle;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW's starting ATB ("First Strike"; source/asm/banks/c2.asm 1615-1700 InitializeATBTimers, readme 702-724). Per
    // unit a byte b of 255 (percent = b / 255 x 100), X = 10 - units in battle, R = 4 x a distinct random 0..9 per unit:
    //  * front attack and pincer (BNW: "Pincer behaves like Front"): b = min(255, 2 x Speed + rand(0..Speed+29) + R + X) | 1
    //  * back attack: characters min(255, 2 x Speed + R + X) | 1 (no random part), monsters full
    //  * preemptive and side attack: characters full, monsters 1
    // The PR starts from game_constant_int's ACCUMULATION_ATB_* ranges (random 50-80% and the like, no Speed term), set
    // in BattleProgressATB.SetPreeMptive; this postfix on it overwrites them (before 0.26.19 it hooked InitATBGauge,
    // which FF6 never calls: the rules never applied). The PR's EnemyPreeMptive (no SNES counterpart)
    // keeps the PR's values. Config: [Battle] TurnRules.
    static class FirstStrike
    {
        internal static bool Enabled = true;
        static readonly Random Rng = new Random();
        internal static string Last = "";

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            // FF6's start of battle (probe runs, 2026-11-28): BattleProgressATBFF6.Init -> BattleProgressATB.Init ->
            // AddGaugeStatus (every gauge 0) -> SetPreeMptive (BattlePopPlug.Calculation rolls the encounter type, then
            // the PR's starting gauges); after Init, in the same frame, SetPreeMptive runs again and sets the gauges of
            // its ranges once more (a back attack's monsters 100). BattleProgressATB.InitATBGauge is never called. So the
            // hook is SetPreeMptive's postfix, every call in the frame Init ran in (none later in the battle).
            Hooks.Patch(h, typeof(BattleProgressATB), "Init", typeof(FirstStrike), prefix: nameof(Init_Prefix));
            Hooks.Patch(h, typeof(BattleProgressATB), "SetPreeMptive", typeof(FirstStrike), postfix: nameof(Opening_Postfix));
            Hooks.Patch(h, typeof(BattlePopPlug), "Calculation", typeof(FirstStrike), postfix: nameof(Calc_Postfix));
            PrKit.Harness.Register("firststrike", a =>
            {
                if (a.Length >= 2 && a[0] == "force")
                {
                    if (a[1] == "off") { forced = null; return "firststrike: encounter types as rolled"; }
                    if (!Enum.TryParse<BattlePopPlug.PreeMptiveState>(a[1], out var st)) return "firststrike: no state " + a[1];
                    forced = st;
                    return "firststrike: next battles open as " + st;
                }
                return Dump();
            }, "firststrike [force <Normal|BackAttack|PreeMptive|SideAttack|EnemySideAttack|off>]: the last starting-ATB line and the gauges now; force: the encounter type of the battles after (test runs)");
        }

        // the gauges now, "P<Speed>:<gauge>" per party member then "E..." per monster (the harness's firststrike)
        static string Gauges()
        {
            var bm = BattlePlugManager.Instance();
            var g = bm?.BattleProgress?.TryCast<BattleProgressATB>()?.gaugeStatusDictionary;
            if (bm == null || g == null) return "gauges -";
            var parts = new List<string>();
            var pl = bm.GetPlayerUnits();
            if (pl != null) foreach (var u in pl) if (u != null) parts.Add(g.ContainsKey(u) ? $"P{(int)u.BattleUnitDataInfo.Parameter.ConfirmedAgility()}:{g[u]:0}" : "P?");
            var en = bm.GetEnemyUnits();
            if (en != null) foreach (var u in en) if (u != null) parts.Add(g.ContainsKey(u) ? $"E{(int)u.BattleUnitDataInfo.Parameter.ConfirmedAgility()}:{g[u]:0}" : "E?");
            return "gauges " + string.Join(" ", parts);
        }
        // test runs: the harness's "firststrike force <state>" decides the next battles' encounter type
        static BattlePopPlug.PreeMptiveState? forced;
        public static void Calc_Postfix(BattlePopPlug __instance, ref BattlePopPlug.PreeMptiveState __result)
        {
            if (!forced.HasValue) return;
            __result = forced.Value;
            __instance.ChangeState(forced.Value);   // Calculation stores the state itself: GetResult reads that one
        }
        static string Dump() => $"firststrike: last '{Last}' now state {BattlePlugManager.Instance()?.BattlePopPlug?.GetResult()} {Gauges()}";

        internal static int FrontByte(int speed, int r, int x, Func<int, int> rand) => Math.Min(255, 2 * speed + rand(speed + 30) + r + x) | 1;
        internal static int BackByte(int speed, int r, int x) => Math.Min(255, 2 * speed + r + x) | 1;

        static int initFrame = -10;
        public static void Init_Prefix() => initFrame = UnityEngine.Time.frameCount;

        public static void Opening_Postfix(BattleProgressATB __instance)
        {
            if (!Enabled || !Hooks.IsA<BattleProgressATB>(__instance)) return;
            if (UnityEngine.Time.frameCount - initFrame > 1) return;   // the battle's opening only
            try
            {
                var battle = BattlePlugManager.Instance();
                if (battle == null) return;
                var state = battle.BattlePopPlug?.GetResult() ?? BattlePopPlug.PreeMptiveState.Normal;
                bool front = state == BattlePopPlug.PreeMptiveState.Normal || state == BattlePopPlug.PreeMptiveState.EnemySideAttack;
                bool back = state == BattlePopPlug.PreeMptiveState.BackAttack;
                bool partyFirst = state == BattlePopPlug.PreeMptiveState.PreeMptive || state == BattlePopPlug.PreeMptiveState.SideAttack;
                if (!front && !back && !partyFirst) return;
                var players = new List<BattleUnitData>();
                var enemies = new List<BattleUnitData>();
                var pl = battle.GetPlayerUnits();
                if (pl != null) foreach (var u in pl) if (u != null) players.Add(u);
                var en = battle.GetEnemyUnits();
                if (en != null) foreach (var u in en) if (u != null) enemies.Add(u);
                int x = Math.Max(0, 10 - players.Count - enemies.Count);
                var slots = Enumerable.Range(0, 10).OrderBy(_ => Rng.Next()).ToList();
                int next = 0;
                var shown = new List<string>();
                void Set(BattleUnitData u, int b, string who, int spd)
                {
                    float pct = b * 100f / 255f;
                    __instance.ChangeATBGaugeByUnitData(u, pct);
                    if (shown.Count < 10) shown.Add($"{who}{spd} {pct:0}%");
                }
                foreach (var u in players)
                {
                    int spd = u.BattleUnitDataInfo?.Parameter?.ConfirmedAgility() ?? 0;
                    int r = 4 * slots[next++ % 10];
                    if (partyFirst) Set(u, 255, "P", spd);
                    else if (back) Set(u, BackByte(spd, r, x), "P", spd);
                    else Set(u, FrontByte(spd, r, x, n => Rng.Next(n)), "P", spd);
                }
                foreach (var u in enemies)
                {
                    int spd = u.BattleUnitDataInfo?.Parameter?.ConfirmedAgility() ?? 0;
                    int r = 4 * slots[next++ % 10];
                    if (partyFirst) Set(u, 1, "E", spd);
                    else if (back) Set(u, 255, "E", spd);
                    else Set(u, FrontByte(spd, r, x, n => Rng.Next(n)), "E", spd);
                }
                Last = $"first strike ({state}, X {x}): {string.Join(", ", shown)}";
                Hooks.NoteOnce("bnw-first-strike-" + state, () => "[atb] " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("FirstStrike", e); }
        }
    }
}
