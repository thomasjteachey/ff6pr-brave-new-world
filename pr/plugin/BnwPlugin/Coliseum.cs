using System.Collections.Generic;
using System.Linq;
using PrKit.Ff6;   // SnesColiseum: the game's Coliseum lookups answered from a mod's table

namespace BraveNewWorld.PR
{
    // BNW's Coliseum: all 255 wagers changed (opponent, reward, shown or hidden). The porter writes
    // the rows (tools/bnw_plugin_data.coliseum, bnw_data.json "coliseum"; BNW hides a reward at
    // byte 3 = $00, reversible_bets.asm); the kit's SnesColiseum answers the PR's lookups. The
    // opponents' stats are BNW's through the porter (port.sync_monster_copies: the PR's Coliseum
    // copies of monsters). Not shown: BNW's arrow / trade-loop marks beside each bet.
    static class Coliseum
    {
        static Dictionary<int, int[]> table = new Dictionary<int, int[]>();

        public static void Apply(HarmonyLib.Harmony h)
        {
            table = SnesColiseum.Table(EsperHooks.D?.ColiseumRows);
            if (table.Count == 0) { Plugin.L.LogWarning("[coliseum] no table in bnw_data.json: the PR's matches stay"); return; }
            SnesColiseum.Apply(h, new SnesColiseumMod
            {
                Name = "BNW",
                Match = w => table.TryGetValue(w, out var m) ? m : null,
            });
        }

        internal static string Check() =>
            table.Count == 0 ? "[coliseum] self-test: no table" :
            (SnesColiseum.Check(table.Keys.OrderBy(k => k), 255) is string bad ? $"[coliseum] self-test FAILED: {bad}" : "[coliseum] self-test ok");
    }
}
