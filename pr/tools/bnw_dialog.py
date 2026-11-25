"""Brave New World's dialogue (and its alternate script, Vanilla New World) for the PR's story lines.

BNW's scripts are text in its source (FF3usME dumps, [caption #N] = SNES dialogue N + 1):
    mods/brave-new-world/source/bnw 3.0/scripts/dialog_script.txt          Brave New World
    mods/brave-new-world/source/bnw 3.0/scripts/vnw/dialog_script.txt      Vanilla New World
("the bnw script but with no 4th wall break, less profanities", Vanilla New World.ips: the user
wants it as a Config option). tools/dialog_map.py places each PR story line on an SNES dialogue
and the speaker turns of it; here each placed line gets BNW's turns for it, in the PR's text
conventions: names as (TINA)-style tags, "Name:" prefixes dropped (the PR shows the speaker), the
SNES's ellipsis "_" as "...", pauses and SNES end codes dropped.

Output: <mod>/bnw_dialog.json = {"bnw": {key: [page, ...]}, "vnw": {...}}: BNW's own text only.
The plugin (DialogText.cs) puts it into the game's lines when they load, keeping each PR line's
own timing tags (<WAIT..><AUTO_P>, <PRESS>) and joining pages the way that line does.

Left as the PR has them: lines dialog_map couldn't place, centered narration (<CT>), choice
options (the question gets BNW's text), item messages, speaker names.

    .venv/Scripts/python tools/bnw_dialog.py [--rebuild-map]
"""
import collections
import json
import os
import re
import sys
import textwrap

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kitpath  # noqa: E402,F401
import dialog_map as M  # noqa: E402
import prdata as P  # noqa: E402

SRC = os.path.join(P.PROJECT if hasattr(P, "PROJECT") else os.path.dirname(P.ROOT), "mods", "brave-new-world", "source", "bnw 3.0", "scripts")
SCRIPTS = {"bnw": "dialog_script.txt", "vnw": os.path.join("vnw", "dialog_script.txt")}
OUT = os.path.join(P.ROOT, "mods", "BraveNewWorld", "bnw_dialog.json")
NAMES = ["(TINA)", "(ROCK)", "(KAIEN)", "(SHADOW)", "(EDGAR)", "(MASH)", "(CELES)", "(STRAGUS)", "(RELM)",
         "(SETZER)", "(MOG)", "(GAU)", "(GOGO)", "(UMARO)"]
SPEAKER = re.compile(r"^\s*(<A\d+>|[A-Z][A-Za-z0-9.'\- ]{0,16}):(?:\s+|(?=_))")
TURN_SPLIT = re.compile(r"\n(?=\s*(?:<A\d+>|[A-Z][A-Za-z0-9.'\- ]{0,16}):(?:\s|_))")


def captions(path, first=1):
    """{dialogue: text}: the field script's caption #N is dialogue N + 1; the battle script's
    caption #N is battle dialogue N (dialog_map numbers it BATTLE + N)."""
    t = open(path, encoding="latin-1").read()
    return {int(m.group(1)) + first: m.group(2).strip()
            for m in re.finditer(r"\[caption #(\d+)\]\n(.*?)(?=\n\[caption #|\n\[Section|\Z)", t, re.S)}


def turns(caption):
    """A dialogue's speaker turns: its pages (<EOP>), and a page split where a new "Name:" starts."""
    # the battle script writes names in hex (<A$01>): as the field script's <A1>
    caption = re.sub(r"<A\$([0-9A-Fa-f]{2})>", lambda m: f"<A{int(m.group(1), 16)}>", caption)
    out = []
    for page in caption.split("<EOP>"):
        if not page.strip():
            continue
        out += [x for x in TURN_SPLIT.split(page) if x.strip()]
    return out


