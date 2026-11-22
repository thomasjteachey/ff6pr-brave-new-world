using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Last.Battle;
using Last.Battle.Function;
using Last.Data;
using Last.Data.Master;
using Last.Data.User;
using Last.Management;
using Last.Systems;
using UnityEngine;
using Harness = PrKit.Harness;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW's informative misses (c2.asm "Informative Miss", Bropedio's inform-miss-3.asm): a status
    // attack that changes nothing shows
    //   "Null"  when the target is immune to every status it would set (BNW's MaybeNull; the
    //           instant-death and fractional immunities are statuses the PR's monsters resist too),
    //   "Fail"  when the target was open to a status but evaded it (BNW's Stamina evasion, which
    //           CommandRules' Spirit evasion makes a forced miss: StamEvdChk / StatusHelp),
    //   "Miss"  otherwise (hit roll, status already there, the PR's own special refusals).
    // The PR decides a status per target in AddConditionFunction.Calc / AddConditionMultiFunction
    // .Calc (decompiled tools/decomp/bnw_ui): IsAddable, then the serial provider's
    // CheckForceMissCondition (forced miss), then the target's resistance list (1 = resists); a
    // miss of all of them makes the function's result HitType.Miss. Here each such miss is sorted
    // as BNW does (Fail before Null before Miss) and kept per target; when the game makes that
    // target's miss popup (BattleUtility.CreateDamageView(unit, .., isMiss) -> ViewDamageEntity
    // .ViewMiss, which sets the "miss_sprite" image and scales it to the sprite's size in pixels),
    // the image gets a "FAIL" or "NULL" drawn in the Miss sprite's own style: 7-pixel white
    // letters (240,240,240), a 1-pixel dark outline (24,24,40), 1 pixel between letters. English
    // only (BNW's words; the other languages keep their own Miss).
    static class BattleUi
    {
        internal static bool Enabled = true;
        internal enum Kind { Miss, Null, Fail }   // BNW's order: Fail wins over Null, Null over Miss
        const int English = 2;
        static readonly HashSet<int> PrSpecialRefusals = new HashSet<int> { 601, 515, 605 };   // Imp, Control, WeakMaker
        const float KeepSeconds = 30f;   // a classification waits this long for its popup (summons animate long)

        // per target parameter: the act's sort of its status misses (Hit: a status function hit it)
        static readonly Dictionary<IntPtr, (Kind Kind, bool Hit, float At, int Frame)> kinds = new Dictionary<IntPtr, (Kind, bool, float, int)>();
        static IntPtr calcTarget;
        static bool calcActive, calcFail, calcNull;
        static Kind? pending;   // the kind for the miss popup being made
        internal static int Shown;   // FAIL/NULL popups drawn (tests)

        public static void Apply(Harmony h, ConfigFile config)
        {
            Enabled = config.Bind("Battle", "InformativeMiss", true,
                "BNW's informative misses: a status attack shows Null on an immune target and Fail when the status was evaded (English).").Value;
            Hooks.Patch(h, typeof(AddConditionFunction), "Calc", typeof(BattleUi), prefix: nameof(Calc_Prefix), postfix: nameof(Calc_Postfix));
            Hooks.Patch(h, typeof(AddConditionMultiFunction), "Calc", typeof(BattleUi), prefix: nameof(Calc_Prefix), postfix: nameof(Calc_Postfix));
            // after CommandRules' postfix on the same method (Plugin.cs applies CommandRules first)
            Hooks.Patch(h, typeof(Serial.FF6.Battle.AddConditionFunctionSerial), "CheckForceMissCondition", typeof(BattleUi),
                postfix: nameof(ForceMiss_Postfix));
            Hooks.Patch(h, typeof(BattleUtility), "CreateDamageView", typeof(BattleUi), prefix: nameof(View_Prefix), postfix: nameof(View_Postfix),
                args: new[] { typeof(BattleUnitData), typeof(int), typeof(bool), typeof(bool) });
            Hooks.Patch(h, typeof(ViewDamageEntity), "ViewMiss", typeof(BattleUi), postfix: nameof(ViewMiss_Postfix));
            // a new act: the last one's sorts go (its popups are made during its own animation)
            Hooks.Patch(h, typeof(Serial.FF6.Calc.FunctionProvider), "CreateActFunction", typeof(BattleUi), prefix: nameof(Act_Prefix));
            Harness.Register("bnwui", Command, "bnwui miss | show <enemy> <fail|null|miss> | popup <fail|null|miss> [enemy] | gau | rages | " +
                "status <slow|haste|stop> <party slot>: BNW's battle UI self-tests (Fail/Null, Rage list, ATB colours)");
            Harness.Register("gauturn", (a, tick) => Turn(Gau, tick), "gauturn: in battle, only Gau's gauge fills until his command menu opens");
            Harness.Register("turn", (a, tick) => Turn(a.Length > 0 && int.TryParse(a[0], out var c) ? c : Gau, tick),
                "turn <character status id>: in battle, only that character's gauge fills until its command menu opens");
        }

        // --- the status calc ------------------------------------------------------------------------

        public static void Act_Prefix() => kinds.Clear();

        public static void Calc_Prefix(CharacterParameterBase targetParameter)
        {
            calcActive = Enabled && targetParameter != null;
            calcTarget = targetParameter?.Pointer ?? IntPtr.Zero;
            calcFail = calcNull = false;
        }

        public static void ForceMiss_Postfix(CharacterParameterBase targetParameter, Condition condition, bool __result)
        {
            if (!calcActive || targetParameter == null || condition == null || targetParameter.Pointer != calcTarget) return;
            try
            {
                if (__result)
                {
                    if (!PrSpecialRefusals.Contains(condition.ConditionType)) calcFail = true;   // BNW's Spirit evasion
                    return;
                }
                // not forced: the resistance list decides next (AddConditionFunction.Calc); 1 = resists
                var res = targetParameter.ConfirmedResistanceCondtionList();
                if (res != null && res.ContainsKey(condition.Id) && res[condition.Id] == 1) calcNull = true;
            }
            catch (Exception e) { Hooks.WarnOnce("BattleUi.ForceMiss", e); }
        }

        public static void Calc_Postfix(FunctionBase __instance, CharacterParameterBase targetParameter)
        {
            if (!calcActive) return;
            calcActive = false;
            try
            {
                if (targetParameter == null) return;
                var key = targetParameter.Pointer;
                bool hit = __instance.calcResult?.GetHitType() != HitType.Miss;
                var kind = hit ? Kind.Miss : calcFail ? Kind.Fail : calcNull ? Kind.Null : Kind.Miss;
                // several status functions of one act (one frame): BNW sorts them together, and a
                // status that landed means the act didn't miss there
                if (kinds.TryGetValue(key, out var was) && was.Frame == Time.frameCount)
                {
                    hit |= was.Hit;
                    if (was.Kind > kind) kind = was.Kind;
                }
                kinds[key] = (kind, hit, Time.realtimeSinceStartup, Time.frameCount);
                if (hit) return;
                if (kind != Kind.Miss)
                    Hooks.NoteOnce("miss-" + kind, () => $"[battle ui] a status miss sorted as {kind.ToString().ToUpper()} (BNW's informative miss)");
            }
            catch (Exception e) { Hooks.WarnOnce("BattleUi.Calc", e); }
        }

        internal static Kind? KindFor(BattleUnitData unit)
        {
            if (unit == null) return null;
            var keys = new List<IntPtr>();
            var p = unit.BattleUnitDataInfo?.Parameter;
            if (p != null) keys.Add(p.Pointer);
            var owned = unit.TryCast<BattlePlayerData>()?.ownedCharacterData?.Parameter;
            if (owned != null) keys.Add(owned.Pointer);
            foreach (var k in keys)
                if (kinds.TryGetValue(k, out var v) && Time.realtimeSinceStartup - v.At <= KeepSeconds)
                {
                    kinds.Remove(k);
                    return v.Hit ? null : v.Kind;
                }
            return null;
        }

        // --- the popup ------------------------------------------------------------------------------

        public static void View_Prefix(BattleUnitData targetUnitData, bool isMiss)
        {
            pending = null;
            if (!Enabled || !isMiss) return;
            try
            {
                if ((int)(MessageManager.Instance?.currentLanguage ?? 0) != English) return;
                var k = forced ?? KindFor(targetUnitData);
                if (k == Kind.Fail || k == Kind.Null) pending = k;
            }
            catch (Exception e) { Hooks.WarnOnce("BattleUi.View", e); }
        }

        public static void View_Postfix() => pending = null;

        public static void ViewMiss_Postfix(ViewDamageEntity __instance)
        {
            if (pending == null) return;
            var kind = pending.Value;
            pending = null;
            try
            {
                var image = __instance.prefab?.transform?.GetChild(0)?.GetComponent<UnityEngine.UI.Image>();
                var sprite = SpriteFor(kind);
                if (image == null || sprite == null) return;
                image.sprite = sprite;
                var rt = image.rectTransform;
                var z = rt.localScale.z;
                rt.localScale = new Vector3(sprite.rect.width, sprite.rect.height, z);   // as ViewMiss: the sprite's size in pixels
                Shown++;
                Hooks.NoteOnce("popup-" + kind, () => $"[battle ui] {kind.ToString().ToUpper()} shown in place of Miss ({sprite.rect.width}x{sprite.rect.height})");
            }
            catch (Exception e) { Hooks.WarnOnce("BattleUi.ViewMiss", e); }
        }

        // 7 rows per letter, '#' = a letter pixel
        static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { "#", "#", "#", "#", "#", "#", "#" },
            ['L'] = new[] { "#...", "#...", "#...", "#...", "#...", "#...", "####" },
            ['N'] = new[] { "#...#", "##..#", "#.#.#", "#.#.#", "#..##", "#...#", "#...#" },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        };
        static readonly Dictionary<Kind, Sprite> sprites = new Dictionary<Kind, Sprite>();

        static Sprite SpriteFor(Kind kind)
        {
            if (sprites.TryGetValue(kind, out var s) && s != null) return s;
            string word = kind == Kind.Fail ? "FAIL" : "NULL";
            var mask = Mask(word, out int w, out int h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var fill = new Color32(240, 240, 240, 255);
            var edge = new Color32(24, 24, 40, 255);
            var clear = new Color32(0, 0, 0, 0);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool on = mask[y, x], near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int yy = y + dy, xx = x + dx;
                            if (yy >= 0 && yy < h && xx >= 0 && xx < w && mask[yy, xx]) { near = true; break; }
                        }
                    // the texture's rows go bottom-up
                    pixels[(h - 1 - y) * w + x] = on ? fill : near ? edge : clear;
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            tex.hideFlags = HideFlags.HideAndDontSave;   // kept across battles (unused-asset sweeps)
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = "BNW_" + word;
            s.hideFlags = HideFlags.HideAndDontSave;
            sprites[kind] = s;
            return s;
        }

        // the word's letter pixels with a 1-pixel border for the outline: [row, column]
        internal static bool[,] Mask(string word, out int w, out int h)
        {
            h = 9;
            w = 2 + word.Sum(c => Glyphs[c][0].Length) + word.Length - 1;
            var m = new bool[h, w];
            int x0 = 1;
            foreach (var c in word)
            {
                var g = Glyphs[c];
                for (int y = 0; y < 7; y++)
                    for (int x = 0; x < g[y].Length; x++)
                        if (g[y][x] == '#') m[y + 1, x0 + x] = true;
                x0 += g[0].Length + 1;
            }
            return m;
        }

        // --- harness self-tests ---------------------------------------------------------------------

        static Kind? forced;   // popup test: the kind for the next popup

        static string Command(string[] a)
        {
            var what = a.Length > 0 ? a[0] : "";
            try
            {
                switch (what)
                {
                    case "miss": return MissSelfTest();
                    case "popup": return Popup(a.Length > 1 ? a[1] : "fail", a.Length > 2 && int.TryParse(a[2], out var n) ? n : 0);
                    case "show": return Show(a.Length > 1 && int.TryParse(a[1], out var e) ? e : 0, a.Length > 2 ? a[2] : "null");
                    case "gau": return GauJoin();
                    case "join": return Join(a.Length > 1 && int.TryParse(a[1], out var j) ? j : Gau, a.Length > 2 && int.TryParse(a[2], out var sl) ? sl : -1);
                    case "rages": return Rages();
                    case "bars": return WaitGauge.Bars();
                    case "status": return Status(a.Length > 1 ? a[1] : "haste", a.Length > 2 && int.TryParse(a[2], out var s) ? s : 0);
                    default: return "bnwui miss | show <enemy> <fail|null|miss> | popup <fail|null|miss> [enemy] | gau | rages | status <slow|haste|stop> <slot>";
                }
            }
            catch (Exception e) { return $"error: {e.GetType().Name}: {e.Message}"; }
        }

        // One act of `ab` by the first party member on `enemy`, through the game's functions (nothing
        // applied): the game's hit type there, and this file's sort of a miss ("FAIL", "NULL", "Miss").
        // keep: leave the sort for the next popup on that monster.
        static string RunStatusAct(Ability ab, BattleUnitData caster, BattleUnitData enemy, bool keep)
        {
            try { return RunStatusActOnce(ab, caster, enemy, keep); }
            catch (Exception e)
            {
                Hooks.NoteOnce("ui-act-" + ab.Id, () => $"[battle ui] ability {ab.Id} not run: {e.Message}");
                return "none";
            }
        }

        static string RunStatusActOnce(Ability ab, BattleUnitData caster, BattleUnitData enemy, bool keep)
        {
            var fns = ParameterActFunctionManagment.GetFunction(ab);
            if (fns == null || fns.Count == 0) return "none";
            var mm = MasterManager.Instance;
            var abilities = new Il2CppSystem.Collections.Generic.List<Ability>();
            abilities.Add(ab);
            var targets = new Il2CppReferenceArray<BattleUnitData>(1);
            targets[0] = enemy;
            var act = new BattleActData(caster, targets, mm.GetData<Command>(7), abilities, BattleActData.ActType.Normal,
                (BattleActData.TargetType)0, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
            var results = new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>();
            var attacks = new Il2CppSystem.Collections.Generic.Dictionary<BattleUnitData, ICalcResult>();
            var tp = enemy.BattleUnitDataInfo?.Parameter;
            if (tp != null) kinds.Remove(tp.Pointer);
            string got = "none";
            foreach (var f in fns)
            {
                if (f == null) continue;
                f.Init(act, results, attacks);
                f.FixedTarget();
                f.HitCalc();
                var dic = f.ICalcResultDic ?? results;
                if (dic.ContainsKey(enemy)) got = dic[enemy].GetHitType().ToString();
            }
            if (got == "Miss" && tp != null && kinds.TryGetValue(tp.Pointer, out var k) && !k.Hit && k.Kind != Kind.Miss) got = k.Kind.ToString().ToUpper();
            if (!keep && tp != null) kinds.Remove(tp.Pointer);
            return got;
        }

        static IEnumerable<Ability> StatusOnly()
        {
            foreach (var ab in MasterManager.Instance.GetList<Ability>().Values)
                if (ab != null && ab.Id >= 1 && ab.Id <= 160 && ab.StandardValue == 0 && ab.BattleFunctionGroupId != 0 && ab.ConditionGroupId != 0)
                    yield return ab;
        }

        // The game's popup for a real status miss on a monster: an ability whose act there the game
        // misses and this file sorts as `want` (up to 60 tries each), then the game's own miss popup
        // (BattleUtility.CreateDamageView, as BattleBasicFunction makes it), drawn from that sort.
        static string Show(int index, string want)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var players = battle.GetPlayerUnits();
            var enemies = battle.GetEnemyUnits();
            if (players == null || players.Count == 0 || enemies == null || enemies.Count == 0) return "error: no party or monster";
            var enemy = enemies[Math.Clamp(index, 0, enemies.Count - 1)];
            string target = want == "fail" ? "FAIL" : want == "null" ? "NULL" : "Miss";
            foreach (var ab in StatusOnly())
            {
                for (int i = 0; i < 60; i++)
                {
                    var got = RunStatusAct(ab, players[0], enemy, true);
                    if (got == "none" || got == "Hit" || got == "Critical") break;   // not a status miss here
                    if (got != target) continue;
                    int before = Shown;
                    BattleUtility.CreateDamageView(enemy, 0, false, true);
                    string name = MessageManager.Instance.GetMessage(Content(ab.Id)?.MesIdName ?? "", false);
                    return $"show {want} on monster {index} ({enemy.TryCast<BattleEnemyData>()?.GetMonster()?.Id}): ability {ab.Id} " +
                           $"{name?.Replace("<IC_", "<")} missed as {got} on try {i + 1}; popup drawn as " +
                           $"{(Shown > before ? target : "the game's Miss")}";
                }
            }
            return $"show {want} on monster {index}: no ability missed that way";
        }

        // In a battle: status-only abilities run on the first monster the way the game runs an
        // act's functions (GetFunction, Init, FixedTarget, HitCalc; nothing applied), each 40
        // times: what the game made of each try and how this file sorted its misses.
        static string MissSelfTest()
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var players = battle.GetPlayerUnits();
            var enemies = battle.GetEnemyUnits();
            if (players == null || players.Count == 0 || enemies == null || enemies.Count == 0) return "error: no party or monster";
            var parts = new List<string>();
            int nullSeen = 0, failSeen = 0;
            for (int n = 0; n < enemies.Count && n < 4; n++)
            {
                var enemy = enemies[n];
                var res = enemy.BattleUnitDataInfo?.Parameter?.ConfirmedResistanceCondtionList();
                var resisted = new List<int>();
                if (res != null) foreach (var kv in res) if (kv.Value == 1) resisted.Add(kv.Key);
                resisted.Sort();
                var rows = new List<string>();
                int abilities = 0;
                foreach (var ab in StatusOnly())
                {
                    var c = new Dictionary<string, int>();
                    for (int i = 0; i < 40; i++)
                    {
                        var got = RunStatusAct(ab, players[0], enemy, false);
                        c[got] = c.TryGetValue(got, out var x) ? x + 1 : 1;
                        if (got == "none") break;
                    }
                    if (c.ContainsKey("none")) continue;
                    abilities++;
                    if (c.ContainsKey("FAIL")) failSeen++;
                    if (c.ContainsKey("NULL")) nullSeen++;
                    if (c.ContainsKey("FAIL") || c.ContainsKey("NULL"))
                        rows.Add($"{ab.Id}: " + string.Join(" ", c.Select(kv => $"{kv.Key} {kv.Value}")));
                }
                parts.Add($"monster {n} ({enemy.TryCast<BattleEnemyData>()?.GetMonster()?.Id}, resists [{string.Join(" ", resisted)}]): " +
                          $"{abilities} status abilities, {rows.Count} with FAIL/NULL: {string.Join(", ", rows.Take(12))}");
            }
            return $"[battle ui] miss self-test {(nullSeen > 0 && failSeen > 0 ? "ok" : "INCOMPLETE")}: abilities with NULL {nullSeen}, " +
                   $"with FAIL {failSeen} | " + string.Join(" | ", parts);
        }

        static Content Content(int abilityId)
        {
            foreach (var c in MasterManager.Instance.GetList<Content>().Values)
                if (c != null && c.TypeId == 4 && c.TypeValue == abilityId) return c;
            return null;
        }

        // The game's own miss popup on a monster, as a FAIL / NULL / plain miss.
        static string Popup(string kind, int index)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var enemies = battle.GetEnemyUnits();
            if (enemies == null || enemies.Count == 0) return "error: no monster";
            var unit = enemies[Math.Clamp(index, 0, enemies.Count - 1)];
            forced = kind == "null" ? Kind.Null : kind == "fail" ? Kind.Fail : Kind.Miss;
            int before = Shown;
            try { BattleUtility.CreateDamageView(unit, 0, false, true); }
            finally { forced = null; }
            return $"popup {kind} on monster {index}: {(Shown > before ? "drawn as " + kind.ToUpper() : "the game's Miss")}";
        }

        // A tick handler: in battle, every other party member's gauge held empty and Gau's full until
        // his command menu opens (the KeyInput BattleMenuController's selected character).
        static string Turn(int who, int tick)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return tick > 600 ? "error: no battle" : null;
            var menu = UnityEngine.Object.FindObjectOfType<Last.UI.KeyInput.BattleMenuController>();
            var sel = menu?.SelectedTarget;
            if (sel?.ownedCharacterData?.characterStatusId == who && menu.IsShow) return $"{sel.ownedCharacterData.Name}'s menu is open (tick {tick})";
            var atb = battle.BattleProgress?.TryCast<BattleProgressATB>();
            var players = battle.GetPlayerUnits();
            if (atb == null || players == null) return null;
            bool there = false;
            foreach (var p in players)
            {
                bool mine = p.TryCast<BattlePlayerData>()?.ownedCharacterData?.characterStatusId == who;
                there |= mine;
                atb.ChangeATBGaugeByUnitData(p, mine ? 100f : 0f);
            }
            return there ? null : $"error: character {who} isn't in the battle";
        }

        static string Status(string which, int slot)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var players = battle.GetPlayerUnits();
            if (players == null || slot < 0 || slot >= players.Count) return "error: no such party member";
            var type = which == "slow" ? Last.Defaine.ConditionType.Slow : which == "stop" ? Last.Defaine.ConditionType.Stop : Last.Defaine.ConditionType.Heist;
            battle.BattleConditionController?.InterruptAddCondition(players[slot], type);
            var now = players[slot].BattleUnitDataInfo?.GetConfirmedConditionType();
            return $"{which} on party member {slot}: {(now != null && now.Contains(type) ? "set" : "NOT set")}";
        }

        // A character joins (outside a battle) into party slot `slot` (-1: a free one, else the last)
        static string Join(int who, int slot)
        {
            var udm = UserDataManager.Instance();
            if (udm == null) return "error: no user data";
            OwnedCharacterData c = null;
            if (udm.OwnedCharacterList != null) foreach (var x in udm.OwnedCharacterList) if (x?.characterStatusId == who) c = x;
            if (c == null) Last.Interpreter.Instructions.SystemCall.Current.JoinCharacter(who, -1, (Last.Defaine.User.CorpsId)1, true, false, false);
            if (udm.OwnedCharacterList != null) foreach (var x in udm.OwnedCharacterList) if (x?.characterStatusId == who) c = x;
            if (c == null) return $"error: character {who} didn't join";
            var party = udm.GetPartyMemberData();
            int members = 0;
            bool inParty = false;
            if (party != null) foreach (var x in party) { members++; if (x.characterStatusId == who) inParty = true; }
            if (!inParty)   // a test run saves nothing
                Last.Management.CorpsClient.Instance().Set(slot >= 0 ? slot : Math.Min(members, 3), c.Id, (Last.Defaine.User.CorpsId)1);
            var names = new List<string>();
            party = udm.GetPartyMemberData();
            if (party != null) foreach (var x in party) names.Add(x.Name);
            return $"party [{string.Join(", ", names)}]";
        }

        // Gau joins (outside a battle) and learns every Rage the mod has (for the Rage list).
        const int Gau = 12;

        static string GauJoin()
        {
            var udm = UserDataManager.Instance();
            if (udm == null) return "error: no user data";
            bool inParty = false;
            var party = udm.GetPartyMemberData();
            if (party != null) foreach (var c in party) if (c.characterStatusId == Gau) inParty = true;
            OwnedCharacterData gau = null;
            if (udm.OwnedCharacterList != null) foreach (var c in udm.OwnedCharacterList) if (c?.characterStatusId == Gau) gau = c;
            if (gau == null) Last.Interpreter.Instructions.SystemCall.Current.JoinCharacter(Gau, -1, (Last.Defaine.User.CorpsId)1, true, false, false);
            if (udm.OwnedCharacterList != null) foreach (var c in udm.OwnedCharacterList) if (c?.characterStatusId == Gau) gau = c;
            if (gau == null) return "error: Gau didn't join";
            party = udm.GetPartyMemberData();
            inParty = false;
            int members = 0;
            if (party != null) foreach (var c in party) { members++; if (c.characterStatusId == Gau) inParty = true; }
            if (!inParty)   // a test run saves nothing: the party's last slot (or a free one) is Gau's
                Last.Management.CorpsClient.Instance().Set(Math.Min(members, 3), gau.Id, (Last.Defaine.User.CorpsId)1);
            var names = new List<string>();
            party = udm.GetPartyMemberData();
            if (party != null) foreach (var c in party) names.Add(c.Name);
            return $"party [{string.Join(", ", names)}]; " + Rages();
        }

        static string Rages()
        {
            var udm = UserDataManager.Instance();
            OwnedCharacterData gau = null;
            if (udm?.OwnedCharacterList != null) foreach (var c in udm.OwnedCharacterList) if (c?.characterStatusId == Gau) gau = c;
            if (gau == null) return "error: Gau hasn't joined (bnwui gau)";
            var mm = MasterManager.Instance;
            // the PR keeps every rage in Gau's list, learned at skill level 100 (unlearned below)
            var have = new Dictionary<int, OwnedAbility>();
            foreach (var o in gau.OwnedAbilityList) if (o?.Ability != null) have[o.Ability.Id] = o;
            var contents = new Dictionary<int, Content>();
            foreach (var c in mm.GetList<Content>().Values) if (c != null && c.TypeId == 4) contents[c.TypeValue] = c;
            int added = 0, described = 0;
            string sample = null;
            foreach (var ab in mm.GetList<Ability>().Values)
            {
                if (ab == null || ab.TypeId != 625 || !contents.TryGetValue(ab.Id, out var content)) continue;
                var desc = content.MesIdDescription;
                if (!string.IsNullOrEmpty(desc) && desc != "None")
                {
                    described++;
                    sample ??= $"{MessageManager.Instance.GetMessage(content.MesIdName, false)}: {MessageManager.Instance.GetMessage(desc, false)}";
                }
                // the rages BNW lets Gau learn: the ones with BNW's own description (2/3: ...; 1/3: ...)
                var text = string.IsNullOrEmpty(desc) || desc == "None" ? "" : MessageManager.Instance.GetMessage(desc, false) ?? "";
                if (!text.StartsWith("2/3:")) continue;
                if (have.TryGetValue(ab.Id, out var owned))
                {
                    if (owned.SkillLevel >= 100) continue;
                    owned.SkillLevel = 100;
                }
                else gau.OwnedAbilityList.Add(new OwnedAbility(ab, content, 100));
                added++;
            }
            return $"Gau (lv {gau.Parameter?.ConfirmedLevel()}): {added} rages learned, {gau.OwnedAbilityList.Count} abilities; " +
                   $"{described} rages with a description, e.g. {sample}";
        }
    }
}
