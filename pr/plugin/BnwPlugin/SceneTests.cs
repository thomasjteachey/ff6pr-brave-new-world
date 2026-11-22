using System;
using Last.Management;

namespace BraveNewWorld.PR
{
    // BNW's added scenes (tools/port_bnw_added_scenes.py, bnw-scenes 2026-11-20): harness commands their
    // headless tests need, so a test pays a fee or checks a gift at once instead of playing to it.
    //   gil [amount]   the party's gil (set it: the game's own OwnedItemClient AddGil / RemoveGil)
    // Test runs only (the harness); they never save.
    static class SceneTests
    {
        public static void Register()
        {
            PrKit.Harness.Register("gil", Gil, "gil [amount]: the party's gil (set it to amount)");
        }

        static string Gil(string[] a)
        {
            var udm = UserDataManager.Instance();
            if (udm == null) return "error: no user data (load or start a game first)";
            if (a.Length > 0)
            {
                if (!int.TryParse(a[0], out int want) || want < 0) return "error: gil [amount]";
                try
                {
                    int now = udm.OwendGil;
                    var items = new OwnedItemClient();
                    if (want > now) items.AddGil(want - now);
                    else if (want < now) items.RemoveGil(now - want);
                }
                catch (Exception e) { return $"error: {e.GetType().Name}: {e.Message}"; }
            }
            return $"gil {udm.OwendGil}";
        }
    }
}