def convert(turn, mode="plain"):
    """One BNW turn as PR text (None: nothing to show). mode "plain": one paragraph (the PR wraps
    it); "lines": BNW's line breaks kept, indents dropped (centered narration: the plugin centers
    each line); "formatted": line breaks kept, an indented line as the PR's (FULL_SPACE) indent, the
    SNES's waits (<E$hh>) as the PR's <WAIThh> (letters, the opera's lyrics: the PR kept the SNES's
    timing values, so the singing stays with the music)."""
    s = turn
    if "<I>" in s or re.match(r"\s*(Got|Received|Found|Obtained)\b", s):
        return None
    if "<C>" in s:
        s = s.split("<C>")[0]
    s = SPEAKER.sub("", s.strip("\n") if mode == "formatted" else s.strip())
    if re.search(r"[\\{}|^£]|\$(?![0-9A-F]{2}>)", s):
        return None   # SNES button glyphs (a controls tutorial): the PR's own lines show its controls
    s = re.sub(r"<A(\d+)>", lambda m: NAMES[int(m.group(1))] if int(m.group(1)) < len(NAMES) else "", s)
    s = s.replace("<$75>", "♪").replace("<P>", " ")   # <P>: a battle message's pause
    if mode == "formatted":
        s = re.sub(r"<E\$([0-9A-F]{2})[A-E]?>", lambda m: f"<WAIT{m.group(1).lower()}>", s)
        s = s.replace("<D>", "")
    else:
        s = s.replace("<D>", " ")
    s = re.sub(r"<D\d+>|<E\$[0-9A-F]+[A-E]?>|<OP\$[0-9A-F]+>|<\$[0-9A-F]+>", "", s)
    s = s.replace("_", "...")
    s = s.replace("[", '"').replace("]", '"')   # BNW's font shows these as quotation marks
    if mode == "plain":
        s = re.sub(r"\s*\n\s*", " ", s)
        s = re.sub(r" {2,}", " ", s).strip()
    elif mode == "lines":
        rows = [re.sub(r" {2,}", " ", r).strip() for r in s.split("\n")]
        rows = [r for r in rows if r]
        if len(rows) == 1 and len(rows[0]) > 36:
            rows = textwrap.wrap(rows[0], 34)
        s = "\n".join(rows)
    else:
        rows = []
        for r in s.split("\n"):
            if not r.strip():
                continue
            indent = len(r) - len(r.lstrip(" "))
            t = re.sub(r" {2,}", " ", r.strip())
            rows.append(("(FULL_SPACE)(FULL_SPACE)" if indent >= 2 and rows else "") + t)
        s = re.sub(r"(<WAIT[0-9a-f]+>)+$", "", "\n".join(rows))   # the PR line's own end wait follows
    return s or None


def mode_of(pr):
    """How a PR line takes BNW's text: None = it stays the PR's."""
    if "(ITEM_N)" in pr or "(NUM)" in pr:
        return None
    if "<CT>" in pr:
        if pr.startswith("<CT>Obtained"):
            return "obtained"
        plain = re.sub(r"<[^>]*>|\([A-Z_]+\)|\\n", " ", pr)
        return "lines" if len(re.findall(r"[A-Za-z]", plain)) >= 35 else None
    if re.search(r"<WAIT[0-9a-f]+>\\n", pr):
        return "lyrics"
    if "(FULL_SPACE)" in pr:
        return "formatted"
    return "plain"


def obtained(pr, caption):
    """An esper's "Obtained X magicite!": BNW's name for it (the menus show BNW's names); items stay
    the PR's (the PR's script gives its own item)."""
    m = re.search(r"magicite \[([^\]]+)\]", caption)
    if "magicite" not in pr or not m:
        return None
    return [f"Obtained {m.group(1).strip()} magicite!"]


