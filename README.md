# Brave New World for FINAL FANTASY VI Pixel Remaster

Brave New World 3.0, the FF6 rebalance and
redesign by Synchysi and its contributors, ported to the Steam Pixel Remaster. **In development,
not released for players yet** (about 81% done: data, espers, most of the battle engine and
enemy AI are in; dialogue and monster art are not).

## How it works

- `kit/`: the [modding kit](https://github.com/thomasjteachey/ff6pr-kit) (a submodule). Its
  porter turns BNW's ROM into Pixel Remaster data: characters, equipment, spells, espers,
  monsters and their AI, formations, encounters, shops, chests, names and descriptions.
  `kit/pr/PORTING.md` is the project's full development log (BNW's status table and notes).
- `pr/tools/profile_bnw.py`: what BNW does differently that the porter can't read from the
  ROM's tables. `pr/tools/bnw_plugin_data.py`: BNW's esper system, relic and weapon rules,
  exported for the plugin. `pr/tools/port_bnw.py` runs the port.
- `pr/plugin/BnwPlugin`: the BepInEx plugin for BNW's mechanics (esper levels and the SP bank,
  restrictions and bonuses, weapon procs and relics, the Wait Gauge, BNW's battle formulas,
  Slot, Morph, Umaro, Cover...). `pr/plugin/Rules`: its game-free rules, with offline tests.
- `pr/tools/probe_check.py`: checks the in-game formula probe against FF6's and BNW's formulas.

## Build

Needs Windows, FF6 PR with BepInEx 6 and Magicite installed, .NET 6 SDK, Python 3.12, the US v1.0
SNES ROM (`rom/vanilla/Final Fantasy III (USA).sfc`), and Brave New World 3.0 from its own
distribution: its source in `mods/brave-new-world/source` and the patched ROM at
`build/brave-new-world/Brave New World 3.0 FRC.sfc`. None of these are in this repository.

    git clone --recurse-submodules <this repo>
    cd pr && python -m venv .venv && .venv/Scripts/pip install -r requirements.txt
    .venv/Scripts/python ../kit/pr/tools/extract.py --game "<your FF6 PR folder>"   # pr/vanilla
    .venv/Scripts/python tools/port_bnw.py                                         # pr/mods/BraveNewWorld
    bash ../kit/pr/tools/deploy.sh --bnw --pc                                      # build + install
    PR_MOD=BraveNewWorld .venv/Scripts/python -m unittest tests.test_mod           # port checks

## Credits

Brave New World by Synchysi and its contributors (see BNW's readme). BepInEx, Il2CppInterop,
HarmonyX, Magicite by Silvris. FINAL FANTASY VI is Square Enix's; this is an unofficial fan mod.
