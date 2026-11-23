# Brave New World on the Pixel Remaster

Owner: BNW lead. Moved verbatim from `pr/PORTING.md` on 2026-11-20 (roadmap item 5); `git log --follow` / the split commit trace any paragraph. Status rows stay in `pr/PORTING.md`.

## Live progress (newest first)

- 2026-11-23 (BNW 0.25.0 session): **v0.26.3 released on GitHub (Latest) and on the Nexus draft (mod 167, private).**
  https://github.com/thomasjteachey/ff6pr-brave-new-world/releases/tag/v0.26.3
  - Vargas froze after Gale Cut (user, Deck): BattleAI.ViewMes threw ErrorFactorBase every frame after BattleEvent 07
    (the PR's Mash-joins scene holds the message window). Fix (kit SnesAiHostHooks.cs, another session's edit): after a
    PR-played battle event, the turn's remaining attack and battle lines are dropped. Laptop run 20261123-120754: lines
    dropped, battle ends, field runs on. Kit source not yet published (pin 6a1024c); the DLL carries it.
  - Zip SHA-256 ecf2e4e6...0c5a; DLL md5 e4ae6dda = PC install (the Deck has 639d69f0: same code labelled 0.26.2; the game was running). The 11-22 "v0.26.2" release by another session was the renge-only attempt that didn't cure it.
- 2026-11-22 (BNW 0.25.0 session): **v0.26.1 released on GitHub (Latest) and on the Nexus draft (mod 167, private).**
  https://github.com/thomasjteachey/ff6pr-brave-new-world/releases/tag/v0.26.1
  - User-reported fixes (notes: boomerang-dual-wield.md): `<d7>` before boomerang names (ff6snes ICONS from 0xD7);
    Harvester and 10 other heal spells missing on allies (port_spells adds the HP-recovery function); two of the same
    Boomerang/dagger dual-wield (BattleExtras TwoSword_Postfix); Vargas's Gale Cut freeze (port_spells keeps the
    template's renge when targeting is unchanged).
  - Checks: 7 Debug builds, Rules.Tests 40/40, PrKit.Tests 19/19, test_mod 51 OK (test_wc 146 OK by the fixer).
    Zip SHA-256 26342a29...e2b6, DLL matches the PC install. Kit pin 6a1024c.
- 2026-11-22 (BNW 0.25.0 session): **v0.26.0 released on GitHub (Latest) and on the Nexus draft (mod 167, private).**
  https://github.com/thomasjteachey/ff6pr-brave-new-world/releases/tag/v0.26.0
  - X-Fight keeps the weapon's procs: RelicRules.XFightProc_Prefix/Postfix on FightActFunctionToAddAbility.SetAddtiionAbility
    (IsDisorderlyShootAttack answers false inside it; the PR returned there before adding the weapon ability).
  - Stopped at 94% weekly: Hotswap delay, X-Fight strike-2 retarget, Giant's dying attack by poison/sap (now plugin-side
    possible or kit, freeze over) and the OPEN research rows are left (bnw-v026-progress.md).
  - Checks: BNW + WC Debug builds, Rules.Tests 40/40, test_mod OK, QA bnw-xfight smoke (laptop): 13/13 audit + 10/10 item
    hooks, no [patch]/exception. Zip SHA-256 d11b1eda...a6d8, DLL matches the PC install. Kit pin 6a1024c.
- 2026-11-22 (BNW 0.25.0 session): **v0.25.1 released on GitHub (Latest) and on the Nexus draft (mod 167, private).**
  https://github.com/thomasjteachey/ff6pr-brave-new-world/releases/tag/v0.25.1
  - Audit gaps (unclear rows): rods as items at BNW power with defense (profile_bnw ability_overrides 277-279, content
    flag group 4) and no item casts on weapons 58-60; no Earring/esper boost on item casts; Overcast on zombie-immune
    -> KO; petrify-lifting heals on petrified targets (CheckHaveCondition prefix); X-Fight 1/32 crits; dual-wield Jump
    right hand only. Not built: Hotswap delay, X-Fight procs/strike-2 retarget, Giant's dying attack by poison (kit,
    frozen), the OPEN research rows. Notes: side-session-notes bnw-v026-progress.md.
  - bnw-final's unconfirmed checks: ai-440 + endfight-rewards PASS (test fixes: aikeep on, "formation -1"); Whelk
    PASS split; ai-bosses-2#b battles all end, its pass list names monsters outside its battles (test definition).
  - Checks: 7 Debug builds, Rules.Tests 40/40, PrKit.Tests 19/19, test_mod 51 OK, QA bnw-gaps smoke (laptop): [audit] 13
    of 13 hooks, [items] 10 of 10, no exceptions. Behaviour of the new rules not yet hand-checked in game.
  - BraveNewWorld-PR.zip SHA-256 6fc63468...f233; its DLL matches the PC install. Kit pin 6a1024c.
- 2026-11-21 23:00 (BNW 0.25.0 session): **v0.25.0 released on GitHub (Latest) and uploaded to the Nexus draft (mod 167,
  still private).** https://github.com/thomasjteachey/ff6pr-brave-new-world/releases/tag/v0.25.0
  - Landed bnw-v025-items (tools/worktree.py land): Throw stars spread + split, Golem, Palidor's Jump, MP-crit procs,
    audit batch C (Shadow's leave, Zozo unequip, Terra/Edgar optimize, Ultros EL respec, spear dual-wield ban, Umaro's
    equip menu, event refills fill MP, NoGainz + Veldt), BattleUi on the kit's SnesInformativeMiss, port_bnw_descriptions.
  - Relic fix: RelicRules.MagicCount_Postfix on CalcExecuteCorrection.GetEquipAccessoryNum (the magic list only):
    count capped at 1, the esper's +25% when 0. The old GetAccessoryMagicCorrectionValue hook never ran (inlined).
    Still open: item casts (rods/Windbreaker) get the boost and vanilla power.
  - Checks: 7 Debug builds, Rules.Tests 40/40, PrKit.Tests 19/19, re-port, test_mod 51 OK; QA bnw-v025-c (PC 22:45):
    umaroequip ok (first try at load misses the vtable entry, the retry unlocks it), [items] 10 of 10 hooks,
    bnwrespec, bnwgear, no fail lines. Kit pin 6a1024c (kit frozen for WC 0.6.10).
  - BraveNewWorld-PR.zip: 923 files, SHA-256 2e884b2f...5665; its DLL matches the PC install.
- 2026-11-21 19:47 (BNW lead): **v0.24.0 released on GitHub (Latest).** The user chose "ship now" with bnw-final's
  failures as known issues. https://github.com/thomasjteachey/ff6pr-brave-new-world/releases/tag/v0.24.0
  - Repo commit b6ae655, tag v0.24.0, kit 6a1024c.
  - BraveNewWorld-PR.zip: 841 files, 1,422,059 bytes, SHA-256 0fb6a574...f903. Its DLL matches the PC install; the
    download's hash checks out.
  - Known issues in the notes: revive amounts, GP Rain cost, First Strike and the statue's parts are untested; battle-end
    and boss-AI runs unconfirmed; relic Magic and rod/Windbreaker casts for 0.24.1.
  - Not done: the Nexus page (none exists yet; must stay private) and the Steam Deck push (`deploy.sh --bnw --deck`).
    Installing BNW on the PC set WC aside (modswitch restores it).