# BNW's own lines the PR has no key for, in scenes the BNW port adds to the PR's map scripts (keys
# BNW_<PR event>_<nn>; the plugin adds them to the game's text, with their speaker, in every language
# and script choice: there's no PR text to fall back on). Key -> SNES dialogue (dialog_map numbering).
ADDED_LINES = {
    # the Sealed Gate (PR sc_e_0460_1, before its first battle; bnw-enemy-ai's script): Kefka followed you
    **{f"BNW_E0460_{n + 1:02d}": 0xA60 + n for n in range(6)},
    # Mog joining in the World of Ruin (PR sc_e_0778, BNW CC/396C): his tips on BNW's Dances
    "BNW_E0778_01": 0x279, "BNW_E0778_02": 0x27B,
}
# speakers for added lines whose BNW text names none (the PR shows the name box from these)
ADDED_SPEAKERS = {"BNW_E0778_01": "(MOG)", "BNW_E0778_02": "(MOG)"}


def repurposed_dialogues():
    """SNES dialogue numbers BNW took from a scene it removed and uses elsewhere (the auction's lines
    are its Sealed Gate's, Bob's, the Esper World's...): the maps vanilla's field scripts show the
    number in and BNW's don't meet (tools/snes_events.py on both ROMs; BNW's own event code in bank
    ED included). A PR line placed on such a number would get unrelated text. Numbers vanilla uses that
    BNW doesn't are left alone: mostly BNW code the decoder doesn't reach yet, not removed scenes."""
    import event_diff
    import snes_events as E
    path = os.path.join(P.ROOT, "..", "build", "brave-new-world", "Brave New World 3.0 FRC.sfc")
    if not os.path.exists(path):
        return set()

    def usage(rom):
        p = E.Program(rom).walk_all()
        u = collections.defaultdict(set)
        for (off, md), ins in p.code.items():
            if ins.cls == "dialog" and "dlg" in ins.args:
                u[ins.args["dlg"] & 0x7FFF].update(p.maps_of[off])
        return u
    uv, ub = usage(E.load("us")), usage(event_diff.rom_for(path, [(0x230000, 0x260000)]))
    return {d for d in uv if d in ub and not (uv[d] & ub[d])}   # the line map numbers dialogues as the event code does


# choices the port gives more options than the PR's (port_bnw_scenes): the PR's option group -> the
# option keys it adds after the PR's own, which take BNW's extra options' text
EXTRA_OPTIONS = {
    "E0873_00_010_c": ["BNW_E0873_00_010_c_03"],   # the Narshe blacksmith: Forge Apocalypse / Forge Illumina / Wait
}


# BNW-only dialogues by number, for scenes and NPCs the port adds where the PR has no event of its own
# (bnw-enemy-ai's Bob, who sells the auction's espers): key BNW_D<number, 4 hex digits>, its options
# BNW_D<number>_c_01.. (in order), its "Got the magicite [X]" as the PR's "Obtained X magicite!"
ADDED_DIALOGUES = [
    0xA56, 0xA57, 0xA58, 0xA59, 0xAC3, 0xAC4, 0xB19,   # Bob at Dragon's Neck Cabin (SNES map 186, CB/4F27)
    0xA5A, 0xA55,                                       # Bob at the Coliseum (413, CB/4F85)
    0xA5B, 0xA5C, 0xA5D, 0xA5E, 0xA5F,                  # Ultros's EL respec (413, CB/4E5E; port_bnw_menus)
    0x8C8, 0x8C9, 0x9E6,                                # the Cultists' Tower seller (362, CC/51FF)
    0x675,                                              # Mikhail points Edgar to Julian (Figaro, SNES 58, CB/5201; DialogText)
    0x16B, 0x16C, 0x16D,                                # the Returners' briefing's end (CB/034D; port_bnw_scenes)
    # bnw-scenes (tools/port_bnw_added_scenes.py): BNW's added scenes, single-speaker dialogues
    0x5FC, 0x69F, 0x6A2, 0xABF,                         # Kefka's Tower's barrier (the Falcon, SNES 17, EE/AF50)
    0x0F5, 0xB60, 0x529,                                # Leo's Crest: South Figaro WoR (EE/B0B0), Leo's grave (EE/B0CD)
    0x84D,                                              # Shadow hired with a full party (Kohlingen 191, ED/8DC6)
    0x0FB,                                              # "Sealed by a mysterious force." (Kefka's Tower 331, CB/5252)
    0x6A5,                                              # the Falcon's wheel: Lift off / Find Doom Gaze / Do nothing (CB/5238)
]

