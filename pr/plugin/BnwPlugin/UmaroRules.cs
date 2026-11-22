using PrKit.Ff6;   // SnesUmaro: Umaro's odds, the shared toss pick and the Mog critical (moved there 2026-11-20)

namespace BraveNewWorld.PR
{
    // Umaro as BNW has him: the kit's SnesUmaro with BNW's odds (C2/5269, bnw_data.json "umaro";
    // readme "Yeti Attack Odds"): Tackle, and Snowstorm / the ally toss when their relic is worn,
    // each 75 of 255, Fight the rest: none 70% Fight, one relic 40%, both 10%. A thrown Mog is a
    // sure critical (vanilla's rule, kept by BNW at C216D6; the PR lost it). Powers are data
    // (port.py ability_overrides: Tackle 255, toss 382). Config: [Battle] TurnRules.
    static class UmaroRules
    {
        internal static bool Enabled = true;

        public static void Apply(HarmonyLib.Harmony h)
        {
            if (!Enabled) return;
            var d = EsperHooks.D;
            SnesUmaro.Apply(h, new SnesUmaroMod
            {
                Name = "BNW",
                Rows = d.UmaroRows.Length == 4 ? d.UmaroRows : null,
                Columns = d.UmaroColumns,
                MogToss = true,
            });
        }

        internal static string Check() => SnesUmaro.Check();

        // probe battles only (a throwaway battle the game then quits; the harness plays real ones)
        internal static string TossSelfTest() =>
            !Enabled ? "[umaro] toss self-test: off" : !FormulaProbe.Running || GameHarness.Active ? null : SnesUmaro.TossSelfTest();
    }
}
