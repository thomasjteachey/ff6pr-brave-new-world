using System.Collections.Generic;
using Last.Data.Master;
using Last.Management;
using Harness = PrKit.Harness;

namespace BraveNewWorld.PR
{
    // Harness helpers for item and EXP checks (QA bnw-gaps-a, 2026-11-21): give items by PR item id, and print each
    // character's level, EXP and the EXP table's total for the next level. Memory only: test runs never save.
    static class HarnessItems
    {
        public static void Register()
        {
            Harness.Register("bnwgive", Give, "bnwgive <PR item id> [count]: add that item to the inventory (memory only)");
            Harness.Register("exp", Exp, "exp: each party member's level, EXP and the EXP table's total for the next level");
            Harness.Register("bnwhurt", a => Hurt(), "bnwhurt: every party member to 1 HP and 0 MP (memory only), for heal checks (QA recovery points)");
            Harness.Register("bnwhp", a => Hp(), "bnwhp: each party member's HP and MP");
            Harness.Register("bnwnpcs", a => Npcs(), "bnwnpcs: the current map's NPCs: entity id, action (2 runs the script, 4 only " +
                "talks), script id, message key (QA: Bob's cabin NPC must be action 2 with script 9963)");
            Harness.Register("bnwequip", Equip, "bnwequip <party index 0-3> <PR weapon id> [hand slot 1|2]: put that weapon in the " +
                "member's hand (given first if not owned; memory only), for weapon-effect checks (QA bnw-weapons)");
        }

        // the party's own data (UserDataManager's list, not the clones)
        static List<Last.Data.User.OwnedCharacterData> Members()
        {
            var udm = UserDataManager.Instance();
            var party = udm?.GetPartyMemberData();
            var all = udm?._OwnedCharacterList_k__BackingField;
            var out_ = new List<Last.Data.User.OwnedCharacterData>();
            if (party == null || all == null) return out_;
            foreach (var p in party)
                if (p != null)
                    foreach (var o in all)
                        if (o != null && o.Id == p.Id) { out_.Add(o); break; }
            return out_;
        }

        static string Hp()
        {
            var parts = new List<string>();
            foreach (var o in Members())
            {
                var p = o.Parameter;
                if (p == null) continue;
                parts.Add($"{o.Name} HP {p.CurrentHP}/{p.ConfirmedMaxHp()} MP {p.CurrentMP}/{p.ConfirmedMaxMp()}");
            }
            return parts.Count == 0 ? "error: no party" : "hp: " + string.Join("; ", parts);
        }

        static string Npcs()
        {
            var parts = new List<string>();
            try
            {
                int map = UserDataManager.Instance()?.CurrentMapId ?? -1;
                foreach (var e in UnityEngine.Object.FindObjectsOfType<Last.Entity.Field.FieldEntity>())
                {
                    var p = e?.Property;
                    if (p == null || p.ObjectType != 6) continue;
                    var t = p.TryCast<PropertyTalk>();
                    var ev = p.TryCast<Last.Map.PropertyEvent>();
                    parts.Add($"{p.EntityId}: action {(ev != null ? ev.ActionId : -1)} script {(ev != null ? ev.ScriptId : 0)} msg {t?.MessageKey ?? "-"}");
                }
                return $"npcs on map {map}: " + (parts.Count == 0 ? "none" : string.Join("; ", parts));
            }
            catch (System.Exception ex) { return "error: " + ex.Message; }
        }

        static string Hurt()
        {
            foreach (var o in Members())
            {
                var p = o.Parameter;
                if (p == null) continue;
                p.CurrentHP = 1;
                p.CurrentMP = 0;
            }
            return "bnwhurt: " + Hp();
        }

        // the member's own data (UserDataManager's list, not GetOwnedCharactersClone's copies), so a battle started next
        // builds its parameters with the weapon
        static string Equip(string[] a)
        {
            if (a.Length < 2 || !int.TryParse(a[0], out int idx) || !int.TryParse(a[1], out int weapon))
                return "error: bnwequip <party index 0-3> <PR weapon id> [hand slot 1|2]";
            int slot = a.Length > 2 && int.TryParse(a[2], out int s) && (s == 1 || s == 2) ? s : 1;
            var udm = UserDataManager.Instance();
            var mm = MasterManager.Instance;
            if (udm == null || mm == null) return "error: no save loaded";
            int contentId = -1;
            foreach (var ct in mm.GetList<Content>().Values)
                if (ct != null && ct.TypeId == 2 && ct.TypeValue == weapon) { contentId = ct.Id; break; }
            if (contentId < 0) return $"error: no content for weapon {weapon}";
            var party = udm.GetPartyMemberData();
            if (party == null || idx < 0 || idx >= party.Count || party[idx] == null) return $"error: no party member {idx}";
            int charId = party[idx].Id;
            Last.Data.User.OwnedCharacterData member = null;
            var all = udm._OwnedCharacterList_k__BackingField;
            if (all != null) foreach (var o in all) if (o != null && o.Id == charId) { member = o; break; }
            if (member == null) return $"error: member {charId} not in the owned list";
            var item = udm.SearchOwnedItem(contentId);
            if (item == null)
            {
                new OwnedItemClient().AddOwnedItem(contentId, 1);
                item = udm.SearchOwnedItem(contentId);
            }
            if (item == null) return $"error: weapon {weapon} (content {contentId}) not in the inventory after giving it";
            member.EquipmentData[(Last.Defaine.EquipSlotType)slot] = item;
            return $"bnwequip: {member.Name} holds weapon {weapon} (content {contentId}) in hand slot {slot}";
        }

        static string Give(string[] a)
        {
            if (a.Length < 1 || !int.TryParse(a[0], out int item)) return "error: bnwgive <PR item id> [count]";
            int count = a.Length > 1 && int.TryParse(a[1], out int c) ? c : 1;
            var mm = MasterManager.Instance;
            if (mm == null) return "error: no master data";
            foreach (var ct in mm.GetList<Content>().Values)
                if (ct != null && ct.TypeId == 1 && ct.TypeValue == item)
                {
                    new OwnedItemClient().AddOwnedItem(ct.Id, count);
                    return $"bnwgive: item {item} (content {ct.Id}) x{count}";
                }
            return $"error: no content for item {item}";
        }

        static string Exp(string[] a)
        {
            var udm = UserDataManager.Instance();
            var mm = MasterManager.Instance;
            if (udm == null || mm == null) return "error: no save loaded";
            var total = new Dictionary<int, int>();
            foreach (var kv in mm.GetList<ExpTable>())
                if (kv.Value != null && kv.Value.GroupId == 1) total[kv.Value.Lv] = kv.Value.TotalExp;
            var parts = new List<string>();
            var party = udm.GetOwnedCharactersClone(false);
            if (party == null) return "error: no party";
            foreach (var o in party)
            {
                if (o == null) continue;
                int lv = o.Parameter?.BaseLevel ?? 0;
                int next = total.TryGetValue(lv + 1, out int t) ? t : -1;
                parts.Add($"{o.Name} Lv {lv} EXP {o.CurrentExp} next at {next}");
            }
            return "exp: " + string.Join("; ", parts) + $" | table lv2 {(total.TryGetValue(2, out int l2) ? l2 : -1)}";
        }
    }
}
