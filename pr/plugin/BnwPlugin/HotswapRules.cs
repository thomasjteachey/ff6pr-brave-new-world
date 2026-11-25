using System;
using System.Collections.Generic;
using HarmonyLib;
using Last.Battle;
using Last.Data.Master;
using Last.Defaine;
using Last.Management;
using PrKit;
using KeyInfo = Last.UI.KeyInput.BattleInfomationController;

namespace BraveNewWorld.PR
{
    // BNW's Hotswap Delay (readme 803-806, Bropedio/GrayShadows; c1.asm "Equipment Swap Menu Sustain and
    // Validation" QueueSwap, c2.asm 1230 SwapOrDefend): swapping a weapon or shield in battle, a free action in
    // vanilla, is queued as the Defend command (its subcommand the hand): the menu closes, the swap is that
    // turn's action and Defend's half turn follows (CommandRules: gauge 126/256 after the act). Both hands take
    // two swaps, a full turn. A swap doesn't set "defending" (SwapOrDefend returns 0 for a swap).
    //
    // The PR (decompiled tools/decomp/hotswap, hotswap2): the KeyInput BattleInfomationController's slot list is
    // state 7 (EquipSelect); picking a slot opens its item list (state 8, EquipChangeInit); picking an item runs
    // <>c__DisplayClass82_0.<EquipChangeInit>b__2: OwnedCharacterClient.SetEquip at once, onEquipChange, back to
    // state 7 (a cancel, b__3, goes back to 7 too). A command ends through CommandDone(targets, command,
    // abilities, targetType), which hands the act to the battle (OnCommandSelect does that for Defend:
    // [the player], the command's AbilityId, target type 6).
    // Here: EquipChangeInit's prefix notes the hands; when the slot list comes back (EquipSelectInit) with a hand
    // changed, the next UpdateController (a frame later, outside the state machine's Change) finishes the
    // command as Defend; that act's Defend condition is taken off when it ends. Config [Battle] TurnRules.
    static class HotswapRules
    {
        internal static bool Enabled = true;
        const int DefendCommand = 2;
        const int EquipSelect = 7;
        static (int Right, int Left)? before;          // the hands when an item list opened
        static IntPtr pending;                         // a controller whose swap still has to end the turn
        static KeyInfo last;                           // the battle's controller (harness)
        static readonly HashSet<IntPtr> swappers = new HashSet<IntPtr>();   // units whose queued Defend is a swap
        internal static int Swaps, Ended;              // tests and the harness

        public static void Apply(Harmony h)
        {
            if (!Enabled) return;
            Hooks.Patch(h, typeof(KeyInfo), "EquipChangeInit", typeof(HotswapRules), prefix: nameof(ChangeInit_Prefix));
            Hooks.Patch(h, typeof(KeyInfo), "EquipSelectInit", typeof(HotswapRules), postfix: nameof(SelectInit_Postfix));
            Hooks.Patch(h, typeof(KeyInfo), "UpdateController", typeof(HotswapRules), postfix: nameof(Update_Postfix));
            Hooks.Patch(h, typeof(BattleProgressATB), "ActExectionEndDelegate", typeof(HotswapRules), postfix: nameof(ActEnd_Postfix));
            PrKit.Harness.Register("bnwswap", HarnessCmd,
                "bnwswap [state]: from the battle's Item list, open its equip-slot list (the equip change) " +
                "for the character whose command menu is up; state: the controller's state, hands, swaps and turns ended");
        }

        static (int, int)? Hands(KeyInfo c)
        {
            var eq = c?.selectedTargetPlayer?.ownedCharacterData?.EquipmentData;
            if (eq == null) return null;
            int Id(EquipSlotType s) => eq.ContainsKey(s) ? eq[s]?.ItemId ?? 0 : 0;
            return (Id(EquipSlotType.Slot1), Id(EquipSlotType.Slot2));
        }

        public static void ChangeInit_Prefix(KeyInfo __instance)
        {
            try { before = Hands(__instance); }
            catch (Exception e) { Hooks.WarnOnce("Hotswap.ChangeInit", e); }
        }

        public static void SelectInit_Postfix(KeyInfo __instance)
        {
            try
            {
                if (before == null) return;
                var now = Hands(__instance);
                var was = before.Value;
                before = null;
                if (now == null || now.Value == was) return;   // cancelled, or the same item again
                Swaps++;
                pending = __instance.Pointer;
                Kit.Info($"[hotswap] {__instance.selectedTargetPlayer?.ownedCharacterData?.Name}: hands {was.Right}/{was.Left} -> " +
                         $"{now.Value.Item1}/{now.Value.Item2}: the swap takes the turn (Defend, half turn)");
            }
            catch (Exception e) { Hooks.WarnOnce("Hotswap.SelectInit", e); }
        }

        public static void Update_Postfix(KeyInfo __instance)
        {
            try
            {
                last = __instance;
                if (pending == IntPtr.Zero || __instance.Pointer != pending) return;
                pending = IntPtr.Zero;
                var who = __instance.selectedTargetPlayer;
                var mm = MasterManager.Instance;
                var cmd = mm?.GetData<Command>(DefendCommand);
                var ab = cmd == null ? null : mm.GetData<Ability>(cmd.AbilityId);
                if (who == null || ab == null) return;
                var targets = new Il2CppSystem.Collections.Generic.List<BattleUnitData>();
                targets.Add(who);
                var abilities = new Il2CppSystem.Collections.Generic.List<Ability>();
                abilities.Add(ab);
                swappers.Add(who.Pointer);
                __instance.CommandDone(targets.Cast<Il2CppSystem.Collections.Generic.IEnumerable<BattleUnitData>>(), cmd, abilities,
                    (BattleActData.TargetType)6);
                Ended++;
                Hooks.NoteOnce("hotswap-turn", () => "[hotswap] a swap ends the turn as Defend (BNW's Hotswap Delay)");
            }
            catch (Exception e) { Hooks.WarnOnce("Hotswap.Update", e); }
        }

        public static void ActEnd_Postfix(BattleUnitData battleUnitData, bool isCounter)
        {
            try
            {
                if (isCounter || battleUnitData == null || !swappers.Remove(battleUnitData.Pointer)) return;
                BattlePlugManager.Instance()?.BattleConditionController?.InterruptRemoveCondition(battleUnitData, ConditionType.Defend);
                Kit.Info($"[hotswap] swap act done: {battleUnitData.TryCast<BattlePlayerData>()?.ownedCharacterData?.Name} not defending");
            }
            catch (Exception e) { Hooks.WarnOnce("Hotswap.ActEnd", e); }
        }

        static string HarnessCmd(string[] a)
        {
            var c = last;
            if (c == null) return "no battle controller seen yet";
            var who = c.selectedTargetPlayer?.ownedCharacterData?.Name ?? "nobody";
            var hands = Hands(c);
            string state = $"state {c.stateMachine?.Current} for {who}, hands {hands?.Item1}/{hands?.Item2}, swaps {Swaps}, turns ended {Ended}";
            if (a.Length > 0 && a[0] == "state") return state;
            if (c.selectedTargetPlayer == null) return "no command menu is up; " + state;
            // from the Item menu's list only (UseItemSelect sets up what EquipSelectInit reads; from the command
            // list it throws), as the player switches to the equip change there
            if (c.stateMachine.Current != KeyInfo.State.UseItemSelect) return "open the Item command first; " + state;
            c.stateMachine.Change((KeyInfo.State)EquipSelect);
            return "equip-slot list opened; " + state;
        }
    }
}