- 2026-11-21 06:57 (BNW lead): **PAUSED (the user: "pause everything"). Resume note:**
  - **State:** the tree is consistent. All seven plugins build in Debug; Rules.Tests 36/36, PrKit.Tests 19/19, test_mod
    50/50. Nothing of mine is running, and nothing is committed or published. The ATB child (local_57cc0ffb) has been
    told to pause too.
  - **Uncommitted, mine:** EngineTests.cs, Schematics.cs, SuplexRules.cs, kit SnesSuplex.cs (kit lead reviewed OK; in
    the ~08:00 kit publish), bnw_publish.py README, bnw.md. Also build/dist's release notes and Nexus text.
    PrKit/Harness.cs is the kit lead's.
  - **QA's BNW turn at the pause:**
    - bnw-atb: selftests failed 6, all fixed on main. The harness run froze: QA's step order, fixed.
    - configcheck: blocked at map 112.
    - gaps-a: failed, because headless Wait freezes menu battles.
    - bnw-13: running.
    - bnw-atb3 (selftests rerun + fixed atbtest): queued after bnw-13. Pass lines are the "self-test ok" lines.
  - **Next on resume:**
    1. A harness command `bnwuse <party slot> <PR item id> <target slot>` that builds an item act and queues it through
       `battle.BattleActExection.SetBattleActData` (as BcCommands does). Then gaps-a's Tonic / Phoenix Down / Phoenix
       Tear / Defibrator checks run without menus. First Strike: read each gauge on the battle's first frame.
    2. test_batch.py:1052 (watch_map) and :1064 (warp_free) still reject sc_map_* scripts. Runner-speed was told; check
       it landed.
    3. After bnw-13 and bnw-atb3 pass: release candidate, bnw-final, then the 0.24.0 release steps. The kit pin comes
       from the ~08:00 kit publish.
- 2026-11-21 06:49 (BNW lead): **QA bnw-atb selftests (PC, 06:34): 6 FAILED; 4 fixed here, 3 with the ATB child.**
  - **Suplex, a real bug:** BNW's Suplex (ability 134) is function 3 (function group 469; vanilla's is 484, function 2).
    BattleFunction3 runs BattleBasicFunction.HitCalc, so the kit's BattleFunction2 postfix never turned a hit. Kit
    `SnesSuplex`: `SnesSuplexMod.BasicFunctions` (BNW only) also hooks BattleBasicFunction.HitCalc; one
    `Turn(BattleBaseFunction)`. Kit lead told.
  - **Schematics, a test bug:** the PC save is the World of Balance with Edgar, and Julian gives only in the World of
    Ruin.
  - **Back-row cover:** covered 0 of 200 with Edgar moved to the front row. The test now gives the knight full HP and no
    Near Fatal for the run, and logs his HP and statuses beforehand.
  - **Noise Blaster, a test bug (never passed):** `ActUtility.GetUseItemAbility` gives no ability for item 30. The test
    falls back to the Tools command's ability 24.
  - With the ATB child: the clock wait (0.46 s vs BNW's 0.57), the "rules: ok" line with every rate 0, and Stamina evasion
    (rolls 0).
  - Checked: Debug builds of all seven plugins, PrKit.Tests 19/19. Suplex is "(if verified)" in the notes until QA
    reruns the selftests.
- 2026-11-21 06:38 (BNW lead): **0.24.0 pre-staged.** `build/dist/BraveNewWorld-nexus.txt` now matches the release
  notes draft (battle timing, items and skills, Bushido, weapon rules, recovery points, the seal after Kaiser, the 0.25.0
  "not in yet" list, one save point in a room the PR lacks; `<<IF VERIFIED>>` / `<<IF NOT VERIFIED>>` lines for QA's
  bnw-atb-1 / bnw-gaps-a / bnw-13). `tools/bnw_publish.py`'s README says ~96% (it said 87%, monster art missing). Dry
  run into scratch: 83 files, 1,134 KB, kit at cf21b07e32 (the kit publish refreshes it first), no user paths; every
  kit module the BNW tools import is in the kit repo. Offline: test_mod 50/50, Rules.Tests 36/36. Waiting on QA's PC
  turn (~07:05).
- 2026-11-21 02:20 (bnw-atb, the BNW lead's child): **audit batch B, battle formulas and evasion: 5 of 6 in, plugin**
  (`BnwPlugin/AuditRules.cs` + probe self-tests `AuditTests.cs` "audit ..."; Debug builds of all five plugins, Rules.Tests
  36/36; data: bnw_plugin_data spirit.stamina_first_abilities / boss_fractional / respect_row_abilities and
  die_at_zero_mp; in game: QA, with bnw-atb-1's probe run). Asm first, the PR's side decompiled (tools/decomp/audit_evade,
  audit_imp, audit_counter). (1) Stamina, the only evasion (c0.asm StamPhase from C2/22A8; StamEvdChk C2/23B2): 13
  fractional / MP / North Cross abilities miss when rand(0..127) <= Stamina, in BattleBasicFunction.IsFixedMiss (and its 4
  overrides); BNW gives them hit rate 255, so the PR's MBlock roll never runs. (2) Fractional on bosses (C2/50F4, effect
  $30): the PR's miss on an IsBoss() / KO-resistant enemy (BaseValueFixedHPMagicFunction.Calc) becomes the provider's
  magic damage at BNW's power (Demi 8, Quartr 12, Cave In 12, Cyclonic 14, Grav Bomb 15, Air Blast 8, Lode Stone 10).
  (3) Imp (C2/320D, C2/5xxx, C2/46E6 ClearImp, OvercastFix): BattleUtility.IsSelectableCommand's Imp block lifted (groups
  400 spells, 302 special commands) but for Morph; Imp misses / isn't added on a Morphed unit; Morph takes Imp off. (4)
  Counters on misses (C2/343C ParryCounter): CounterModule.IsCounter lets a Miss through its own checks (IsActable, < 2
  queued); every counter kind, as BNW. (5) Back row (C2/3392 GetRowFlag): BNW halves only its respect-row physical skills
  (Bushido 85/88/91 = PR 124/127/130, Pummel, Suplex, Bum Rush); GetFightStatus x (BNW's / the PR's): the PR halved every
  Bushido unless CalcExecuteCorrection.IsIgnoreRearguardAttackAbility, never a Blitz (isIgnoreCorps). (6) Dies at 0 MP
  (monster byte $12 bit 0, 47 BNW monsters, 62 PR ids with Coliseum copies): each enemy's isUnableMP set to BNW's every 20
  battle frames, so the PR's own 0-MP death runs (BattleConditionController, UnableFight). **Not done: Mindblow itself**
  (BNW Bushido 86: MP damage, power 10, effect $1A) is still the PR's Retort stance in the port (ability 125, power 0,
  function group 477; its counter strike 261): a porter function override, the lead's. **NOT A GAP:** "Stamina vs HP drain"
  (readme 858-871): c0.asm StamPhase (436-452) takes only fractional ($11A4 bit 7) / MP ($11A3 bit 7) / North Cross ($52):
  Drain keeps the normal evasion in BNW too; "Bushido halved from the back row": only 85/88/91 carry BNW's respect-row bit
  (spell byte 7 bit 4), the other techniques aren't halved (the PR halved them all: now undone).
  02:29: no detours on tiny native code after the battle-start crash: IsFixedMiss's overrides (an 11-byte thunk, a shared
  3-byte return) and EnemyCharacterParameter.IsBoss (11 bytes) dropped; the base IsFixedMiss covers the rule.