# bnw-scenes (tools/port_bnw_added_scenes.py): BNW-only dialogues whose speaker changes inside them get one key
# per speaker turn (turns(): each page, split where a new "Name:" starts), BNW_D<number>_T<nn> from 01 in
# BNW's order, each with its own speaker (a turn with none, "Got [X]", has no name box); the VNW script's
# turns line up with BNW's for these. Single-speaker dialogues stay in ADDED_DIALOGUES.
ADDED_TURN_DIALOGUES = [
    0x5FD, 0xABD, 0x6A0, 0xABE, 0x6A1, 0x5FF, 0xAC0,   # Kefka's Tower's barrier (the Falcon, SNES 17, EE/AF50)
    0x0F7,                                              # Leo's Spirits, South Figaro WoR (EE/B0C3)
]


def _turn_speaker(turn):
    m = SPEAKER.match(turn)
    if not m:
        return ""
    a = re.fullmatch(r"<A(\d+)>", m.group(1))
    return (NAMES[int(a.group(1))] if int(a.group(1)) < len(NAMES) else "") if a else m.group(1).strip()


def turn_lines(caps):
    """{BNW_D<number>_T<nn>: [speaker, page]} for ADDED_TURN_DIALOGUES."""
    out = {}
    for d in ADDED_TURN_DIALOGUES:
        for n, t in enumerate(turns(caps.get(d, "")), 1):
            page = convert(t) or _got(t)
            if page:
                out[f"BNW_D{d:04X}_T{n:02d}"] = [_turn_speaker(t), page]
    return out


def turn_keys(d):
    """The keys of one ADDED_TURN_DIALOGUES dialogue, in order (BNW's script), for the scripts that show it."""
    return sorted(k for k in turn_lines(captions(os.path.join(SRC, SCRIPTS["bnw"])))
                  if k.startswith(f"BNW_D{d:04X}_T"))


def _got(turn):
    """BNW's "Got the magicite [X]" / "Got [X]" in the PR's words, or None."""
    m = re.search(r"Got the magicite \[([^\]]+)\]", turn)
    if m:
        return f"Obtained {m.group(1).strip()} magicite!"
    m = re.search(r"Got \[([^\]]+)\]", turn)
    return f"Obtained {m.group(1).strip()}!" if m else None


def added_lines(caps):
    """{key: [speaker, page, ...]} for ADDED_LINES and ADDED_DIALOGUES from a script's captions; speaker
    "" = no name box."""
    out = {}
    lines = dict(ADDED_LINES)
    lines.update({f"BNW_D{d:04X}": d for d in ADDED_DIALOGUES})
    for d in ADDED_DIALOGUES:
        for n, o in enumerate(choices(caps.get(d, "")) or [], 1):
            out[f"BNW_D{d:04X}_c_{n:02d}"] = ["", o]
    for key, d in lines.items():
        bt = turns(caps.get(d, ""))
        if not bt:
            continue
        m = SPEAKER.match(bt[0])
        speaker = ""
        if m:
            name = m.group(1)
            a = re.fullmatch(r"<A(\d+)>", name)
            speaker = (NAMES[int(a.group(1))] if int(a.group(1)) < len(NAMES) else "") if a else name.strip()
        pages = [p for p in (convert(x) or _got(x) for x in bt) if p]
        if pages:
            out[key] = [speaker or ADDED_SPEAKERS.get(key, "")] + pages
    out.update(turn_lines(caps))   # bnw-scenes: per-speaker-turn keys (ADDED_TURN_DIALOGUES)
    return out


# "Obtained <name>!" / "Obtained <name> magicite!" in a PR story line (a page each); the plugin
# (DialogText) uses the same pattern on the line it has
OBTAINED = re.compile(r"Obtained (.+?)( magicite)?!")
NAME_KEYS = re.compile(r"MSG_(ITEM|WEAPON|ARMOR|MAGIC)_NAME_\d+$")


