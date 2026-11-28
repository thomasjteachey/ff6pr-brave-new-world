using System;
using System.Linq;
using HarmonyLib;
using Last.Battle;
using Last.Interpreter;
using Hooks = PrKit.Hooks;
using SysCall = Last.Interpreter.Instructions.SystemCall.Current;

namespace BraveNewWorld.PR
{
    // Gau's return after a Leap (the user, 2026-11-27: "if i autobattle on the last turn of a fight, gau doesn't come
    // back out"). The PR's Veldt battle script (sc_b_0239, script 8010) asks SetGauMode at the start ("ガウバトルモード
    // 設定": script-local 2 = 2 when Gau comes back after this fight); after the win it turns the enemy side into a
    // stand-in Gau (monsters 549/550, AI sc_ai_549_Gau) and resumes the battle. The stand-in rejoins on its turn
    // (mode 1 -> Event00 -> JoinGauInBattle) but ran off when hit (mode 3 -> Event02), and auto-battle hits it. The fix
    // is data: port_bnw_party_events drops the stand-in's "attacked" jumps, as the SNES gives no window to hit him.
    // Here: log lines for both ends, and the harness "gaureturn" that queues the return for the next battle.
    static class VeldtReturn
    {
        const int GauModeLocal = 2;          // sc_b_0239 ScriptLocal[2] "BattleGauMode"
        const int ReturnAfterWin = 2;        // SetGauMode: Gau comes back after this battle is won
        internal static string Last = "";    // tests
        static int armed = -1;               // harness: the mode the next SetGauMode sets (-1: none)

        public static void Apply(Harmony h)
        {
            Hooks.Patch(h, typeof(SysCall), "SetGauMode", typeof(VeldtReturn), postfix: nameof(SetGauMode_Postfix));
            Hooks.Patch(h, typeof(SysCall), "JoinGauInBattle", typeof(VeldtReturn), prefix: nameof(JoinGauInBattle_Prefix));
            Hooks.Patch(h, typeof(DiveConditionFunction), "ActEndUpdate", typeof(VeldtReturn), postfix: nameof(DiveActEnd_Postfix));
            PrKit.Harness.Register("divewatch", a => DiveWatch == "" ? "divewatch: nothing (no Leap held a battle open)" : "divewatch: " + DiveWatch,
                "divewatch: the state logged when Gau's Leap (Dive) didn't end the battle in 3 s");
            PrKit.Harness.Register("gaureturn", a => { armed = ReturnAfterWin; Last = ""; return "gaureturn: the next battle's SetGauMode queues Gau's return"; },
                "gaureturn: the next Veldt-script battle (sc_b_0239, e.g. encount 2002) brings Gau back after the win (script-local 2 = 2); " +
                "its log says whether he rejoined ([veldt] ... rejoins)");
            PrKit.Harness.Register("gaumode", a =>
            {
                if (a.Length < 1 || !int.TryParse(a[0], out int m)) return $"gaumode: now {Mode()}; gaumode <n> arms the next battle";
                armed = m;
                return $"gaumode: the next battle's SetGauMode sets BattleGauMode {m} (1: Gau in the party on the Veldt, Leap ends the fight)";
            }, "gaumode [n]: the next Veldt-script battle's BattleGauMode (script-local 2) as SetGauMode would set it on the Veldt " +
               "(harness battles crash while standing on the Veldt: test elsewhere)");
            PrKit.Harness.Register("leap", a => Leap(a), "leap [PR monster id] [chant] [dead] [other]: queue Gau's Leap (command 24, ability 16) at the first enemy standing (or that monster); " +
                "chant: through the ATB progress's SetActData, as a menu choice (its chant time: BNW's wait rule); dead: at a monster already down; other (with chant): another member's Fight chosen first");
        }

        static int Mode() => DataStorage.Instance?.Get(DataStorage.Category.kScriptLocalVariable, GauModeLocal) ?? -1;

        public static void SetGauMode_Postfix()
        {
            try
            {
                Plugin.L.LogInfo($"[veldt] SetGauMode: BattleGauMode {Mode()}");
                if (armed >= 0)
                {
                    DataStorage.Instance?.Set(DataStorage.Category.kScriptLocalVariable, GauModeLocal, armed);
                    Plugin.L.LogInfo(armed == ReturnAfterWin ? "[veldt] harness: Gau's return queued for this battle"
                                                            : $"[veldt] harness: BattleGauMode {armed} for this battle");
                    armed = -1;
                }
                if (Mode() == ReturnAfterWin)
                {
                    Last = "queued";
                    Plugin.L.LogInfo("[veldt] Gau comes back after this battle (BattleGauMode 2)");
                }
            }
            catch (Exception e) { Hooks.WarnOnce("VeldtReturn.SetGauMode", e); }
        }

