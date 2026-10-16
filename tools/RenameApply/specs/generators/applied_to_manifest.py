"""Turns the rename-applied logs (RenameApply --applied, waves R and R2) plus spec/final/upgrade-hand-rows.json into the
'types', 'defs' and 'settings' parts of the K1 upgrade manifest (SaveUpgradeManifest.ToJson shape) for the upgrader's tables.

Usage: applied_to_manifest.py <R applied json> [<R2 applied json>] --dll-types <post-R2 dll-types.tsv> --tree <post-R2 tree>
       [--ownership <ownership-postR2.json>] > rows.json        (exit 1 when a check fails; the problems go to stderr)

WAVE-AWARE: a row maps a legacy name to what EXISTS in the tree the rows ship with (the post-R2 build).
  * Generated rows: save-visible types (and implicit nested types) and save-visible defs (and their derived names) are
    renamed old -> post-R2 name; chains are collapsed so no legacy name points at another legacy name.
  * Transitional types (C23) and hand rows whose legacy name still exists after R2 (under its own or its R name) keep the
    R target NOW; their planned Wave S disposition goes to 'pendingWaveS' and is applied by the owning module in Wave S, in
    the same change that deletes the type/def.
  * Hand rows and retired defs whose legacy name no longer exists apply their disposition (rename/drop) now.
  * Every rename target must exist: types in the post-R2 assembly (dll_types.ps1) or outside Mugirl (vanilla), defs in the
    post-R2 Defs. Every row carries the owning module (spec row, hand row, or the owner of the defining file).
Def roots are database root short names (C14) of the post-R2 build; 'legacyRoot' names the root type the legacy corpus uses
when the wave renamed a Def subclass that is its own root. The K1 'settings' block comes from the hand rows' settings entry and is checked
against the R log (the settings class rename must be in it).
"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import FINAL, REPO, load_json, walk_files
sys.path.insert(0, str(REPO / "tools" / "RenameApply"))
from GenerateUpgradeModules import compose_def_rows

args = sys.argv[1:]
include_retired = "--include-retired" in args
if include_retired:
    args.remove("--include-retired")
opts = {}
for flag in ("--dll-types", "--tree", "--ownership"):
    if flag in args:
        i = args.index(flag)
        opts[flag] = Path(args[i + 1])
        del args[i:i + 2]
if "--dll-types" not in opts or "--tree" not in opts:
    raise SystemExit(__doc__)
logs = [load_json(Path(p)) for p in args]
logs = [l.get("maps", l) for l in logs]
hand = load_json(FINAL / "upgrade-hand-rows.json")
ownership = load_json(opts["--ownership"])["files"] if "--ownership" in opts else {}
errors = []

# ------------------------------------------------------------------------------------------------ the shipped tree
final_types = set()
for line in opts["--dll-types"].read_text(encoding="utf-8").splitlines():
    final_types.add(line.split("\t")[1])
ROOT_OVERRIDES = {"AlienRace.ThingDef_AlienRace": "ThingDef", "Mugirl.SlaveApparelDef": "ThingDef", "AlienRace.AlienBackstoryDef": "BackstoryDef"}
final_defs = {}          # (root, defName) -> defining file
tree = opts["--tree"]
# Comp labels and enum values do not carry a Class attribute, but their legacy
# owner type still participates in the save contract and validator resolution.
legacy_owners = set()
legacy_def_roots = {}
for filename in ("legacy-labels.tsv", "legacy-enums.tsv"):
    source = tree / "Source/Tests/MugirlRace.Validation/Baselines/legacy" / filename
    if not source.is_file():
        errors.append(f"missing legacy owner corpus: {source}")
        continue
    for line in source.read_text(encoding="utf-8-sig").splitlines():
        if line and not line.startswith("#"):
            legacy_owners.add(line.split("\t", 1)[0])
for line in (tree / "Source/Tests/MugirlRace.Validation/Baselines/legacy/legacy-defs.tsv").read_text(encoding="utf-8-sig").splitlines():
    if line and not line.startswith("#"):
        legacy_root, legacy_name = line.split("\t")[:2]
        legacy_def_roots.setdefault((legacy_root.lstrip("?").split(".")[-1], legacy_name), set()).add(legacy_root)
for f in walk_files(tree / "1.7", (".xml",)):
    rel = str(f.relative_to(tree)).replace("\\", "/")
    if "/Defs/" not in rel or "/Languages/" in rel:
        continue
    try:
        root = ET.parse(f).getroot()
    except ET.ParseError:
        continue
    for el in root:
        if not isinstance(el.tag, str) or el.get("Abstract") == "True" or not el.findtext("defName"):
            continue
        cls = el.get("Class") or el.tag
        r = ROOT_OVERRIDES.get(cls, ROOT_OVERRIDES.get(el.tag, el.tag.split(".")[-1]))
        final_defs[(r, el.findtext("defName").strip())] = rel
final_def_names = {n for _, n in final_defs}


def def_exists(root, name):
    return (root, name) in final_defs


def type_exists(name):
    return name in final_types or not name.startswith("Mugirl")


# ------------------------------------------------------------------------------------------------ maps of the logs
type_new = {}                      # old -> post-R2 name (all type rows, save-visible or not)
type_row = {}
for log in logs:
    for t in log.get("types", []):
        prev = {k for k, v in type_new.items() if v == t["old"]}
        type_new[t["old"]] = t["new"]
        type_row.setdefault(t["old"], t)
        for k in prev:
            type_new[k] = t["new"]


def current_type(name):
    """Name under which a legacy type exists after the waves (None when it is gone)."""
    n = type_new.get(name, name)
    return n if n in final_types else None


types, defs, pending_types, pending_defs = [], [], [], []
def outer_row(old):
    return type_row.get(old.split("+")[0]) if "+" in old else None


for old, t in sorted(type_row.items()):
    # nested types that moved with their outer type matter only when the outer type is save-visible
    if old not in legacy_owners and not t.get("saveVisible") and not (t.get("implicit") and (outer_row(old) or {}).get("saveVisible")):
        continue
    new = type_new[old]
    if not type_exists(new):
        errors.append(f"type {old} -> {new}: target not in the post-R2 assembly")
    row = {"old": old, "new": new, "disposition": "rename", "policy": None, "module": t.get("module") or (outer_row(old) or {}).get("module") or "",
           "reason": "Wave R rename" + (" (nested type moved with its outer type)" if t.get("implicit") else "")}
    tr = t.get("transitional")
    if tr:
        row["reason"] = f"Wave R rename; transitional (C23): Wave S {tr.get('waveS')}, then {tr.get('upgrade')}"
        pending_types.append({"old": old, "current": new, "waveS": tr.get("waveS"), "upgrade": tr.get("upgrade"), "module": row["module"]})
    types.append(row)

emitted = {t["old"] for t in types}
for h in hand["types"]:
    if h["disposition"] == "XmlAlias":
        continue      # PublicApi alias subclasses (D4): XML only, never in saves under a new name
    cur = current_type(h["old"])
    module = h.get("module") or (type_row.get(h["old"]) or {}).get("module") or ""
    planned = h["disposition"] + (f":{h['target']}" if h.get("target") else "") + (f" ({h['policy']})" if h.get("policy") else "") + (f" step={h['step']}" if h.get("step") else "")
    if h["disposition"] != "Map" and not include_retired:
        pending_types.append({"old": h["old"], "current": cur, "waveS": "retired policy", "upgrade": planned, "module": module, "source": h.get("source")})
        continue
    if cur is not None:
        # the legacy type still exists (renamed or not): the hand disposition is Wave S work
        pending_types.append({"old": h["old"], "current": cur, "waveS": "per hand row", "upgrade": planned, "module": module, "source": h.get("source")})
        if cur != h["old"] and h["old"] not in emitted:
            types.append({"old": h["old"], "new": cur, "disposition": "rename", "policy": None, "module": module,
                          "reason": f"hand row deferred to Wave S ({planned}); until then the R name"})
        continue
    if h["disposition"] == "Map":
        if not type_exists(h["target"]):
            errors.append(f"hand type {h['old']} -> {h['target']}: target not in the post-R2 assembly")
        row = {"old": h["old"], "new": h["target"], "disposition": "rename", "policy": None}
    else:
        row = {"old": h["old"], "new": None, "disposition": "drop", "policy": h.get("policy") or h.get("step")}
    types = [t for t in types if t["old"] != h["old"]]
    types.append({**row, "module": module, "reason": "hand row: " + h.get("source", "")})

for log in logs:
    for d in log.get("defs", []):
        # Non-save-visible Defs still occur in side documents, translation keys
        # and the historical content corpus. Include every actual Def rename.
        # root = the database root at RUNTIME (BackCompatibleDefName normalises the new defType): a Def subclass that is its
        # own root may have been renamed by the same wave (CorporateIntroductionDef -> CorporateIntroductionConfigDef).
        legacy_root_full = d.get("rootDefType") or d["defType"]
        root = type_new.get(legacy_root_full, legacy_root_full).split(".")[-1]
        legacy_root = legacy_root_full.split(".")[-1]
        corpus_roots = legacy_def_roots.get((legacy_root, d["old"]), set())
        if not d.get("saveVisible") and not d.get("derived") and not corpus_roots:
            # New engine-only resources (for example 1.7 MaterialDef) have no
            # legacy persisted contract. Do not invent stale upgrade rows.
            continue
        if len(corpus_roots) == 1:
            legacy_root = next(iter(corpus_roots))
        if not d.get("derived") and not def_exists(root, d["new"]):
            errors.append(f"def {root} {d['old']} -> {d['new']}: target not defined in the post-R2 Defs")
        defs.append({"root": root, **({"legacyRoot": legacy_root} if legacy_root != root else {}), "old": d["old"], "new": d["new"], "disposition": "rename",
                     "policy": f"conditional (derived name; {d.get('derivedBy') or 'shape'})" if d.get("derived") else None,
                     "module": d.get("module") or "", "reason": "Wave R rename" + (" (derived " + d["old"].split("_")[0] + "_ name)" if d.get("derived") else "")})


def def_module(root, name, fallback):
    f = final_defs.get((root, name))
    return ownership.get(f, fallback) if f else fallback


for h in hand["defs"]:
    root = h["defType"].split(".")[-1]
    if def_exists(root, h["old"]) or (h["disposition"] != "Map" and not include_retired):
        pending_defs.append({"root": root, "old": h["old"], "upgrade": h["disposition"] + (f":{h['target']}" if h.get("target") else ""),
                             "module": def_module(root, h["old"], h.get("module", "")), "source": h.get("source")})
        continue
    if h["disposition"] == "Map" and not def_exists(root, h["target"]):
        errors.append(f"hand def {root} {h['old']} -> {h['target']}: target not defined in the post-R2 Defs")
    defs.append({"root": root, "old": h["old"], "new": h.get("target") if h["disposition"] == "Map" else None,
                 "disposition": "rename" if h["disposition"] == "Map" else "drop", "policy": h.get("policy"),
                 "module": h.get("module", ""), "reason": "hand row: " + h.get("source", "")})
for r in hand["retiredDefs"]:
    root = r["defType"].split(".")[-1]
    if def_exists(root, r["old"]) or (not r.get("new") and not include_retired):
        pending_defs.append({"root": root, "old": r["old"], "upgrade": "drop" if not r.get("new") else f"rename:{r['new']}",
                             "module": def_module(root, r["old"], r.get("module", "")), "source": r.get("evidence")})
        continue
    defs.append({"root": root, "old": r["old"], "new": r.get("new"), "disposition": "drop" if not r.get("new") else "rename",
                 "policy": r.get("policy"), "module": r.get("module", ""), "reason": "retired: " + r.get("evidence", "")})

# ------------------------------------------------------------------------------------------------ settings (K1)
st = hand.get("settings")
settings = None
if st:
    fc = st["fileClass"]
    if type_new.get(fc["old"]) != fc["new"]:
        errors.append(f"settings.fileClass {fc['old']} -> {fc['new']}: not what the applied logs did ({type_new.get(fc['old'])})")
    if fc["new"] not in final_types:
        errors.append(f"settings.fileClass target {fc['new']} not in the post-R2 assembly")
    settings = {"fileClass": {"old": fc["old"], "new": fc["new"]}, "labels": st.get("labels", []), "module": st.get("module", "core"),
                "note": "schema: SaveSchema.SettingsCurrent of the upgrader"}

# ------------------------------------------------------------------------------------------------ collapse, check, write
tmap = {t["old"]: t for t in types}
for t in types:
    seen = set()
    while t.get("new") in tmap and t["new"] not in seen and t["new"] != t["old"]:
        seen.add(t["new"])
        nxt = tmap[t["new"]]
        t["new"], t["disposition"], t["policy"] = nxt["new"], nxt["disposition"], nxt["policy"]
types = list({t["old"]: t for t in types}.values())
defs = list({(d["root"], d["old"]): d for d in defs}.values())
try:
    defs = compose_def_rows(defs)
except ValueError as problem:
    errors.append(str(problem))
for t in types:
    if t["disposition"] == "drop" and t["old"] in final_types:
        errors.append(f"type {t['old']}: dropped although it still exists after R2")
    if not t["module"]:
        errors.append(f"type {t['old']}: no module")
for d in defs:
    if d["disposition"] == "drop" and def_exists(d["root"], d["old"]):
        errors.append(f"def {d['root']} {d['old']}: dropped although it is still defined after R2")
    if not d["module"]:
        errors.append(f"def {d['root']} {d['old']}: no module")
# one pending row per legacy name (a transitional marker and a hand row may describe the same type)
merged_pending = {}
for p in pending_types:
    q = merged_pending.setdefault(p["old"], dict(p))
    if p is not q and p.get("source"):
        q.update({k: v for k, v in p.items() if v})
pending_types = list(merged_pending.values())
out = {"_doc": "K1 rows generated by spec/final/_gen/applied_to_manifest.py (wave-aware: rows map to what the post-R2 build contains; "
               "pendingWaveS lists dispositions the owning module applies in Wave S when it deletes the type/def).",
       "types": sorted(types, key=lambda x: x["old"]), "defs": sorted(defs, key=lambda x: (x["root"], x["old"])),
       "settings": settings, "pendingWaveS": {"types": pending_types, "defs": pending_defs}, "errors": errors}
json.dump(out, sys.stdout, ensure_ascii=False, indent=1)
print()
for e in errors:
    print("ERROR", e, file=sys.stderr)
print(f"manifest rows: types {len(types)}, defs {len(defs)}, pending Wave S {len(pending_types)} types / {len(pending_defs)} defs, "
      f"errors {len(errors)}", file=sys.stderr)
sys.exit(1 if errors else 0)
