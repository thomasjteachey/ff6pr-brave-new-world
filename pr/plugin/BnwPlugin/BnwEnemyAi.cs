using System;
using System.IO;
using System.Text.Json;
using HarmonyLib;
using PrKit.Ff6;   // SnesAi: the kit's FF6 AI interpreter; SnesAiHost: the battle it runs in

namespace BraveNewWorld.PR
{
    // BNW's monsters run their own SNES AI scripts the way BNW's engine runs them (the user, 2026-11-19:
    // "Start putting BNW's monsters on the kit's AI interpreter"): bnw_data.json's snes_ai (the porter's
    // tools/port_ai.py snes_ai_data, profile_bnw.snes_ai) holds every monster's script with BNW's rules
    // (SnesAiRules.Bnw: c2.asm's counter rules, MP-low x128, vindictive targeting, bosses that die only by
    // script), and the kit's SnesAiHost runs the ones the porter shimmed. Monsters the porter couldn't
    // shim keep their compiled or the PR's own script.
    static class BnwEnemyAi
    {
        sealed class Bnw : SnesAiMod
        {
            public SnesAiData D;
            public override SnesAiData Data => D;
            public override string Tag => "bnw";
            public override bool Verbose => FormulaProbe.Running || PrKit.TestRun.Running;   // BNW's headless runs log every turn
            // the scripts' timers on BNW's battle clock: held in Wait's menus and while nATB holds time, x2 under Quickfill
            public override float BattleClockDt() => PrKit.Ff6.SnesBattleClock.Dt();
        }

        internal static SnesAiData Data;

        public static void Apply(Harmony h, string dataPath)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(dataPath));
                if (!doc.RootElement.TryGetProperty("snes_ai", out var sai) || sai.ValueKind != JsonValueKind.Object)
                {
                    Plugin.L.LogInfo("[bnw] enemy AI: no snes_ai in bnw_data.json (monsters keep their compiled scripts)");
                    return;
                }
                Data = SnesAiData.Parse(sai);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning($"[bnw] enemy AI: can't read {dataPath}: {e.Message}");
                return;
            }
            SnesAiHost.Patch(h, new Bnw { D = Data });
            Plugin.L.LogInfo($"[bnw] enemy AI: {Data.Plugin.Count} monsters on their BNW scripts, {Data.BossDeath.Count} bosses die by script");
        }

        public static void Update()
        {
            if (Data != null) SnesAiHost.Update();
        }
    }
}
