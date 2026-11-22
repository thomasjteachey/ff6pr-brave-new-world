namespace BraveNewWorld.Rules
{
    // Ultros's EL respec at the Coliseum (readme 598-606, 2574; ca_cb_cc.asm:499-562 CB/4E5E; c0.asm:232-266
    // RespecELs, general action $67), game-free: the plugin's BnwPlugin/MenuRules.cs runs it for the porter's
    // script (tools/port_bnw_menus.py, sc_bnw_ultros_respec), which also takes the gil.
    public static class RespecRules
    {
        // CB/4E7D 85 A8 61: 25,000 GP and the Receipt (bit 1DD); with it CB/4E8D: 60,000 + 40,000 GP
        public const int FirstPrice = 25000, LaterPrice = 100000;
        public const int Umaro = 14;   // character_status id (Terra 1 .. Umaro 14)

        public static int Price(bool hasReceipt) => hasReceipt ? LaterPrice : FirstPrice;

        // BNW's script respecs the active party's Terra..Gau (C0 A0..AB checks, 67 00..0B: SNES 0-11 = ids 1-12);
        // Gogo and Umaro (no ELs) are neither unequipped nor reset.
        public static bool Respecs(int characterStatusId) => characterStatusId >= 1 && characterStatusId <= 12;

        // RespecELs: LDA !EL,Y / STA !EL_bank,Y (every EL earned is unspent again), Vigor/Speed/Stamina/Magic and
        // level-1 max HP/MP from the base table (ED/7CA0+), then level averaging rebuilds HP/MP from level 1
        // (C0/9F4A). The port keeps the EL bonuses apart (EsperProgress.Spent, added by EsperHooks on top of
        // the PR's stats), so the reset is: the bank = EL, nothing spent. EP, EL and the SP bank stay.
        public static EsperProgress Respec(EsperProgress p)
        {
            var r = p.Clone();
            r.ElBank = p.El;
            r.Spent = new StatBonus();
            return r;
        }
    }
}
