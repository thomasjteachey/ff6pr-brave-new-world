"""Brave New World's own description tables, for BNW's profile (profile_bnw.py) and plugin data.

The kit's tools/bnw_text.py is the shared description decoder (the SNES game's item / spell / lore /
Blitz / Bushido / bonus text tables, read from any FF6 ROM); what only BNW has lives here, in BNW's
repository (repo_publish BNW_TOOLS: port_bnw_*.py), moved out of the kit file on 2026-11-21:

  rage_descriptions(rom) -> {SNES monster id: (2/3 attack, 1/3 attack)}  BNW's Rage table at C4/A820
  esper_descriptions()   -> [...27] summon effect text from BNW's asm (asm/banks/c4.asm), SNES esper order
  BNW_ROM                   the built BNW ROM (bnw_plugin_data reads its description tables)
"""
import os
import re

import kitpath  # noqa: F401  (the modding kit's tools, when it's this repository's kit/ submodule)
import bnw_text as T
import prdata as P

BNW_ROM = os.path.normpath(os.path.join(P.ROOT, "..", "build", "brave-new-world", "Brave New World 3.0 FRC.sfc"))
C4_ASM = os.path.join(T.SOURCE, "asm", "banks", "c4.asm")

# BNW's Rage descriptions (bnw 3.0 ragedance_descriptions.asm, by dn and Bropedio): a pointer per
# monster at C4/A820 (bank C4), each text two lines, the 2/3 attack then the 1/3 attack (the
# menu adds the fractions); an empty text = no description (a rage Gau can't learn in BNW)
RAGE_PTRS, RAGE_BANK = 0x04A820, 0x040000
FRACTIONS = {0x0F: "2/3", 0x10: "1/3", 0x13: "5/8", 0xCF: "*"}   # and the font's "*"


def rage_descriptions(rom_path=BNW_ROM):
    """{SNES monster id: (2/3 attack, 1/3 attack)} in plain words: icons as their element's name,
    magic-type dots dropped (the PR's spell names carry their own icon)."""
    with open(rom_path, "rb") as f:
        rom = f.read()
    if len(rom) % 0x8000 == 0x200:
        rom = rom[0x200:]
    out = {}
    for e in range(255):   # 255: past the table's end (Tonberry's slot holds other data)
        p = int.from_bytes(rom[RAGE_PTRS + 2 * e:RAGE_PTRS + 2 * e + 2], "little")
        raw = rom[RAGE_BANK + p:RAGE_BANK + p + 160]
        b = raw[:raw.index(0)] if 0 in raw else b""
        if not b:
            continue
        text = "".join(FRACTIONS.get(x) or T.CODES.get(x, T.FONT.get(x, "")) for x in b)
        text = text.replace("`", "\"").replace("^", "\"").replace("[", "\"").replace("]", "\"")
        text = re.sub(r"\{(white|black|gray|lore)\}", "", text)
        text = re.sub(r"\{([A-Za-z]+)\}(?=\{)", r"\1/", text)   # {Ice}{Ice}{Ice}: one element
        text = re.sub(r"\{([A-Za-z]+)\}", r"\1", text)
        text = re.sub(r"\b(\w+)/\1/\1\b", r"\1", text)
        lines = [re.sub(r"\s+", " ", x).strip() for x in text.split("\n")]
        if len(lines) == 2 and all(lines):
            out[e] = (lines[0], lines[1])
    return out


def esper_descriptions():
    """BNW's 27 esper summon descriptions from its asm (the "Ramuh" pointer list in c4.asm), SNES esper order."""
    labels, pointers = T._labels_and_pointers(C4_ASM)
    order = next((p for p in pointers.values() if p and p[0] == "Ramuh"), [])
    return [T.tidy(labels.get(lbl, "")) for lbl in order]
