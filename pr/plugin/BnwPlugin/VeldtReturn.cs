using System;
using System.Linq;
using HarmonyLib;
using Last.Battle;
using Last.Interpreter;
using Hooks = PrKit.Hooks;
using SysCall = Last.Interpreter.Instructions.SystemCall.Current;

namespace BraveNewWorld.PR
{
    // Gau's return after a Leap (the user, 2026-11-27: "if i autobattle on the last turn of a fight, gau doesn't come
    // back out"). The PR's Veldt battle script (sc_b_0239, script 8010) asks SetGauMode at the start ("ガウバトルモード
    // 設定": script-local 2 = 2 when Gau comes back after this fight); after the win it turns the enemy side into a
    // stand-in Gau (monsters 549/550, AI sc_ai_549_Gau) and resumes the battle. The stand-in rejoins on its turn
    // (mode 1 -> Event00 -> JoinGauInBattle) but ran off when hit (mode 3 -> Event02), and auto-battle hits it. The fix
    // is data: port_bnw_party_events drops the stand-in's "attacked" jumps, as the SNES gives no window to hit him.
    // Here: log lines for both ends, and the harness "gaureturn" that queues the return for the next battle.
    static class VeldtReturn
    {
        const int GauModeLocal = 2;          // sc_b_0239 ScriptLocal[2] "BattleGauMode"
        const int ReturnAfterWin = 2;        // SetGauMode: Gau comes back after this battle is won
        internal static string Last = "";    // tests
        static bool armed;                   // harness: the next SetGauMode queues the return

        public static void Apply(Harmony h)
        {
            Hooks.Patch(h, typeof(SysCall), "SetGauMode", typeof(VeldtReturn), postfix: nameof(SetGauMode_Postfix));
            Hooks.Patch(h, typeof(SysCall), "JoinGauInBattle", typeof(VeldtReturn), prefix: nameof(JoinGauInBattle_Prefix));
            PrKit.Harness.Register("gaureturn", a => { armed = true; Last = ""; return "gaureturn: the next battle's SetGauMode queues Gau's return"; },
                "gaureturn: the next Veldt-script battle (sc_b_0239, e.g. encount 2002) brings Gau back after the win (script-local 2 = 2); " +
                "its log says whether he rejoined ([veldt] ... rejoins)");
        }

        static int Mode() => DataStorage.Instance?.Get(DataStorage.Category.kScriptLocalVariable, GauModeLocal) ?? -1;

        public static void SetGauMode_Postfix()
        {
            try
            {
                if (armed)
                {
                    armed = false;
                    DataStorage.Instance?.Set(DataStorage.Category.kScriptLocalVariable, GauModeLocal, ReturnAfterWin);
                    Plugin.L.LogInfo("[veldt] harness: Gau's return queued for this battle");
                }
                if (Mode() == ReturnAfterWin)
                {
                    Last = "queued";
                    Plugin.L.LogInfo("[veldt] Gau comes back after this battle (BattleGauMode 2)");
                }
            }
            catch (Exception e) { Hooks.WarnOnce("VeldtReturn.SetGauMode", e); }
        }

        public static void JoinGauInBattle_Prefix()
        {
            try
            {
                string standIn = "";
                var battle = BattlePlugManager.Instance();
                if (battle != null && battle.IsBattle())
                    standIn = string.Join(", ", battle.GetEnemyUnits().Where(u => u != null).Select(u =>
                        $"monster {u.GetMonster()?.Id} HP {u.BattleUnitDataInfo?.Parameter?.CurrentHP}"));
                bool auto = global::Last.UI.BattleUIManager.Instance?.IsAuto() ?? false;
                Last = $"rejoined (auto-battle {(auto ? "on" : "off")}; stand-in {standIn})";
                Plugin.L.LogInfo("[veldt] Gau rejoins after the battle: " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("VeldtReturn.JoinGau", e); }
        }
    }
}