def obtained_names(pre):
    """{PR line key: [name key per "Obtained X!" in it, "" where X isn't an item or esper]}: the
    PR's lines name the item its script gives by the PR's name (the Gold Hairpin), which BNW renamed
    (Goggles), so the plugin puts in the game's own current name for that key. Keys only: no text."""
    names = collections.defaultdict(set)
    for k, v in P.messages().items():
        if NAME_KEYS.match(k):
            names[re.sub(r"<[^>]*>", "", v).strip()].add(k)
    out = {}
    for k, v in pre.items():
        if k.startswith("SPEAKER") or "Obtained" not in v:
            continue
        keys = []
        for m in OBTAINED.finditer(v):
            found = names.get(m.group(1).strip(), set())
            if m.group(2):   # an esper: its magic name, not an item's
                found = {x for x in found if x.startswith("MSG_MAGIC_NAME")}
            else:
                found = {x for x in found if not x.startswith("MSG_MAGIC_NAME")}
            keys.append(next(iter(found)) if len(found) == 1 else "")
        if any(keys):
            out[k] = keys
    # These scripts give a different reward in BNW. Resolve each obtained line through
    # the content row actually awarded, including both swapped magicite locations.
    import idmap
    for key, kind, item in (("E0062_00_066_a_14", "item", 184),
                            ("E0062_01_067_a_47", "item", 183),
                            ("E0651_00_212_a_02", "esper", 0x3F),
                            ("E0963_00_192_a_01", "esper", 0x4E)):
        content = idmap.item_content(item) if kind == "item" else idmap.esper_content(item - 0x36)
        out[key] = [P.Table("content")[content]["mes_id_name"]]
    # The Sealed Gate gives four different items; its replacement narration already
    # lists all four, so one-item name substitution would corrupt the line.
    out.pop("E0450_00_408_a_02", None)
    return out


def choices(caption):
    """BNW's choice options in order (converted), or None."""
    if "<C>" not in caption:
        return None
    opts = [convert(o.strip()) for o in caption.split("<C>")[1:]]
    return opts if all(opts) else None


TAG_NAME = {"(TINA)": "terra", "(ROCK)": "locke", "(KAIEN)": "cyan", "(SHADOW)": "shadow", "(EDGAR)": "edgar",
            "(MASH)": "sabin", "(CELES)": "celes", "(STRAGUS)": "strago", "(RELM)": "relm", "(SETZER)": "setzer",
            "(MOG)": "mog", "(GAU)": "gau", "(GOGO)": "gogo", "(UMARO)": "umaro", "(KINGAKU)": "kefka"}
SNES_NAME = ["terra", "locke", "cyan", "shadow", "edgar", "sabin", "celes", "strago", "relm", "setzer", "mog",
             "gau", "gogo", "umaro"]
SAME = {"biggs": "biggs/wedge", "wedge": "biggs/wedge", "vicks": "biggs/wedge"}
# speakers compared at all: named characters (generic NPC names differ between the scripts:
# "Old Man" / "Man", "Soldier" / "Guard")
NAMED = set(SNES_NAME) | {"kefka", "biggs/wedge", "banon", "leo", "gestahl", "ultros", "arvis", "cid", "duane",
                          "katarin", "lone wolf", "siegfried", "ziegfried", "maduin", "madeline", "elder", "draco", "maria"}


def speaker_key(name):
    """A speaker as one comparable word ("" = none)."""
    if not name:
        return ""
    n = name.strip()
    m = re.fullmatch(r"<A(\d+)>", n)
    if m:
        n = SNES_NAME[int(m.group(1))] if int(m.group(1)) < len(SNES_NAME) else ""
    n = TAG_NAME.get(n, n).lower()
    return SAME.get(n, n)


def turn_speaker(turn):
    m = SPEAKER.match(turn)
    return speaker_key(m.group(1)) if m else ""