        // harness leap: Gau's Leap queued the way the battle menu does (AiFights.Pummel's pattern)
        static string Leap(string[] a)
        {
            var battle = BattlePlugManager.Instance();
            if (battle == null || !battle.IsBattle()) return "error: not in a battle";
            var gau = battle.GetPlayerUnits().FirstOrDefault(p => p?.ownedCharacterData?.characterStatusId == 12);
            if (gau == null) return "error: Gau isn't in this battle";
            bool chant = a.Contains("chant");   // the menu's way: the ATB progress starts the act's chant (BNW's wait rule)
            int want = a.FirstOrDefault(x => int.TryParse(x, out _)) is string n ? int.Parse(n) : 0;
            bool dead = a.Contains("dead");   // at a monster already down (a Bomb that blew itself up before the Leap ran)
            var target = battle.GetEnemyUnits().FirstOrDefault(u => (dead ? u?.BattleUnitDataInfo?.Parameter?.CurrentHP <= 0 : u?.BattleUnitDataInfo?.Parameter?.CurrentHP > 0) && (want == 0 || u.GetMonster()?.Id == want));
            if (target == null) return "error: no enemy standing";
            var command = PrKit.Master.Get<global::Last.Data.Master.Command>(24);
            var ability = PrKit.Master.Get<global::Last.Data.Master.Ability>(16);
            var abilities = new Il2CppSystem.Collections.Generic.List<global::Last.Data.Master.Ability>();
            abilities.Add(ability);
            var targets = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<BattleUnitData>(1);
            targets[0] = target;
            var act = new BattleActData(gau, targets, command, abilities, BattleActData.ActType.Normal,
                BattleActData.TargetType.SingleEnemy, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false);
            var atb = chant ? battle.BattleProgress?.TryCast<BattleProgressATB>() : null;
            if (chant && atb == null) return "error: no ATB progress";
            // first: another party member's Fight waiting on its chant (the Deck, 2026-11-27: Sabin's Fight was chosen
            // before the Leap, whose 0 s wait put it ahead)
            string other = "";
            if (atb != null && a.Contains("other"))
            {
                var mate = battle.GetPlayerUnits().FirstOrDefault(p => p != null && p.ownedCharacterData?.characterStatusId != 12 && p.BattleUnitDataInfo?.Parameter?.CurrentHP > 0);
                if (mate == null) return "error: nobody else to act";
                var fabilities = new Il2CppSystem.Collections.Generic.List<global::Last.Data.Master.Ability>();
                fabilities.Add(PrKit.Master.Get<global::Last.Data.Master.Ability>(1));
                var ftargets = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<BattleUnitData>(1);
                ftargets[0] = target;
                atb.SetActData(new BattleActData(mate, ftargets, PrKit.Master.Get<global::Last.Data.Master.Command>(1), fabilities, BattleActData.ActType.Normal,
                    BattleActData.TargetType.SingleEnemy, BattleActData.ArmType.Right, (BattleActData.ActAbilityType)0, 1, false));
                other = $" after {PrKit.Harness.Who(mate)}'s Fight";
            }
            if (atb != null) atb.SetActData(act);
            else battle.BattleActExection.SetBattleActData(act);
            int mode = -1;
            try { mode = Mode(); } catch (Exception) { }   // (no script locals once the battle's script ended)
            return $"queued Gau's Leap at monster {target.GetMonster()?.Id}{(atb != null ? " (chanted)" : "")}{other}; BattleGauMode {mode}";
        }

        // Leap that didn't end the fight (the user, Deck, 2026-11-27: "gau vanished, his ATB kept filling, the bombs
        // fought on"). The PR's end: DiveConditionFunction.ActEndUpdate (every act end, every unit's conditions)
        // sets BattleEndJugment.isForceOnSave; Execute (BattleActExection.EndFinishingState, at an act's end, not
        // while the interrupt stack's next act is NotDelete/Final) turns it into end 6, only while enabled, resultType
        // None and CurrentEndJugmentType Normal. Logged once a battle: Gau dived for 3 s and the battle still on.
        static float diveSince = -1, leapSeen = -1;
        static bool diveLogged, diveEndRan, liftLogged;
        internal static string DiveWatch = "";

        public static void DiveActEnd_Postfix()
        {
            if (diveEndRan) return;
            diveEndRan = true;
            try
            {
                var j = BattlePlugManager.Instance()?.BattleEndJugment;
                Plugin.L.LogInfo($"[veldt] Dive ActEndUpdate ran: isForceOnSave {j?.isForceOnSave}, enabled {j?.GetEnabled()}, result {j?.GetEndJugment()}");
            }
            catch (Exception e) { Hooks.WarnOnce("VeldtReturn.DiveActEnd", e); }
        }

