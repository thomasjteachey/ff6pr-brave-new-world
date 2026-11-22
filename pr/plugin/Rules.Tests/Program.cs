using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BraveNewWorld.Rules;
using PrKit;

// Tiny test runner: each Test() prints PASS/FAIL; exit code = number of failures.
static class Program
{
    static int failures, passes;
    static BnwData D;
    static string DataPath;

    static int Main(string[] args)
    {
        var path = args.Length > 0 ? args[0] : FindData();
        Console.WriteLine($"data: {path}");
        DataPath = path;
        D = BnwData.Load(path);

        Test("data shape", DataShape);
        Test("esper users", EsperUsers);
        Test("EP formula", EpFormula);
        Test("EL gain from zero", ElGain);
        Test("EL caps (EL 25, EP 50000, SP bank 30)", Caps);
        Test("no esper, no progress", NoEsper);
        Test("Gogo/Umaro never gain EL", NoElForGogo);
        Test("spending ELs", Spending);
        Test("stat cap 128 when spending", StatCap);
        Test("save packing round-trip", Packing);
        Test("EL bonuses match BNW's own text", ElBonusText);
        Test("equip bonuses match BNW's own text", EquipBonusText);
        Test("resistance merging", Resistances);
        Test("percent and flat bonuses", Arithmetic);
        Test("bonus line text", BonusLines);
        Test("menu spend rules", MenuSpend);
        Test("esper screen status line", StatusLines);
        Test("bank then spend matches BNW totals", BankThenSpend);
        Test("test kit setup", TestKitSetup);
        Test("bonus row hint text", SpendHints);
        Test("EL beside LV and Next EL", LevelDisplay);
        Test("boss test list", BossTests);
        Test("esper spells and SP costs", EsperSpells);
        Test("learning spells with banked SP", LearnWithSp);
        Test("SP bank hint text", LearnHints);
        Test("weapon proc odds", ProcOdds);
        Test("item lists the game hard-codes", HardItemLists);
        Test("command wait times", CommandWaitTimes);
        Test("Spirit-based abilities and special multipliers", SpiritData);
        Test("Shock and the two-handed grip", ShockAndGrip);
        Test("Sketch's Magic and True Knight cover", SketchAndCover);
        Test("BNW's elemental share (multi-element attacks)", ElementShareRule);
        Test("monster images from this PC's game", MonsterImagesFromGame);
        Test("BNW's join levels (no averaging, World of Ruin floors)", JoinLevelRule);
        Test("BNW's ATB numbers and command waits in seconds (Speed Balance, C2/067B)", AtbWaits);
        Test("audit batch B data (Stamina-only evasion, fractional on bosses, respect row, 0 MP deaths)", AuditData);
        Test("battle items data (spread stars, MP-crit procs)", BattleItemsData);
        Test("spears and dual wield (c3.asm DW_Chk, c1.asm SwapDual)", DualWieldPairs);
        Test("NoGainz and the Veldt (noexp_overhaul.asm): no EXP or EP, SP banks, gil x2", NoGainzRewards);
        Test("Ultros's EL respec (RespecELs)", ElRespec);

        Console.WriteLine($"\n{passes} passed, {failures} failed");
        return failures;
    }

