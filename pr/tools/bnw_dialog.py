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


def build(rebuild_map=False):
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
    result, stats = {}, {}
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
        for k in option:
            groups[k.rsplit("_", 1)[0]].append(k)
        for g, ks in groups.items():
            ks.sort()
            if any(mode.get(k) is None for k in ks):
                continue   # an option that quotes the opera's lyrics: the PR's choice stays
            for d in sorted(stem_dlg.get("_".join(ks[0].split("_")[:3]), ())):
                opts = choices(caps.get(d, ""))
                if opts and len(opts) == len(ks):
                    for k, o in zip(ks, opts):
                        lines[k] = [o]
                    st["choice options"] += len(ks)
                    break
        result[name] = lines
        st["lines"] = len(lines)
        stats[name] = dict(st)
    changed = sum(1 for k in result["vnw"] if result["vnw"][k] != result["bnw"].get(k))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump({"version": 1, **result}, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    print(f"{OUT}: " + "; ".join(f"{n} {s}" for n, s in stats.items()) + f"; Vanilla New World differs on {changed} lines")
    return result


if __name__ == "__main__":
    build("--rebuild-map" in sys.argv)