        public static void Track()
        {
            try
            {
                var battle = BattlePlugManager.Instance();
                if (battle == null || !battle.IsBattle()) { diveSince = leapSeen = -1; diveLogged = diveEndRan = liftLogged = false; return; }
                float now = UnityEngine.Time.time;
                bool judged = battle.BattleEndJugment != null && (int)battle.BattleEndJugment.GetEndJugment() != 0;   // ending: nothing to watch
                var staged = battle.BattleActExection?.inStagingBattleActData;
                if (staged != null && staged.Command?.Id == 24 && staged.AttackUnitData?.TryCast<BattlePlayerData>()?.ownedCharacterData?.characterStatusId == 12)
                    leapSeen = now;   // Gau's Leap playing out
                var gau = battle.GetPlayerUnits()?.FirstOrDefault(p => p?.ownedCharacterData?.characterStatusId == 12);
                bool dive = gau != null && HasDive(gau);
                if (!dive && diveSince >= 0 && !liftLogged && !judged)   // Gau's Dive gone while the battle runs (Gau kept in the party?)
                {
                    liftLogged = true;
                    Plugin.L.LogWarning($"[veldt] Gau's Dive lifted in battle after {now - diveSince:0.0} s: " + DiveState(battle));
                }
                if (!dive) diveSince = -1;
                else if (diveSince < 0) diveSince = now;
                if (diveLogged || judged) return;
                bool held = dive && now - diveSince >= 3;
                bool after = leapSeen >= 0 && now - leapSeen >= 6;   // a Leap played 6 s ago and the battle is on
                if (!held && !after) return;
                diveLogged = true;
                DiveWatch = $"Gau {(gau == null ? "not in the battle" : dive ? "dived" : "has NO Dive")}" +
                            (leapSeen >= 0 ? $" ({now - leapSeen:0.0} s after his Leap played)" : "") + "; " + DiveState(battle);
                Plugin.L.LogWarning("[veldt] Leap didn't end the battle in 3 s: " + DiveWatch);
            }
            catch (Exception e) { Hooks.WarnOnce("VeldtReturn.Track", e); }
        }

        static bool HasDive(BattleUnitData u)
        {
            var list = u?.BattleUnitDataInfo?.GetConfirmedConditionType();
            if (list == null) return false;
            foreach (var c in list) if ((int)c == 614) return true;
            return false;
        }

        static string Act(BattleActData a)
        {
            if (a == null) return "-";
            string ab = "";
            try { if (a.abilityList != null && a.abilityList.Count > 0) ab = $" ability {a.abilityList[0]?.Id}"; } catch (Exception) { }
            return $"{PrKit.Harness.Who(a.AttackUnitData)} cmd {a.Command?.Id}{ab} type {a.CurrentActType}";
        }

        static string Acts(Il2CppSystem.Collections.Generic.List<BattleActData> l)
        {
            if (l == null) return "null";
            var s = new System.Collections.Generic.List<string>();
            for (int i = 0; i < l.Count; i++) s.Add(Act(l[i]));
            return "[" + string.Join("; ", s) + "]";
        }

        static string DiveState(BattlePlugManager battle)
        {
            var sb = new System.Text.StringBuilder();
            var j = battle.BattleEndJugment;
            try { sb.Append($"judgment enabled {j?.GetEnabled()} result {j?.GetEndJugment()} type {j?.CurrentEndJugmentType} isForce {j?.isForce} isForceOnSave {j?.isForceOnSave}"); }
            catch (Exception e) { sb.Append($"judgment ? ({e.GetType().Name})"); }
            sb.Append($"; Dive ActEndUpdate {(diveEndRan ? "ran" : "never ran")}");
            try
            {
                var f = PrKit.Ff6.SnesAiHost.Current;
                sb.Append(f == null ? "; AI host: no fight" : $"; AI host HoldEnd {f.HoldEnd} until {f.HoldUntil:0.0} (clock {f.Clock:0.0})");
            }
            catch (Exception e) { sb.Append($"; AI host ? ({e.GetType().Name})"); }
            try
            {
                var x = battle.BattleActExection;
                sb.Append($"; act state {x?.nowState} staging {Act(x?.inStagingBattleActData)} stack {Acts(x?.battleActDataStack)} " +
                          $"interrupt {Acts(x?.battleActDataStackInterrupt)} chain {Acts(x?.chainActDataStack)}");
            }
            catch (Exception e) { sb.Append($"; acts ? ({e.GetType().Name}: {e.Message})"); }
            try
            {
                sb.Append("; players [" + string.Join(", ", battle.GetPlayerUnits().Select(u => $"{PrKit.Harness.Who(u)} {PrKit.Harness.Hp(u)}")) + "]");
                sb.Append("; enemies [" + string.Join(", ", battle.GetEnemyUnits().Select(u => $"{PrKit.Harness.Who(u)} {PrKit.Harness.Hp(u)}")) + "]");
            }
            catch (Exception e) { sb.Append($"; units ? ({e.GetType().Name})"); }
            return sb.ToString();
        }

        public static void JoinGauInBattle_Prefix()
        {
            try
            {
                string standIn = "";
                var battle = BattlePlugManager.Instance();
                if (battle != null && battle.IsBattle())
                    standIn = string.Join(", ", battle.GetEnemyUnits().Where(u => u != null).Select(u =>
                        $"monster {u.GetMonster()?.Id} HP {u.BattleUnitDataInfo?.Parameter?.CurrentHP}"));
                bool auto = global::Last.UI.BattleUIManager.Instance?.IsAuto() ?? false;
                Last = $"rejoined (auto-battle {(auto ? "on" : "off")}; stand-in {standIn})";
                Plugin.L.LogInfo("[veldt] Gau rejoins after the battle: " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("VeldtReturn.JoinGau", e); }
        }
    }
}
