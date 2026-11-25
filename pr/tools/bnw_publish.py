"""The public Brave New World repository (ff6pr-brave-new-world): this project's BNW parts, copied
into their own git tree, with the modding kit as its kit/ submodule.

    .venv/Scripts/python tools/bnw_publish.py [OUT]     (default: ../../ff6pr-brave-new-world)

Copies the BNW plugin and its rules library, BNW's tools (its porter profile, plugin data, formula
probe) and its port checks into OUT with the project's layout (pr/...), writes its README,
.gitignore, requirements and manifest, and records the kit (ff6pr-kit, kit/) at the kit
repository's current commit: publish and commit the kit first (tools/kit_publish.py). Nothing of
Worlds Collide's. No BNW files either (its ROM, patch or source): a build uses the player's own.
OUT's .git is left alone: commit and push from there. The checks are tools/repo_publish.py's.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kitpath  # noqa: E402,F401  (the modding kit's tools, when it's this repository's kit/ submodule)
import repo_publish as R  # noqa: E402

DEFAULT_OUT = os.path.join(os.path.dirname(R.PROJECT), "ff6pr-brave-new-world")

PARTS = [
    ("pr/plugin/BnwPlugin", ["**"]),
    ("pr/plugin/Rules", ["**"]),
    ("pr/plugin/Rules.Tests", ["**"]),
    ("pr/tools", R.BNW_TOOLS),
    ("pr/tests", ["test_mod.py", "test_bnw_sprites.py"]),
    ("pr/docs", ["bnw.md"]),
]

README = """# Brave New World for FINAL FANTASY VI Pixel Remaster

