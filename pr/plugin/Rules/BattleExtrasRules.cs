using System;

namespace BraveNewWorld.Rules
{
    // Game-free parts of BattleExtras (readme audit batch C, 2026-11-21), offline-tested in Rules.Tests.

    // A weapon's place in BNW's dual-wield rules: weapon data byte 12 ($D8500C) bits 3 ("Gauntlet", the
    // two-handed grip) and 4 ("Genji Glove", dual wield). The port carries both as equip abilities on the
    // weapon (251 = the PR's bothHands type 627, 252 = TwoSwordStyle type 12): of BNW 3.0's 90 weapons,
    // 7 are Spear (the Mythril Pike..Gungnir), 28 DualWield (daggers, claws, ninja blades, boomerangs),
    // 8 Both (the katanas, Excalibur), 47 None; the port's abilities match the ROM's bits for all 90.
    public enum WeaponGrip { None, Spear, DualWield, Both }

    public static class DualWieldRules
    {
        public static WeaponGrip Grip(bool twoHanded, bool dualWield) =>
            twoHanded ? (dualWield ? WeaponGrip.Both : WeaponGrip.Spear) : (dualWield ? WeaponGrip.DualWield : WeaponGrip.None);

        // May a weapon go in one hand while the other hand holds `held`? BNW's equip menu (c3.asm DW_Chk_RH /
        // DW_Chk_LH / Item_Chk at C3/F13D, called from C3/9A90 and C3/9ABC in place of vanilla's Genji Glove check):
        //   held only "Gauntlet" (a spear)          -> no weapon at all
        //   held only "Genji Glove" (dual wield)    -> any weapon but a spear
        //   held neither or both                    -> only a dual-wield-only weapon
        // The same pairs as BNW's in-battle swap (c1.asm SwapDual / CanDual: no "block" bit on either weapon
        // and an "enable" bit on one), so the rule is symmetric.
        public static bool MayPair(WeaponGrip held, WeaponGrip selected)
        {
            if (held == WeaponGrip.Spear) return false;
            if (held == WeaponGrip.DualWield) return selected != WeaponGrip.Spear;
            return selected == WeaponGrip.DualWield;
        }
    }

    // BNW 3.0's battle rewards off (bnw 3.0/asm/noexp_overhaul.asm at C2/5DAB, in BNW's ROM): an enemy's
    // EXP counts only when the party is not on the Veldt ($11E4 bit 1) and the Config menu's "Exp. Gain" is
    // on ($1D4D bit 3, BNW's NoGainz option); when none counted, the GP total is doubled (ASL $2F3E). EP is
    // XP/8 x SP (Calc_EP), so it is 0 too; the SP bank still fills (Add_EP runs from C2/6235 every battle).
    public static class RewardRules
    {
        public static bool NoExp(bool noGainz, bool veldt) => noGainz || veldt;

        public static int Exp(int exp, bool noGainz, bool veldt) => NoExp(noGainz, veldt) ? 0 : exp;

        public static int Gil(int gil, bool noGainz, bool veldt) =>
            NoExp(noGainz, veldt) && gil > 0 ? (int)Math.Min(int.MaxValue / 2, 2L * gil) : gil;
    }
}