    static string FindData()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "mods", "BraveNewWorld", "bnw_data.json");
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException("bnw_data.json not found; pass its path as the first argument");
    }

    static void Test(string name, Action body)
    {
        try { body(); passes++; Console.WriteLine($"PASS  {name}"); }
        catch (Exception e) { failures++; Console.WriteLine($"FAIL  {name}: {e.Message}"); }
    }

    static void Eq<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected {expected}, got {actual}");
    }

    static void True(bool cond, string what)
    {
        if (!cond) throw new Exception(what);
    }

    static Esper E(string name) => D.Espers.First(e => e.Name == name);

    // ------------------------------------------------------------------------------

    static void DataShape()
    {
        Eq(25, D.Rules.MaxEl, "max EL");
        Eq(25, D.Rules.EpChart.Length, "EP chart length");
        for (int i = 1; i < D.Rules.EpChart.Length; i++)
            True(D.Rules.EpChart[i] > D.Rules.EpChart[i - 1], $"EP chart not increasing at {i}");
        True(D.Rules.EpChart[^1] <= D.Rules.MaxEp, "last EL reachable under the EP cap");
        Eq(27, D.Espers.Count, "esper count");
        Eq(D.Espers.Count, D.Espers.Select(e => e.ContentId).Distinct().Count(), "distinct content ids");
        True(D.ElBonuses.Count >= 16, "16 EL bonus kinds");
    }

    static void EsperUsers()
    {
        // BNW: every esper but the unused slot has one or two users; nobody is Gogo/Umaro.
        foreach (var e in D.Espers.Where(e => e.Name != "Loki"))
        {
            True(e.UserStatusIds.Length is 1 or 2, $"{e.Name} has {e.UserStatusIds.Length} users");
            True(e.UserStatusIds.All(c => c >= 1 && c <= 12), $"{e.Name} users in Terra..Gau");
        }
        var ramuh = E("Ramuh");
        True(EsperRules.CanEquip(D, 2, ramuh.ContentId), "Locke can equip Ramuh");
        True(EsperRules.CanEquip(D, 7, ramuh.ContentId), "Celes can equip Ramuh");
        True(!EsperRules.CanEquip(D, 1, ramuh.ContentId), "Terra cannot equip Ramuh");
        True(EsperRules.CanEquip(D, 1, 99999), "unknown esper stays equippable");
        // each character can use several espers
        for (int c = 1; c <= 12; c++)
            True(D.Espers.Count(e => e.UserStatusIds.Contains(c)) >= 2, $"character {c} has at least 2 espers");
    }

    static void EpFormula()
    {
        Eq(300, EsperRules.EpGain(800, 3), "800 XP x 3 SP");
        Eq(0, EsperRules.EpGain(7, 10), "XP under 8 gives nothing");
        Eq(0, EsperRules.EpGain(5000, 0), "no SP, no EP");
        Eq(15, EsperRules.EpGain(127, 1), "XP rounds down to a multiple of 8");
    }

    static void ElGain()
    {
        var p = EsperProgress.Empty;
        Eq(0, EsperRules.AfterBattle(D, ref p, 1, true, 248, 1), "31 EP: no EL yet");
        Eq(31, p.Ep, "EP");
        Eq(1, EsperRules.AfterBattle(D, ref p, 1, true, 8, 1), "32 EP: EL 1");
        Eq(1, p.El, "EL");
        Eq(1, p.ElBank, "EL bank");
        Eq(2, p.SpBank, "SP bank");
        // one big battle can give several ELs (chart: 64, 192, 384)
        Eq(3, EsperRules.AfterBattle(D, ref p, 1, true, 8 * 352, 1), "jump to 384 EP");
        Eq(4, p.El, "EL after jump");
        Eq(4, p.ElBank, "bank after jump");
    }

    static void Caps()
    {
        var p = EsperProgress.Empty;
        int gained = EsperRules.AfterBattle(D, ref p, 3, true, 8 * 30000, 10);
        Eq(25, gained, "all 25 ELs");
        Eq(50000, p.Ep, "EP capped");
        Eq(25, p.El, "EL capped");
        Eq(10, p.SpBank, "SP bank");
        Eq(0, EsperRules.AfterBattle(D, ref p, 3, true, 8 * 30000, 25), "nothing past EL 25");
        Eq(30, p.SpBank, "SP bank capped at 30");
        Eq(-1, EsperRules.NextElAt(D, 25), "no next EL at max");
    }

    static void NoEsper()
    {
        var p = EsperProgress.Empty;
        Eq(0, EsperRules.AfterBattle(D, ref p, 1, false, 100000, 10), "no EL");
        Eq(0, p.Ep, "no EP");
        Eq(0, p.SpBank, "no SP");
    }

    static void NoElForGogo()
    {
        var p = EsperProgress.Empty;
        Eq(0, EsperRules.AfterBattle(D, ref p, 13, true, 8000, 10), "Gogo");
        Eq(10000, p.Ep, "EP still recorded");
        Eq(0, p.El, "EL stays 0");
    }

    static void Spending()
    {
        var p = EsperProgress.Empty;
        True(EsperRules.SpendEl(D, ref p, E("Phoenix"), default) == null, "nothing to spend");
        p.ElBank = 2;
        var terrato = E("Terrato"); // +40 HP per EL
        var a = EsperRules.SpendEl(D, ref p, terrato, default);
        Eq(40, a.Hp, "HP added");
        Eq(1, p.ElBank, "bank decremented");
        var shiva = E("Shiva");     // +2 Magic per EL
        var b = EsperRules.SpendEl(D, ref p, shiva, default);
        Eq(2, b.Magic, "magic added");
        Eq(40, p.Spent.Hp, "spent HP total");
        Eq(2, p.Spent.Magic, "spent magic total");
        Eq(0, p.ElBank, "bank empty");
    }

    static void StatCap()
    {
        var p = EsperProgress.Empty;
        p.ElBank = 3;
        var ramuh = E("Ramuh");     // +2 Vigor per EL
        var baseStats = new BaseStats { Power = 125 };
        Eq(2, EsperRules.SpendEl(D, ref p, ramuh, baseStats).Power, "125 -> 127");
        Eq(1, EsperRules.SpendEl(D, ref p, ramuh, baseStats).Power, "127 -> 128");
        Eq(0, EsperRules.SpendEl(D, ref p, ramuh, baseStats).Power, "stays at 128");
        Eq(3, p.Spent.Power, "total");
    }

    static void Packing()
    {
        var p = new EsperProgress
        {
            Ep = 50000, El = 25, ElBank = 25, SpBank = 30,
            Spent = new StatBonus { Hp = 1000, Mp = 750, Power = 50, Agility = 49, Vitality = 1, Magic = 63 },
        };
        var w = p.Pack();
        True(w.All(x => x >= 0), "packed words are non-negative");
        var q = EsperProgress.Unpack(w[0], w[1], w[2]);
        Eq(p.Ep, q.Ep, "EP"); Eq(p.El, q.El, "EL"); Eq(p.ElBank, q.ElBank, "bank"); Eq(p.SpBank, q.SpBank, "SP");
        Eq(1000, q.Spent.Hp, "HP"); Eq(750, q.Spent.Mp, "MP"); Eq(50, q.Spent.Power, "Vigor");
        Eq(49, q.Spent.Agility, "Speed"); Eq(1, q.Spent.Vitality, "Stamina"); Eq(63, q.Spent.Magic, "Magic");
        var z = EsperProgress.Unpack(0, 0, 0);
        Eq(0, z.Ep + z.El + z.ElBank + z.SpBank + z.Spent.Hp, "zeros");
        // the most a character can bank fits
        Eq(25 * 40, new EsperProgress { Spent = new StatBonus { Hp = 25 * 40 } }.Pack()[1] & 0x7FF, "max HP total");
    }

    // "+40 HP per EL spent", "+1 Magic/+15 MP per EL spent", "+2 Vigor per EL spent"
    static void ElBonusText()
    {
        int checkedCount = 0;
        foreach (var e in D.Espers.Where(e => e.ElBonusText.Length > 0))
        {
            var want = new StatBonus();
            foreach (Match m in Regex.Matches(e.ElBonusText, @"\+(\d+) (HP|MP|Vigor|Speed|Spirit|Magic)"))
            {
                int n = int.Parse(m.Groups[1].Value);
                switch (m.Groups[2].Value)
                {
                    case "HP": want.Hp += n; break;
                    case "MP": want.Mp += n; break;
                    case "Vigor": want.Power += n; break;
                    case "Speed": want.Agility += n; break;
                    case "Spirit": want.Vitality += n; break;
                    case "Magic": want.Magic += n; break;
                }
            }
            var got = D.ElBonusOf(e);
            Eq(want.ToString(), got.ToString(), $"{e.Name} ('{e.ElBonusText}')");
            checkedCount++;
        }
        True(checkedCount >= 26, $"checked {checkedCount} espers");
    }

    static void EquipBonusText()
    {
        var halve = new Dictionary<string, int> { ["Fire"] = 1, ["Ice"] = 2, ["Bolt"] = 3, ["Earth"] = 4, ["Water"] = 5, ["Poison"] = 6, ["Holy"] = 7, ["Wind"] = 8 };
        var auto = new Dictionary<string, int> { ["Haste"] = 21, ["Shell"] = 22, ["Safe"] = 23, ["Reflect"] = 92, ["Regen"] = 20 };
        int checkedCount = 0;
        foreach (var e in D.Espers.Where(e => e.EquipBonusText.Length > 0))
        {
            var t = e.EquipBonusText.Replace("\n", " ");
            var q = e.Equip;
            Match m;
            if ((m = Regex.Match(t, @"Halves (\w+) damage")).Success)
                Eq(halve[m.Groups[1].Value].ToString(), string.Join(",", q.HalfAttributes), e.Name);
            else if ((m = Regex.Match(t, @"Auto-(\w+)")).Success)
                True(q.AutoConditions.Contains(auto[m.Groups[1].Value]), $"{e.Name}: auto {m.Groups[1].Value}");
            else if ((m = Regex.Match(t, @"(Vigor|Speed|Spirit|Magic|M\.Def\.|Def|M\.Evade|Evade) \+(\d+)")).Success)
            {
                int n = int.Parse(m.Groups[2].Value);
                int got = m.Groups[1].Value switch
                {
                    "Vigor" => q.Power, "Speed" => q.Agility, "Spirit" => q.Vitality, "Magic" => q.Magic,
                    "Def" => q.Defense, "M.Def." => q.AbilityDefense, "Evade" => q.EvasionRate, _ => q.AbilityEvasionRate,
                };
                Eq(n, got, $"{e.Name} '{t}'");
            }
            else if ((m = Regex.Match(t, @"(HP|MP)\+(\d+)%")).Success)
                Eq(int.Parse(m.Groups[2].Value), m.Groups[1].Value == "HP" ? q.MaxHpPct : q.MaxMpPct, e.Name);
            else if ((m = Regex.Match(t, @"(Physical|Magical) output \+(\d+)%")).Success)
                Eq(int.Parse(m.Groups[2].Value), m.Groups[1].Value == "Physical" ? q.PhysicalPct : q.MagicPct, e.Name);
            else if (t.Contains("Blocks"))
                True(q.ProtectConditions.Length >= 3, $"{e.Name}: blocks {q.ProtectConditions.Length} conditions");
            else
                throw new Exception($"{e.Name}: unrecognised bonus text '{t}'");
            checkedCount++;
        }
        Eq(26, checkedCount, "espers with an equip bonus");
    }

    static void Resistances()
    {
        var r = new Dictionary<int, int> { [1] = 1, [2] = 3, [3] = 4 };
        EsperRules.AddHalves(r, new[] { 1, 2, 3, 4 });
        True(!r.ContainsKey(1), "half cancels weak");
        Eq(3, r[2], "null kept");
        Eq(4, r[3], "absorb kept");
        Eq(2, r[4], "half added");
        var c = new Dictionary<int, int> { [6] = 1 };
        EsperRules.AddProtections(c, new[] { 6, 12 });
        Eq(2, c.Count, "protections");
    }

    static void Arithmetic()
    {
        Eq(1250, EsperRules.WithPercent(1000, 25, 9999), "+25%");
        Eq(9999, EsperRules.WithPercent(9000, 25, 9999), "HP cap");
        Eq(999, EsperRules.WithPercent(900, 25, 999), "MP cap");
        Eq(1000, EsperRules.WithPercent(1000, 0, 9999), "no bonus");
        Eq(255, EsperRules.WithAdd(250, 10, 255), "flat cap");
    }

    static void MenuSpend()
    {
        var ramuh = E("Ramuh");   // Locke (2) and Celes (7)
        var p = EsperProgress.Empty;
        Eq(EsperRules.SpendResult.NoElToSpend, EsperRules.TrySpend(D, ref p, 2, ramuh, default, out _), "empty bank");
        p.ElBank = 1;
        Eq(EsperRules.SpendResult.CantUseEsper, EsperRules.TrySpend(D, ref p, 1, ramuh, default, out _), "Terra can't use Ramuh");
        Eq(1, p.ElBank, "refused spend keeps the EL");
        Eq(EsperRules.SpendResult.NoEsperLevels, EsperRules.TrySpend(D, ref p, 13, ramuh, default, out _), "Gogo");
        Eq(EsperRules.SpendResult.CantUseEsper, EsperRules.TrySpend(D, ref p, 2, null, default, out _), "no esper");
        Eq(EsperRules.SpendResult.Spent, EsperRules.TrySpend(D, ref p, 2, ramuh, default, out var added), "Locke spends");
        Eq(2, added.Power, "+2 Vigor");
        Eq(0, p.ElBank, "bank used");
    }

    static void StatusLines()
    {
        var p = EsperProgress.Empty;
        Eq("Unspent EL: 0", EsperRules.StatusLine(D, p, 1, ""), "fresh");
        p.Ep = 100; p.El = 2; p.ElBank = 2;
        Eq("Unspent EL: 2", EsperRules.StatusLine(D, p, 1, ""), "bank");
        Eq("Unspent EL: 2 [Space]", EsperRules.StatusLine(D, p, 1, "Space"), "with an extra spend key");
        Eq("", EsperRules.StatusLine(D, p, 13, ""), "Gogo has no EL");
        Eq(0, EsperRules.StatusLines(D, p, 14, "").Count, "Umaro: nothing");
        var lines = EsperRules.StatusLines(D, p, 1, "Space");
        for (int i = 1; i < lines.Count; i++) True(lines[i].Length < lines[i - 1].Length, $"shrinking: {string.Join(" | ", lines)}");
        Eq("EL 2, next EL in 92 EP.", EsperRules.LevelText(D, p), "level text");
        p.El = 10; p.Ep = 3840 + 1000;
        Eq("EL 10, next EL in 1,048 EP.", EsperRules.LevelText(D, p), "thousands separator");
        p.El = 25; p.Ep = 50000;
        Eq("EL 25 (max).", EsperRules.LevelText(D, p), "max");
    }

    // Bank all 25 ELs, then split them between two of the character's espers (one MP-only
    // bonus, one with HP): totals must equal the single spends, and the bank must end empty.
    static void BankThenSpend()
    {
        Esper mpEsper = null, hpEsper = null;
        int sid = 0;
        for (int c = 1; c <= 12 && sid == 0; c++)
        {
            var mine = D.Espers.Where(e => e.UserStatusIds.Contains(c)).ToList();
            mpEsper = mine.FirstOrDefault(e => D.ElBonusOf(e).Mp > 0 && D.ElBonusOf(e).Hp == 0);
            hpEsper = mine.FirstOrDefault(e => D.ElBonusOf(e).Hp > 0 && D.ElBonusOf(e).Mp == 0);
            if (mpEsper != null && hpEsper != null) sid = c;
        }
        True(sid > 0, "some character has an MP-only and an HP-only esper");
        var p = EsperProgress.Empty;
        Eq(25, EsperRules.AfterBattle(D, ref p, sid, true, 8 * 50000, 1), "25 ELs banked");
        Eq(0, p.Spent.Hp + p.Spent.Mp, "nothing applied while banked");
        for (int i = 0; i < 10; i++)
            Eq(EsperRules.SpendResult.Spent, EsperRules.TrySpend(D, ref p, sid, mpEsper, default, out _), mpEsper.Name);
        Eq(10 * D.ElBonusOf(mpEsper).Mp, p.Spent.Mp, $"10 x {mpEsper.Name} MP");
        Eq(15, p.ElBank, "15 left");
        while (p.ElBank > 0) EsperRules.TrySpend(D, ref p, sid, hpEsper, default, out _);
        Eq(15 * D.ElBonusOf(hpEsper).Hp, p.Spent.Hp, $"15 x {hpEsper.Name} HP");
        Eq(EsperRules.SpendResult.NoElToSpend, EsperRules.TrySpend(D, ref p, sid, hpEsper, default, out _), "bank empty");
        var w = p.Pack();
        var q = EsperProgress.Unpack(w[0], w[1], w[2]);
        Eq(p.Spent.ToString(), q.Spent.ToString(), "totals survive save packing");
    }

    static void SpendHints()
    {
        var ramuh = E("Ramuh"); // +2 Vigor, Locke/Celes
        var p = EsperProgress.Empty;
        p.ElBank = 3; p.El = 2; p.Ep = 100;
        Eq("Confirm: spend 1 EL on Ramuh for +2 Vigor. EL 2, next EL in 92 EP.", EsperRules.SpendHint(D, p, 2, ramuh, "Locke"), "can spend");
        Eq("Terra can't use Ramuh. EL 2, next EL in 92 EP.", EsperRules.SpendHint(D, p, 1, ramuh, "Terra"), "can't use");
        Eq("Gogo doesn't gain Esper Levels.", EsperRules.SpendHint(D, p, 13, ramuh, "Gogo"), "Gogo");
        p.ElBank = 0;
        Eq("No unspent ELs. Ramuh gives +2 Vigor per EL. EL 2, next EL in 92 EP.", EsperRules.SpendHint(D, p, 2, ramuh, "Locke"), "empty bank");
        p.El = 25;
        True(EsperRules.SpendHint(D, p, 2, ramuh, "Locke").EndsWith("EL 25 (max)."), "max EL");
    }

    static void BossTests()
    {
        True(D.BossTests.Count > 0, "some bosses run BNW's AI");
        foreach (var t in D.BossTests)
        {
            True(t.Parties.Length > 0, $"boss {t.SnesId} has a formation to start");
            // the game's own formations, or the ones made for BNW's story fights (port_events: 7000 + the PR's
            // event party, BNW's line-up there, the Cranes' 7441; port_ai.new_fights: 8000 + formation, fights
            // the PR has no party for, Kaiser's 8410), not the porter's random encounters (2000+)
            True(t.Parties.All(p => p > 0 && (p < 2000 || p >= 7000 && p < 9000)), $"boss {t.SnesId}: the game's own formations, not the porter's");
        }
        Eq(D.BossTests.Count, D.BossTests.Select(t => t.SnesId).Distinct().Count(), "no duplicates");
    }

    static void LevelDisplay()
    {
        True(EsperRules.ShowsEl(D, 1, true), "Terra with espers");
        True(!EsperRules.ShowsEl(D, 1, false), "before any esper (BNW: before meeting Ramuh)");
        True(!EsperRules.ShowsEl(D, 13, true), "Gogo");
        True(!EsperRules.ShowsEl(D, 15, true), "guest");
        var p = EsperProgress.Empty;
        Eq(32, EsperRules.EpToNextEl(D, p), "fresh");
        p.Ep = 40; p.El = 1;
        Eq(24, EsperRules.EpToNextEl(D, p), "partway");
        p.El = 25; p.Ep = 50000;
        Eq(0, EsperRules.EpToNextEl(D, p), "0 at max, as BNW");
    }

    static void TestKitSetup()
    {
        var p = TestKit.Progress(D, EsperProgress.Empty, 1);
        Eq(10, p.El, "EL 10");
        Eq(10, p.ElBank, "10 to spend");
        Eq(D.Rules.EpChart[10] - 1, p.Ep, "one EP short of EL 11");
        Eq(20, p.SpBank, "SP bank");
        // the next battle with an esper levels up
        Eq(1, EsperRules.AfterBattle(D, ref p, 1, true, 8, 1), "one EP more: EL 11");
        Eq(11, p.El, "EL 11");
        // never lowers progress, keeps spent bonuses
        var high = new EsperProgress { Ep = 49000, El = 24, ElBank = 12, SpBank = 30, Spent = new StatBonus { Hp = 400 } };
        var h = TestKit.Progress(D, high, 1);
        Eq(24, h.El, "EL kept"); Eq(12, h.ElBank, "bank kept"); Eq(49000, h.Ep, "EP kept"); Eq(30, h.SpBank, "SP kept");
        Eq(400, h.Spent.Hp, "spent kept");
        Eq(0, TestKit.Progress(D, EsperProgress.Empty, 13).El, "Gogo untouched");
        Eq(26, TestKit.Espers(D).Count(), "26 usable espers handed out");
    }

    static void EsperSpells()
    {
        string List(string esper) => string.Join(", ", E(esper).Spells.Select(s => $"{s.Name} {s.Sp}"));
        Eq("Scan 5, Cure 2 15, Restore 15", List("Unicorn"), "Unicorn (matches the game screen)");
        Eq("Bolt 1, Bolt 2 15, Demi 10", List("Ramuh"), "Ramuh");
        Eq("Ultima 30", List("Ragnarok"), "Ragnarok");
        foreach (var e in D.Espers)
        {
            True(e.Spells.Length <= 3, $"{e.Name}: {e.Spells.Length} spells");
            foreach (var s in e.Spells)
                True(s.Sp >= 1 && s.Sp <= 30 && s.AbilityId > 0, $"{e.Name} {s.Name}: {s.Sp} SP, ability {s.AbilityId}");
        }
        Eq(83, E("Unicorn").SpellByAbility(83)?.AbilityId ?? 0, "lookup by PR ability id (Scan)");
        True(E("Unicorn").SpellByAbility(42) == null, "Bolt isn't Unicorn's");
    }

    static void LearnWithSp()
    {
        var unicorn = E("Unicorn");
        int terra = 1, scan = 83, cure2 = 32, restore = 37;
        True(unicorn.UserStatusIds.Contains(terra), "Terra can use Unicorn");
        var p = new EsperProgress { SpBank = 20, Spent = new StatBonus() };
        Eq(EsperRules.LearnResult.Learned, EsperRules.TryLearn(D, ref p, terra, unicorn, scan, false, out var cost), "buy Scan");
        Eq(5, cost, "Scan costs 5");
        Eq(15, p.SpBank, "bank 20 -> 15");
        Eq(EsperRules.LearnResult.Learned, EsperRules.TryLearn(D, ref p, terra, unicorn, cure2, false, out cost), "buy Cure 2");
        Eq(0, p.SpBank, "bank 15 -> 0");
        Eq(EsperRules.LearnResult.NotEnoughSp, EsperRules.TryLearn(D, ref p, terra, unicorn, restore, false, out cost), "Restore: bank empty");
        Eq(0, p.SpBank, "nothing taken");
        p.SpBank = 30;
        Eq(EsperRules.LearnResult.AlreadyKnown, EsperRules.TryLearn(D, ref p, terra, unicorn, scan, true, out _), "known: no charge");
        Eq(EsperRules.LearnResult.NotThisEspersSpell, EsperRules.TryLearn(D, ref p, terra, unicorn, 42, false, out _), "not Unicorn's spell");
        var outsider = D.Espers.First(e => !e.UserStatusIds.Contains(terra) && e.Spells.Length > 0);
        Eq(EsperRules.LearnResult.CantUseEsper,
            EsperRules.TryLearn(D, ref p, terra, outsider, outsider.Spells[0].AbilityId, false, out _), $"Terra can't use {outsider.Name}");
        Eq(EsperRules.LearnResult.NoSpBank, EsperRules.TryLearn(D, ref p, 13, unicorn, scan, false, out _), "Gogo has no SP bank");
        Eq(30, p.SpBank, "refusals never charge");
        // a full bank buys any spell, Ultima included
        var rag = E("Ragnarok");
        int user = rag.UserStatusIds.First(id => id <= D.Rules.MaxElCharacterStatusId);
        Eq(EsperRules.LearnResult.Learned, EsperRules.TryLearn(D, ref p, user, rag, rag.Spells[0].AbilityId, false, out _), "Ultima with 30 SP");
        Eq(0, p.SpBank, "30 -> 0");
    }

    static void LearnHints()
    {
        var unicorn = E("Unicorn");
        var scan = unicorn.SpellByAbility(83);
        var p = new EsperProgress { SpBank = 20, Spent = new StatBonus() };
        Eq("Confirm: learn Scan now for 5 SP (SP 20/30).", EsperRules.LearnHint(D, p, 1, unicorn, scan, false, "Terra"), "affordable");
        p.SpBank = 3;
        Eq("Learn now for 5 SP: not enough banked (SP 3/30).", EsperRules.LearnHint(D, p, 1, unicorn, scan, false, "Terra"), "short");
        Eq("", EsperRules.LearnHint(D, p, 1, unicorn, scan, true, "Terra"), "known: no hint");
        Eq("", EsperRules.LearnHint(D, p, 13, unicorn, scan, false, "Gogo"), "Gogo: no hint");
        Eq("Confirm: equip Unicorn. Pick a spell below to learn it with banked SP (SP 3/30).",
            EsperRules.NameHint(D, p, 1, unicorn, false), "name row");
        Eq("Confirm: remove Unicorn.", EsperRules.NameHint(D, p, 13, unicorn, true), "name row, no bank");
        var full = new EsperProgress { SpBank = 30, Spent = new StatBonus() };
        foreach (var e in D.Espers)
            foreach (var s in e.Spells)
            {
                var hint = EsperRules.LearnHint(D, full, e.UserStatusIds.FirstOrDefault(), e, s, false, "Shadow");
                True(hint.Length < 70, $"{e.Name} {s.Name}: '{hint}' fits one line");
            }
    }

    static void ProcOdds()
    {
        var p = D.Procs;
        Eq(28, p.Weapons.Count, "proc weapons (25 spell procs + 3 Net)");
        Eq(5, p.HighRateContentIds.Length, "High Proc Rate relics");
        foreach (var kv in p.Weapons)
            True(kv.Value.Rate is 25 or 50 && kv.Value.High is 50 or 75 && kv.Value.High >= kv.Value.Rate, $"weapon {kv.Key}: {kv.Value}");
        Eq(25, p.RateFor(15, false), "Flametongue 1 in 4");
        Eq(50, p.RateFor(15, true), "... 1 in 2 with a High Proc Rate relic");
        Eq(50, p.RateFor(55, false), "Fire Rod has High Proc Rate itself");
        Eq(50, p.RateFor(63, false), "Light Brush: brush odds");
        Eq(75, p.RateFor(63, true), "... 3 in 4 with High Proc Rate");
        Eq(-1, p.RateFor(1, true), "no proc: left alone");
    }

    static void HardItemLists()
    {
        HardList L(string member) => D.HardItems.First(h => h.Member == member);
        string N(HardItem[] items) => string.Join(", ", items.Select(i => i.Name));
        Eq("Moogle Charm", N(L("NonEncountItemId").Vanilla), "no-encounter vanilla");
        Eq("Dirty Undies", N(L("NonEncountItemId").Bnw), "no-encounter BNW");
        Eq("Mystery Egg", N(L("EncountDownItemId").Bnw), "half-encounter BNW");
        Eq("", N(L("HealingItemId").Bnw), "walking regen removed");
        // GetAccessoryPhysicalCorrectionValue's dictionary: one list per type (a list with no key, from
        // an older bnw_data.json, rewrote every type with the Atlas Armlet's items)
        HardList P(int key) => D.HardItems.First(h => h.Member == "PhysicalCorrectionAccessoryIds" && h.Key == key);
        Eq(4, D.HardItems.Count(h => h.Member == "PhysicalCorrectionAccessoryIds"), "one list per physical correction type");
        Eq("Offering", N(P(0).Vanilla), "type 0 vanilla");
        Eq("Daryl's Soul", N(P(0).Bnw), "type 0 BNW: Daryl's Soul, the only Offering accessory");
        Eq("Genji Glove", N(P(1).Vanilla), "type 1 vanilla");
        Eq("", N(P(1).Bnw), "type 1 BNW: dual wield is a weapon property");
        Eq("Atlas Armlet, Hero Ring", N(P(2).Vanilla), "physical boost vanilla");
        Eq("Gauntlet", N(P(3).Vanilla), "type 3 vanilla");
        Eq("", N(P(3).Bnw), "type 3 BNW: the grip is a weapon property");
        True(P(2).Bnw.Length > 0 && P(2).Bnw.All(i => i.ItemId >= 93), "type 2 BNW: accessories");
        True(!L("MagicCorrectionAccessoryIds").Bnw.Any(i => i.Name is "Earrings" or "Hero Ring"), "magic boost leaves Earrings/Hero Ring");
        Eq("", N(L("bloodSwordId").Bnw), "no drain weapon in BNW");
        foreach (var h in D.HardItems)
            foreach (var i in h.Vanilla.Concat(h.Bnw))
                True(i.ItemId > 0 && i.ContentId > 0, $"{h.Member} {i.Name}: ids {i.ItemId}/{i.ContentId}");
        Eq(0, D.Passives["exp_x2"].Length, "BNW has no x2 EXP item");
        Eq(0, D.Passives["gil_x2"].Length, "BNW has no x2 gil item");
    }

    static void JoinLevelRule()
    {
        int T(int id, bool first, bool wor, int avg) => JoinRules.Target(id, first, wor, avg);
        // first joins: the average + BNW's offset (0; Gogo -3, Umaro +5), clamped 1-99
        Eq(14, T(2, true, false, 14), "Locke's first join: the average (vanilla +2)");
        Eq(11, T(JoinRules.Gogo, true, false, 14), "Gogo: average - 3");
        Eq(19, T(JoinRules.Umaro, true, false, 14), "Umaro: average + 5");
        Eq(1, T(JoinRules.Gogo, true, false, 2), "never below 1");
        Eq(99, T(JoinRules.Umaro, true, false, 97), "never above 99");
        Eq(21, T(11, true, true, 12), "Mog first met in the World of Ruin: his rejoin floor 21");
        Eq(9, T(JoinRules.Gogo, true, true, 12), "Gogo's first join has no floor");
        // World of Balance rejoins: kept level, but Shadow and Setzer average (the PR's own)
        Eq(0, T(1, false, false, 30), "Terra after the IMTRF: no averaging");
        Eq(0, T(2, false, false, 30), "Locke after the banquet: no averaging");
        Eq(-1, T(JoinRules.Shadow, false, false, 30), "Shadow at Kohlingen: averaged");
        Eq(-1, T(JoinRules.Setzer, false, false, 30), "Setzer: averaged");
        // World of Ruin rejoins: the floor, whatever the average
        Eq(21, T(8, false, true, 40), "Strago at the Cultists' Tower: 21");
        Eq(18, T(6, false, true, 5), "Sabin: 18");
        Eq(19, T(5, false, true, 30), "Edgar: 19");
        Eq(20, T(JoinRules.Setzer, false, true, 30), "Setzer: 20");
        Eq(18, T(7, false, true, 30), "Celes: 18");
        Eq(-1, T(15, false, true, 30), "guests: the PR's own");
    }

    static void ElementShareRule()
    {
        const int N = BattleMath.ElemNormal, W = BattleMath.ElemWeak, H = BattleMath.ElemHalf, X = BattleMath.ElemNull, A = BattleMath.ElemAbsorb;
        int S(params int[] t) => BattleMath.ElementShare(t);
        // one element: the game's own rule
        Eq(N, S(N), "normal"); Eq(W, S(W), "weak"); Eq(H, S(H), "half"); Eq(X, S(X), "immune"); Eq(A, S(A), "absorb");
        // two elements (points / 2 against 2)
        Eq(N, S(W, X), "weak + immune = normal (vanilla: no damage)");
        Eq(N, S(W, H), "weak + resist: 5 / 2 = 2 = normal");
        Eq(W, S(W, N), "weak + normal: 6 / 2 = 3 > 2 = double");
        Eq(H, S(N, X), "normal + immune = half (vanilla: no damage)");
        Eq(H, S(H, X), "resist + immune = half");
        Eq(X, S(X, X), "both immune = no damage");
        Eq(A, S(W, A), "an absorbed element heals");
        Eq(W, S(H, H, W, W), "1 + 1 + 4 + 4 = 10, 5 > 4 = double");
        Eq(N, S(H, H, W), "resist + resist + weak: 6 / 2 = 3 = 3 elements = normal");
        Eq(H, S(N, N, X), "normal + normal + immune: 4 / 2 = 2 < 3 = half");
        // the table the game reads: one element carries the outcome, the other attack elements normal
        var table = new Dictionary<int, int> { [1] = W, [2] = X, [5] = H };
        var view = BattleMath.ElementView(table, new[] { 1, 2 });
        True(view != null && !view.ContainsKey(1) && !view.ContainsKey(2) && view[5] == H, "weak + immune -> both normal, others kept");
        view = BattleMath.ElementView(table, new[] { 2, 3 });
        True(view != null && view[2] == H && !view.ContainsKey(3), "immune + normal -> half on the first");
        view = BattleMath.ElementView(table, new[] { 1, 3 });
        True(view != null && view[1] == W && !view.ContainsKey(3), "weak + normal -> the weak element stays weak");
        True(BattleMath.ElementView(table, new[] { 1 }) == null, "one element: the game's own rule");
        True(BattleMath.ElementView(new Dictionary<int, int> { [1] = A }, new[] { 1, 2 }) == null, "absorb: the game's own rule");
    }

    static void CommandWaitTimes()
    {
        var w = D.Waits;
        Eq(29, w.Commands.Count, "commands with a SNES wait");
        Eq((16, 16), w.Commands[1], "Fight unchanged");
        Eq((32, 32), w.Commands[7], "Magic unchanged");
        Eq((224, 112), w.Commands[30], "Jump halved");
        Eq((16, 0), w.Commands[2], "Defend now instant");
        Eq((16, 32), w.Commands[12], "Bushido doubled");
        Eq((0, 64), w.Commands[8], "Morph (Trance) now waits");
        Eq(9, w.SummonAbilityType, "summons are ability type 9");
        Eq(16, w.SummonVanilla, "Summon vanilla");
        Eq(64, w.SummonBnw, "Summon x4");
    }

    static void SpiritData()
    {
        True(D.SpiritMagicAbilities.Count >= 33, $"{D.SpiritMagicAbilities.Count} Spirit-based abilities (33 spells + proc copies)");
        True(D.SpiritEvadeAbilities.Count >= 57, $"{D.SpiritEvadeAbilities.Count} Spirit-evadable abilities");
        True(D.SpecialMultipliers.Count > 50, $"{D.SpecialMultipliers.Count} special attack ratios");
        foreach (var kv in D.SpecialMultipliers)
            True(kv.Key >= 377 && kv.Value > 0.1 && kv.Value <= 9 && Math.Abs(kv.Value - 1) > 1e-6, $"special {kv.Key}: x{kv.Value}");
        Eq(1.5, D.SpecialMultipliers[379], "Phalanx: BNW x3 over vanilla's x2");
    }

    // c2.asm C23EDD (Shock) and 4914 (the grip), in the SNES's 16-bit steps
    static void ShockAndGrip()
    {
        Eq(3750, BattleMath.Shock(50, 40, 3000, false), "Lv 50, Stamina 40, 3000 HP: 3/4 of 5000");
        Eq(1875, BattleMath.Shock(50, 40, 3000, true), "the same from the back row: halved");
        Eq(25, BattleMath.Shock(99, 120, 9999, false), "Lv 99, Stamina 120, 9999 HP: 3 x 21879 wraps past 65535 to 101 (BNW's own)");
        Eq(16335, BattleMath.Shock(99, 120, 9900, false), "just under the wrap: 3 x 21780 = 65340, / 4");
        Eq(619, BattleMath.Shock(99, 128, 9999, false), "Lv 99, Stamina 128, 9999 HP: 68013 wraps to 2477, / 4");
        Eq(10049, BattleMath.Shock(99, 255, 9999, false), "x past 32767: ASL's carry goes into the ADC");
        Eq(1249, BattleMath.ShockCost(9999), "the user loses max HP / 8");
        Eq(382, BattleMath.Grip(255), "grip: 255 + 127 (Umaro's toss, profiles.py)");
        Eq(150, BattleMath.Grip(100), "grip: x1.5");
        Eq(1, BattleMath.Grip(1), "grip: 1 + 0");
        True(D.WeaponFlagSpells.Count >= 2, $"{D.WeaponFlagSpells.Count} weapon-flag spells (Windslash, Aero and their proc copies)");
        True(D.WeaponFlagSpells.Contains(153) && D.WeaponFlagSpells.Contains(154), "PR 153 !WindSlash, 154 !Aero");
    }

    // c2.asm SketchMag (Magic doubled in 8 bits) and C2123B TrueKnight / EvalKnight
    static void SketchAndCover()
    {
        Eq(60, BattleMath.SketchVigor(60), "Magic 60 stands in for Vigor 60");
        Eq(127, BattleMath.SketchVigor(127), "Magic 127: 254, the top");
        Eq(2, BattleMath.SketchVigor(130), "Magic 130: 260 wraps to 4 (BNW's ASL), so Vigor 2");

        BattleMath.CoverGuardInfo G(bool knight = true, bool back = false, bool dying = false, bool defending = false,
                                    bool blocked = false, int sta = 64, int hp = 1000) =>
            new BattleMath.CoverGuardInfo { Knight = knight, BackRow = back, Dying = dying, Defending = defending, Blocked = blocked, Stamina = sta, Hp = hp };
        Func<int, int> low = n => 0, high = n => n - 1;   // random always 0 / always the top
        var one = new List<BattleMath.CoverGuardInfo> { G() };
        Eq(0, BattleMath.CoverGuard(true, false, one, high), "a Near Fatal target is always covered");
        Eq(-1, BattleMath.CoverGuard(false, false, one, low), "a healthy front-row target never is");
        Eq(0, BattleMath.CoverGuard(false, true, one, low), "a healthy back-row target: random 0 < Stamina 64");
        Eq(-1, BattleMath.CoverGuard(false, true, one, high), "random 191 >= Stamina 64: no cover");
        int seen = -1;
        BattleMath.CoverGuard(false, true, new List<BattleMath.CoverGuardInfo> { G(defending: true) }, n => { seen = n; return 0; });
        Eq(96, seen, "a defending guard rolls 0..95 (twice the chance)");
        Eq(-1, BattleMath.CoverGuard(true, false, new List<BattleMath.CoverGuardInfo> { G(back: true) }, low), "no cover from the back row");
        Eq(-1, BattleMath.CoverGuard(false, true, new List<BattleMath.CoverGuardInfo> { G(dying: true) }, low), "a Near Fatal guard covers only a Near Fatal ally");
        Eq(0, BattleMath.CoverGuard(true, false, new List<BattleMath.CoverGuardInfo> { G(dying: true) }, low), "... which it does");
        Eq(-1, BattleMath.CoverGuard(true, false, new List<BattleMath.CoverGuardInfo> { G(knight: false) }, low), "only the knight's effect covers");
        Eq(-1, BattleMath.CoverGuard(true, false, new List<BattleMath.CoverGuardInfo> { G(blocked: true) }, low), "a blocked guard doesn't");
        var two = new List<BattleMath.CoverGuardInfo> { G(hp: 800), G(hp: 1200), G(hp: 1200, back: true) };
        Eq(1, BattleMath.CoverGuard(true, false, two, low), "the front-row guard with the most HP");
        Eq(1, BattleMath.CoverGuard(true, false, new List<BattleMath.CoverGuardInfo> { G(hp: 900), G(hp: 900) }, low), "equal HP: the later one");
        // the covering knight's Evade (HalveEvasion over vanilla's block value C2/2861)
        Eq(0, BattleMath.CoverEvade(0), "Evade 0 stays 0");
        Eq(0, BattleMath.CoverEvade(1), "Evade 1 -> 0");
        Eq(20, BattleMath.CoverEvade(41), "Evade 41 -> 20");
        Eq(63, BattleMath.CoverEvade(127), "Evade 127 -> 63");
        Eq(63, BattleMath.CoverEvade(128), "Evade 128 -> 63 (the block value bottoms out at 1)");
    }

    static void BonusLines()
    {
        Eq("EL: +2 Vigor   Equip: Halves Bolt damage", EsperRules.BonusLine(D, E("Ramuh")), "format");
        foreach (var e in D.Espers.Where(e => e.Name != "Loki"))
            True(EsperRules.BonusLine(D, e).Length is > 10 and < 70, $"{e.Name}: '{EsperRules.BonusLine(D, e)}'");
    }

    // BNW's monster images (the kit's PrKit/MonsterImages.cs) made from this PC's game into a scratch folder:
    // every image the recipe names is made, with the pixels the porter made (its sha1), as a PNG
    // + .spriteData + keys/Export.json per group; a second run only checks the marker.
    // FF6PR_GAME (else the default Steam library) is the game; skipped without the game or a recipe.
    static void MonsterImagesFromGame()
    {
        var recipe = MonsterImageRecipe.FromJson(File.ReadAllText(DataPath), "monster_images");
        var game = Environment.GetEnvironmentVariable("FF6PR_GAME") ?? @"D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR";
        if (recipe == null || !Directory.Exists(Path.Combine(game, MonsterImages.BundleDir)))
        {
            Console.WriteLine("      (skipped: " + (recipe == null ? "no monster_images in bnw_data.json" : "no game at " + game) + ")");
            return;
        }
        var dir = Path.Combine(Path.GetTempPath(), "bnw_monster_images_test");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        var warnings = new List<string>();
        var line = MonsterImages.Ensure(game, dir, recipe, warnings.Add);
        Console.WriteLine("      " + line);
        Eq(0, warnings.Count, "warnings (" + string.Join("; ", warnings) + ")");
        foreach (var img in recipe.Images)
        {
            var png = MonsterImages.PngPath(dir, img);
            True(File.Exists(png) && File.Exists(Path.ChangeExtension(png, ".spriteData")), img.Key + " written");
            var b = File.ReadAllBytes(png);
            True(b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G', img.Key + " is a PNG");
            True(File.ReadAllText(Path.Combine(dir, img.Group, "keys", "Export.json")).Contains("/" + img.Key + "\""), img.Key + " in its group's keys");
            if (img.Thumb == null) continue;
            True(File.Exists(MonsterImages.ThumbPngPath(dir, img.Thumb)), img.Thumb.Group + " thumbnail written");
            True(File.ReadAllText(Path.Combine(dir, img.Thumb.Group, "keys", "Export.json")).Contains("/Thumbnail\""), img.Thumb.Group + " keys");
        }
        True(MonsterImages.UpToDate(dir, recipe), "marker");
        True(MonsterImages.Ensure(game, dir, recipe, warnings.Add).EndsWith("up to date"), "second start skips");
        // the final battle tower: its statue parts are layers the plugin erases when they die; without them
        // the picture differs only where a part is (BnwMonsterImages.Track)
        var tower = recipe.Images.FirstOrDefault(i => i.Parts != null && i.Parts.Count > 0);
        if (tower == null) return;
        var cache = new Dictionary<string, Dictionary<string, BundleTexture>>();
        var full = MonsterImages.Build(game, tower, cache, warnings.Add);
        var bare = MonsterImages.BuildBase(game, tower, cache, warnings.Add);
        var under = new bool[tower.W * tower.H];
        foreach (var part in tower.Parts)
        {
            var layer = MonsterImages.PartLayer(tower, part);
            int n = 0;
            for (int k = 0; k < under.Length; k++) if (layer[4 * k + 3] != 0) { under[k] = true; n++; }
            True(n > 20, $"part {part.Monster}: {n} pixels");
        }
        int outside = 0, inside = 0;
        for (int k = 0; k < under.Length; k++)
        {
            bool same = full[4 * k] == bare[4 * k] && full[4 * k + 1] == bare[4 * k + 1] && full[4 * k + 2] == bare[4 * k + 2];
            if (!under[k] && !same) outside++;
            if (under[k] && !same) inside++;
        }
        Eq(0, outside, "tower: pixels that differ without the parts outside them");
        True(inside > 1000, $"tower: {inside} pixels change when the parts go");
    }
    // BNW's waits at its own ATB rate (Rules/BnwAtb.cs): worked by hand from c2.asm C2/09D2 and C2/1193 (wait timer
    // += ($3AC8 = 75 x (Speed + 51) / 16) / 2 a tick, done at wait x 256; 30.0494 ticks a second)
    static void AtbWaits()
    {
        BnwAtb.UseBnwNumbers();
        Eq(379, PrKit.Ff6.SnesAtbRules.AtbAdd(30, false, 2, false, false), "a Speed 30 character's add: 75 x 81 / 16");
        Eq(350, PrKit.Ff6.SnesAtbRules.AtbAdd(30, true, 2, false, false), "a Speed 30 monster's add: 112 x 50 / 16");
        Eq(393, PrKit.Ff6.SnesAtbRules.AtbAdd(33, false, 2, false, false), "a Speed 33 character's add: 75 x 84 / 16");
        const double Tps = 60.0988 / 2;
        void Secs(double ticks, int wait, int speed, bool haste, bool slow, string what)
        {
            double got = BnwAtb.Seconds(wait, speed, haste, slow);
            True(Math.Abs(got - ticks / Tps) < 1e-9, $"{what}: expected {ticks / Tps:0.0000} s ({ticks} ticks), got {got:0.0000}");
        }
        Secs(21, 16, 33, false, false, "Fight at Speed 33: 4096 / 196 -> 21 ticks (0.70 s; the PR's own chant 1.64 s)");
        Secs(42, 32, 33, false, false, "Magic at Speed 33: 8192 / 196 -> 42 ticks");
        Secs(84, 64, 33, false, false, "Summon at Speed 33: 16384 / 196 -> 84 ticks");
        Secs(147, 112, 33, false, false, "Jump's air time at Speed 33: 28672 / 196 -> 147 ticks");
        Secs(18, 16, 33, true, false, "Fight with Haste: 90 x 84 / 16 = 472, 4096 / 236 -> 18 ticks");
        Secs(27, 16, 33, false, true, "Fight with Slow: 60 x 84 / 16 = 315, 4096 / 157 -> 27 ticks");
        Secs(27, 16, 33, true, true, "Slow wins over Haste");
        Secs(0, 0, 33, false, false, "Steal: no wait");
        var t = D.Waits;
        int w;
        Eq(BnwAtb.Wait.Timed, BnwAtb.WaitFor(t, 0, t.FightCommandId, false, false, false, false, false, false, out w), "Fight waits");
        Eq(16, w, "Fight's wait $10");
        Eq(BnwAtb.Wait.Timed, BnwAtb.WaitFor(t, t.SummonAbilityType, 7, false, false, false, false, false, false, out w), "a summon waits");
        Eq(64, w, "Summon's wait $40 (the summon's own, not Magic's)");
        Eq(BnwAtb.Wait.Timed, BnwAtb.WaitFor(t, PrKit.Ff6.SnesAtbRules.LandingAbility, BnwAtb.JumpCommand, false, false, false, false, false, false, out w), "Jump's landing");
        Eq(112, w, "Jump's air time $70 (vanilla $E0)");
        Eq(BnwAtb.Wait.Now, BnwAtb.WaitFor(t, PrKit.Ff6.SnesAtbRules.UpAtOnceAbility, BnwAtb.JumpCommand, false, false, false, false, false, false, out w), "Jump goes up at once");
        Eq(BnwAtb.Wait.Game, BnwAtb.WaitFor(t, 0, t.FightCommandId, true, false, false, false, false, false, out w), "a monster: the game's");
        Eq(BnwAtb.Wait.Now, BnwAtb.WaitFor(t, 0, t.FightCommandId, false, false, true, false, false, false, out w), "a Berserk/Muddle turn: at once");
        Eq(BnwAtb.Wait.Timed, BnwAtb.WaitFor(t, 0, 7, false, false, false, false, false, true, out w), "a Mimic: Mimic's wait");
        Eq(0, w, "BNW's Mimic: no wait");
        Eq(BnwAtb.Wait.Unknown, BnwAtb.WaitFor(t, 0, 999, false, false, false, false, false, false, out w), "a command the table lacks");
    }
    // the lists AuditRules reads (bnw_plugin_data from BNW's ROM; the asm rules in AuditRules.cs)
    static void AuditData()
    {
        var first = D.StaminaFirstAbilities;
        foreach (int id in new[] { 62, 63, 66, 147, 185, 315, 334, 342, 349, 351, 361, 368, 369 })
            True(first.Contains(id), $"Stamina-only evasion for ability {id} (StamPhase: fractional / MP / North Cross)");
        Eq(8, D.BossFractional.TryGetValue(62, out var demi) ? demi : -1, "Demi's power on a boss");
        Eq(12, D.BossFractional.TryGetValue(63, out var quartr) ? quartr : -1, "Quartr's power on a boss");
        True(!D.BossFractional.ContainsKey(342), "Launcher (special effect $2C) keeps the PR's boss rule");
        Eq("124 127 130 132 133 139", string.Join(" ", D.RespectRowAbilities.OrderBy(x => x)), "BNW's physical respect-row skills");
        True(D.DieAtZeroMp.Count >= 47, $"monsters dying at 0 MP: {D.DieAtZeroMp.Count} (47 SNES monsters + Coliseum copies)");
    }
    // the lists BattleItems reads (bnw_plugin_data.proc_rules, port_procs' copies; c4.asm SpellCastId / CastTarget)
    static void BattleItemsData()
    {
        var p = D.Procs;
        Eq("159 161", string.Join(" ", p.ThrowSpread.OrderBy(x => x)), "stars a Throw may spread (Shuriken, Ninja Star content ids)");
        Eq(1, p.CritSwap.Count, "one MP-crit swap (Doom's proc copy -> X-Zone's)");
        int xzone = p.CritSwap.Values.First();
        True(p.CritAllEnemies.Contains(xzone), "X-Zone's copy hits all enemies");
        Eq(2, p.CritAllEnemies.Count, "Quartr's and X-Zone's copies hit all enemies");
        True(p.CritAllEnemies.Any(c => p.NoCrit.Contains(c)), "Quartr's copy is never critical (spell byte 7 bit 5)");
        Eq(3, p.NoCrit.Count, "never-critical proc copies: Holy, Flare, Quartr");
    }
    // BNW's equip-menu check as the asm reads it (c3.asm Item_Chk, C3/F13D); flags = weapon byte 12 & $18
    static bool DwChk(int held, int picked)
    {
        if (held == 0x08) return false;               // only "Gauntlet" (a spear): no off-hand weapon
        if (held == 0x10) return picked != 0x08;      // only "Genji Glove": anything but a spear
        return picked == 0x10;                        // neither or both: only a dual-wield weapon
    }

    // BNW's in-battle swap (c1.asm FlagDual / SwapDual / CanDual): $18 sets neither bit; "block" on either
    // weapon or no "enable" on both = no dual wield
    static bool SwapDual(int a, int b)
    {
        int Bits(int f) => f == 0x18 ? 0 : f >> 3;    // bit 1 enable, bit 0 block
        int m = Bits(a) | Bits(b);
        return (m & 1) == 0 && (m & 2) != 0;
    }

    static void DualWieldPairs()
    {
        WeaponGrip G(int f) => DualWieldRules.Grip((f & 0x08) != 0, (f & 0x10) != 0);
        var flags = new[] { 0x00, 0x08, 0x10, 0x18 };
        foreach (var a in flags)
            foreach (var b in flags)
            {
                Eq(DwChk(a, b), DualWieldRules.MayPair(G(a), G(b)), $"menu: ${a:X2} held, ${b:X2} picked");
                Eq(SwapDual(a, b), DualWieldRules.MayPair(G(a), G(b)), $"battle swap: ${a:X2} + ${b:X2}");
            }
        Eq(WeaponGrip.Spear, G(0x08), "Mythril Pike's bits");
        Eq(false, DualWieldRules.MayPair(WeaponGrip.DualWield, WeaponGrip.Spear), "no spear beside a dagger");
        Eq(false, DualWieldRules.MayPair(WeaponGrip.Spear, WeaponGrip.DualWield), "nothing beside a spear");
        Eq(false, DualWieldRules.MayPair(WeaponGrip.Both, WeaponGrip.Both), "no two katanas");
        Eq(false, DualWieldRules.MayPair(WeaponGrip.Both, WeaponGrip.None), "no plain sword beside a katana");
        Eq(true, DualWieldRules.MayPair(WeaponGrip.Both, WeaponGrip.DualWield), "a dagger beside a katana");
        Eq(true, DualWieldRules.MayPair(WeaponGrip.DualWield, WeaponGrip.None), "a plain sword beside a dagger");
    }

    static void NoGainzRewards()
    {
        Eq(1000, RewardRules.Exp(1000, false, false), "EXP normally");
        Eq(0, RewardRules.Exp(1000, true, false), "NoGainz: no EXP");
        Eq(0, RewardRules.Exp(1000, false, true), "Veldt: no EXP");
        Eq(500, RewardRules.Gil(500, false, false), "gil normally");
        Eq(1000, RewardRules.Gil(500, true, false), "NoGainz: gil x2");
        Eq(1000, RewardRules.Gil(500, false, true), "Veldt: gil x2");
        Eq(1000, RewardRules.Gil(500, true, true), "both: x2 once (one ASL)");
        Eq(int.MaxValue / 2, RewardRules.Gil(int.MaxValue, true, false), "no overflow");
        // EP follows EXP (Calc_EP: XP/8 x SP); the SP bank still fills (Add_EP from C2/6235)
        var p = new EsperProgress();
        Eq(0, EsperRules.AfterBattle(D, ref p, 1, true, RewardRules.Exp(4000, true, false), 3), "no EL");
        Eq(0, p.Ep, "no EP");
        Eq(3, p.SpBank, "SP banked");
    }

    // ---- the test function for Rules.Tests/Program.cs (Test("Ultros's EL respec (RespecELs)", ElRespec)) ----
    static void ElRespec()
    {
        var p = EsperProgress.Empty;
        p.Ep = 12345; p.El = 9; p.ElBank = 2; p.SpBank = 17;
        p.Spent = new StatBonus { Hp = 120, Mp = 15, Power = 3, Agility = 1, Vitality = 2, Magic = 1 };
        var r = RespecRules.Respec(p);
        Eq(9, r.ElBank, "every EL earned back in the bank");
        Eq(9, r.El, "EL kept");
        Eq(12345, r.Ep, "EP kept");
        Eq(17, r.SpBank, "SP bank kept");
        True(r.Spent != null && r.Spent.IsEmpty, "no bonuses left");
        Eq(120, p.Spent.Hp, "the original is untouched");
        var w = r.Pack();
        var back = EsperProgress.Unpack(w[0], w[1], w[2]);
        Eq(9, back.ElBank, "packs: bank");
        True(back.Spent.IsEmpty, "packs: nothing spent");
        Eq(25000, RespecRules.Price(false), "first respec 25,000 GP (CB/4E7D)");
        Eq(100000, RespecRules.Price(true), "later ones 100,000 GP (CB/4E8D: 60,000 + 40,000)");
        True(RespecRules.Respecs(1) && RespecRules.Respecs(12), "Terra and Gau respec");
        True(!RespecRules.Respecs(13) && !RespecRules.Respecs(14) && !RespecRules.Respecs(0), "Gogo, Umaro and guests don't");
    }

}
