using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BraveNewWorld.Rules;
using HarmonyLib;
using Last.Data.User;
using Last.Interpreter;
using Last.Interpreter.Instructions;
using Last.Management;
using Harness = PrKit.Harness;
using Hooks = PrKit.Hooks;

namespace BraveNewWorld.PR
{
    // BNW's menu-side rules (readme audit batch C, 2026-11-21; the porter's half is tools/port_bnw_menus.py):
    //
    //  * Umaro's equip menu unlocked ("Bare Naked Yeti", readme 1779-1787; c3.asm:123-125, C3/1E6E CMP #$0E: the
    //    equip menu's Umaro check now compares with a character no menu shows). The PR locks him in code:
    //    Serial.FF6.Management.CharacterInfomationData.IsChangeEquipmentOnlyAccessory(id) is "return id == 14"
    //    (tools/decomp/bnw_menus); its menu restriction lists keep Umaro out of Magic only (Equipment's list is
    //    the guests 15-32), so the Equip menu already opens for him with only the relic slots changeable. That
    //    method's machine code (RVA 0x4421B0: 83 FA 0E 0F 94 C0 C3, 7 bytes) is shared with three battle lambdas
    //    (v == BattleFlagType.NoARBattle): a Harmony detour would change those too. Every caller reaches it
    //    through the class's vtable (CharacterInfomationProvider.IsChangeEquipmentOnlyAccessory: mov rax,[rcx] /
    //    jmp [rax+0x1F0]; tools/ghidra/callers.py: no direct calls), so the plugin points that one vtable entry
    //    at its own function: the shared code is never touched, the lambdas keep the game's.
    //  * SysCalls from the porter's scripts (a prefix on External.Misc.SystemCall, as ShopCalls' "BNW:" names;
    //    ShopCalls leaves "BNW_MENUS:" alone):
    //      BNW_MENUS:respec                    Ultros's EL respec (ca_cb_cc.asm:499-562): for each party member
    //                                          Terra..Gau, 8D unequip + 67 RespecELs (RespecRules.Respec), HP/MP
    //                                          filled (C0/9F55); the script has taken the gil (BNW:pay)
    //      BNW_MENUS:umaro_unequip all|reserve the airship NPCs (ee.asm EE/B053 / EE/B066): Umaro unequipped
    //                                          too, if recruited (reserve: and not in the party); the PR's own
    //                                          calls skip him (tools/decomp/unequip)
    static class MenuRules
    {
        internal static bool UmaroEquip = true, Respec = true;
        internal static string UmaroState = "not applied", LastRespec = "", LastUmaroUnequip = "";
        const string Prefix = "BNW_MENUS:";

        public static void Apply(Harmony h)
        {
            try { UmaroState = UnlockUmaro(); }
            catch (Exception e) { UmaroState = "error: " + e.Message; Hooks.WarnOnce("UmaroEquip", e); }
            Plugin.L.LogInfo($"[menus] Umaro's equip menu: {UmaroState}");
            var m = AccessTools.Method(typeof(External.Misc), "SystemCall");
            if (m == null) Plugin.L.LogWarning("[menus] no External.Misc.SystemCall: Ultros's respec and Umaro's unequip are off");
            else Hooks.Patch(h, m, typeof(MenuRules), prefix: nameof(SystemCall_Prefix));
            Harness.Register("bnwrespec", _ => "bnwrespec: " + RespecParty(),
                "bnwrespec: Ultros's EL respec on the party now (unequip, ELs back to the bank, HP/MP filled; no gil; memory only)");
            Harness.Register("umaroequip", _ => SelfTest(),
                "umaroequip: what the game's equip menu asks (IsChangeEquipmentOnlyAccessory) for Umaro and Terra");
        }

        // --- Umaro's equip menu --------------------------------------------------------------------------

        delegate byte OnlyAccessoryFn(IntPtr self, int characterStatusId, IntPtr method);
        static OnlyAccessoryFn own;   // the function the game now calls: kept here so the GC never frees its thunk
        static bool selfTested;

        // the vtable entry the game calls: the game's own answer (id == 14) unless BNW's unlock is on
        static byte OnlyAccessory(IntPtr self, int characterStatusId, IntPtr method) =>
            (byte)(!UmaroEquip && characterStatusId == RespecRules.Umaro ? 1 : 0);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);

        // Il2CppClass then its vtable ({methodPtr, method} pairs): the entry is found by both pointers matching
        // the method's own (its MethodInfo and code), never by a fixed offset. Seen at class+0x1F0 (slot 12).
        const int VtableFrom = 0x100, VtableTo = 0x300;

        static unsafe string UnlockUmaro()
        {
            if (!UmaroEquip) return "off";
            var m = AccessTools.Method(typeof(Serial.FF6.Management.CharacterInfomationData), "IsChangeEquipmentOnlyAccessory",
                new[] { typeof(int) });
            if (m == null) return "no CharacterInfomationData.IsChangeEquipmentOnlyAccessory: left locked";
            var klass = Hooks.ClassPtr<Serial.FF6.Management.CharacterInfomationData>();   // runs the interop type's static ctor
            var field = Il2CppInterop.Common.Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(m);
            var info = field == null ? IntPtr.Zero : (IntPtr)field.GetValue(null);
            if (info == IntPtr.Zero || klass == IntPtr.Zero) return "no method info or class pointer: left locked";
            var code = *(IntPtr*)info;   // Il2CppMethodInfo.methodPointer
            var dll = GetModuleHandle("GameAssembly.dll");
            long rva = dll == IntPtr.Zero ? -1 : (long)code - (long)dll;
            for (int off = VtableFrom; off <= VtableTo; off += 8)
            {
                var entry = (IntPtr*)((byte*)klass + off);
                if (entry[0] != code || entry[1] != info) continue;
                own = OnlyAccessory;
                entry[0] = Marshal.GetFunctionPointerForDelegate(own);
                return $"unlocked (vtable entry class+0x{off:X}, the game's code at RVA 0x{rva:X} left as it is)";
            }
            return $"vtable entry not found (code RVA 0x{rva:X}): left locked";
        }

