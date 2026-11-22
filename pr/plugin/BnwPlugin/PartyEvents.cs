using System;
using System.Collections.Generic;
using System.Linq;
using Last.Defaine;
using Last.Management;
using Harness = PrKit.Harness;

namespace BraveNewWorld.PR
{
    // QA for BNW's party events (tools/port_bnw_party_events.py, batch C of the readme audit, 2026-11-21): the events
    // themselves are the port's (the PR's own SysCalls "控えメンバーの装備を外す", "最強装備：ティナ/エドガー" and
    // ShopCalls' "BNW:unequip 4"; Shadow's battle-end lottery taken out of the battle scripts), so this file only adds
    // a harness readout of everyone's gear, and a way to strip some first:
    //   bnwgear                 each character the game has (status ids 1-14): party (P), roster but not in the party
    //                           (R) or neither (-), and the content id in each equipment slot (1-6)
    //   bnwgear strip <ids>     those characters' equipment back to the bag first (the game's own
    //                           OwnedCharacterClient.RemoveAllEquip, as the scripts' unequips do), then the readout
    // Test runs only (the harness); they never save. No hooks.
    static class PartyEvents
    {
        static readonly string[] Names = { "", "Terra", "Locke", "Cyan", "Shadow", "Edgar", "Sabin", "Celes", "Strago", "Relm",
                                           "Setzer", "Mog", "Gau", "Gogo", "Umaro" };

        public static void Register()
        {
            Harness.Register("bnwgear", Gear, "bnwgear [strip <status ids>]: each character's place (P party, R roster, - gone) and " +
                "equipment content ids (slots 1-6); strip: those characters unequipped first (memory only)");
        }

        static string Name(int id) => id > 0 && id < Names.Length ? Names[id] : "#" + id;

        static string Gear(string[] a)
        {
            var udm = UserDataManager.Instance();
            var all = udm?._OwnedCharacterList_k__BackingField;
            if (udm == null || all == null) return "error: no user data (load or start a game first)";
            var stripped = new List<string>();
            try
            {
                if (a.Length > 0 && a[0].Equals("strip", StringComparison.OrdinalIgnoreCase))
                {
                    var want = a.Skip(1).Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).ToList();
                    var client = new OwnedCharacterClient();
                    foreach (var o in all)
                        if (o != null && want.Contains(o.characterStatusId)) { client.RemoveAllEquip(o.Id); stripped.Add(Name(o.characterStatusId)); }
                }
                var party = new HashSet<int>();
                var members = udm.GetPartyMemberData();
                if (members != null) foreach (var p in members) if (p != null) party.Add(p.Id);
                var roster = new HashSet<int>();
                var clone = udm.GetOwnedCharactersClone(false);
                if (clone != null) foreach (var c in clone) if (c != null) roster.Add(c.Id);
                var rows = new List<string>();
                foreach (var o in all)
                {
                    if (o == null || o.characterStatusId < 1 || o.characterStatusId > 14) continue;
                    string where = party.Contains(o.Id) ? "P" : roster.Contains(o.Id) ? "R" : "-";
                    var eq = o.EquipmentData;
                    var slots = new List<string>();
                    for (int s = 1; s <= 6; s++)
                    {
                        var slot = (EquipSlotType)s;
                        int id = eq != null && eq.ContainsKey(slot) && eq[slot] != null ? eq[slot].ContentId : 0;
                        slots.Add(id.ToString());
                    }
                    rows.Add($"{Name(o.characterStatusId)}({o.characterStatusId}) {where} [{string.Join(",", slots)}]");
                }
                return (stripped.Count > 0 ? $"stripped {string.Join(", ", stripped)}; " : "") + "bnwgear: " + string.Join("; ", rows);
            }
            catch (Exception e) { return $"error: {e.GetType().Name}: {e.Message}"; }
        }
    }
}
