"""Facts for the R2 DefOf split, read from the POST-R tree: every [DefOf] field, its def, the XML file that defines the def,
the owner module of that file and the features that read the field. Usage: defof_facts.py <post-R tree> -> defof-facts.json"""
import fnmatch
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import GEN, FINAL, SPEC, load_json, save_json, parse_xml, walk_files, def_root

TREE = Path(sys.argv[1])
MAIN = TREE / "Source" / "MugirlRace"
r_specs = [load_json(f) for f in sorted((FINAL / "R").glob("*.json"))]
merged = load_json(SPEC / "merged.json")

# ---------------------------------------------------------------- [DefOf] classes and fields (post-R source)
FIELD_RX = re.compile(r"^(?P<attrs>(?:[ \t]*\[[^\n]*\]\s*\n)*)[ \t]*public static (?P<type>[\w.]+)\??\s+(?P<name>\w+)\s*(?:=|;)", re.M)
defof = []
for f in walk_files(MAIN, (".cs",)):
    text = f.read_text(encoding="utf-8-sig")
    if "[DefOf]" not in text:
        continue
    ns = re.search(r"^namespace\s+([\w.]+)\s*;", text, re.M).group(1)
    cls = re.search(r"\[DefOf\]\s*\n(?:[^\n]*\n)*?[^\n]*\bclass\s+(\w+)", text).group(1)
    for m in FIELD_RX.finditer(text):
        attrs = m.group("attrs")
        alias = re.search(r'DefAlias\("([^"]+)"\)', attrs)
        mayreq = re.findall(r"\[(MayRequire[^\]]*)\]", attrs)
        defof.append({"class": f"{ns}.{cls}", "file": str(f.relative_to(TREE)).replace("\\", "/"), "field": m.group("name"),
                      "type": m.group("type").split(".")[-1], "defName": alias.group(1) if alias else m.group("name"),
                      "alias": bool(alias), "mayRequire": mayreq, "line": text.count("\n", 0, m.start("type")) + 1})

# ---------------------------------------------------------------- where each def is defined (post-R content)
ROOT = {"ThingDef_AlienRace": "ThingDef", "SlaveApparelDef": "ThingDef", "BackstoryDef": "BackstoryDef", "AlienBackstoryDef": "BackstoryDef"}
defined = defaultdict(list)   # (root, name) -> [rel file]
for f in walk_files(TREE / "1.7", (".xml",)):
    rel = str(f.relative_to(TREE)).replace("\\", "/")
    if "/Languages/" in rel or "/Patches/" in rel:
        continue
    root = parse_xml(f)
    if root is None or root.tag != "Defs":
        continue
    for el in root.children:
        t = (el.attrs.get("Class") or el.tag).split(".")[-1]
        t = ROOT.get(t, t)
        for c in el.children:
            if c.tag == "defName":
                defined[(t, c.text.strip())].append(rel)

owned = []
for s in r_specs:
    for p in s.get("ownedContentFiles", []):
        owned.append((p, s["module"]))


def owner_of_content(rel):
    hits = [(p, m) for p, m in owned if fnmatch.fnmatch(rel, p) or rel == p]
    hits.sort(key=lambda x: -len(x[0]))
    return hits[0][1] if hits else None


# ---------------------------------------------------------------- readers of every field, by FINAL feature folder
def feature_of(rel):
    p = rel.replace("\\", "/")
    m = re.match(r"Source/MugirlRace/Features/([^/]+)/", p)
    if m:
        return "Features/" + m.group(1)
    m = re.match(r"Source/MugirlRace/(Core|Rendering|Compatibility/[^/]+|PublicApi|Platform|Mathematics|DefOf)/", p)
    if m:
        return m.group(1)
    if p.startswith("Source/Tests/"):
        return "Tests"
    return "?"


readers = defaultdict(lambda: defaultdict(list))
all_cs = list(walk_files(TREE / "Source", (".cs",)))
short_cls = {d["class"].rsplit(".", 1)[1] for d in defof}
for f in all_cs:
    rel = str(f.relative_to(TREE)).replace("\\", "/")
    text = f.read_text(encoding="utf-8-sig")
    for m in re.finditer(r"\b(" + "|".join(sorted(short_cls)) + r")\.(\w+)", text):
        cls = m.group(1)
        for d in defof:
            if d["class"].endswith("." + cls) and d["field"] == m.group(2):
                readers[(d["class"], d["field"])][feature_of(rel)].append(f"{rel}:{text.count(chr(10), 0, m.start()) + 1}")
for d in defof:
    d["definedIn"] = sorted(set(defined.get((d["type"], d["defName"]), [])))
    d["owner"] = sorted({owner_of_content(x) for x in d["definedIn"]} - {None})
    d["readers"] = {k: v for k, v in readers[(d["class"], d["field"])].items()}

save_json(GEN / "defof-facts.json", defof)
print(len(defof), "fields")
# table: see defof-facts.json