Brave New World 3.0, the FF6 rebalance and
redesign by Synchysi and its contributors, ported to the Steam Pixel Remaster. **In development:
preview builds are on the [Releases](https://github.com/thomasjteachey/ff6pr-brave-new-world/releases)
page** (about 96% done: data, espers, the battle engine and timing, BNW's enemy AI, monster art,
scenes, Coliseum and dialogue are in; it hasn't had a full playthrough yet).

## Playing it

Download `BraveNewWorld-PR.zip` from the latest release and extract it into the game folder (next
to `FINAL FANTASY VI.exe`). Needs BepInEx 6 (IL2CPP x64, bleeding edge, made with 6.0.0-be.697)
and Magicite installed first; the zip's README has the details. Don't install it beside Worlds
Collide, and start a new game.

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
"""

# Publishing a GitHub release (not a pre-release) uploads its tested BraveNewWorld-PR.zip to Nexus mod 167 as the main
# file, like WC's (wc_publish.py; 2026-11-25: the user chose releases without a browser, and this API upload for BNW).
# Mod 167 is unpublished (the user publishes it): uploading a file never publishes the page. The v1 API may not list an
# unpublished mod, so the v3 uid is computed (game id << 32 | mod id; FF6 PR is game 4335). The key is the repository's
# Actions secret NEXUSMODS_API_KEY, copied sealed from the user's own secret; nobody reads it.
NEXUS_WORKFLOW = """name: Publish Nexus release

on:
  release:
    types: [published]
  workflow_dispatch:
    inputs:
      version:
        description: Release version to upload (for example, 0.26.7)
        required: true
        type: string

permissions:
  contents: read

concurrency:
  group: nexus-mod-167
  cancel-in-progress: false

jobs:
  publish:
    if: >-
      github.event_name == 'workflow_dispatch' ||
      (github.event.release.draft == false && github.event.release.prerelease == false)
    runs-on: ubuntu-latest

    steps:
      - name: Resolve release version
        id: version
        shell: bash
        env:
          DISPATCH_VERSION: ${{ inputs.version }}
          RELEASE_TAG: ${{ github.event.release.tag_name }}
        run: |
          version="${DISPATCH_VERSION:-$RELEASE_TAG}"
          version="${version#v}"
          if [[ ! "$version" =~ ^[0-9]+\\.[0-9]+\\.[0-9]+$ ]]; then
            echo "Invalid release version: $version" >&2
            exit 1
          fi
          echo "version=$version" >> "$GITHUB_OUTPUT"

      - name: Download the tested release zip
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
          VERSION: ${{ steps.version.outputs.version }}
        run: |
          mkdir -p release
          gh release download "v$VERSION" --repo "$GITHUB_REPOSITORY" --pattern "BraveNewWorld-PR.zip" --dir release
          test -f release/BraveNewWorld-PR.zip

      - name: Check the zip (the player package, no ROM, no scripts)
        shell: python
        run: |
          import pathlib
          import zipfile

          with zipfile.ZipFile("release/BraveNewWorld-PR.zip") as archive:
              names = [entry.filename for entry in archive.infolist() if not entry.is_dir()]
          roots = {pathlib.PurePosixPath(name).parts[0] for name in names}
          if roots != {"BepInEx", "FINAL FANTASY VI_Data", "BraveNewWorld-README.txt"}:
              raise SystemExit(f"Unexpected top-level entries: {sorted(roots)}")
          for name in names:
              path = pathlib.PurePosixPath(name)
              if path.is_absolute() or ".." in path.parts:
                  raise SystemExit(f"Unsafe archive path: {name}")
              if path.suffix.lower() not in (".dll", ".json", ".csv", ".txt"):
                  raise SystemExit(f"Not plugin or data (no ROMs, scripts or programs): {name}")
          if "BepInEx/plugins/BraveNewWorld/BraveNewWorld.PR.dll" not in names:
              raise SystemExit("The zip has no Brave New World plugin")
          print(f"{len(names)} files ok")

      - name: Read the release notes for the Nexus changelog
        id: notes
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
          VERSION: ${{ steps.version.outputs.version }}
        run: |
          gh release view "v$VERSION" --repo "$GITHUB_REPOSITORY" --json body --jq .body |
            sed -e '/^#/d' -e 's/\\*\\*//g' -e 's/`//g' -e 's/^- /* /' | sed -e '/./,$!d' > notes.txt
          {
            echo "changelog<<NOTES_END"
            cat notes.txt
            echo "NOTES_END"
          } >> "$GITHUB_OUTPUT"

      - name: Find the Nexus file to update
        id: nexus_file
        shell: bash
        env:
          NEXUSMODS_API_KEY: ${{ secrets.NEXUSMODS_API_KEY }}
        run: |
          mod_uid=$(( 4335 * 4294967296 + 167 ))
          response="$(curl --fail --silent --show-error \\
            --header "apikey: $NEXUSMODS_API_KEY" \\
            --header "User-Agent: ff6pr-brave-new-world/release-workflow" \\
            "https://api.nexusmods.com/v3/mods/$mod_uid/files")"
          file_id="$(jq -er '.data.mod_files | map(select(.is_active == true)) |
            if length == 1 then .[0].id else error("Expected exactly one active file, got \\(length)") end' <<<"$response")"
          echo "file_id=$file_id" >> "$GITHUB_OUTPUT"

      - name: Upload Brave New World to Nexus Mods
        uses: Nexus-Mods/upload-action@98532f7670c27cbecc78fae03fa3e278147827e2 # v1.0.0-beta.11
        with:
          api_key: ${{ secrets.NEXUSMODS_API_KEY }}
          file_id: ${{ steps.nexus_file.outputs.file_id }}
          filename: release/BraveNewWorld-PR.zip
          version: ${{ steps.version.outputs.version }}
          display_name: Brave New World ${{ steps.version.outputs.version }}
          description: Brave New World 3.0 for FINAL FANTASY VI Pixel Remaster. The install steps are on the mod page.
          category: main
          archive_existing_version: "true"
          primary_mod_manager_download: "true"
          allow_mod_manager_download: "false"
          show_requirements_pop_up: "false"
          update_mod_version: "true"
          changelog: ${{ steps.notes.outputs.changelog }}
          mod_id: 167
"""


def main():
    out = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT)
    entries = list(R.collect(PARTS))
    texts = {"README.md": README, ".gitignore": R.GITIGNORE_COMMON + "mods/\n",
             "pr/requirements.txt": R.REQUIREMENTS, "pr/modkit.json": R.manifest(keep=("bnw",)),
             ".github/workflows/publish-nexus.yml": NEXUS_WORKFLOW}
    done = R.write(out, entries, texts, submodules=[("kit", R.KIT_URL, R.head(R.KIT_REPO))])
    print(f"BNW publish: {done} -> {out}")


if __name__ == "__main__":
    main()
