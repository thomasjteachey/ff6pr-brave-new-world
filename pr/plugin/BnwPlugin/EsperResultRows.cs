using System;
using System.Collections.Generic;
using BraveNewWorld.Rules;
using HarmonyLib;
using Last.Management;
using Harness = PrKit.Harness;
using Hooks = PrKit.Hooks;
using KeyMenu = Last.UI.KeyInput.ResultMenuController;
using KeySkill = Last.UI.KeyInput.ResultSkillController;

namespace BraveNewWorld.PR
{
    // BNW's "<name> gained an EL" (c2.asm Show_EL: battle message $46 after the level-ups, once per
    // character whose EL went up, d1.asm ".46 db !char_name, gained an EL") on the PR's victory
    // screen. The PR's keyboard/gamepad result menu (Last.UI.KeyInput.ResultMenuController, decompiled
    // tools/decomp/bnw_engine) draws its sentence rows on the points page: ShowPointsInit calls
    // ResultSkillController.SetJobProficiencyLevelUpList(CharacterList) when Any character's
    // IsJobProficiencyLevelUp is set (the other games' job level-ups), and that makes one row per flagged
    // character (CreateLevelUpContents + SetJobProficiencyData, if Config's IsDisplayBattleResultJobLevelUp
    // is on). EsperHooks flags each character whose EL went up (EsperHooks.ElGains, same order), and the
    // postfix below writes BNW's line into those rows, or makes them when the Config hides job level-ups.
    // (Until 2026-11-20 this hooked SetLevelUpList, which only the abilities page (state 6) calls: FF6's
    // battles never reach it, so the row never showed.) The touch menu is the phone's: left alone.
    static class EsperResultRows
    {
        internal static int RowsShown;
        internal static string LastRow = "";

        public static void Apply(Harmony h)
        {
            Hooks.Patch(h, typeof(KeySkill), "SetJobProficiencyLevelUpList", typeof(EsperResultRows), postfix: nameof(Rows_Postfix));
            Harness.Register("bnwel", SetUp, "bnwel: the party's esper users one EP short of their next EL, each with an esper " +
                "(equipped from the owned ones if none), so the next won battle raises their EL (memory only; test runs never save)");
        }

        public static void Rows_Postfix(KeySkill __instance)
        {
            if (!EsperHooks.Levels || EsperHooks.ElGains.Count == 0 || __instance == null) return;
            try
            {
                var parent = __instance.view?.contentsParent;
                var list = __instance.levelUpContentList;
                if (parent == null || list == null) return;
                int made = list.Count;   // the game's rows for the flagged characters (the method starts by clearing the list)
                for (int i = 0; i < EsperHooks.ElGains.Count; i++)
                {
                    string name = EsperHooks.ElGains[i];
                    var row = i < made ? list[i] : __instance.CreateLevelUpContents(parent);
                    var v = row?.view;
                    if (v == null) continue;
                    v.SetJobProficiencyLevelUpText(name, "");
                    string text = $"{name} gained an EL";
                    if (v.fixedMessageText != null) v.fixedMessageText.text = text;
                    if (i >= made) list.Add(row);
                    RowsShown++;
                    LastRow = text;
                    Plugin.L.LogInfo($"[espers] victory screen: \"{text}\" (BNW's EL message{(i < made ? "" : ", a row of its own")})");
                }
            }
            catch (Exception e) { Hooks.WarnOnce("EsperResultRows", e); }
            finally { EsperHooks.ElGains.Clear(); }
        }

        // harness: the party ready to gain an EL in the next battle
        static string SetUp(string[] a)
        {
            var D = EsperHooks.D;
            var udm = UserDataManager.Instance();
            if (D == null || udm == null) return "error: no save loaded";
            var party = udm.GetOwnedCharactersClone(false);
            if (party == null || party.Count == 0) return "error: no party";
            var done = new List<string>();
            var client = new OwnedCharacterClient();
            foreach (var c in party)
            {
                if (c == null) continue;
                int sid = c.characterStatusId;
                if (sid < 1 || sid > D.Rules.MaxElCharacterStatusId) continue;
                if (EsperHooks.EsperOf(c) == null)
                {
                    foreach (var e in TestKit.Espers(D))
                    {
                        if (Array.IndexOf(e.UserStatusIds, sid) < 0) continue;
                        if (!udm.OwnedMagicStoneList.Contains(e.ContentId))
                        {
                            // a new game owns none: given for this run (test runs never save), as the F8 test kit does
                            new OwnedItemClient().AddMagicStone(e.ContentId);
                            if (!udm.OwnedMagicStoneList.Contains(e.ContentId)) udm.OwnedMagicStoneList.Add(e.ContentId);
                        }
                        bool taken = false;
                        foreach (var o in udm.OwnedCharacterList)
                            if (o != null && o.MagicStoneId == e.ContentId) { taken = true; break; }
                        if (taken) continue;
                        client.SetMagicStone(c.Id, e.ContentId);
                        break;
                    }
                }
                var esper = EsperHooks.EsperOf(FindOwned(udm, c.Id) ?? c);
                if (esper == null) { done.Add($"{c.Name}: no esper"); continue; }
                var p = EsperStore.Get(sid);
                int need = EsperRules.NextElAt(D, p.El);
                if (need <= 0) { done.Add($"{c.Name}: EL max"); continue; }
                p.Ep = need - 1;
                EsperStore.Put(sid, p);
                done.Add($"{c.Name}: EL {p.El}, EP {p.Ep} with {esper.Name}");
            }
            EsperStore.Invalidate();
            return "bnwel: " + string.Join("; ", done);
        }

        static Last.Data.User.OwnedCharacterData FindOwned(UserDataManager udm, int id)
        {
            foreach (var o in udm.OwnedCharacterList)
                if (o != null && o.Id == id) return o;
            return null;
        }
    }
}
