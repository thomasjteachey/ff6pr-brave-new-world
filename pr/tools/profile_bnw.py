"""Brave New World's settings for the shared SNES -> Pixel Remaster porter (the kit's tools/port.py).

    .venv/Scripts/python tools/port_bnw.py      (or tools/port.py bnw)

profiles.PROFILES["bnw"] imports this module. Everything BNW does differently from vanilla that the
porter can't read from the ROM's data tables is set here.
"""
import os

import kitpath  # noqa: F401  (the modding kit's tools, when it's this repository's kit/ submodule)
from profiles import PROJECT, Profile


def _final_tier_pieces(table):
    """BNW's final-battle tile pieces (final_tier_pieces below) with their files' paths in BNW's source."""
    d = os.path.join(PROJECT, "mods", "brave-new-world", "source", "bnw 3.0", "tools", "asm")
    return {t: [(os.path.join(d, f), a, w, r) for f, a, w, r in rows] for t, rows in table.items()}


class BraveNewWorld(Profile):
    name = "bnw"
    mod_name = "BraveNewWorld"
    rom = os.path.join(PROJECT, "build", "brave-new-world", "Brave New World 3.0 FRC.sfc")
    rage_odds = (67, 33)
    rename_all = True
    monster_names = True   # BNW's own monster names (279 of them its renames: Pug, Warmech, Gluttony...)
    sync_monster_copies = True   # the PR's Coliseum copies of monsters take BNW's stats (port.sync_monster_copies)
    equip_stop_block = True   # equipment byte 8 bit 4 = a Stop block (White Cape, Ribbon), not auto-Stop (port_resist)
    # values BNW sets in its event code (port.py table_overrides): Shadow's hire in Kohlingen costs 1000 GP
    # (BNW CC/6FF6 gil_remove 0x3E8; vanilla 0xBB8). The PR's sc_e_0285 takes RemoveGil [203]: the price of
    # the product in group 203 (External.Misc.RemoveGil, decompiled tools/decomp/bnw_gil), product 562
    # BNW's Phoenix Tear (SNES 249) took vanilla Magicite's slot (PR item 18, Magicite Shard: a random esper,
    # function 46): it revives like Phoenix Down (its targets, effect, sound and function group 2), at 3/4 of
    # max HP (BnwPlugin ReviveRules)
    table_overrides = {"product": {562: {"coefficient": 1000}},
                       "item": {18: {"system_id": 1, "renge_id": 7, "menu_renge_id": 7, "battle_renge_id": 7,
                                     "battle_effect_asset_id": 343, "menu_se_asset_id": 593,
                                     "menu_function_group_id": 2, "battle_function_group_id": 2}}}
    spell_bank_only = True
    esper_learning = "bnw"
    stat_cap = 255  # c2.asm GengiCheckFunction: stats with gear stop at 255 (EL bonuses at 128)
    defense_flags = "mod"  # every spell's ignore-defense flag from BNW's ROM (port_flags)
    runic_flags = "mod"    # Runic absorbs every black/white/grey spell, no enemy skills (port_flags)
    hit_flags = "mod"      # "can't miss" from BNW (87 spells changed, e.g. Blitzes can miss)
    split_flags = "mod"    # "no split damage" from BNW (52 changed: Quake/Ultima don't split, summons do)
    dance_rules = "bnw"  # Shift/Repeat/Uncommon/Rare at 10/5/1 of 16, no stumble (port_dances)
    # BNW took the MagiTek slots for other spells (c2.asm C21DE0: its Lore/enemy-attack range
    # starts at $84): 131 a monster Fire, 132-137 Rage attacks (Bomb's Exploder, Exocite's Rock,
    # Cephalid's Tentacle, Mantodea's Shrapnel, Blink, Ninja Wave), 138 empty. Mapped onto the
    # PR's Magitek moves (218-225, the Magitek command's ability group 7) they replaced them: the
    # user's new game had no usable Magitek. They get abilities of their own (port_spells
    # add_missing makes the rows); the PR's Magitek moves stay vanilla's.
    spell_relocations = {131: 6131, 132: 6132, 133: 6133, 134: 6134, 135: 6135, 136: 6136, 137: 6137, 138: None}
    # BNW's skill menu shows each dance's four moves (readme "Rage & Dance Moves"): the dance's
    # description lists them (port_dances.describe_steps, as WC's -dda)
    dance_descriptions = True
    event_fights = True        # the story's battles fight BNW's formations (port_events)
    # merchants BNW adds (port_merchants): shop 86 (Dried Meat, Tonic, Tincture, Phoenix Down,
    # Bouncy Ball, Ghost Ring), sold by NPC 16 BNW put in Doma Castle (SNES map 126 at (55, 9),
    # facing left; World of Ruin). The PR's room is Map_20451_2 (laid out 3 tiles lower: the
    # Remedy chest SNES (55, 5) is the PR's (55, 8)), where (55, 12) is walled in: the merchant
    # stands at (55, 10) in the same room, facing left (8; 1 up, 4 down), the PR's merchant sprite
    added_merchants = [{"shop": 86, "group": "map_20451", "map": "Map_20451_2", "x": 55, "y": 10, "direction": 8,
                        "asset_id": 29, "script": "sc_npc_20451_2_bnw_shop", "label": "BNW商人（道具屋）"}]
    # Edgar's tools (port_tools): BNW's Tool_Data_1/2 (c2.asm 2767): tool -> spell it casts
    tool_spells = {0xA4: 0xA4 - 0x27, 0xA5: 0xA5 - 0x27, 0xA7: 0xA7 - 0x0D, 0xA9: 0xA9 - 0x0E}
    mp_heal_spells = [155]  # !ManaBat: restores an ally's MP (no vanilla spell to borrow from)
    tool_rules = "bnw"      # Drill also sets Sap
    slot_jackpot = 151      # 7-7-7 casts Jackpot, 7-7-Bar is a loss (port_slots; reels: plugin SlotRules)
    # single values BNW sets in assembly, not in spell data (port.py ability_overrides):
    # Umaro's Tackle (PR 201 "Slam") is Fight at Battle Power 255 (c2.asm Tackle: LDA #$FF);
    # his ally toss (PR 200) 255 with the Gauntlet bonus, x1.5 = 382 (c2.asm C2/16DA, readme
    # "Umaro Hit Hard"). Odds: plugin UmaroRules.
    # Shock (PR 19, type 618, the Shock command's; BNW's "!Shock", SNES spell 130, which idmap
    # gives to the PR's slot result 214): ignores defense (spell byte 2 bit 5; content flag
    # group 11 = vanilla's group 1 + flag 5); its damage and cost are the plugin's (BattleFormulas)
    ability_overrides = {201: {"standard_value": 255}, 200: {"standard_value": 382},
                         19: {"content_flag_group_id": 11},
                         137: {"process_prog": "None"},   # Chakra: a plain recovery (ChakraGpRules), not Mantra's CHAKRA
                         # Bushido slots BNW reordered (function_overrides above): Mindblow an MP hit, Empowerer the
                         # drain program, Dragon none
                         125: {"system_id": 5, "standard_value": 10, "process_prog": "None"},
                         126: {"process_prog": "DRAGON"},
                         128: {"process_prog": "None"}}
    # sprites (port_sprites images mode): BNW's recolors become new images (the PR's art in BNW's
    # colors, made by the plugin from the player's game: plugin/Rules/MonsterImages.cs), its shape
    # swaps point at the right PR art, and Kaiser (new in BNW, vanilla's empty CzarDragon slot) is
    # BNW's own sprite, shown in Doom Dragon's group (its shadow); bnw_data.json "monster_images"
    battle_messages = True  # monster battle lines in compiled AI (battle_text; WC's live seeds don't load story text)
    # monsters run BNW's own scripts in the plugin (the kit's SnesAiHost, the user 2026-11-19: "Start putting
    # BNW's monsters on the kit's AI interpreter"), with BNW's engine changes to how scripts run
    # (SnesAiRules.Bnw: c2.asm's counter rules, MP-low x128, vindictive targeting, bosses die by script)
    # and the misc effects its boss deaths use; bnw_data.json carries the scripts (snes_ai)
    snes_ai = True
    snes_ai_rules = "bnw"
    snes_ai_status = True
    # BNW's boss fights on its own AI (the enemy-AI session, 2026-11-20; port_ai.BossPolicy.snes_reason):
    # the AI data gives each story fight BNW's formation number (its scripts check it, FC 1B, and aim at
    # its slots: BNW renumbered them; port_ai.event_formations), plus per-fight settings for the PR battle
    # scripts that keep running alongside BNW's AI
    snes_ai_event_formations = True
    snes_ai_formations = {423: 0x182}   # the first Phunbaba (it blows the party away); 0x183 is the fight Terra wins
    # PR battle scripts that work with BNW's AI in the plugin (what they still do there)
    snes_ai_hybrid_scripts = {
        "sc_b_0086": "Tunnel Armor's fight: the intro talk at the start (BNW's event 10 there)",
        "sc_b_2025": "the Soul Train's escape animation (MagicTrainAnimStart: the party's idle pose only)",
        "sc_b_2023": "Doom Gaze's HP kept between encounters (its HP save/restore)",
        "sc_b_2018": "Fiend's counter routine (FiendCounterAttack) fires only on the PR AI's variable 9: idle",
        "sc_b_2009": "Ifrit/Shiva's tag-team routine (IfritMoveCheck) moves one only on the PR AI's variable 9: "
                     "idle; BNW shows both from the start (start_shows lifts the script's NoSelect on Shiva)",
        "sc_b_0055": "Vargas: the PR plays Mash joining (mode 1) and Pummel's finish (mode 3) at BNW's events 07 / 09",
        "sc_b_0065": "Ultros at the Lete River: the PR plays its dive and the exit (mode 1) at BNW's event 0a",
        "sc_b_0115": "Kefka's escape (Shadow gives chase): the PR plays it and the exit (mode 1) at BNW's event 1d",
        "sc_b_0709_1": "the first Phunbaba: its breath (PR 371 = BNW's attack ea) blows the party out (the PR's "
                       "routine), and the PR ends that fight (pr_ends)",
        "sc_b_0067": "the Merchant: the PR plays Locke's disguise (mode 1) at BNW's event 0f (Steal)",
        "sc_b_0071": "the Trooper: the PR plays Locke's disguise (mode 1) at BNW's event 0e (Steal)",
    }
    # SNES battle events (F7) -> the PR monster mode its battle script reads (GetEnemyMode), or None: the PR's
    # battle script plays that scene by itself (an intro) or it has no PR counterpart (only talk)
    snes_ai_events = {
        259: {0x07: 1, 0x09: 3},   # Vargas: Mash joins; Pummel ends it
        260: {0x10: None},         # Hell Angel: the intro talk (sc_b_0086 at the start)
        298: {0x20: None},         # Kefka (Narshe): a talk the PR doesn't have
        300: {0x0A: 1},            # Ultros (Lete River): dives, the fight ends
        367: {0x1D: 1},            # Kefka (camp): runs, Shadow gives chase
        305: {0x13: None},         # Ziegfried: his talk before he leaves
        314: {0x0F: 1},            # the Merchant, stolen from: Locke's disguise (sc_b_0067)
        373: {0x0E: 1},            # the Trooper, stolen from: Locke's disguise (sc_b_0071)
        311: {0x0B: None},         # Kaiser: its opening event (no PR counterpart)
    }
    # BNW's fights the PR has no party for: formation -> the PR party whose look the new one takes
    # (port_ai.new_fights: party 8000 + formation). Kaiser (0x19a: the post-game superboss) on the dragons'
    # look (Gold Dragon's party 370); Kefka and two Centurions (0x179, where vanilla had an event battle)
    # on the look of Kefka's fight at Narshe (515; vanilla's event battle there has no PR party); the Dream
    # Stooges (346: Curly, Larry, Moe and the hidden Shemp, SNES 37, whom Larry's and Moe's counters bring
    # in: F5 04 00 08) on their PR fight's look (461, which has no Shemp). The PR events that start them are
    # the events' side (sc_e_0901's EncountBoss: bnw-dream).
    snes_ai_new_fights = {0x19A: 370, 0x179: 515, 346: 461}
    snes_ai_new_fights_keep_routine = {346}   # the Stooges: 461 is the same fight; its sc_b_2028 sets them up
    # ...and the PR map scripts that start them where BNW does (port_boss_events: Kaiser in BNW's own
    # corridor room, a new PR map, once all 8 dragons are down; the Sealed Gate's first battle); BNW's new lines
    # before the Kefka fight (SNES dialogues 0xA60-0xA65) once the text pipeline has keys for them
    bnw_boss_events = True
    bnw_dragon_visits = True    # after each dragon, its tally shown in Kaiser's room, that dragon's marker going (CB/5090)
    bnw_rewards = True         # BNW's changed event gifts in the PR's scripts (port_bnw_rewards)
    bnw_party_events = True    # Shadow never leaves at random, Zozo unequips the bench + Shadow, Terra/Edgar optimized (port_bnw_party_events)
    bnw_respec_npc = True      # Ultros's EL respec, 25,000 GP then 100,000 (port_bnw_menus; plugin MenuRules)
    bnw_umaro_equip = True     # airship NPCs unequip Umaro too (port_bnw_menus; menu lock: plugin MenuRules)
    bnw_hp_refills_mp = True   # every event HP refill restores MP (port_bnw_menus.port_late)
    bnw_save_points = True      # BNW's 11 new save points as the PR's SavePoint objects (port_bnw_mechanics)
    bnw_recovery_points = True  # BNW's 14 full-recovery save points: a step-on heal on each (port_bnw_mechanics)
    bnw_added_scenes = True     # BNW's added/extended scenes: Kefka's Tower's barrier, Leo's Crest, Shadow's full-party hire, South Figaro's wall (port_bnw_added_scenes)
    bnw_bob = True              # Bob's esper sales (Dragon's Neck Cabin, Coliseum, Cultists' Tower)
    bnw_veldt_freebies = True   # the World of Balance's missable Veldt formations opened at the world's end
    bnw_dream = True            # Cyan's dream as BNW's: no split, three Cyans, Stooge #1 (port_bnw_dream, bnw-dream)
    bnw_event_lines = [f"BNW_E0460_{n:02d}" for n in range(1, 7)]   # bnw-text-ui's keys (DialogText's added lines)
    snes_ai_brought_in = {303: 360,   # Chupon, brought in by Ultros (formation 0x16b)
                          341: 340}   # Rizopas, brought in by the Piranhas' PR routine (sc_b_2004)
    snes_ai_pr_ends = [364]           # the first Phunbaba: the PR's breath routine ends the fight
    snes_ai_zero_hp_wait = [259]      # Vargas fights on at 0 HP until Pummel (BNW's script)
    snes_ai_start_shows = [440]       # Ifrit and Shiva both from the start
    snes_ai_event_bits = [0x4F]       # Dadaluma fed Dried Meat (his counter, F9 01 0D 07): a Hell Claw after (ED/8E09)
    snes_ai_coliseum_copies = (500, 611)   # the PR's Coliseum copies run their regular's BNW script (as the SNES does)
    snes_event_alloc = os.path.join("data", "snes_event_alloc_bnw.json")   # the mod's new PR flags (FlagAlloc)
    snes_ai_keep = {
        276: "its BNW script is the SNES battle event 04 and the battle's end; the PR's own ability 1414 plays that scene",
        281: "its death changes the formation (F2 to 0x153, Intangir Z); the PR stages that as the same unit rising "
             "(KingBehemahInBattle) with its own AI's second phase",
        371: "a story scene (hit: SNES event 17 and the end); the PR's battle event sc_b_0555_1 plays it on its own signal",
        64: "BNW's script for it is empty (main and counter end) and no PR fight of its own fields it",
        277: "no PR fight fields it (BNW: SNES battle event 12 and the end)",
        302: "Relm joins mid-fight: sc_b_0547 plays it from the PR AI's own state (variables, timers); BNW's Ultros "
             "moves among four slots (formation 0x17d) the PR's one-Ultros party doesn't have",
        359: "the PR's Wrexsoul fight (party 463) fields vanilla's line-up: vanilla's Wrexsoul (290, a slot BNW "
             "emptied: BNW's is monster 13, formation 0x15c) with the PR's routine sc_b_0939 and AI",
    }
    sprite_images = True
    sprite_new_art = {311: 37, 37: 288}   # Kaiser (Doom Dragon's group: its shadow); Shemp, the dream's fourth Stooge (Larry's)
    sprite_final_tiers = True      # the final battle's tower picture with BNW's statue parts (tools/final_tiers.py)
    # BNW's background pieces for statue tiers 2-3 (final_battle.asm TransferDeadGFX: Tier2PointerTable /
    # Tier3PointerTable, copied to VRAM as each tier begins), from its source: (file, VRAM word address,
    # words per row, rows); tools/final_tiers.py writes them over its SNES captures
    final_tier_pieces = _final_tier_pieces({2: [("Tier2_RightGuy.bin", 0x1610, 0x20, 3), ("Tier2_Middle.bin", 0x1650, 0x40, 6),
                                                ("Tier2_LeftGuy.bin", 0x16E0, 0x20, 2), ("Tier2_Tiger.bin", 0x1C10, 0x60, 4)],
                                            3: [("Tier3_Goddess.bin", 0x1590, 0x20, 6), ("Tier3_God.bin", 0x1870, 0x20, 3)]})
    sprite_standins = {311: 37}    # if Kaiser's own art is ever left out: Doom Dragon's
    # encounter types: BNW weights back/pincer/side/normal 33/33/33/156 of 255 (c2.asm:3953;
    # vanilla 31/8/8/208 = the PR's 13/3/3/68+13 preemptive) -> 13/13/13 and normal 48, the
    # PR's preemptive 13 kept; pincers start the party's ATB like a front attack
    game_constants = {**{f"{speed}_{move}_{kind}": (v, 0) for speed in ("LOW", "HIGH") for move in ("DASH", "WALKING")
                         for kind, v in (("NORMAL", 48), ("BACK_ATTACK", 13), ("CROSS_FIRE", 13), ("SIDE_ATTACK", 13),
                                         ("PREEMPTIVE_ATTACK", 13))},
                      "ACCUMULATION_ATB_CROSS_FIRE": (50, 80),
                      # readme "Levels & Experience": character levels cap at 50 (vanilla's 99)
                      "MAX_LV": (50, 0)}
    port_exp_table = True   # BNW's EXP curve (SNES ED/8220; port.port_exp_table)
    # effects BNW gives in its code (port.py function_overrides): Restore (BNW's Remedy, PR 37) and Regen / RegenX
    # (38, 39) also cure HP ("Cures HP (spirit-based)": the PR's heal, function 1 as Cure's, spirit from the spell's
    # flag); Elixir and Megalixir (items 7, 8) also lift Restore's statuses (condition group 2004). Readme "Life Hack",
    # BNW's descriptions; the audit of 2026-11-20
    function_overrides = {
        "ability": {37: {"battle": [(12, 2004, 0, 0), (1, 2, 2, 0), (23, 0, 0, 0)],
                         "menu": [(12, 403, 0, 0), (1, 2, 2, 0), (23, 0, 0, 0)]},
                    38: {"battle": [(9, 20, 0, 0), (1, 2, 2, 0), (23, 0, 0, 0)]},
                    39: {"battle": [(9, 20, 0, 0), (1, 2, 2, 0), (23, 0, 0, 0)]},
                    # Chakra (Sabin, Spiraler's slot): MP recovery as an Ether's; the amount, (Stamina + Level) / 2,
                    # is BnwPlugin ChakraGpRules (process_prog None below: not Mantra's CHAKRA)
                    137: {"battle": [(1, 1, 3, 0)]},
                    # BNW reordered Bushido (c3 names: Dispatch, Mindblow, Empowerer, Flurry, Dragon, Eclipse, Tempest,
                    # Cleave) and port_spells' name match never saw it (Bushido names are their own table), so slots 86,
                    # 87 and 89 kept vanilla's Retort, Slash and Empowerer (2026-11-21, the audit's Mindblow row):
                    #  * 125 Mindblow (SNES 86): MP damage, BlowFish effect $1A = power x 50 = 500 (c2.asm BlowFish),
                    #    no defense, no split: Rasp's MP damage function; BnwPlugin SkillRules sets the 500
                    #  * 126 Empowerer (87): vanilla Empowerer's (PR 128: function 23, process DRAGON = HP/MP drain)
                    #  * 128 Dragon (89): magic damage on Spirit (bnw_data spirit list), ignores Defense, may Petrify
                    #    (condition 11, as Break)
                    125: {"battle": [(3, 2, 0, 0)]},
                    126: {"battle": [(23, 0, 0, 0)]},
                    128: {"battle": [(3, 1, 0, 0), (9, 11, 0, 0)]},
                    # Palidor (103): port_spells rebuilds statuses from the SNES status bits, and BNW cleared Palidor's
                    # (its code hides the riders instead: c2.asm PaliHide, C2/3936), so the group lost vanilla's Jump
                    # (condition 28): back to vanilla's group 471 shape, the Jump plus the landing act (ability 104)
                    103: {"battle": [(9, 28, 0, 0), (41, 104, 0, 0)]}},
        "item": {7: {"battle": [(1, 1, 2, 0), (1, 1, 3, 0), (12, 2004, 0, 0)],
                     "menu": [(1, 1, 2, 0), (1, 1, 3, 0), (12, 403, 0, 0)]},
                 8: {"battle": [(1, 1, 2, 0), (1, 1, 3, 0), (12, 2004, 0, 0)]}},
    }
    # BNW's meanings for weapon special effects that differ from vanilla (byte 27 high
    # nibble; c2.asm Special Effect jump tables): 3 = x2 vs humans + 50% Net, 5 = ignore
    # Defense + x2 vs humans, 6 = 50% Net, B = Windslash, E = 50% Aero instead of the
    # swing, F = undead slayer. Procs (Net, Windslash, Aero) are port_procs' job.
    weapon_specials = {3: {"species": 2, "prog": "ADD_MAGIC"}, 5: {"species": 2}, 6: {"prog": "ADD_MAGIC"},
                       11: {"prog": "ADD_MAGIC"}, 14: {"prog": "ADD_MAGIC"},
                       15: {"species": 1}}  # undead slayer: x2 vs undead (species 1); its 50% kill is plugin todo
    # BNW spell procs from special effects: 3/6 = 50% Net beside the swing, B = Windslash
    # (25%) and E = Aero (50%) instead of the swing (c2.asm 2853, 3061, 2951)
    special_procs = {3: ("add", 0xAD, 50), 6: ("add", 0xAD, 50), 11: ("change", 0x98, 25), 14: ("change", 0x99, 50)}
    # an MP-critical swing turns Doomstick's Doom into X-Zone (c4.asm SpellCastId): X-Zone (PR 58) needs a proc copy too
    extra_proc_copies = (58,)
    # proc odds (bnw 3.0 item.asm RandomCast): 1 in 4; "Use Brush Odds" (byte 11 bit 2) 1 in 2;
    # "High Proc Rate" (byte 12 bit 7, weapon or relic) 1 in 2, or 3 in 4 with brush odds
    proc_odds = {"brush": (11, 2), "high": (12, 7), "brush_rate": 50, "high_rate": 50, "brush_high_rate": 75}
    # status tuning (PR condition.csv rows; BNW engine constants, not ROM data):
    # Haste/Slow x1.2/x0.8 ATB (c2.asm:397; PR rate_value1 = percent, vanilla 50),
    # Image breaks on 1/3 of dodges (c2.asm:1466; vanilla 25%), Sleep and Stop last
    # half as long (c2.asm:3608-3628: 9 vs 18 ticks)
    condition_tuning = {21: {"rate_value1": "20"}, 25: {"rate_value1": "20"}, 19: {"until_value": "33"},
                        7: {"until_value": "15"}, 26: {"until_value": "15"},
                        # Freeze: BNW 10 of vanilla's 34 (c2.asm 3628), the PR's 60 scaled alike
                        32: {"until_value": "18"},
                        # Reflect: no timer in BNW (c2.asm C2/5B06 countdown removed); until_type 2 =
                        # until removed, as Regen. It breaks 85/256 per reflection (plugin)
                        24: {"until_type": "2", "until_value": "0"}}
    # relic bits BNW repurposed: 10.4 Leo's Crest turns Runic into Shock (c3.asm C3/619A)
    relic_bit_funcs = {(10, 4): {(61, 16, 28, 0)}}

    def descriptions(self):
        import bnw_text
        return bnw_text.sections(self.rom)

    def esper_descriptions(self):
        import port_bnw_descriptions
        return port_bnw_descriptions.esper_descriptions()

    def rage_descriptions(self):
        # BNW's Rage menu shows each rage's two attacks (ragedance_descriptions.asm): its own
        # words for the 64 rages Gau can learn; port_dances.describe_rages names the rest's
        import port_bnw_descriptions
        return port_bnw_descriptions.rage_descriptions(self.rom)

    def extra_data(self, rom):
        import bnw_plugin_data
        import ff6snes
        import port_ai
        data = bnw_plugin_data.plugin_data(rom)
        names = ff6snes.enemies(rom)
        # bosses now running BNW's AI, with their formations, for the plugin's F9 boss test
        data["boss_tests"] = [{"snes_id": t["snes_id"], "name": names[t["snes_id"]].name, "parties": t["parties"],
                               "hybrid": t.get("hybrid", False)}
                              for t in getattr(port_ai.port, "boss_tests", [])]
        sa = getattr(port_ai.port, "snes_ai", None)
        if sa is not None and self.snes_ai:
            data["snes_ai"] = sa   # the plugin's SnesAiHost runs these (BnwEnemyAi.cs)
        # BNW's random-battle rates for the kit's SnesEncounters (BnwEncounters.cs): its rate codes,
        # its battle roll (4-236: at least ~10 steps between battles), its party odds (also the sets' weights)
        import port_encounter_rates
        import prdata
        data["encounters"] = port_encounter_rates.rules(ff6snes.Rom(prdata.VANILLA_ROM), rom, "bnw")
        # monster images (port_sprites images mode): the plugin builds them at first start
        import port_sprites
        if port_sprites.port.images:
            data["monster_images"] = port_sprites.port.images
        files = {"bnw_data.json": data}
        # BNW's dialogue (tools/bnw_dialog.py) is a file of the mod folder, which the porter starts
        # afresh: made again here from the line map (build/dialog, tools/dialog_map.py) when it's there
        import bnw_dialog
        import dialog_map
        if os.path.exists(os.path.join(dialog_map.OUT, "dialog_map.json")):
            files["bnw_dialog.json"] = bnw_dialog.build(write=False)
        else:
            print("no build/dialog/dialog_map.json: bnw_dialog.json not made (tools/bnw_dialog.py --rebuild-map)")
        # BNW's changed scenes in the PR's map scripts (bnw-text-ui: the blacksmith's forge, lines BNW cut)
        import port_bnw_scenes
        port_bnw_scenes.port(self.out)
        # last: every event "HP to max" also fills MP (C0/AED5; readme audit C, port_bnw_menus)
        import port_bnw_menus
        port_bnw_menus.port_late(self.out, print, self)
        return files

    def extra_messages(self, rom):
        import bnw_plugin_data
        return bnw_plugin_data.messages(bnw_plugin_data.plugin_data(rom))


PROFILE = BraveNewWorld()