- 2026-11-21 02:00 (bnw-atb, the BNW lead's child): **ATB, plan_atb.md 2-4, landed** (on the kit lead's pieces; Debug
  builds of all five plugins; PrKit.Tests 18/18, WcAi.Tests 62/62, Rules.Tests 34/34; in game: QA bnw-atb-1, not yet
  run). `BnwPlugin/AtbRules.cs`: Speed Balance = the kit's SnesAtb with BNW's C2/09D2 numbers (characters 75/90/60 x
  (Speed + 51), monsters x1.5 x (Speed + 20), no battle-speed term; fill only, the waits: below); BattleFormulas' ratio
  on the PR's rate (~19% slow both sides) removed; Quickfill = SnesQuickfill (x2 while no one's turn is up: gauges,
  chants, status timers, running away); nATB-only = SnesActionFreeze (time held while BattleActExection.GetStatingAct) +
  BattleSettingsLock (Wait, battle speed x1, the Active/Wait and Battle Speed rows hidden, the saved config untouched).
  Config: `[Battle] AtbFormula`, `Quickfill`, `nATBOnly` (a new key, default on: the old `nATB`, default off, never
  stopped time: NatbRules read BattleActExection.nowState, which the game never writes; NatbRules.cs deleted). Kit
  (reviewed): SnesBattleClock + SnesAiMod.BattleClockDt, so BNW's AI script timers (battle and monster timers) run on
  the battle clock (held, x2) as on the SNES. Self-test `AtbTests.cs` (probe "atb", harness `atbtest`): the rule
  numbers, ATBCalc per second at Speeds 1-128, Wait and x1, then 12 s recorded frame by frame on auto: no gauge or chant
  moves while an action holds time, the fill = BNW's rate x the frame's Quickfill factor, the AI timer = the battle
  clock. Left: the PR's elapsedTime and dying-special timer neither held nor doubled (readers unknown; QA's line logs
  elapsedTime); the Config menu's cursor with rows 1-2 hidden (not seen); BNW's status clock add 75/90/60 ($3ADD) only
  matters if BNW moves onto SnesStatusClock. 02:35: command waits now BNW's SNES timing with AtbFormula on
  (Rules/BnwAtb.cs, WaitGauge.AbsoluteWait: BNW's table on BNW's rate; Fight at Speed 33 0.70 s, was 1.64; Jump's air
  $70; Summon $40; Rules.Tests 35/35; atbtest times each wait on the battle clock). Palidor's riders ($C8 a tick) are
  timed as Jump's (not told apart in the PR; within ~6%).

