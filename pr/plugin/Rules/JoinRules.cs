namespace BraveNewWorld.Rules
{
    // The level a character joins the party at in BNW (offline-tested in Rules.Tests; the plugin's
    // JoinLevels applies it in the PR's SystemCall.Current.CorrectLevel).
    //
    // SNES (vanilla): a character's first join sets the level to the average of every recruited
    // character's level plus a per-character offset (init data ED/7CB5 bits 2-3 -> C0/A228: +0, +2, +5, -3);
    // a rejoin changes the level only where the event script says so (event command 77 char_level_avg:
    // raise to the average, never lower). BNW (ca_cb_cc.asm, "new level averaging behavior"):
    //  * first joins: every offset 0 but Gogo -3 and Umaro +5;
    //  * World of Ruin recruitments: command 66 instead of 77, level = max(own, RejoinLvl) (c0.asm Level18:
    //    21, but Edgar 19, Sabin and Celes 18, Setzer 20);
    //  * World of Balance rejoins: the 77s removed (NOPs: Terra and the IMTRF four, Thamasa after Leo, Mog,
    //    Locke and Terra after the banquet), kept only for Shadow (Imperial Camp, Kohlingen, the Floating
    //    Continent) and Setzer (the Blackjack).
    // The PR levels every join of a character not in the roster (CorrectLevel: the average of the other
    // available characters, statusId < 15, plus its own first-join table, clamped 1-99, raise only).
    public static class JoinRules
    {
        public const int Shadow = 4, Setzer = 10, Gogo = 13, Umaro = 14;

        // statusId 1-14: Terra, Locke, Cyan, Shadow, Edgar, Sabin, Celes, Strago, Relm, Setzer, Mog, Gau, Gogo, Umaro
        public static readonly int[] RejoinLevel = { 21, 21, 21, 21, 19, 18, 18, 21, 21, 20, 21, 21, 21, 21 };
        public static readonly int[] FirstJoinOffset = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -3, 5 };
        // vanilla's (C0/A228 by ED/7CB5), for the record: Locke, Cyan, Edgar, Sabin, Strago, Gau, Gogo +2, Mog +5
        public static readonly int[] VanillaFirstJoinOffset = { 0, 2, 2, 0, 2, 2, 0, 2, 0, 0, 5, 2, 2, 0 };

        // The level to raise the character to (0 = leave it), or -1 = the PR's own averaging (BNW keeps 77 there).
        // average: the PR's average of the other available characters.
        public static int Target(int statusId, bool firstJoin, bool worldOfRuin, int average)
        {
            if (statusId < 1 || statusId > 14) return -1;
            if (firstJoin)
            {
                int lv = average + FirstJoinOffset[statusId - 1];
                if (lv < 1) lv = 1;
                if (lv > 99) lv = 99;
                // a first join in the World of Ruin is a recruitment too (Mog if he was missed): its 66 runs after
                if (worldOfRuin && statusId != Gogo && statusId != Umaro && lv < RejoinLevel[statusId - 1])
                    lv = RejoinLevel[statusId - 1];
                return lv;
            }
            if (worldOfRuin) return RejoinLevel[statusId - 1];
            return statusId == Shadow || statusId == Setzer ? -1 : 0;
        }
    }
}
