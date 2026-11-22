using System;
using System.Collections.Generic;
using System.Linq;
using Last.Management;
using Last.Map;
using DS = Last.Interpreter.DataStorage;
using SysCall = Last.Interpreter.Instructions.SystemCall.Current;

namespace BraveNewWorld.PR
{
    // Cyan's dream as BNW has it (tools/port_bnw_dream.py, bnw-dream 2026-11-20): harness commands for its
    // headless test, so the test reaches Doma's bed with the right party at once instead of playing there.
    //   dreamparty [ids]   the party becomes these characters (status ids; default Cyan, Locke, Edgar, Sabin):
    //                      the others leave it, these join (the game's own JoinCharacter); Mt. Zozo's Cyan back
    //                      (ScenarioFlag2 109) and the dream not done (199), the bed's rule (sc_map_20451_4)
    //   dreamstate         the map, the party and reserve, the dream's flags, and its entities on the
    //                      Dreamscape's two maps (520: Cyans 63-65, their touch objects 66/55/51, the Stooges'
    //                      boys 67/58/54 and triggers; 521: the push-back 12-15, the fight 8-11, the way on 7)
    // Test runs only (the harness); they never save.
    static class DreamTest
    {
        static readonly string[] Names = { "", "Terra", "Locke", "Cyan", "Shadow", "Edgar", "Sabin", "Celes", "Strago", "Relm",
                                           "Setzer", "Mog", "Gau", "Gogo", "Umaro" };
        // the dream's flags: BNW's (port_bnw_dream: ScenarioFlag4 3-5 Cyan #1-#3 still there, 6 Stooge #1 met) and the PR's
        static readonly (string cat, int idx, string what)[] Flags =
        {
            ("ScenarioFlag4", 3, "cyan1"), ("ScenarioFlag4", 4, "cyan2"), ("ScenarioFlag4", 5, "cyan3"), ("ScenarioFlag4", 6, "stooge1"),
            ("ScenarioFlag2", 179, "in"), ("ScenarioFlag2", 180, "stooge2"), ("ScenarioFlag2", 181, "comp1"), ("ScenarioFlag2", 182, "stooge3"),
            ("ScenarioFlag2", 183, "comp2"), ("ScenarioFlag2", 184, "fought"), ("ScenarioFlag2", 185, "train"), ("ScenarioFlag2", 199, "done"),
            ("ScenarioFlag2", 109, "zozo"),
        };
        static readonly Dictionary<int, int[]> Entities = new Dictionary<int, int[]>
        {
            [520] = new[] { 63, 64, 65, 66, 55, 51, 67, 68, 69, 58, 56, 57, 54, 52, 53 },
            [521] = new[] { 12, 13, 14, 15, 8, 9, 10, 11, 7 },
        };

        public static void Register()
        {
            PrKit.Harness.Register("dreamparty", DreamParty, "dreamparty [status ids]: the party becomes these characters " +
                "(default 3 2 5 6: Cyan, Locke, Edgar, Sabin), Mt. Zozo's Cyan back, the dream not done (Doma's bed takes them)");
            PrKit.Harness.Register("dreamstate", _ => State(), "dreamstate: the map, party, reserve, Cyan's dream's flags and entities");
        }

        static string Name(int id) => id > 0 && id < Names.Length ? Names[id] : "#" + id;

        static List<int> PartyIds()
        {
            var udm = UserDataManager.Instance();
            var owned = udm?.GetOwnedCharactersClone(true);
            var ids = new List<int>();
            if (udm == null || owned == null) return ids;
            foreach (var c in udm.GetCorpsListClone())
            {
                if (c == null || (int)c.Id != 1) continue;
                foreach (var o in owned) if (o != null && o.Id == c.CharacterId) { ids.Add(o.characterStatusId); break; }
            }
            return ids;
        }

        static string Corps()
        {
            var udm = UserDataManager.Instance();
            var owned = udm?.GetOwnedCharactersClone(true);
            if (udm == null || owned == null) return "no user data";
            var parts = new List<string>();
            foreach (var c in udm.GetCorpsListClone())
            {
                if (c == null) continue;
                string who = "id " + c.CharacterId;
                foreach (var o in owned) if (o != null && o.Id == c.CharacterId) { who = Name(o.characterStatusId); break; }
                parts.Add($"{who}:{(int)c.Id}");
            }
            return string.Join(" ", parts);
        }

        static string DreamParty(string[] a)
        {
            var udm = UserDataManager.Instance();
            var ds = DS.Instance;
            if (udm == null || ds == null) return "error: no user data (load or start a game first)";
            var want = a.Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0 && v < 15).ToList();
            if (want.Count == 0) want = new List<int> { 3, 2, 5, 6 };
            try
            {
                foreach (int id in PartyIds().ToList())
                    if (!want.Contains(id)) SysCall.SecedeCharacter(id, false);
                foreach (int id in want)
                    if (!PartyIds().Contains(id)) SysCall.JoinCharacter(id, -1, (Last.Defaine.User.CorpsId)1, true, true, false);
                ds.Set(DS.Category.kScenarioFlag2, 109, 1);
                ds.Set(DS.Category.kScenarioFlag2, 199, 0);
            }
            catch (Exception e) { return $"error: {e.GetType().Name}: {e.Message} (party {string.Join(",", PartyIds().Select(Name))})"; }
            return $"dreamparty: party [{string.Join(", ", PartyIds().Select(Name))}]; corps {Corps()}";
        }

        static string Entity(FieldController fc, int id)
        {
            Last.Entity.Field.FieldEntity e = null;
            try { e = fc?.GetFieldEntity(id); } catch (Exception) { }
            if (e == null) return $"{id}:-";
            string vis = "?";
            try
            {
                var r = e.GetComponentInChildren<UnityEngine.SpriteRenderer>();
                vis = !e.gameObject.activeInHierarchy ? "off" : r == null ? "on" : r.enabled && r.color.a > 0.01f ? "shown" : "hidden";
            }
            catch (Exception) { }
            string pos = "", anim = "";
            try { var p = e.GetEntityPosition(); pos = $"@{p.x:0.#},{p.y:0.#}"; } catch (Exception) { }
            try { anim = e.GetCurrentAnimKey() ?? ""; } catch (Exception) { }
            int asset = -1;
            try { asset = e.Property?.TryCast<PropertyNpc>()?.AssetId ?? -1; } catch (Exception) { }
            return $"{id}:{vis}{pos}{(asset >= 0 ? " a" + asset : "")}{(anim != "" ? " " + anim : "")}";
        }

        static string State()
        {
            var udm = UserDataManager.Instance();
            var ds = DS.Instance;
            if (udm == null || ds == null) return "dreamstate: no user data";
            int map = udm.CurrentMapId;
            var flags = string.Join(" ", Flags.Select(f => $"{f.what}={ds.Get(f.cat, f.idx)}"));
            string ents = "";
            if (Entities.TryGetValue(map, out var ids))
            {
                var fc = UnityEngine.Object.FindObjectOfType<FieldMap>()?.fieldController;
                ents = "; entities " + string.Join(" ", ids.Select(i => Entity(fc, i)));
            }
            return $"dreamstate: map {map}; party [{string.Join(", ", PartyIds().Select(Name))}]; corps {Corps()}; flags {flags}{ents}; " +
                   $"script {PrKit.MapScripts.Current}";
        }
    }
}
