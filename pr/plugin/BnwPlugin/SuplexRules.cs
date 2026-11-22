using PrKit.Ff6;   // SnesSuplex: Suplex misses a monster that can't be suplexed (the SNES's monster flag)

namespace BraveNewWorld.PR
{
    // Suplex as BNW has it on the SNES: it misses a monster whose byte $13 bit 2 is set ("can't be
    // suplexed"); BNW keeps vanilla's check and sets the flag on its own 84 monsters (vanilla 159).
    // The list (PR monster ids, the Coliseum's copies of those monsters too) is bnw_data.json
    // "no_suplex" (tools/bnw_plugin_data.py). The PR's Suplex never misses for that reason
    // (pr/SNES_PR_DISCREPANCIES.md). Config: [Battle] DamageRules.
    static class SuplexRules
    {
        internal static bool Enabled = true;

        public static void Apply(HarmonyLib.Harmony h)
        {
            var list = EsperHooks.D?.NoSuplex;
            if (list == null || list.Length == 0)
            {
                Plugin.L.LogWarning("[suplex] no list in bnw_data.json: Suplex keeps the PR's rule");
                return;
            }
            // BNW's Suplex is function 3 (function group 469), not vanilla's function 2: the basic functions' HitCalc too
            SnesSuplex.Apply(h, new SnesSuplexMod { Name = "BNW", NoSuplex = list, Enabled = () => Enabled, BasicFunctions = true });
            // probe battles only (the harness plays real ones)
            FormulaProbe.AddSelfTest("suplex", () => !FormulaProbe.Running || GameHarness.Active ? null : SnesSuplex.SelfTest());
        }
    }
}
