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
    // before BattleProgressATB.InitATBGauge (BC's BcJunctionStart relies on the same order); this postfix overwrites
    // them. The PR's EnemyPreeMptive (no SNES counterpart) keeps the PR's values. Config: [Battle] TurnRules.
    static class FirstStrike
    {
        internal static bool Enabled = true;
        static readonly Random Rng = new Random();
        internal static string Last = "";

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            Hooks.Patch(h, typeof(BattleProgressATB), "InitATBGauge", typeof(FirstStrike), postfix: nameof(Init_Postfix));
        }

        internal static int FrontByte(int speed, int r, int x, Func<int, int> rand) => Math.Min(255, 2 * speed + rand(speed + 30) + r + x) | 1;
        internal static int BackByte(int speed, int r, int x) => Math.Min(255, 2 * speed + r + x) | 1;

        public static void Init_Postfix(BattleProgressATB __instance)
        {
            if (!Enabled || !Hooks.IsA<BattleProgressATB>(__instance)) return;
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
                void Set(BattleUnitData u, int b, string who)
                {
                    float pct = b * 100f / 255f;
                    __instance.ChangeATBGaugeByUnitData(u, pct);
                    if (shown.Count < 10) shown.Add($"{who} {pct:0}%");
                }
                foreach (var u in players)
                {
                    int spd = u.BattleUnitDataInfo?.Parameter?.ConfirmedAgility() ?? 0;
                    int r = 4 * slots[next++ % 10];
                    if (partyFirst) Set(u, 255, "P");
                    else if (back) Set(u, BackByte(spd, r, x), "P");
                    else Set(u, FrontByte(spd, r, x, n => Rng.Next(n)), "P");
                }
                foreach (var u in enemies)
                {
                    int spd = u.BattleUnitDataInfo?.Parameter?.ConfirmedAgility() ?? 0;
                    int r = 4 * slots[next++ % 10];
                    if (partyFirst) Set(u, 1, "E");
                    else if (back) Set(u, 255, "E");
                    else Set(u, FrontByte(spd, r, x, n => Rng.Next(n)), "E");
                }
                Last = $"first strike ({state}, X {x}): {string.Join(", ", shown)}";
                Hooks.NoteOnce("bnw-first-strike-" + state, () => "[atb] " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("FirstStrike", e); }
        }
    }
}