- 2026-11-21 05:24 (BNW lead): **Bob never sold in play, fixed** (found by bnw-atb's review): his cabin NPC had the PR's
  message-only talk action (4) with the sale script 9963, so talking to him showed his old line; vanilla's 183 scripted
  NPCs use action 2. port_bnw_mechanics.bob sets action 2; re-ported 05:22, test_mod 50/50. A scan of every ported
  entity file finds no other scripted NPC on action 4. QA checks talking to him (bnw-mech-1 ran the script directly).
  bnw-atb's batch C (Shadow's leave, Zozo unequip, Terra/Edgar optimize at her scenario's start, the Ultros respec,
  the Spear ban, Umaro's equip menu, party-select MP, NoGainz + Veldt; Dance backgrounds PR-native) is on branch
  bnw-v025-items with the 0.25.0 battle items, not landed.

- 2026-11-21 04:57 (BNW lead): **Cyan's dream works end to end** (QA dream17, laptop, no graphics, 0801d2fb): the bed, 520's
  Cyans and Stooge #1, 521, the Stooges' fight (sc_b_2028 through Play, Shemp), sc_e_0901_1 restoring the flags
  (fought=1), 0902, the train, sc_e_0939_1 and its after-map sc_e_0939_2: map 300 with party [Cyan, Locke, Edgar,
  Sabin] and done=1. (The test reads the after-map script's results after a wait: a reserved script isn't "alive".)
  Release notes: the Stooges with Shemp no longer "(if verified)".

- 2026-11-21 04:29 (BNW lead): **the Dream Stooges' fight plays** (QA dream15, laptop, no graphics, 0801d2fb): the real path
  (the bed, 520's Cyans and Stooge #1, 521, 0899) to `script sc_e_0901` and a wait for the battle with no presses:
  CreateObj / InBattle / Play all with "; script sc_b_2028" (no takeover), formation 346; ko 288 / 290 / 289 brought
  Shemp in ("enemy AI turn: monster 37 slot 3"). The earlier stalls were the test's presses during the battle's
  start (the kit now holds them) plus the dream flags; the push-back scene (sc_e_0900) never ends when the test runs
  it from the map's entrance (it walks the player to the door; in play you're at the door), so the test skips it.
  Left: the dream's end (sc_e_0901_1 restoring the flags, fought=1, 0902, train, done=1): QA's timing read it before
  0901_1 ran. Enemy AI row 100%; BNW ~96% (PORTING + dashboard).

- 2026-11-21 01:40 (BNW lead): more audit gaps, plugin (Debug builds; not yet in game): Chakra restores (Stamina + Level)/2
  MP to the party but Sabin (ChakraGpRules + profile: 137's process_prog None and an Ether-like MP group) and GP Rain costs
  Stamina x 10 (prefix on BattleAddExpPlug.AddPlayerSideLostGil inside a GIL_THROW act); WeaponEffects: Anti-Air x2
  grounded / x3 floating on Fight only (GetHawkEyeCorrectionValue), Zantetsuken / Ichimonji's 25% x2 critical against
  death-immune targets and on Mug / Jump; FirstStrike: BNW's starting ATB (c2.asm 1615-1700) over the PR's random
  ranges (postfix on BattleProgressATB.InitATBGauge, BattlePopPlug.GetResult). Harness: `bnwgive`, `exp` (QA).
  Asked the kit lead for the kit side of the ATB plan (SnesAtbRules BNW parameters, SnesQuickfill, SnesActionFreeze,
  ConfigRows.Hide); session_budget had no room for a child. Plans: docs/bnw_audit_2026-11-20/plan_*.md.

- 2026-11-21 01:05 (BNW lead): **audit batch A, data part, ported (test_mod 50/50; in game: QA bnw-gaps-a).** BNW's EXP
  curve (port.port_exp_table, opt-in: SNES ED/8220 x8 cumulative, level 2 at 64 where the PR had 32) and the level cap
  of 50 (MAX_LV via game_constants); healing items as fractions of max (BnwPlugin/ItemHealRules.cs: Tonic 1/2, Potion
  and X-Potion 3/4, Hi-Ether and X-Ether 3/4 MP; a postfix on CalcControllerProvider's recovery calcs by item id, in
  battle; the menu's own use is QA's check); Restore (BNW's Remedy) and Regen / RegenX cure HP and Elixir / Megalixir
  lift statuses (port.py function_overrides, opt-in: function groups found or made by the spell porter's allocator).
  Next: Chakra's MP and GP Rain's cost, First Strike / Quickfill / Speed Balance / nATB, the weapon-effect module
  (plans being researched), then the rest of batch B and C.

- 2026-11-21 01:40 (BNW lead): the kit side of BNW's ATB is in main (kit lead; all opt-in): SnesAtbRules with BNW's Speed
  Balance parameters, SnesAtb.Patch(h, false) (rates only), SnesQuickfill, SnesActionFreeze (via GetStatingAct; NatbRules'
  nowState is never written by the game, so BNW's own freeze never froze anything), BattleSettingsLock (Wait, speed x1,
  rows hidden by ConfigRows.Hide). PrKit.Tests 18/18 with the plan's 379 / 350, sweep 28/28. The BNW ATB child
  (local_57cc0ffb) wires it into BnwPlugin and asks QA for the timing run; its open questions: the Config cursor with rows
  1-2 hidden, and whether elapsed battle time and the dying timer stay unchanged. ai-bosses-1#rest (laptop, 01:04):
  10/10 of its own fights logged AI turns; its 9 misses are done1's fights, which chunk() keeps in the pass list but skips.
  Dialogue 2962: the kit applies ted-ex's special-graphics NPC fix for everyone (my call). Landed 01:35; BNW re-ported
  01:40: repurposed_dialogues() is 33 with 2962, and the port log is unchanged (3177 lines, 13 "repurposed: PR's kept")
  because no PR key maps to 2962 in build/dialog/dialog_map.json (that map doesn't use snes_events), so its lines can't
  change. test_mod 50/50. ai-bosses-2#a: 457 (Chadarnook) lost under aikeep, as BNW does: the demon form's special is
  C7 (unblockable Death; vanilla 48 Condemned), and it counters each hit with it; QA reorders 457 or gives Death
  protection there. ai-bosses-2#b: 433 (the opening Whelk) lost the same way: BNW's shell (SNES 256, level 99, magic
  255; vanilla 4 / 5) answers any hit with Discharge on all (script #256, the readme's "ATB tutorial" gimmick), 9999
  each; the test hit the shell. Also runs last now. My calls to bnw-atb (01:45): absolute SNES command waits; hold or
  double only the elapsed-time / dying timers that affect play; headless fights need auto on under the forced Wait, and
  the lock steps aside in harness/probe runs if Wait + auto still freezes.
  **Dream, correction:** the Stooges' fight 8346 sticking in "battle phase PreeMptiveMes" is real, not the test's
  endfight. It happens in every dream run since 11-20 18:07, including the Deck run of 01:32 with no endfight, and no
  other battle ever shows that phase; the battle never reaches its ATB setup. 8346 = vanilla party 461's row (script
  9028 = sc_b_2028, same layout; get_value 6 -> 10) with BNW's AI host. sc_e_0900 now ends (dream-1 7/9). The Deck's
  6-min wait before the battle is probably -nographics (the laptop reached it in 6 s). QA "dream-diag": battle 461 vs
  8346 vs 8410 from the field, to split host binding / sc_b_2028's SysCall / the field event.
  02:25: QA's visit test ran 8346 from the field on map 112 to Play, and 8410 / 8377 pass (ai-3-triggers), so the party
  and new fights are fine. Every stall shows map 521's own script (sc_map_30821_2) alive during the opening instead of
  sc_b_2028. Next diag: 8346 and 461 started on map 521 without the event. Shemp (SNES 37, BNW formation 346's 4th
  slot, not shown at start) is in 8346's plugin data ([37, cell 6, hidden, brought in by script]); Larry brings him in
  when he falls after Curly and Moe, which the 40 s visit run never did.
  **Kaiser's seal flag (ScenarioFlag2 221) never set, fixed:** sc_bnw_kaiser_1's seal block (from sc_e_0975) keeps its
  sound handle in script-local 1, but the script declared one local, so the game threw at its first PlaySE and the
  script ended about 1 s in. port_boss_events now copies sc_e_0975's two locals; no other BNW script uses an undeclared
  local (scan of every sc_*.json). Re-ported, test_mod 50/50; QA reruns the Kaiser trigger.
- 2026-11-21 01:55 (BNW lead): **the AI boss list is done:** ai-1-rest (10 fights), ai-bosses-2 a + b (14 monsters; 457
  and 433 run last), ai-bosses-3 (8410, 8377) and ai-440 all pass headless. Status re-measured in PORTING.md: ~94%
  (Enemy AI 98%; Battle engine 91% and Events 80% now count the readme audit's open batches B and C).
- 2026-11-21 02:05 (BNW lead): batch B, the lead's half (the BNW ATB child has the other: Stamina evasion, fractional
  vs bosses, Imp, Parry, Blitz/Bushido row, Mindblow). Plugin, Debug build clean, Rules.Tests 35/35; in game: QA
  bnw-weapons. `WeaponEffects.cs`: Undead-Slayer (weapons with effective_species 1 = Demonsbane 7, Tarot 81): 50% vs
  undead, a x2 critical on death-immune targets / Mug / Jump, else KO added to the hit; Atma (30): BnwFight(Stamina) /
  BnwFight(Vigor) x 64 / Level on the PR's vanilla Atma (logged, Level term checked in game); Dice: BNW's dice are the
  PR's already (2 / 3 dice, the all-equal bonus x the value), only the Berserk x1.5 undone (IsDiceWeapon postfix,
  0x19a = ConditionType.Bersak); all limited to Fight-type strikes (types 4/15/16/608: GetFightStatus also runs for
  Blitzes). Chainsaw: NOT A GAP in outcome (the PR's 25% KO ability 205 always hits a non-immune target, EngineRules
  forces the cut on immune ones; only the animation differs). `ExploderRule.cs`: a party member's Exploder (6132)
  x2.5 of current HP (BaseValueFixedHPMagicFunction, function 33). Harness `bnwequip <member> <weapon> [hand]`.
  **After 0.24.0 (0.25.0):** Throw (stars spreading like a spell, scrolls' forced split: the PR's Throw targeting and
  the item damage's target count, not reachable safely tonight), Golem (caster's max HP, Interceptor's 50% check, no
  inherited bonuses), Palidor (hidden status on all jumpers, quick landing, ATB kept), the MP-crit procs (Punisher,
  Quartrstaff all enemies, Doomstick X-Zone: porter copies + act rewrite), batch C.
- 2026-11-21 03:57 (BNW lead): **recovery save points: tint lands** (QA bnw-recovery-shot rerun, PC graphics, 03:54):
  "map 359: 1 of 1 recovery save point(s) tinted (trigger-sparkle distance 8; 102 field entities, 1 effect entities, 1
  of object type 10)"; the shot shows the sparkle violet with a white core (the plain one blue-white). Multiplying the
  PR's blue particle art can't make it BNW's pink-red (no red in it to raise); kept as the recovery points' look (they
  read clearly apart), noted as approximate. With the heal (03:35) the recovery points are done.
- 2026-11-21 03:54 (BNW lead): **Palidor fixed for 0.24.0:** the porter had dropped ability 103's Jump condition (statuses
  rebuilt from BNW's cleared bits); bnw-atb landed the profile override alone (function_overrides 103: Jump condition 28 +
  landing act 104). Re-ported: "ability 103: functions [(9, 28), (41, 104)]", save points 10 of 11, recovery points 15
  for 14, test_mod 50/50. Palidor's other BNW fixes (landing together, ATB kept, Hidden) are 0.25.0 (branch
  bnw-v025-items).