# a line left as the PR's costs nothing, a turn left out a little, a weak match more: wrong text
# in a box is worse than the PR's own
DROP_TURN, EMPTY_KEY, ASSIGN_COST = 0.04, 0.0, 0.08


def _segment(order, pr_text, bnw_turns, key_speaker, guess, guess_weight=0.2):
    """Several PR lines on one dialogue: each line gets a run of consecutive BNW turns (maybe none,
    and a turn may go to no line), the runs in order, maximizing for each line the English word
    overlap with the PR line, plus a little for the first guess's turns and the speaker's name
    (a named speaker in another named speaker's line costs a lot)."""
    import dialog_align as D
    n, m = len(order), len(bnw_turns)
    tt = [set(D.toks(convert(t) or "")) for t in bnw_turns]
    ts = [turn_speaker(t) for t in bnw_turns]
    kt = [set(D.toks(pr_text.get(k, ""))) for k in order]
    ks = [key_speaker.get(k, "") for k in order]
    gi = {j: i for i, k in enumerate(order) for j in guess.get(k, [])}

    def score(i, a, b):
        if a == b:
            return -EMPTY_KEY
        words = set().union(*tt[a:b])
        s = 2 * len(words & kt[i]) / max(1, len(words) + len(kt[i])) - ASSIGN_COST
        for j in range(a, b):
            if gi.get(j) == i:
                s += guess_weight / (b - a)
            if ts[j] in NAMED and ks[i] in NAMED:
                s += 0.15 if ts[j] == ks[i] else -0.6
        return s

    NEG = float("-inf")
    best = [[NEG] * (m + 1) for _ in range(n + 1)]
    back = [[None] * (m + 1) for _ in range(n + 1)]
    best[0][0] = 0.0
    for j in range(1, m + 1):
        best[0][j] = best[0][j - 1] - DROP_TURN
        back[0][j] = (0, j - 1, None)
    for i in range(1, n + 1):
        for j in range(m + 1):
            for a in range(j + 1):
                for start in range(a + 1):
                    if best[i - 1][start] == NEG:
                        continue
                    v = best[i - 1][start] - DROP_TURN * (a - start) + score(i - 1, a, j)
                    if v > best[i][j]:
                        best[i][j], back[i][j] = v, (start, a, j)
    # the last line's run may end before the last turn: the rest dropped
    j_end = max(range(m + 1), key=lambda j: best[n][j] - DROP_TURN * (m - j))
    out, i, j = {}, n, j_end
    while i > 0:
        start, a, b = back[i][j]
        out[order[i - 1]] = list(range(a, b))
        i, j = i - 1, start
    return out


def distribute(keys, jp_turns, bnw_turns, key_speaker=None, pr_text=None):
    """keys: [(first JP turn, last JP turn, key)] in the dialogue's order -> {key: [BNW turn indexes]}.
    Same number of turns: by the JP turn ranges; otherwise by position (each BNW turn's middle, as a
    fraction of the dialogue's length, falls in one key's range)."""
    keys = sorted(keys)
    out = _place(keys, jp_turns, bnw_turns)
    if pr_text and len(keys) > 1 and bnw_turns:
        same = len(bnw_turns) == len(jp_turns)
        return _segment([k for a, b, k in keys], pr_text, bnw_turns, key_speaker or {}, out, 0.2 if same else 0.05)
    if not key_speaker:
        return out
    # a turn whose speaker isn't its key's goes to the key of its speaker in the dialogue, else
    # nowhere (its PR line wasn't placed, or the PR cut it): never into another speaker's box
    order = [k for a, b, k in keys]
    fixed = {k: [] for k in order}
    for k in order:
        for j in out[k]:
            ts, ks = turn_speaker(bnw_turns[j]), key_speaker.get(k, "")
            if ts not in NAMED or ks not in NAMED or ts == ks:
                fixed[k].append(j)
                continue
            same = [k2 for k2 in order if key_speaker.get(k2, "") == ts]
            if same:
                k2 = min(same, key=lambda x: abs(order.index(x) - order.index(k)))
                fixed[k2].append(j)
    return {k: sorted(v) for k, v in fixed.items()}