        // the game's own question, asked the way its menus ask it (the provider's virtual call)
        internal static string SelfTest()
        {
            try
            {
                var prov = Serial.ProviderManager.Instance?.CharacterInfomationProvider;
                if (prov == null) return "umaroequip: no CharacterInfomationProvider yet";
                bool umaro = prov.IsChangeEquipmentOnlyAccessory(RespecRules.Umaro), terra = prov.IsChangeEquipmentOnlyAccessory(1);
                return $"umaroequip: {UmaroState}; only relics: Umaro {umaro}, Terra {terra} " +
                       $"({(!umaro && !terra ? "ok: BNW" : umaro && UmaroEquip ? "FAIL: still locked" : "the PR's lock")})";
            }
            catch (Exception e) { return "umaroequip: error " + e.Message; }
        }

        // a once-line for the log when the providers exist (from BnwRunner.Update); a class the game hadn't set up
        // at load (no vtable entry yet) gets one more try here
        internal static void Track()
        {
            if (selfTested || Serial.ProviderManager.Instance?.CharacterInfomationProvider == null) return;
            selfTested = true;
            try { if (own == null && UmaroEquip) Plugin.L.LogInfo("[menus] Umaro's equip menu, again: " + (UmaroState = UnlockUmaro())); }
            catch (Exception e) { Hooks.WarnOnce("UmaroEquip", e); }
            Plugin.L.LogInfo("[menus] " + SelfTest());
        }

        // --- SysCalls ------------------------------------------------------------------------------------

        public static bool SystemCall_Prefix(MainCore mc, ref int __result)
        {
            string name;
            try
            {
                var sv = mc?.op?.sValues;
                name = sv != null && sv.Length > 0 ? sv[0] : null;
            }
            catch { return true; }
            if (name == null || !name.StartsWith(Prefix)) return true;
            __result = 0;
            try
            {
                var parts = name.Substring(Prefix.Length).Split(' ');
                if (parts[0] == "respec") LastRespec = RespecParty();
                else if (parts[0] == "umaro_unequip" && parts.Length == 2) LastUmaroUnequip = UnequipUmaro(parts[1] == "all");
                else Plugin.L.LogWarning($"[menus] unknown call {name}");
            }
            catch (Exception e) { Hooks.WarnOnce("MenuRules.SystemCall", e); }
            return false;
        }

        // the party's own data (UserDataManager's list, as HarnessItems.Members)
        static List<OwnedCharacterData> Party(UserDataManager udm)
        {
            var list = new List<OwnedCharacterData>();
            var party = udm?.GetPartyMemberData();
            var owned = udm?._OwnedCharacterList_k__BackingField;
            if (party == null || owned == null) return list;
            foreach (var p in party)
                if (p != null)
                    foreach (var o in owned)
                        if (o != null && o.Id == p.Id) { list.Add(o); break; }
            return list;
        }

        internal static string RespecParty()
        {
            if (!Respec) return "off";
            if (!EsperStore.Available) return "no save data";
            var udm = UserDataManager.Instance();
            var client = new OwnedCharacterClient();
            var done = new List<string>();
            foreach (var c in Party(udm))
            {
                int sid = c.characterStatusId;
                if (!RespecRules.Respecs(sid)) continue;
                client.RemoveAllEquip(c.Id);                    // 8D: unequip (the Unequipium fix recalculates)
                var before = EsperStore.Get(sid);
                var after = RespecRules.Respec(before);         // 67: every EL back to the bank, the bonuses gone
                EsperStore.Put(sid, after);
                var p = c.Parameter;                            // C0/9F55: HP and MP filled to the new maximums
                if (p != null)
                {
                    p.CurrentHP = p.ConfirmedMaxHp();
                    p.CurrentMP = p.ConfirmedMaxMp();
                }
                done.Add($"{c.Name} EL {after.El} ({before.Spent} undone, {after.ElBank} to spend; HP {p?.CurrentHP} MP {p?.CurrentMP})");
            }
            var line = done.Count == 0 ? "no one in the party has ELs" : string.Join("; ", done);
            Plugin.L.LogInfo($"[menus] respec: {line}");
            return line;
        }

        static string UnequipUmaro(bool all)
        {
            if (!UmaroEquip) return "off";
            var udm = UserDataManager.Instance();
            OwnedCharacterData umaro = null;
            var owned = udm?._OwnedCharacterList_k__BackingField;
            if (owned != null) foreach (var c in owned) if (c?.characterStatusId == RespecRules.Umaro) umaro = c;
            bool inParty = Party(udm).Exists(c => c.characterStatusId == RespecRules.Umaro);
            string why = umaro == null ? "Umaro not recruited" : !all && inParty ? "Umaro in the party" : null;
            if (why == null) new OwnedCharacterClient().RemoveAllEquip(umaro.Id);
            var line = $"{(all ? "all" : "reserve")}: {why ?? $"Umaro (owned {umaro.Id}) unequipped"}";
            Plugin.L.LogInfo($"[menus] umaro unequip {line}");
            return line;
        }
    }
}