- 2026-11-21 03:51 (BNW lead): **the dream fight's takeover is likely the test harness:** the kit lead and QA found the
  same signature in T-Edition. Harness commands sent during a battle's opening (the dream runs pressed action while
  waiting for the battle) let the field's map script take the script slot from the battle's own (map 521's script
  over sc_b_2028). The kit harness now holds every command but ping/help/state/log/aistat/quit until Play (60 s cap).
  It fits every BNW run (the door runs pressed; A1/A3 waited and played). The flag clear and the restored door moves
  stay (harmless, vanilla's door). QA's PC dream-1 decides.
- 2026-11-21 03:50 (BNW lead): recovery tint, second try: QA's PC graphics shot (03:47) found the one effect entity 8
  units from the trigger: the field's units are pixels (the sparkle object sits 8 px above its save cell), so the 2.5
  limit was in the wrong units. Limit 24 px; the tint now also sets the particle system's start color and the
  material's _Color / _TintColor (the sparkle is a FieldEffectParticleEntity). QA reruns bnw-recovery-shot. bnw-atb's
  v0.25.0 items are on branch bnw-v025-items (not landed); its Palidor fix (the porter dropped ability 103's Jump
  condition) is a 0.24.0 bug: asked to land that profile override alone now.
- 2026-11-21 03:35 (BNW lead): **recovery save points heal in game** (QA bnw-recovery, Deck, 0a271221): bnwhurt (1 HP /
  0 MP) -> `script sc_bnw_recovery` on map 359 -> everyone at full HP and MP; map 5 (a plain save point) refuses it.
  The tint found the trigger but no sparkle (no graphics on the Deck, or the sparkle isn't the class looked for):
  RecoveryPoints now takes any FieldEffectParticleEntity / FieldEffectBoardEntity or type-10 object and logs the
  counts; QA's PC graphics run (bnw-recovery-shot) shows the tint.
- 2026-11-21 03:34 (BNW lead): **dream, a correction** (QA's fake-win scan: the harness `battle` sometimes started a fight
  with no battle script; the kit fixed it): dream-A2 ("all five flags: stuck in Init, no enemies") was such a fight, so
  that row says nothing about the flags. A1 (182 only) and A3 (none) did run sc_b_ and reached Play. And the laptop's
  dream-diag v2 (8346 from the field on map 521, the dream's own flag mix) shows sc_map_30821_2 taking over in the
  opening too, with no door event: the takeover isn't the door scene's alone. It needs something of the dream run's
  state that A1/A3's fresh launches lacked. The flag clear (and restore) stays: harmless, and A1/A3 point at it.
  The PC graphics run of dream-1 decides; EncountBoss's decompile (pr/tools/ghidra/encount_boss.txt) waits for RAM.
- 2026-11-21 03:20 (BNW lead): **weapon effects seen in game** (QA bnw-weapons-2, Deck, 0a271221): Undead-Slayer crit on
  a death-immune undead ("54 -> 108", battle 88), Zantetsuken's crit on a death-immune boss x3 ("135 -> 270", 464);
  Atma earlier (bnw-weapons: the PR's 4 at Lv 1 confirms vanilla's x Level / 64, BNW's 253). 464's boss Doom Gaze is
  undead in BNW (SNES byte 18 C1, vanilla 01) and flees after 60 s unless Slowed (script #285): the fight ending won
  with him alive is BNW's. Release notes: those lines no longer "(if verified)".
- 2026-11-21 03:12 (BNW lead): **BNW's full-recovery save points** (the user: "doesn't BNW have special colored save
  points that heal you"; batch C item moved into 0.24.0 on the user's "Add them now"). BNW: 14 save tiles run CB/51C7
  (sound $E9, CA/CFBD: two statuses lifted, HP and MP full, then the save tile's flash and bits) instead of CC/9AEB; their
  sprites use palette 5 (pink-red where the plain ones' palette 6 is blue-violet). Three more palette-5 sprites (313,
  412, 414) have no tile in BNW: shown only. Port (port_bnw_mechanics.recovery_points, profile bnw_recovery_points):
  each one's PR save point (the map table within 3 cells, event layers included; RECOVERY_ROOMS for 41, 114, 240, 272,
  317, 358, paired by elimination and walk agreement) gets a step-on trigger (vanilla's EventGotoMap object) running
  sc_bnw_recovery (script 9964: SE 934 + vanilla sc_e_0555_11's RemoveCondition / RecoveryHP / RecoveryMP), listed in
  the room's package: 15 PR save points for 14 (88 is in two PR rooms). Plugin RecoveryPoints.cs tints each one's
  sparkle (the nearest type-10 effect) pink-red. Harness bnwhurt / bnwhp. Re-ported 03:09, test_mod 50/50; QA
  bnw-recovery queued. Also: the new snes_pr_maps.json (ted-maps, 03:00) places map 63's Figaro save point (10 of 11).
- 2026-11-21 02:58 (BNW lead): **the Stooges' fight, second cause:** QA dream-1 (laptop, 02:44, the flag fix in) still
  stalled in PreeMptiveMes: at CreateObj sc_b_2028 was alive, then map 521's own script (sc_map_30821_2) replaced it
  within the second, with a slot-21 save right after the battle's setup (Kaiser's fight has none): map 521 reloaded as
  the battle started and its map script killed the battle script. Likely why: the port had removed the door scene's
  moves (door cue, the player's knock-back) after the 18:49 stall, which was really the flags. Restored (split
  puppets still out), flag clear kept; re-ported 02:58, test_mod 50/50; QA reruns dream-1. Decompiled CurlaxsBattle
  (pr/tools/ghidra/dream_curlax.txt -> tools/decomp/dream_curlax): it finds Curlax (PR 289), reads its parameter's
  int array at +0x170 ([10] == 1 -> 0; [2] > 3 -> an act, a message, condition 0x271, a 30 s timer in DataStorage
  11, then script local 2 = 1 = BattlePause): only once the battle runs, so not the opening stall.
- 2026-11-21 02:35 (BNW lead): **the Stooges' opening stall, found and fixed in the port:** QA dream-diag3 (Deck,
  battle 8346 on map 521 from the field): ScenarioFlag2 179-183 all 0 -> plays; 182 alone -> plays; all five -> stuck in
  Init with no enemies; BNW's state at the door (179, 180, 181, 183 set, 182 not) -> stuck in PreeMptiveMes. The fight's
  routine (sc_b_2028, SysCall CurlaxsBattle) reads the dream's split flags. port_bnw_dream: sc_e_0901 clears 179-183
  right before its EncountBoss (pr_script.hook), sc_e_0901_1 sets 179/180/181/183 back before vanilla's 184 (only
  sc_map_30821_1 reads them: 180, 182). Re-ported, test_mod 50/50; QA runs dream-1 through the door. Dashboard and
  PORTING re-scored: ~95% (engine 95: batch B coded; scenes 88; dream 85).
- 2026-11-21 02:25 (BNW lead): **Bushido reordered by BNW, three slots fixed** (from the audit's Mindblow row). BNW's
  Bushido (its C3 name table): Dispatch, Mindblow, Empowerer, Flurry, Dragon, Eclipse, Tempest, Cleave; port_spells
  matches templates by the spell table's names, which Bushido doesn't use, so slots 86/87/89 kept vanilla's Retort
  (stance), Slash (1/2 HP + status) and Empowerer (drain). profile_bnw function/ability overrides: 125 Mindblow = Rasp's
  MP damage (3, 2), system 5, power 10, with BnwPlugin MindblowRule setting BNW's 500 MP (BlowFish effect $1A: power x
  50, no defense, no split; BNW's own text "500 MP dmg"); 126 Empowerer = vanilla Empowerer's function 23 + process
  DRAGON (HP/MP drain); 128 Dragon = magic damage (on Spirit: bnw_data spirit list) + Petrify (condition 11), process
  None. Flurry/Eclipse/Tempest keep their slots' vanilla families (multi-hit, magic + status, multi-hit) with BNW's
  numbers and statuses. Re-ported; test_mod 50/50, Rules.Tests 36/36. Not seen in game (needs Cyan's Bushido in a
  harness battle). Also: battle speed is no longer pinned (the kit lead; its reader was unsafe to detour).
- 2026-11-21 02:20 (BNW lead): **blocker: main's BNW Release (02:10, c404d6a5) crashes natively ~2 s into any
  battle** (QA, laptop, battle 464 and battle 7; no Exception; the 00:59 build ran all night). Suspects in order: the
  01:50 ATB pieces (fill rates, Quickfill, SnesActionFreeze + BattleSettingsLock: they run from the first battle frame),
  SnesBattleClock, then the lead's revive / Exploder / weapon hooks. QA bisects with [Battle] AtbFormula, Quickfill,
  nATBOnly all false and fetches Unity's crash stack; the ATB child reviews its frame hooks. Also: GP Rain's cost hook
  never applied (BattleAddExpPlug.AddPlayerSideLostGil is 4 shared bytes, `playerLostGil += lostGil`); moved:
  ChakraGpRules reads playerLostGil around BattleFunction23.HitCalc and sets BNW's Stamina x 10 (Debug build clean).
- 2026-11-21 02:14 (BNW lead): **Shemp never entered, fixed (kit, sent to the kit lead for review):** QA's KO order
  (Curly, Moe, then Larry) won the fight with no entry. Larry (288) and Moe (289) are pr_entrances (the fight keeps
  sc_b_2028), so the host's Plan dropped Larry's F5 04 00 08 ("monster 288: 2 entries/ends left to the PR's battle
  script"); and his death script (held at 0 HP -> DeathScript) didn't hold the battle end. PrKit SnesAiHostHooks:
  BringsUnlisted keeps an F5 for members the PR party doesn't list (Shemp: [37, cell 6, 0, 0]); HoldForDeathEntries
  holds the end for a 0-HP death that brings others in (DeathScript, Counter at zero). All five plugins build,
  kit_cs_check OK, WcAi.Tests 62/62; QA reruns the KO order.
  **The Stooges' opening stall is the battle routine itself:** QA dream-diag v2: battle 8346 started on map 521 from
  the field (no event) sits in PreeMptiveMes with sc_b_2028 alive; on map 112 it plays. sc_b_2028 loops SysCall
  "夢の三兄弟バトル" = SystemCall.Current.CurlaxsBattle and runs BattlePause 1 while its result is non-zero: it reads
  the dream's state. QA tests the dream flags (ScenarioFlag2 179-183) on map 521; the decompile (pattern
  pr/tools/ghidra/dream_curlax.txt -> tools/decomp/dream_curlax) waits for a PC with no game run on it. The laptop's
  refused scene scripts are not the port (all five listed; the Deck passed the same steps): QA retries `script`.
- 2026-11-21 01:20 (BNW lead): the revive fix below never ran. QA's load log: both its hooks were "Ambiguous match" (each
  `Calc` type also inherits the other's overload), so Harmony patched nothing and Phoenix Down still revived the PR way.
  ReviveRules now names both overloads (`args:`); every other hook added 11-20/21 has one overload (checked in il2cpp_out/
  dump.cs). Built clean; not installed yet (QA's headless run had the game open at 01:05). The PC gets it with bnw-13's
  install (every BNW batch builds and installs BnwPlugin); bnw-gaps-a checks Phoenix Tear (18) and the Defibrator (34) and
  fails on "Ambiguous match". The Deck push (deck_push.sh bnw, the BNW lead's) follows once gaps-a's revive lines pass.
- 2026-11-20 20:45 (BNW lead): the user's smoke test. **Fixed and installed on the PC (20:39; the Deck waits: its game was open):**
  BNW's revive amounts, `BnwPlugin/ReviveRules.cs` (Phoenix Down 1 HP, Phoenix Tear 3/4, Life and the Defibrator 250-500,
  Life 2 full: a postfix on BaseValueFixedHPFunction.Calc while ResultValueFixedHPRecoveryFunction.Calc runs; function 34 has
  no flat or random mode, tools/decomp/revive), and Phoenix Tear, which still summoned an esper from vanilla Magicite's slot
  (PR item 18; profile_bnw table_overrides give it Phoenix Down's revive). test_mod 50/50, Rules.Tests 34/34; not yet seen in
  game. **Open:** (1) non-humanoids don't counter Steal (the user): BNW's scripts have it (Leafer: if hit by command 05, attack
  101) and the AI host maps Steal 607 -> 05, but it only counters when the PR calls CounterAct with IsReadyActCounter; QA's
  laptop request steal-counter-1 (build/batches/bnw-steal.json) shows whether a Steal ever gets there. (2) Cyan's dream still
  stops on the laptop (20:24 port): sc_e_0900 (the door's push-back) never ends even without entity 0's puppet, and the
  Stooges' fight 8346 sits at PreeMptiveMes even with sc_b_2028 kept. The door scene reached its fight only once ALL puppet
  calls were gone, so the player's own puppet (-1001) seems to hang in the dream maps (the PR may still run them in "corps"
  mode); next: a push-back without puppets (or SetPos), and why 8346 doesn't leave its opening (compare a harness `battle
  461`). (3) A full scan of BNW's readme against the port (pr build scratch bnw_scan_1-3 / bnw_verify_1-3) is being
  verified; its real gaps come next, before the release.

## How much BNW changes (bytes differing from vanilla v1.0)

Enemy AI 95%, dialogue 99%, espers 82%, level-up growth 98%, spell names 91%,
item descriptions 95%, enemy stats 59%, formations 56%, shops 46%, spells 39%,
character init 34%, items 25%. Code: ~75 KB changed/added (battle C2 23%,
menus C3 17%, new code in free space).

## BNW specifics found

- BNW reassigns spell slots (e.g. slot 22 = Demi). Port spells together with
  their names/descriptions, not numbers alone.
- BNW name tables moved: magic names 0x2FFC00 (8 bytes × 54), esper names
  0x2FFDB0 (9 × 27), skill names 0x26F567 (11 × 175). Item names stay at 0x12B300
  (new icon bytes ≥ 0xEB).
- BNW turned relics 188/189/208/209 (Cherub Down, Cure Ring, Gauntlet, Genji Glove)
  into consumables: needs new PR item rows.
- Esper equip restrictions: `EsperData` table in `source/asm/banks/c3.asm`, 4 bytes
  per character (bit = esper id, SNES order).
- `source/bnw 3.0/scripts/`: `dialog_script.txt` (full BNW dialogue),
  `monstBattleScripts.txt` (all AI as annotated text, 2.1-era reference).

## Plan

**Gaps found by the readme audit (2026-11-20 evening; the user: "any other things we might have missed? please scan the
docs for BNW").** Three Sonnet passes read BNW 3.0's readme against the port, three more verified each flag against
the asm, docs/engine.md, the plugin and the porter (tables in docs/bnw_audit_2026-11-20/: bnw_scan_1-3, bnw_verify_1-3;
about 200 mechanics, ~44 confirmed MISSING, ~24 STILL UNCLEAR). The release waits for the batch A and B fixes. Spot-checked
by the lead: the EXP curve (SNES 0x2D8220 differs in 98 of 99 levels; the porter never writes exp_table) and the healing
items (BNW: Tonic 1/2 max HP, Potion 3/4, X-Potion 3/4 party, Hi-Ether 3/4 MP, X-Ether 3/4 party; the port keeps the PR's
50/250/9999 and 150/999).
- **A. Data / porter (quick, most visible):** BNW's EXP curve (exp_table); fractional healing items (+ Elixir/Megalixir
  lifting statuses); Remedy and Regen healing HP; Chakra restoring MP (not Mantra's HP); GP Toss/Rain cost Stamina x 10;
  Condemned's shorter timer; Kirin and Heal Force reviving with HP; Abort on Enemies (Phoenix Down, Holy Water, Phoenix
  Tear, equipment used as items).
- **B. Battle engine (plugin):** Steal counters from non-humans (QA steal-counter-1); First Strike starting ATB (speed-
  weighted, monsters full on back attacks); Quickfill; Stamina as the only evasion vs Demi / drain / MP damage; Blitz and
  Bushido respecting the back row; Throw (stars spread, scrolls split); Imp (spells castable, Terra immune while Morphed);
  one weapon-effect module: Undead-Slayer 50% kill, Zantetsuken/Ichimonji crit on death-immune targets, Chainsaw kill +
  damage, Anti-Air x2/x3, Dice (three dice, doubles), Atma Weapon's formula, Punisher's Dark crit, Quartrstaff/Doomstick
  procs; Parry & Counter on misses; Mindblow killing undead at 0 MP; Golem, Exploder (250% current HP), Palidor; fractional
  damage minor vs bosses; Speed Balance on the PR's measured fill (10-19% slow now).
- **C. Events / menus:** Shadow's random leave removed; full-recovery save points; Zozo unequipping non-party members;
  Terra/Edgar forced optimize at scenario ends; the Ultros respec NPC / EL respec + funds check; Spear dual-wield ban;
  Umaro's equip menu (WC's WcRecruits -eu hook); party-select MP restore; Dance switching the background; NoGainz and the
  Veldt's no-EXP / double GP.
- **D. Decisions:** ~~nATB is off by default (NatbRules) on a premise WC's measurements disproved, and the Active/Wait and
  battle-speed options stay: BNW is nATB-only.~~ Done 2026-11-21 (AtbRules.cs: nATB-only on by default, rows hidden).
- STILL UNCLEAR (in the verify tables): Hotswap's turn cost, Rage learning off the Veldt, X-Fight details, +25% on heals,
  heals on petrified allies, SOS relics resetting on death, equip-cure, Colosseum wager on a loss, counter-of-counter,
  nulled-element statuses, Mute vs Aqualung, the Giant's dying attack, rods used as items.

After BNW's release (the BNW lead, agreed with the kit lead / bc-battle, 2026-11-20): BNW's NatbRules onto the kit's SnesActionFreeze; BattleUi.cs's informative misses onto the kit's SnesInformativeMiss (bc-battle's copy; BNW's miss self-test, 13 NULL / 16 FAIL, must pass the same); BNW's Coliseum stays on SnesColiseum (WC moves onto it later, the coordinator's call). Also (the kit lead): bnw_text.py's BNW-only parts (rage_descriptions at C4/A820, FRACTIONS) into a bnw_* module BNW's repo carries; Kaiser's room and the other rooms port_boss_events converts with tools/snes_rooms, whose cell model now matches by SNES picture first (ted-maps), looked at in a run (bnw-11's kaiser_visit.png).


1. [x] Install BepInEx + Magicite, extract vanilla PR data
2. [x] Map SNES IDs → PR IDs (characters, items, enemies, spells), verified on vanilla
3. [x] Converter v1 (`tools/port_bnw.py`) → `mods/BraveNewWorld`, installed and
       verified in game (Guard HP 50, Terra Vigor 30/Magic 42, Dagger 60)
4. [~] Port: done = characters, growth, equipment stats/prices/equip groups,
       consumable prices, enemy stats/steals/drops, spells/skills (all 255 slots:
       numbers, element, targeting, status effects, names, descriptions), esper
       spell lists (BNW: rate 0, spells only from the SP bank), Terra/Celes natural magic, Blitz/Bushido
       levels, item names/descriptions, enemy elements/immunities/initial statuses,
       armor elements/status protection/auto-status, weapon elements, formation
       AP (347), shops (83 matched, 515 rows), chests (118 in 70 map files),
       encounters (144 sets, 191 new formations), Rages (138).
       Done since: weapon procs, weapon special effects, relic effects (v0.10.0).
       Todo = enemy special attacks, 4
       relic→consumable conversions (188/208/209 are dead slots in BNW, 189 = the
       inert Gum Pod; PR still has the vanilla relics in those armor rows), 13 BNW
       chests that were empty in vanilla,
       enemy sprites (done 2026-11-20 but the final battle's tiers, see "BNW monster images" in
       Live progress: `tools/sprite_compare.py` compares the sprites as the SNES draws them
       (cropped, palette slots matched by content; the 2026-11-05 count of "~220 recolors" came
       from comparing palette slot numbers, which BNW moved without changing colors): 353 look
       like vanilla (~50 of them with a few touched-up pixels in vanilla's colors), 15 recolors,
       2 shape swaps, 11 new art. The recolors and Kaiser are images the plugin makes from the
       player's game at first start (port_sprites images mode, plugin/Rules/MonsterImages.cs))
5. [~] Port: enemy AI — 271 regular enemies + 49 bosses (24 hybrids, untested)
       on BNW's AI; todo = the other 39 bosses (entrances/exits, events, per-boss PR routines), targeting,
       battle messages
6. [~] Port: text — 2026-11-05 groundwork: `tools/snes_dialog.py` decodes the SNES dialogue
       (3084, pointers CC/E602, two-letter pairs C0/DFA0) and `tools/dialog_align.py` ties SNES
       speaker turns to PR story keys by word overlap + in-order gap filling: 1249 of 3344 turns
       (1032 confident anchors, 217 fills), 1244 of 4293 PR lines (29%). Not enough to swap text
       (a wrong line is worse than the PR's own); next: per-scene sequence alignment (DP),
       townsfolk lines (N... keys) by map, then BNW's text for each matched turn (names as
       (TINA)-style tags, "Name:" prefixes dropped: the PR's speaker is in story_cha). A Japanese
       table for the SNES JP ROM (rom/vanilla) would allow JP-to-JP matching, far closer; no
       table is in the project (a download would need the user's OK; the user chose English only).
       Later the same day: per-scene sequence alignment (anchors locate each PR scene's window
       of SNES turns, then an in-order DP; seeds for scenes without anchors) + speaker check
       (story_cha speaker vs the SNES "NAME:" prefix, +-0.2): 2418 of 4293 PR lines (56%),
       speakers agree on 95% of pairs where both name one (most disagreements are naming:
       Mayor/Elder, Girl/Woman). Found: BNW's script is GBA-translation-based, like the PR's
       ("There's the city.", "But... I want to know now..." read the same in both), so most BNW
       lines already read close to the PR's; but of 2357 BNW-to-PR pairs, 978 differ a lot and
       sampling shows many of those are off by a line or two inside the right scene (the PR
       splits/merges speaker turns, which a 1:1 alignment can't follow). Not ported: wrong lines
       would be worse than the PR's own. Next would be page-level (<PRESS>/<P>) segmentation with
       1:n alignment, then porting only lines BNW really changed.
       2026-11-20 re-measured (each speaker turn's best TF-IDF match among all PR story lines):
       BNW's 3,814 turns (~36,000 words) match the PR no better than vanilla's Woolsey text does
       (>= 0.8: BNW 470 = 12%, vanilla 427 = 13%; < 0.4: BNW 1001, vanilla 740). BNW's English is
       its own rewrite of the SNES script, not the GBA/PR wording, so "only the lines BNW changed"
       is nearly all of them. BNW keeps vanilla's dialogue numbering (same index = same line,
       median similarity 0.60), so a vanilla SNES dialogue -> PR key map carries over to BNW as is.
       Same day, through the Japanese: see the Live progress row "dialogue by Japanese" (65% of PR story lines
       placed by JP-to-JP matching, 73% with the English aligner; BNW's and Vanilla New World's scripts are text in
       BNW's source, by dialogue number).
       The JP ROM is in the project (rom/vanilla/Final Fantasy VI (Japan).sfc): JP-to-JP matching
       against story_mes_ja needs only a text table (kana, plus kanji matched from the ROM's font
       glyphs); not yet checked that JP and US dialogue numbers agree.
       Dialogue checked 2026-11-04: the PR keys story lines per event
       (`story_mes_en.txt`: E0055_00_343_a_01 = event 55, scene, an event-local number
       1..999, variant, line; 3546 lines, 234 numbers), not by SNES dialogue number
       (SNES #343 is a Banon line, PR's 343 is Vargas), and its English is a different
       translation: porting BNW's script needs fuzzy alignment per event. Low priority.
       Done = names and descriptions (spells, items, espers, bonuses);
       todo = BNW script (dialogue), battle messages
7. [~] C# plugin (BepInEx IL2CPP) for BNW mechanics, from `asm/banks`: done =
       esper restrictions (greyed list), EP/EL with EL bank + cursor row on the esper
       screen, EL beside LV and Next EL on the status screen, esper equip bonuses
       (not the two +25% output ones), vanilla level-up bonus off, F8 test kit,
       SP bank spell buying (v0.9.0).
       Confirmed in game: EP/EL after battle (v0.8.7, with boss EXP fixed), SP bank
       (v0.9.1). Unconfirmed: EL data across save/load, equip bonuses in battle.
       Weapons/relics (v0.10.0): proc odds per attacker, hard-coded item lists moved to
       BNW's items, x2 EXP/gil off. v0.10.1 (`RelicRules.cs`): two-handed x1.5
       (`ProviderManager.Instance.EquipmentAbilityProvider.serialData`
       .TwoHandedMagnification 175 -> 150; its getter is too tiny to hook), Black Belt
       counter (Stamina+32)/129 (`ActUtility.GetBlackBeltCounterAct` prefix rolls;
       BattlePassiveConstants.BlackBeltCounterPercentage set to 100 so the game's gate
       passes), Dragon Horn 25% one extra bounce (`SerialBattleUtility
       .CanContiniusJumpAttack` postfix, a new jump = no check for 4 s), MP crit cost
       Level/2 halved by equip ability 291 (`IsUseMpCriticalOccurrence` useMp), +25% magic
       relics don't stack and esper +25% physical/magic counts
       (`GetAccessory{Physical,Magic}CorrectionValue`; value logged first time to confirm
       multiplier vs fraction), undead slayers x2 vs undead (data: effective_species 1).
       Still todo: Offering 2 hits at -25% (PR "disorderly shoot", 4 at half:
       `CreateContinuousActData`), counters on misses too, True Knight cover rules, dual
       wield pairing rules (spears block the off-hand), undead slayer's 50% kill, Moogle
       Charm (faster Jump landing, Dance without the Dance status), Zephyr Cape SOS Haste,
       Ghost Ring Overcast, physical +25% also on heals, Hyper Wrist/Sneak Ring built-in
       boosts (not found in the API), Umaro relic odds, GP Rain formula, Mug only on the
       first dual-wield strike.
       Todo = "EL gained" message,
       weapon spell procs, unported relic bits, Spirit stat, evasion
       rework, Dance "Choreography", Rage odds, Blitz/Bushido changes, Tools,
       battle UI (enemy HP/status on X/Y, Scan hints), bug fixes,
       **Wait Gauge cast bars (user asked: "in battle i should see the cast bars like
       bnw") — v0.11.0, `WaitGauge.cs`, not yet seen in game**: the PR already waits
       between a command and its action ("chanting": per-ability `ability_wait`, Attack
       3, magic 5, summons 3, Jump air time 30, roughly SNES $10/$20/$E0;
       `BattleProgressATBFF6.GetChantingTime(actData)`). The plugin scales each chant time
       by BNW/vanilla CommandWaitTimes for the act's command (bnw_data.json command_waits;
       summons = ability type 9; Morph, vanilla 0, gets Fight's time per unit x $40), and
       `BattlePlayerInfomationController.UpdateATB(player, value)` shows the wait left
       (from `BattleProgressATB.chantingInfo` {actData, time}; the clock's direction and
       the full-bar value are learned at runtime and logged), tinting the bar
       (`view.atbGauge.gaugeImage`, `maxGaugeImage`) by status (ConditionType Stop, Slow,
       Heist = Haste). Original notes: BNW's config option "Wait Gauge" (on by default, c3.asm GaugeOn) makes
       a character's ATB bar show the chosen command's wait time draining until it
       runs (c1.asm WaitRemaining / DrawGauge "Rewritten to support Wait Gauge";
       ATB endcaps change once the bar is full). Needs BNW's per-command wait times
       too (c2.asm CommandWaitTimes at $C2067B: Fight/Item/Throw/Sketch/Rage/Dance
       $10, Magic/Bushido/Tools/Blitz/Lore/Control/Slot $20, Morph $40, others 0).
       **ATB bar color by status (user asked)**: ee.asm StatusATB picks the gauge
       palette: Stop red (213,41,41), Slow yellow (255,222,0), Haste green
       (41,213,41), otherwise white/grey (bin/palettes-bnw.bin palettes 7/3/6/5,
       brightest color shown); Stop wins over Slow over Haste.
8. [~] In-game testing on PC (user) and Steam Deck (deploy.sh; loads under Proton 11:
       BepInEx, Magicite, plugin, data check and guard self-test all OK on the Deck)
9. [ ] Package `pr/mods/BraveNewWorld/` for Magicite + plugin DLL
