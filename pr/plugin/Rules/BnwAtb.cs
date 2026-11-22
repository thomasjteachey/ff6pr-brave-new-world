using PrKit.Ff6;

namespace BraveNewWorld.Rules
{
    // BNW's battle clock numbers and its command waits in seconds, game-free (Rules.Tests; the plugin's AtbRules and
    // WaitGauge apply them).
    //  * Speed Balance (c2.asm C2/09D2 ATBMultipliers): characters k 75 / Haste 90 / Slow 60 (Slow wins) x (Speed + 51)
    //    / 16 a tick, monsters (k + k/2) x (Speed + 20) / 16, no battle-speed term: the kit's SnesAtbRules with these.
    //  * Command waits (c2.asm C2/067B CommandWaitTimes, BNW's table; C2/1195 -> C0/D990 WaitTimer): a chosen
    //    command's wait timer gains half the ATB's add a tick ($3AC8 / 2) until it reaches the command's wait x 256,
    //    the same rule as vanilla's (SnesAtbRules.WaitSeconds) on BNW's add and BNW's table. Jump's air time is its
    //    table entry, $70 (vanilla $E0). Palidor's riders gain a fixed $C8 a tick instead (C0/D990), wait $70 (C2/0B7D):
    //    not told apart in the PR (every landing is timed as Jump's: within ~6% at Speeds 30-50).
    //  * Who waits (as vanilla, SnesAtbRules.ChantOf): monsters and manipulated acts keep the game's (none);
    //    Jump's going up, Berserk/Muddle turns and Rage's auto turns act at once; BNW's Dance keeps the dancer under
    //    control (CommandRules.Dance), so a Dance is a chosen command with its wait, never an automatic one.
    public static class BnwAtb
    {
        public const int NormalK = 75, HasteK = 90, SlowK = 60, CharBias = 51, MonsterBias = 20;
        public const int JumpCommand = 30;   // the PR's Jump (bnw_data.json command_waits: vanilla 224, BNW 112)

        // the kit's SNES ATB math with BNW's numbers (one mod's plugin per game: a static setting is safe)
        public static void UseBnwNumbers()
        {
            SnesAtbRules.NormalK = NormalK;
            SnesAtbRules.HasteK = HasteK;
            SnesAtbRules.SlowK = SlowK;
            SnesAtbRules.CharBias = CharBias;
            SnesAtbRules.MonsterBias = MonsterBias;
            SnesAtbRules.MonsterBattleSpeed = false;
            SnesAtbRules.MonsterHalfMore = true;
        }

        public enum Wait
        {
            Game,      // the game's own chant stays (a monster's, a manipulated act's)
            Now,       // acts at once
            Timed,     // BNW's wait (`wait`, the table's value; 0 = at once)
            Unknown,   // a command BNW's table doesn't have: the game's chant stays
        }

        // What BNW waits before an act, from what the plugin reads off it: its first ability's type, its command, who
        // acts and how. `wait` = the SNES table value ($322C) for Timed.
        public static Wait WaitFor(CommandWaits table, int abilityType, int command, bool monster, bool manipulated,
                                   bool wild, bool rampage, bool autoDance, bool copied, out int wait)
        {
            wait = 0;
            if (abilityType == SnesAtbRules.LandingAbility)
            {
                wait = table.Commands.TryGetValue(JumpCommand, out var j) ? j.Bnw : 0x70;
                return Wait.Timed;
            }
            if (monster || manipulated) return Wait.Game;
            if (abilityType == SnesAtbRules.UpAtOnceAbility || wild || rampage || autoDance) return Wait.Now;
            if (abilityType == table.SummonAbilityType)
            {
                wait = table.SummonBnw;
                return Wait.Timed;
            }
            int cmd = SnesAtbRules.WaitCommand(command, copied);
            if (!table.Commands.TryGetValue(cmd, out var c)) return Wait.Unknown;
            wait = c.Bnw;
            return Wait.Timed;
        }

        // a character's wait in seconds of battle clock (UseBnwNumbers first)
        public static double Seconds(int wait, int speed, bool haste, bool slow) =>
            SnesAtbRules.WaitSeconds(wait, speed, false, 0, haste, slow);
    }
}
