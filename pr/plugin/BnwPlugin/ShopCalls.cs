using System;
using HarmonyLib;
using Last.Interpreter;
using Last.Interpreter.Instructions;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // SysCall "BNW:<name>" lines in the mod's own scripts (tools/port_merchants.py):
    //   BNW:shop <group>   a shop for that product group, through the game's own opener
    //                      SystemCall.Current.OpenShopCommon(group, isFirstClock) (decompiled
    //                      tools/decomp/shop): it opens the shop on the call's first frame and
    //                      returns 1 while the shop is open (the script engine repeats the call),
    //                      0 once it's closed, as every vanilla "ショップを開く:..." call does.
    //   BNW:pay <gil>      BNW's sellers (Bob): map-local 0 = 1 and the gil taken if the party has it, else 0
    //   BNW:unequip <id>   a character's equipment back to the bag (character status id: Shadow 4)
    //   BNW:optimize <id>  a character's best equipment (the game's "最強装備" for anyone)
    //   BNW:veldt_open <party>...  Veldt entries opened as if those parties were fought (BNW's freebies)
    // A prefix on the SysCall instruction takes these names and leaves every other call to the
    // game (the WC plugin does the same for its "WC:" names).
    static class ShopCalls
    {
        internal static int Opened;   // tests: shops this session opened

        public static void Apply(Harmony h)
        {
            var m = AccessTools.Method(typeof(External.Misc), "SystemCall");
            if (m == null) { Plugin.L.LogWarning("[shop] no External.Misc.SystemCall: the mod's merchants are off"); return; }
            h.Patch(m, prefix: new HarmonyMethod(typeof(ShopCalls), nameof(SystemCall_Prefix)));
        }

        static int OpenFlags(global::Last.Map.Encount.BeastFieldEncount beast)
        {
            var a = beast.GetSaveOpenFlags();
            int n = 0;
            if (a != null) for (int i = 0; i < a.Length; i++) if (a[i] != 0) n++;
            return n;
        }

        public static bool SystemCall_Prefix(MainCore mc, ref int __result)
        {
            string name;
            try
            {
                var sv = mc?.op?.sValues;
                name = sv != null && sv.Length > 0 ? sv[0] : null;
            }
            catch { return true; }
            if (name == null || !name.StartsWith("BNW:")) return true;
            __result = 0;
            try
            {
                var parts = name.Substring(4).Split(' ');
                if (parts.Length == 2 && parts[0] == "shop" && int.TryParse(parts[1], out int group))
                {
                    bool first = mc.IsFirstClock();
                    __result = global::Last.Interpreter.Instructions.SystemCall.Current.OpenShopCommon(group, first);
                    if (first)
                    {
                        Opened++;
                        Hooks.NoteOnce("shop-" + group, () => $"[shop] the mod's shop {group} opened");
                    }
                }
                else if (parts.Length == 2 && parts[0] == "pay" && int.TryParse(parts[1], out int price))
                {
                    // BNW's sellers (Bob's espers: tools/port_bnw_mechanics.py) as the game's RemoveGil does it
                    // (decompiled tools/decomp/gil: map-local variable 0 = 1 and the gil taken when the party has
                    // it, else 0), with BNW's price in the call (RemoveGil reads its price from a master row)
                    var udm = global::Last.Management.UserDataManager.Instance();
                    bool paid = udm != null && udm.OwendGil >= price;
                    if (paid) new global::Last.Management.OwnedItemClient().RemoveGil(price);
                    DataStorage.Instance?.Set(DataStorage.Category.kMapLocalVariable, 0, paid ? 1 : 0);
                    Plugin.L.LogInfo($"[shop] pay {price}: {(paid ? "paid" : "not enough gil")} (now {udm?.OwendGil})");
                }
                else if (parts.Length == 2 && (parts[0] == "unequip" || parts[0] == "optimize") && int.TryParse(parts[1], out int who))
                {
                    // BNW's char_unequip / optimize for one character (tools/port_bnw_mechanics.py: Shadow's leaves;
                    // the Floating Continent's Celes): the game's own per-character steps, as its party-wide
                    // SysCalls take them (decompiled tools/decomp/unequip: Current.RemoveEquipmentFromMembers ->
                    // OwnedCharacterClient.RemoveAllEquip(owned id); Current.SetStrongestEquipCommon(status id))
                    var udm = global::Last.Management.UserDataManager.Instance();
                    global::Last.Data.User.OwnedCharacterData oc = null;
                    if (udm?.OwnedCharacterList != null) foreach (var x in udm.OwnedCharacterList) if (x?.characterStatusId == who) oc = x;
                    if (oc == null) Plugin.L.LogInfo($"[shop] {parts[0]} {who}: no such character yet");
                    else if (parts[0] == "unequip") new global::Last.Management.OwnedCharacterClient().RemoveAllEquip(oc.Id);
                    else global::Last.Interpreter.Instructions.SystemCall.Current.SetStrongestEquipCommon(who);
                    if (oc != null) Plugin.L.LogInfo($"[shop] {parts[0]} character {who} (owned {oc.Id})");
                }
                else if (parts.Length >= 2 && parts[0] == "veldt_open")
                {
                    // BNW's Veldt freebies (veldt-freebies.asm, at the world's end: the World of Balance's
                    // missable formations count as met): each party as if it had just been fought, through
                    // the game's own BeastFieldEncount.EncounterMonsterParty (it opens that party's Veldt
                    // entry; the save keeps the open flags: GetSaveOpenFlags)
                    var beast = global::Last.Map.Encount.BeastFieldEncount.Instance();
                    if (beast == null) Plugin.L.LogWarning("[shop] veldt_open: no BeastFieldEncount");
                    else
                    {
                        int before = OpenFlags(beast), n = 0;
                        for (int i = 1; i < parts.Length; i++)
                            if (int.TryParse(parts[i], out int party)) { beast.EncounterMonsterParty(party); n++; }
                        Plugin.L.LogInfo($"[shop] veldt_open: {n} parties; Veldt entries open {before} -> {OpenFlags(beast)}");
                    }
                }
                else Plugin.L.LogWarning($"[shop] unknown call {name}");
            }
            catch (Exception e) { __result = 0; Hooks.WarnOnce("ShopCalls", e); }
            return false;
        }
    }
}
