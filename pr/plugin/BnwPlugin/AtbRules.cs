using BraveNewWorld.Rules;
using HarmonyLib;
using PrKit.Ff6;

namespace BraveNewWorld.PR
{
    // BNW's battle clock on the kit's SNES pieces (plan: docs/bnw_audit_2026-11-20/plan_atb.md 2-4):
    //  * Speed Balance (c2.asm C2/09D2 ATBMultipliers; readme "Speed Balance", "Slow & Haste"): every 2 frames
    //    the 16-bit gauge gains k x (Speed + 51) / 16 for characters, k 75 / Haste 90 / Slow 60 (Slow wins), and
    //    (k + k/2) x (Speed + 20) / 16 for monsters (112 / 135 / 90), with no battle-speed term (BNW took the
    //    Battle Speed setting out: c3.asm 143-151, 285-287, 343). The kit's SnesAtb answers that absolute rate
    //    from BattleProgressATBFF6.ATBCalc (BattleFormulas used to scale the PR's own rate by BNW / vanilla: ~19%
    //    slow for both sides, as the PR's rate isn't vanilla's shape and carries vanilla's battle-speed term).
    //    The fill only here; with it on, WaitGauge times the waits between a command and its act the same way
    //    (BNW's table on BNW's rate, in SNES seconds: Rules/BnwAtb.cs) instead of scaling the PR's chant.
    //  * Quickfill (ef.asm EF/FBC8 from C2/112C BattleFrameLoop; readme "Quickfill"): the battle clock runs x2
    //    while no character holds a battle-menu slot: the kit's SnesQuickfill (gauges, chants, status timers).
    //  * nATB (c2.asm C2/1124 ORA $3A8F, C2/247A STZ $3A8F; c3.asm MagicFunction1/2; readme "nATB System"):
    //    time stops while an action's animation plays (the kit's SnesActionFreeze) and in the battle menus
    //    (Wait, always: BNW has no Active/Wait or Battle Speed rows), so the kit's BattleSettingsLock keeps battles
    //    in Wait and hides Battle Speed (and Battle Mode unless Battle Options offers Wait/Pause), without writing the player's saved config (one config for every
    //    mod installed). The battle speed isn't pinned (its reader is too small to detour safely, 2026-11-21): the
    //    saved value stays; the fill ignores it (SnesAtb) and the waits are timed through it (WaitGauge). Before 2026-11-21 NatbRules did a part of this, off by
    //    default, and read BattleActExection.nowState, which the game never writes (Battle Options 0.2.1's
    //    finding): it never stopped time.
    static class AtbRules
    {
        internal static bool SpeedBalance = true, Quickfill = true, Natb = true;

        public static void Apply(Harmony h)
        {
            System.AppDomain.CurrentDomain.SetData("pr.bnw.natb", Natb);
            BnwAtb.UseBnwNumbers();
            SnesAtb.On = () => SpeedBalance;
            SnesAtb.Tag = SnesQuickfill.Tag = SnesActionFreeze.Tag = BattleSettingsLock.Tag = "[atb]";
            if (SpeedBalance) SnesAtb.Patch(h, false);   // the fill rates; no chants (WaitGauge's)
            SnesQuickfill.On = () => Quickfill;
            if (Quickfill) SnesQuickfill.Patch(h);
            SnesActionFreeze.On = () => Natb;
            SnesActionFreeze.WhileTurnUp = () => false;   // BNW's menus stop time through the game's own Wait
            BattleSettingsLock.On = () => Natb;
            if (Natb)
            {
                SnesActionFreeze.Patch(h);
                BattleSettingsLock.Patch(h);
            }
            Plugin.L.LogInfo($"[atb] Speed Balance {SpeedBalance} (characters {BnwAtb.NormalK}/{BnwAtb.HasteK}/{BnwAtb.SlowK} x (Speed + {BnwAtb.CharBias}), " +
                             $"monsters x1.5 x (Speed + {BnwAtb.MonsterBias}), no battle-speed term), Quickfill {Quickfill}, nATB {Natb}");
            AtbTests.Register(h);
        }
    }
}