def _place(keys, jp_turns, bnw_turns):
    if len(keys) == 1:
        return {keys[0][2]: list(range(len(bnw_turns)))}
    out = {k: [] for a, b, k in keys}
    if len(bnw_turns) == len(jp_turns):
        for j in range(len(bnw_turns)):
            for a, b, k in keys:
                if a <= j <= b:
                    out[k].append(j)
                    break
        return out
    jl = [max(1, len(t)) for t in jp_turns]
    total = sum(jl)
    starts = [sum(jl[:i]) / total for i in range(len(jl))] + [1.0]
    ranges = [(starts[a], starts[b + 1], k) for a, b, k in keys]
    bl = [max(1, len(t)) for t in bnw_turns]
    btot = sum(bl)
    pos = 0
    for j, n in enumerate(bl):
        mid = (pos + n / 2) / btot
        pos += n
        hit = next((k for lo, hi, k in ranges if lo <= mid < hi), None)
        if hit is None:
            hit = min(ranges, key=lambda r: min(abs(mid - r[0]), abs(mid - r[1])))[2]
        out[hit].append(j)
    return out


def build(rebuild_map=False, write=True):
    """{"version": 1, "bnw": {...}, "vnw": {...}}, written to OUT (write=False: only returned; the
    porter writes it with the mod's other files: profile_bnw.extra_data)."""
    path = os.path.join(M.OUT, "dialog_map.json")
    if rebuild_map or not os.path.exists(path):
        M.build(verbose=True)
    dmap = json.load(open(path, encoding="utf-8"))
    jp = json.load(open(os.path.join(M.OUT, "jp_turns.json"), encoding="utf-8"))
    pre = M.pr_lines("en")
    # kept as the PR has them: centered narration, item/number lines, names, and timed lines (the
    # opera's lyrics: <WAITnn> then a new line, (FULL_SPACE) indents)
    mode = {k: (None if k.startswith("SPEAKER") else mode_of(v)) for k, v in pre.items()}
    # the opera stays the PR's: its singing is voiced (the user, 2026-11-20: "don't change the maria
    # song please ... it is voiced, so it would be weird if it were different"): every sung line (♪),
    # and any line or choice that quotes the lyrics (Celes's script, "(O my hero...)" on stage)
    def norm(t):
        return " ".join(re.findall(r"[a-z']+", re.sub(r"<[^>]*>|\([A-Z_]+\)|\\n", " ", t).lower()))
    lyric = {norm(r) for v in pre.values() if "♪" in v for r in re.split(r"\\n", v) if len(norm(r).split()) >= 3}
    for k, v in pre.items():
        rows = [norm(r) for r in re.split(r"\\n", v)]
        if "♪" in v or any(r in lyric for r in rows if r) or \
                (k.split("_")[-2:-1] == ["c"] and len(norm(v)) >= 8 and any(l.startswith(norm(v)) for l in lyric)):
            mode[k] = None
    option = {k for k in pre if re.search(r"_c_\d+$", k)}   # choice options: from BNW's choices below
    cha = {}
    for line in open(f"{P.MESSAGE}/story_cha_en.txt", encoding="utf-8").read().splitlines():
        if "	" in line:
            k, v = line.split("	", 1)
            cha[k] = speaker_key(v)
    by_dlg = collections.defaultdict(list)
    for k, (d, a, b, score, how) in dmap.items():
        if k in pre and mode.get(k) and k not in option:
            by_dlg[d].append((a, b, k))
    result, stats, added = {}, {}, {}
    reused = repurposed_dialogues()
    import dialog_align as DA
    for name, rel in SCRIPTS.items():
        caps = captions(os.path.join(SRC, rel))
        caps.update(captions(os.path.join(SRC, os.path.dirname(rel), "btlBigCaption_script.txt"), M.BATTLE))
        lines, st = {}, collections.Counter()
        for d, keys in by_dlg.items():
            bt = turns(caps.get(d, ""))
            if not bt:
                st["no BNW text"] += len(keys)
                continue
            st["same turn count" if len(bt) == len(jp[d]) else "by position"] += 1
            # the PR repeats a dialogue in several events (a scene's copy for each route): each
            # scene's lines get the whole dialogue, not a share of it
            scenes = collections.defaultdict(list)
            for key in keys:
                scenes[M.scene(key[2])].append(key)
            for group in scenes.values():
                for a, b, k in [key for key in group if mode[key[2]] == "obtained"]:
                    got = obtained(pre[k], caps.get(d, ""))
                    if got:
                        lines[k] = got
                        st["obtained"] += 1
                group = [key for key in group if mode[key[2]] != "obtained"]
                if not group:
                    continue
                for k, idx in distribute(group, jp[d], bt, cha, pre).items():
                    m = mode[k]
                    if m == "lyrics":
                        # sung lines only from BNW's own timed version (its <E$hh> waits)
                        if not any("<E$" in bt[j] for j in idx):
                            continue
                        m = "formatted"
                    pages = [p for p in (convert(bt[j], m) for j in idx) if p]
                    if pages and mode[k] == "lyrics":
                        # the PR sings two lines a box, each box turned by its own timed page break
                        rows = [r for p in pages for r in p.split("\n")]
                        pages = ["\n".join(rows[i:i + 2]) for i in range(0, len(rows), 2)]
                        pages = [re.sub(r"^(\(FULL_SPACE\))+", "", p) for p in pages]
                    if pages and d in reused:
                        a, b = set(DA.toks(pre[k])), set(DA.toks(" ".join(pages)))
                        if 2 * len(a & b) / max(1, len(a) + len(b)) < 0.3:
                            st["repurposed: PR's kept"] += 1
                            continue
                    if pages:
                        lines[k] = pages
                        if m != "plain":
                            st[m] += 1
                    else:
                        st["key without text"] += 1
        # choice options: each PR option group (_c_NN after its question) from the BNW choice in
        # its question's dialogue, when they have as many options
        stem_dlg = collections.defaultdict(set)
        for k, (d, a, b, score, how) in dmap.items():
            if k in pre and k not in option:
                stem_dlg["_".join(k.split("_")[:3])].add(d)
        groups = collections.defaultdict(list)
        for k in sorted(option):   # sorted: a set's order follows each process's string hashes (identical ports, same bytes)
            groups[k.rsplit("_", 1)[0]].append(k)
        extra = {}
        for g, ks in groups.items():
            ks.sort()
            ks = ks + EXTRA_OPTIONS.get(g, [])
            if any(mode.get(k) is None for k in ks if k in mode):
                continue   # an option that quotes the opera's lyrics: the PR's choice stays
            for d in sorted(stem_dlg.get("_".join(ks[0].split("_")[:3]), ())):
                opts = choices(caps.get(d, ""))
                if opts and len(opts) == len(ks):
                    for k, o in zip(ks, opts):
                        if k.startswith("BNW_"):
                            extra[k] = ["", o]       # an option the port adds: DialogText's added lines
                        else:
                            lines[k] = [o]
                    st["choice options"] += len(ks)
                    break
        result[name] = lines
        added[name] = {**added_lines(caps), **extra}
        st["lines"] = len(lines)
        stats[name] = dict(st)
    changed = sum(1 for k in result["vnw"] if result["vnw"][k] != result["bnw"].get(k))
    payload = {"version": 1, **result, "names": obtained_names(pre), "added": added}
    if write:
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        json.dump(payload, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    print(f"{OUT if write else 'bnw_dialog.json'}: " + "; ".join(f"{n} {s}" for n, s in stats.items()) +
          f"; Vanilla New World differs on {changed} lines")
    return payload


if __name__ == "__main__":
    build("--rebuild-map" in sys.argv)
