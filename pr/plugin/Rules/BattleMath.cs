using System.Collections.Generic;
using System.Linq;

namespace BraveNewWorld.Rules
{
    // BNW battle arithmetic the plugin applies, in the SNES's integer steps (offline-tested in
    // Rules.Tests).
    public static class BattleMath
    {
        // The two-handed grip (c2.asm 4914 PlayerPhys): Battle Power + Battle Power / 2.
        public static int Grip(int battlePower) => battlePower + battlePower / 2;

        // Shock (c2.asm C23EDD): x = Level * Stamina + current HP, then (x * 2 + x) / 4 in 16 bits
        // (ASL's carry goes into the ADC; a sum past 65535 wraps, as on the SNES: Level 99,
        // Stamina 128 at 9999 HP hits for 619), halved from the back row.
        public static int Shock(int level, int stamina, int hp, bool backRow)
        {
            int x = (level * stamina + hp) & 0xFFFF;
            int twice = x << 1;
            int v = ((twice & 0xFFFF) + x + (twice > 0xFFFF ? 1 : 0)) & 0xFFFF;
            v >>= 2;
            return backRow ? v >> 1 : v;
        }

        // Shock's cost to its user: max HP / 8 (LSR x3).
        public static int ShockCost(int maxHp) => maxHp >> 3;

        // A covering knight's Evade (c2.asm HalveEvasion) in 8 bits: vanilla's block value
        // (C2/2861: Evade * 2 capped at 255, EOR #$FF, INC, 0 -> 255), then BNW's 255 - block,
        // INC, LSR, LSR. Evade / 2 up to 127; 128 and above give 63.
        public static int CoverEvade(int evade)
        {
            int a = evade <= 0 ? 0 : (evade >= 128 ? 255 : evade * 2);
            int block = ((a ^ 0xFF) + 1) & 0xFF;
            if (block == 0) block = 255;
            int x = (((255 - block) & 0xFF) + 1) & 0xFF;
            return x >> 2;
        }

        // A sketched physical attack's "Vigor": the sketcher's Magic, doubled as the SNES stores
        // Vigor (c2.asm SketchMag: ASL in 8 bits, so Magic 128+ wraps), handed back undoubled.
        public static int SketchVigor(int magic) => ((magic << 1) & 0xFF) >> 1;

        // True Knight (c2.asm C2123B TrueKnight / EvalKnight): the guard that covers an ally, or
        // -1. A guard must wear the knight's effect and stand in the front row. A Near Fatal
        // target is always covered; otherwise only a back-row target, by a guard who isn't Near
        // Fatal, when random(0..191) (0..95 while the guard defends) < the guard's Stamina. The
        // guard must be able to act (Blocked: dead, petrified, seized, the status blacklist), and
        // of those who would, the one with the most HP covers (equal HP: the later one in the
        // list; BNW's loop runs from the last party slot down, so callers pass slot order reversed).
        public sealed class CoverGuardInfo
        {
            public bool Knight, BackRow, Dying, Defending, Blocked;
            public int Stamina, Hp;
        }

        public static int CoverGuard(bool targetDying, bool targetBackRow, System.Collections.Generic.IList<CoverGuardInfo> guards,
            System.Func<int, int> random)
        {
            int best = -1, bestHp = 0;
            for (int i = 0; i < guards.Count; i++)
            {
                var g = guards[i];
                if (g == null || !g.Knight || g.BackRow) continue;
                if (!targetDying)
                {
                    if (!targetBackRow || g.Dying) continue;
                    if (random(g.Defending ? 96 : 192) >= g.Stamina) continue;
                }
                if (g.Blocked || g.Hp < bestHp) continue;
                best = i;
                bestHp = g.Hp;
            }
            return bestHp > 0 ? best : -1;
        }

        // BNW's "Elemental Share" (c2.asm C20BD3 + c0.asm EleModLoop): an attack with several
        // elements counts points per element on the target, immune 0, resistant 1, normal 2, weak 4;
        // no points = no damage, else points / 2 against the number of elements: equal = normal
        // damage, more = double, fewer = half. Any absorbed element heals (checked first, as the
        // game's own). Element types are the PR's ResistanceAttributeType: 0 normal, 1 weak, 2 half,
        // 3 immune ("Invalid"), 4 absorb (401 / 402, double weak / half, count as weak / half).
        public const int ElemNormal = 0, ElemWeak = 1, ElemHalf = 2, ElemNull = 3, ElemAbsorb = 4;

        public static int ElementShare(IReadOnlyList<int> types)
        {
            if (types == null || types.Count == 0) return ElemNormal;
            int points = 0;
            foreach (var t in types)
            {
                if (t == ElemAbsorb) return ElemAbsorb;
                points += t == ElemNull ? 0 : t == ElemHalf || t == 402 ? 1 : t == ElemWeak || t == 401 ? 4 : 2;
            }
            if (points == 0) return ElemNull;
            int half = points >> 1, n = types.Count;
            return half == n ? ElemNormal : half > n ? ElemWeak : ElemHalf;
        }

        // The target's element table as the game should read it for one multi-element attack, so its
        // own rule (the strongest single response wins) gives BNW's share: every attack element normal
        // but one carrying the outcome (an element already giving it, else the first). Null = leave the
        // table as it is (an absorbed element, or a single-element attack: the game's rule is BNW's).
        public static Dictionary<int, int> ElementView(IReadOnlyDictionary<int, int> table, IReadOnlyList<int> attack)
        {
            if (attack == null) return null;
            var elems = attack.Where(a => a > 0).Distinct().ToList();
            if (elems.Count < 2) return null;
            var types = elems.Select(a => table != null && table.TryGetValue(a, out var t) ? t : ElemNormal).ToList();
            int outcome = ElementShare(types);
            if (outcome == ElemAbsorb) return null;
            var view = table == null ? new Dictionary<int, int>() : table.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var a in elems) view.Remove(a);
            if (outcome != ElemNormal)
            {
                int at = types.IndexOf(outcome);
                view[elems[at >= 0 ? at : 0]] = outcome;
            }
            return view;
        }
    }
}
