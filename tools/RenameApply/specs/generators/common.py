"""Shared paths and deterministic helpers for the Wave R / R2 spec generators."""
import json
import os
import re
import xml.parsers.expat
from pathlib import Path

# Immutable historical inputs are preserved under DevData; outputs can be isolated without editing scripts.
# MUGIRL_REPO selects the current integrated tree. MUGIRL_SPEC_INPUT selects an archived input bundle.
# MUGIRL_SPEC_ROOT selects the generated spec workspace (normally a TEMP copy of tools/RenameApply/specs).
REPO = Path(os.environ.get("MUGIRL_REPO", Path(__file__).resolve().parents[4])).resolve()
MODS = Path(os.environ.get("MUGIRL_MODS", REPO.parent)).resolve()
WORKSHOP = Path(os.environ.get("MUGIRL_WORKSHOP", REPO.parents[3] / "workshop" / "content" / "294100")).resolve()
SCRATCH = Path(os.environ.get("MUGIRL_SPEC_INPUT", REPO / "DevData" / "Backups" / "Scratchpad-WaveA-20261015")).resolve()
SPEC = SCRATCH / "spec"
FINAL = Path(os.environ.get("MUGIRL_SPEC_ROOT", REPO / "tools" / "RenameApply" / "specs")).resolve()
GEN = FINAL / "generators"



def load_json(p):
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def save_json(p, data):
    p = Path(p)
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write("\n")


def rel(p, base):
    return str(Path(p).relative_to(base)).replace("\\", "/")


def walk_files(base, exts, skip_dirs=("obj", "bin", ".git", ".vs")):
    base = Path(base)
    for dp, dn, fn in os.walk(base):
        dn[:] = sorted(d for d in dn if d not in skip_dirs)
        for f in sorted(fn):
            if f.lower().endswith(exts):
                yield Path(dp) / f


class XNode:
    __slots__ = ("tag", "attrs", "line", "text", "text_line", "children", "parent")

    def __init__(self, tag, attrs, line, parent):
        self.tag, self.attrs, self.line, self.parent = tag, attrs, line, parent
        self.text, self.text_line, self.children = "", None, []

    def iter(self):
        yield self
        for c in self.children:
            yield from c.iter()

    def path(self):
        parts = []
        n = self
        while n is not None:
            parts.append(n.tag)
            n = n.parent
        return "/".join(reversed(parts))


def parse_xml(path):
    """Line-aware XML parse (expat). Returns root XNode or None."""
    data = Path(path).read_bytes()
    p = xml.parsers.expat.ParserCreate()
    stack, root = [], [None]

    def start(tag, attrs):
        n = XNode(tag, attrs, p.CurrentLineNumber, stack[-1] if stack else None)
        if stack:
            stack[-1].children.append(n)
        else:
            root[0] = n
        stack.append(n)

    def end(tag):
        stack.pop()

    def chars(t):
        if stack:
            n = stack[-1]
            if t.strip() and n.text_line is None:
                n.text_line = p.CurrentLineNumber
            n.text += t

    p.StartElementHandler, p.EndElementHandler, p.CharacterDataHandler = start, end, chars
    try:
        p.Parse(data, True)
    except xml.parsers.expat.ExpatError:
        return None
    return root[0]


# Def root types: the DefDatabase type a def element lands in.
ROOT_OVERRIDES = {
    "AlienRace.ThingDef_AlienRace": "ThingDef",
    "ThingDef_AlienRace": "ThingDef",
    "Mugirl.SlaveApparelDef": "ThingDef",
    "Mugirl.Features.Restraints.RestraintApparelDef": "ThingDef",
}


def def_root(element, cls=None):
    t = cls or element
    if t in ROOT_OVERRIDES:
        return ROOT_OVERRIDES[t]
    if element in ROOT_OVERRIDES:
        return ROOT_OVERRIDES[element]
    return element.split(".")[-1] if element.startswith("Mugirl.") else element


CS_TYPE_RX = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|unsafe|file|new|ref|record)\s+)*(class|struct|interface|enum|record|delegate)\s+(?:[\w<>\[\],. ]+\s+)?(\w+)", re.M)
CS_NS_RX = re.compile(r"^\s*namespace\s+([\w.]+)\s*[;{]?", re.M)


def cs_types(path):
    """(namespace, name, line, kind) of every type declaration (nested included, flat)."""
    text = Path(path).read_text(encoding="utf-8-sig", errors="replace")
    ns_m = CS_NS_RX.search(text)
    ns = ns_m.group(1) if ns_m else ""
    out = []
    for m in re.finditer(r"\b(class|struct|interface|enum|record)\s+(\w+)", text):
        # skip "where T : class" and comments roughly
        line_start = text.rfind("\n", 0, m.start()) + 1
        line = text[line_start:text.find("\n", m.start())]
        if line.strip().startswith("//") or "where " in line[: m.start() - line_start]:
            continue
        out.append((ns, m.group(2), text.count("\n", 0, m.start()) + 1, m.group(1)))
    return out
