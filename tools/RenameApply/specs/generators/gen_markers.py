"""spec/final/moogirl-era-markers.json: what identifies a MooGirl-era (0.1.0-0.4.4/0.4.6) save for the upgrader's detector
and stripper (decisions.md section 5, D3 = MooGirl era NOT supported: no name map, strip + one notice).
Sources: spec/moogirl-types.json, spec/moogirl-defs.json (git history), the 1.7 content (to exclude names that are still current)."""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import FINAL, SPEC, REPO, load_json, save_json, parse_xml, walk_files

types = load_json(SPEC / "moogirl-types.json")["types"]
defs = load_json(SPEC / "moogirl-defs.json")

current = set()
for f in walk_files(REPO / "1.7", (".xml",)):
    rel = str(f.relative_to(REPO)).replace("\\", "/")
    if "/Languages/" in rel:
        continue
    root = parse_xml(f)
    if root is None or root.tag != "Defs":
        continue
    for el in root.children:
        for c in el.children:
            if c.tag == "defName":
                current.add(c.text.strip())

exact_defs = []
for d in defs:
    name = d.get("old") or ""
    if not name or name in current:
        continue
    root = (d.get("defType") or "").split(".")[-1]
    root = {"ThingDef_AlienRace": "ThingDef", "AlienBackstoryDef": "BackstoryDef"}.get(root, root)
    exact_defs.append({"root": root, "name": name})
exact_defs = sorted({(x["root"], x["name"]) for x in exact_defs})
DEF_PATTERNS = ["^MooGirl", "^MuGirl"]

markers = {
    "_doc": "MooGirl-era markers (decisions.md section 5, D3 = MooGirl era not supported). A save carrying any of these is MooGirl-era: the DOM pass "
            "strips MooGirl-era component elements and MooGirl-era def references from Removable containers, leaves everything else to vanilla's "
            "missing-def handling and shows ONE main-thread notice. There is no MooGirl -> Mugirl name map. typePrefixes/defPatterns are the "
            "K1 manifest's moogirlEra block; types/defs are the exact names seen in git history (MooGirl 0.1.0-0.4.4) for the stripper and for "
            "validator coverage. Note: packageId HAR.MuGirlRace lower-cases to the current har.mugirlrace, so it does not identify the era.",
    "generatedFrom": ["spec/moogirl-types.json", "spec/moogirl-defs.json", "1.7 content (names still current are excluded)"],
    "typePrefixes": ["MooGirl.", "MooGirlSkinFix."],
    # Anchored prefixes only (D3: defNames MooGirl*/MuGirl*); MooGirl-era names outside them are listed exactly in "defs".
    "defPatterns": DEF_PATTERNS,
    "types": sorted(({"name": t["old"], "base": t.get("base"), "component": bool(t.get("component")), "firstSeen": t.get("firstSeen"), "lastSeen": t.get("lastSeen")}
                     for t in types), key=lambda x: x["name"]),
    "componentTypes": sorted(t["old"] for t in types if t.get("component")),
    "defs": [{"root": r, "name": n} for r, n in exact_defs],
    "defsNotMatchingPatterns": [{"root": r, "name": n} for r, n in exact_defs if not any(re.match(p, n) for p in DEF_PATTERNS)],
    "settingsFiles": {"pattern": "Mod_*_MooGirlMod.xml", "classPrefixes": ["MooGirl."], "migrated": False,
                      "note": "decisions.md section 2: MooGirl-era settings files are not migrated."},
}
save_json(FINAL / "moogirl-era-markers.json", markers)
print(f"types {len(markers['types'])} (components {len(markers['componentTypes'])}), defs {len(markers['defs'])} (outside patterns {len(markers['defsNotMatchingPatterns'])})")
