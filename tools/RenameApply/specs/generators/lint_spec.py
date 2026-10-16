"""Spec linter for the final Wave R / R2 specs (critique-validation section 2.2 + decisions.md).

Usage: lint_spec.py <tree the R specs apply to> [<post-R tree the R2 specs apply to>] [--post-r2 <post-R2 tree> --r2-applied <json>]
Writes <spec root>/lint-report.json; exit 1 when a rule fails. LINT_SPEC_ROOT points the linter at another spec folder
(negative tests). Rules (ids in the report):
  L01 injective per kind (old unique, new unique, no chains/swaps; defs keyed by (root, name); R2 createTypes unique)
  L02 no new name (type rows, R2 createTypes, defs) equal to a legacy-corpus name of ANOTHER entity (git history + specs)
  L03 no new type short name (type rows, R2 createTypes) equal to a public engine/Unity/HAR/Harmony type short name (error;
      Facial Animation/VEF: warning, not referenced by the mod; transitional rows: info, C32)
  L04 forbidden / shadowing namespace segments (design 3.1 + C25 'Apparel' + any Verse/RimWorld type short name)
  L05 no rename of a frozen public-API name: types (aliased XML classes need publicAlias), defNames and defNamePattern globs,
      abstract Names, the Mod short name
  L06 no rename/move/delete of a member of a PublicApi class, and no rename of a frozen XML field (alias bases included)
  L07 Harmony binding untouched: no rename/move of Harmony-convention methods, of [HarmonyPrefix]/[HarmonyPostfix]/... methods,
      of any member of a [HarmonyPatch] class, of overrides, or of a field that a patch reads through a ___field parameter
  L08 no [TweakValue]/[DebugAction] member renamed or moved (attributes above or on the declaration line)
  L09 ThingDef/RecipeDef rename targets do not end in a digit; ManeuverDef/ToolCapacityDef are not renamed (load IDs)
  L10 every type rename that changes a short name used as a DefInjected handle lists those handles (definjectedHandles)
  L11 transitional rows carry the machine-readable marker {"waveS", "upgrade"} (C23)
  L12 nothing from the MooGirl era is composed (D3): no MooGirl/MuGirl old names
  L13 naming: no "Moo" in new names; new defNames and abstract Names carry the Mugirl_ prefix (design 3.3) unless FA
  L14 R is identity-preserving: no keyed renames, content moves, member moves/deletes, created types, splits; DefOf classes
      keep their names in R (C02/C03: the DefOf split is R2)
  L15 R2 member operations name existing members; every DefOf field goes to the DefOf of the feature that defines the def
      (C04 owner rule; split targets: their feature) and a non-Mugirl def is declared by exactly its reading features
  L16 (with --post-r2) every Source/ and 1.7/ file of the post-R2 tree has exactly one Wave S owner and
      spec/final/ownership-postR2.json matches the recomputation
"""
import fnmatch
import json
import os
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import GEN, FINAL, SPEC, SCRATCH, load_json, save_json, walk_files
from ownership import OWNER_FEATURES, Ownership

args = [a for a in sys.argv[1:]]
POST_R2 = R2_APPLIED = None
for flag in ("--post-r2", "--r2-applied"):
    if flag in args:
        i = args.index(flag)
        if flag == "--post-r2":
            POST_R2 = Path(args[i + 1])
        else:
            R2_APPLIED = Path(args[i + 1])
        del args[i:i + 2]
TREE = Path(args[0])
TREE_R2 = Path(args[1]) if len(args) > 1 else None
findings = []


def F(rule, sev, msg, where=None):
    findings.append({"rule": rule, "severity": sev, "message": msg, **({"where": where} if where else {})})


SPEC_ROOT = Path(os.environ["LINT_SPEC_ROOT"]) if os.environ.get("LINT_SPEC_ROOT") else FINAL
REPORT = SPEC_ROOT / "lint-report.json"


def load_specs(d):
    out = []
    for f in sorted((SPEC_ROOT / d).glob("*.json")):
        s = load_json(f)
        s["_file"] = f"{d}/{f.name}"
        out.append(s)
    return out


R = load_specs("R")
R2 = load_specs("R2") if (SPEC_ROOT / "R2").exists() else []
api = load_json(FINAL / "public-api.json") if (FINAL / "public-api.json").exists() else load_json(GEN / "public-api.partial.json")
frozen = api["entries"]

# ---------------------------------------------------------------- corpus of legacy names (git history + specs)
legacy_types = set()
for line in (SCRATCH / "understand" / "dlls" / "types.tsv").read_text(encoding="utf-8").splitlines():
    parts = line.split("\t")
    if len(parts) > 1 and (parts[1].startswith("Mugirl") or parts[1].startswith("MooGirl")):
        legacy_types.add(parts[1].replace("/", "+"))
legacy_defs = defaultdict(set)   # name -> roots
for name, info in load_json(SCRATCH / "understand" / "defs.json").items():
    for t in info.get("types", []):
        legacy_defs[name].add(t.split(".")[-1])
for r in load_json(SPEC / "content-legacy-defs.json"):
    legacy_defs[r["old"]].add(r["defType"])
for r in load_json(SPEC / "moogirl-defs.json"):
    legacy_defs[r.get("old") or r.get("name", "")].add((r.get("defType") or "").split(".")[-1])
for r in load_json(SPEC / "moogirl-types.json")["types"]:
    legacy_types.add(r["old"])

foreign = defaultdict(list)
for line in (GEN / "foreign-types.tsv").read_text(encoding="utf-8").splitlines():
    asm, ns, name, base = (line.split("\t") + [""])[:4]
    foreign[name.split("`")[0]].append((asm, f"{ns}.{name} ({asm})"))
NOT_REFERENCED_ASMS = {"FacialAnimation", "VEF"}

ROOT_OF = {"ThingDef_AlienRace": "ThingDef", "SlaveApparelDef": "ThingDef"}


def root(t):
    t = t.split(".")[-1]
    return ROOT_OF.get(t, t)


def short_of(full):
    return full.rsplit(".", 1)[-1].split("`")[0].split("+")[-1]


# ---------------------------------------------------------------- rows
types, defs, abstract, created = [], [], [], []
for s in R:
    for t in s.get("types", []):
        types.append((s["_file"], t))
    for d in s.get("defs", []):
        defs.append((s["_file"], d))
    for a in s.get("abstractNames", []):
        abstract.append((s["_file"], a))
    for k in ("keyed", "contentFileMoves", "memberMoves", "memberDeletes", "dissolveTypes", "createTypes", "xmlSplits", "keyedSplits",
              "attributeArgumentRewrites"):
        if s.get(k):
            F("L14", "error", f"{s['_file']}: '{k}' is not an identity-preserving Wave R operation (belongs to R2/S)")
for s in R2:
    for c in s.get("createTypes", []):
        created.append((s["_file"], c))
    for k in ("types", "defs", "abstractNames", "keyed"):
        if s.get(k):
            F("L14", "error", f"{s['_file']}: '{k}' rows belong to Wave R (renames), not R2")


def check_injective(rows, key_old, key_new, what):
    olds, news = defaultdict(list), defaultdict(list)
    for f, r in rows:
        olds[key_old(r)].append(f)
        news[key_new(r)].append(f)
    for k, fs in olds.items():
        if len(fs) > 1:
            F("L01", "error", f"{what} old {k} listed {len(fs)} times ({', '.join(fs)})")
    for k, fs in news.items():
        if len(fs) > 1:
            F("L01", "error", f"{what} new {k} is the target of {len(fs)} rows")
    for f, r in rows:
        if key_new(r) in olds and key_new(r) != key_old(r):
            F("L01", "error", f"{what} chain/swap: {key_old(r)} -> {key_new(r)} which is itself renamed", f)


check_injective(types, lambda r: r["old"], lambda r: r["new"], "type")
check_injective(defs, lambda r: (root(r["defType"]), r["old"]), lambda r: (root(r["defType"]), r["new"]), "def")
check_injective(abstract, lambda r: r["old"], lambda r: r["new"], "abstract Name")
check_injective(created, lambda r: r["fullName"], lambda r: r["file"], "created type")

alias_names = {t["publicAlias"]["name"] for _, t in types if t.get("publicAlias")}
FORBIDDEN_SEGMENTS = {"UI", "Text", "Log", "Find", "Gen", "Widgets", "Scribe", "AlienRace", "HarmonyLib", "RimWorld", "Verse", "LudeonTK",
                      "System", "UnityEngine", "Steamworks", "Apparel"}
engine_short = {n for n, locs in foreign.items() if any(l.startswith(("Verse.", "RimWorld.")) for _, l in locs)}
frozen_type_names = {e["name"] for e in frozen if e["kind"] in ("type", "xmlClass")}
frozen_def_names = {e["name"] for e in frozen if e["kind"] == "defName"}
frozen_def_patterns = [e["name"] for e in frozen if e["kind"] == "defNamePattern"]
frozen_abstract = {e["name"] for e in frozen if e["kind"] == "abstractName"}
frozen_type_shorts = {e["name"] for e in frozen if e["kind"] == "typeShortName"}
frozen_xml_fields = defaultdict(set)    # owner (public name and implementation) -> field names
alias_impl = {t["old"]: t["new"] for _, t in types if t.get("publicAlias")}
for e in frozen:   # public-api.json names the implementation of each alias in its treatment
    m = re.search(r"implementation ([\w.]+)", e.get("treatment", ""))
    if e["kind"] in ("xmlClass", "type") and m:
        alias_impl.setdefault(e["name"], m.group(1))
for e in frozen:
    if e["kind"] == "xmlField":
        frozen_xml_fields[e["owner"]].add(e["name"])
        if e["owner"] in alias_impl:
            frozen_xml_fields[alias_impl[e["owner"]]].add(e["name"])


def check_new_type(new, where, transitional=False, what="type"):
    short_n = short_of(new)
    ns = new.rsplit(".", 1)[0] if "." in new else ""
    if short_n in foreign:
        asms = {a for a, _ in foreign[short_n]}
        sev = "info" if transitional else ("warning" if asms <= NOT_REFERENCED_ASMS else "error")
        F("L03", sev, f"{what} {new}: short name equals {foreign[short_n][0][1]}" + (" (transitional, deleted in Wave S: accepted C32)" if transitional else ""), where)
    for seg in ns.split("."):
        if seg in FORBIDDEN_SEGMENTS or (seg in engine_short and seg not in ("Mugirl",)):
            F("L04", "error", f"{what} {new}: namespace segment '{seg}' shadows an engine name", where)
    if "Moo" in new.replace("Mood", ""):
        F("L13", "error", f"{what} {new}: contains 'Moo' (design 3.2)", where)


for f, t in types:
    old, new = t["old"], t["new"]
    if new != old and new in legacy_types and new not in alias_names:
        F("L02", "error", f"type {old} -> {new}: the new name is a legacy (git history) type name of another entity", f)
    if new != old:
        check_new_type(new, f, bool(t.get("transitional")), f"type {old} ->")
    if old in frozen_type_names and old != new and not t.get("publicAlias"):
        F("L05", "error", f"type {old} is frozen public API (used by dependents) and has no publicAlias", f)
    if short_of(old) in frozen_type_shorts and short_of(old) != short_of(new):
        F("L05", "error", f"type {old}: short name {short_of(old)} is frozen (settings file name)", f)
    reason = (t.get("reason") or "").upper()
    if ("TRANSITIONAL" in reason or "TEMPORARY" in reason) and not isinstance(t.get("transitional"), dict):
        F("L11", "error", f"type {old}: transitional row without marker", f)
    if isinstance(t.get("transitional"), dict) and not {"waveS", "upgrade"} <= set(t["transitional"]):
        F("L11", "error", f"type {old}: malformed transitional marker", f)
    if old.startswith(("MooGirl", "MooGirlSkinFix")):
        F("L12", "error", f"MooGirl-era type composed: {old}", f)
for f, c in created:
    full = c["fullName"]
    if full in legacy_types:
        F("L02", "error", f"createTypes {full}: a legacy (git history) type name", f)
    check_new_type(full, f, False, "createTypes")

FA_TYPES = {"FaceAnimationDef", "MouthShapeDef", "HeadShapeDef", "LidShapeDef", "LidOptionShapeDef", "EmotionShapeDef", "BrowShapeDef",
            "EyeballShapeDef", "FaceAdjustmentDef"}
for f, d in defs:
    r = root(d["defType"])
    hist = legacy_defs.get(d["new"], set())
    if d["new"] != d["old"] and r in hist and d["new"].lower() != d["old"].lower():
        F("L02", "error", f"def {r} {d['old']} -> {d['new']}: new name is a legacy {r} name of another def", f)
    if d["old"] in frozen_def_names or any(fnmatch.fnmatchcase(d["old"], p) for p in frozen_def_patterns):
        F("L05", "error", f"def {r} {d['old']} is frozen public API", f)
    if r in ("ThingDef", "RecipeDef") and re.search(r"\d$", d["new"]):
        F("L09", "error", f"def {r} {d['old']} -> {d['new']}: ThingDef/RecipeDef targets must not end in a digit (load-ID parsing)", f)
    if r in ("ManeuverDef", "ToolCapacityDef"):
        F("L09", "error", f"def {r} {d['old']}: ManeuverDef/ToolCapacityDef names are part of verb load IDs (frozen unless a verb-ID step exists)", f)
    if d["old"].startswith(("MooGirl", "MuGirl")):
        F("L12", "error", f"MooGirl-era def composed: {d['old']}", f)
    if not d["new"].startswith("Mugirl_") and r not in FA_TYPES:
        F("L13", "error", f"def {r} {d['new']}: missing Mugirl_ prefix (design 3.3)", f)
    if "Moo" in d["new"].replace("Mood", ""):
        F("L13", "error", f"def {d['new']}: contains 'Moo'", f)
for f, a in abstract:
    if a["old"] in frozen_abstract:
        F("L05", "error", f"abstract Name {a['old']} is frozen public API", f)
    if not a["new"].startswith("Mugirl_"):
        F("L13", "error", f"abstract Name {a['new']}: missing Mugirl_ prefix", f)

# ---------------------------------------------------------------- source index (types, [DefOf] classes, Harmony ___params)
src_cache = {}


def sources(tree):
    if tree not in src_cache:
        files = {}
        for x in walk_files(tree / "Source" / "MugirlRace", (".cs",)):
            files[x] = x.read_text(encoding="utf-8-sig")
        src_cache[tree] = files
    return src_cache[tree]


def class_decls(tree, full):
    """(file, text, class attribute block, class body start, body end) of the type 'Ns.Type' (all partial parts)."""
    ns, short = full.rsplit(".", 1) if "." in full else ("", full)
    out = []
    for x, text in sources(tree).items():
        m_ns = re.search(r"^namespace\s+([\w.]+)\s*;", text, re.M)
        if (m_ns.group(1) if m_ns else "") != ns:
            continue
        for m in re.finditer(r"((?:^[ \t]*\[[^\n]*\][ \t]*\n)*)^[^\n]*\b(?:class|struct|record)\s+" + re.escape(short) + r"\b", text, re.M):
            out.append((x, text, m.group(1), m.end()))
    return out


DECL_RX = r"((?:^[ \t]*\[[^\n]*\][ \t]*\n)*)^([ \t]*(?:\[[^\n]*?\][ \t]*)*(?:(?:public|internal|private|protected|static|readonly|const|override|virtual|abstract|sealed|new|extern|unsafe|volatile|partial|required|async)\s+)+[^\n;=(]*?\b{name}\b\s*(?:[=;({{<]|=>)[^\n]*)"


def member_decl(tree, full):
    """(declaration line, attribute text incl. same-line attributes, class attribute block, file) of 'Ns.Type.Member[(..)]'."""
    typ, member = full.split("(")[0].rsplit(".", 1)
    for x, text, cls_attrs, start in class_decls(tree, typ):
        for m in re.finditer(DECL_RX.format(name=re.escape(member)), text[start:], re.M):
            decl = m.group(2)
            same_line = " ".join(re.findall(r"\[[^\]]*\]", decl.split(member)[0]))
            return decl, m.group(1) + " " + same_line, cls_attrs, x
    return None


def is_defof_class(tree, full):
    return any("[DefOf]" in a for _, _, a, _ in class_decls(tree, full))


# L14: DefOf classes keep their name in R (identity extraction rows are allowed)
for f, t in types:
    if t["old"] != t["new"] and (is_defof_class(TREE, t["old"]) or short_of(t["old"]).endswith("DefOf")):
        F("L14", "error", f"type {t['old']} -> {t['new']}: DefOf classes are dissolved in R2 (C02/C03), not renamed in R", f)

# ---------------------------------------------------------------- L10 DefInjected handles
handles = defaultdict(set)
for x in walk_files(TREE / "1.7", (".xml",)):
    rel = str(x.relative_to(TREE)).replace("\\", "/")
    if "/DefInjected/" not in rel:
        continue
    for i, line in enumerate(x.read_text(encoding="utf-8-sig").splitlines(), 1):
        for m in re.finditer(r"<([A-Za-z_][\w.\-]*)>", line):
            for seg in m.group(1).split(".")[1:]:
                handles[re.sub(r"-\d+$", "", seg)].add(f"{rel}:{i}")
for f, t in types:
    o, n = short_of(t["old"]), short_of(t["new"])
    if o != n and handles.get(o):
        listed = set(t.get("definjectedHandles") or [])
        missing = handles[o] - listed
        if missing:
            F("L10", "error", f"type {t['old']} -> {t['new']}: DefInjected handle '{o}' used at {len(handles[o])} keys, {len(missing)} not listed in definjectedHandles", f)

# ---------------------------------------------------------------- member rules (L06/L07/L08/L15) over R and R2
HARMONY_NAMES = {"Prepare", "TargetMethod", "TargetMethods", "Prefix", "Postfix", "Transpiler", "Finalizer", "Cleanup"}
HARMONY_ATTRS = re.compile(r"\[\s*(?:HarmonyLib\.)?Harmony(?:Prefix|Postfix|Transpiler|Finalizer|Prepare|TargetMethods?|Cleanup|ReversePatch)\b")
public_api_classes = {"Mugirl.SlaveApparelDef", "Mugirl.MountedPawnUtility"} | set(alias_names)
public_api_bases = set(alias_impl.values())


def harmony_field_params(tree):
    out = set()
    for text in sources(tree).values():
        out |= set(re.findall(r"\b___(\w+)\b", text))
    return out


for specs, tree, wave in ((R, TREE, "R"), (R2, TREE_R2, "R2")):
    if tree is None:
        continue
    field_params = harmony_field_params(tree)
    for s in specs:
        ops = [(m.get("member"), "rename") for m in s.get("memberRenames", [])]
        for mm in s.get("memberMoves", []):
            for mem in mm.get("members", [mm.get("member")] if mm.get("member") else []):
                ops.append((mem, "move"))
        for md in s.get("memberDeletes", []):
            for mem in md.get("members", []):
                ops.append((mem, "delete"))
        for mem, kind in ops:
            if not mem:
                continue
            typ, name = mem.split("@")[0].split("(")[0].rsplit(".", 1)
            if name in HARMONY_NAMES:
                F("L07", "error", f"{wave} {kind} of Harmony-convention method {mem}", s["_file"])
            if typ in public_api_classes and not (typ == "Mugirl.MountedPawnUtility" and name != "IsMugirl" and kind == "move"):
                F("L06", "error", f"{wave} {kind} of a member of a PublicApi class {mem}", s["_file"])
            if kind == "rename" and name in frozen_xml_fields.get(typ, set()):
                F("L06", "error", f"{wave} rename of frozen XML field {mem} (dependents set it in XML)", s["_file"])
            if kind != "delete" and name in field_params:
                F("L07", "error", f"{wave} {kind} of {mem}: a Harmony patch reads it through a ___{name} parameter", s["_file"])
            src = member_decl(tree, mem)
            if src is None:
                F("L15", "error", f"{wave} {kind}: member {mem} not found in the tree", s["_file"])
                continue
            decl, attrs, cls_attrs, _ = src
            if "TweakValue" in attrs or "DebugAction" in attrs:
                F("L08", "error", f"{wave} {kind} of a [TweakValue]/[DebugAction] member {mem}", s["_file"])
            if HARMONY_ATTRS.search(attrs):
                F("L07", "error", f"{wave} {kind} of a Harmony-attributed patch method {mem}", s["_file"])
            if re.search(r"\[\s*(?:HarmonyLib\.)?HarmonyPatch\b", cls_attrs):
                F("L07", "error", f"{wave} {kind} of {mem}: member of a [HarmonyPatch] class (Harmony binds by name)", s["_file"])
            if re.search(r"\boverride\b", decl):
                F("L07", "error", f"{wave} {kind} of an override {mem}", s["_file"])

# ---------------------------------------------------------------- L15 C04 placement of DefOf fields (R2, post-R tree)
if TREE_R2 is not None and R2:
    facts_path = GEN / "defof-facts.json"
    facts = {f"{d['class']}.{d['field']}": d for d in load_json(facts_path)} if facts_path.exists() else {}
    r_spec_map = {s["module"]: s for s in R}
    r2_spec_map = {s["_file"]: s for s in R2}
    own = Ownership(TREE_R2, r_spec_map, r2_spec_map, None)
    split_of = {}
    for s in R2:
        for x in s.get("xmlSplits", []) + s.get("logicalContentOwnership", []):
            for t in x["targets"]:
                for dn in t.get("defs", []):
                    split_of[dn.split("/")[-1]] = (t.get("owner"), t.get("feature"))

    def feature_of(cls):
        if cls.startswith("Mugirl.Core."):
            return "Core"
        if cls.startswith("Mugirl.Rendering."):
            return "Rendering"
        m = re.match(r"Mugirl\.(Features|Compatibility)\.(\w+)\.", cls)
        return f"{m.group(1)}/{m.group(2)}" if m else None

    checked = 0
    for s in R2:
        for mm in s.get("memberMoves", []):
            targets = {mm["to"].split("@")[0]} | {r["to"] for r in mm.get("referenceRules", [])}
            if not all(t.endswith("DefOf") for t in targets):
                continue
            for mem in mm["members"]:
                d = facts.get(mem)
                if d is None:
                    F("L15", "error", f"R2 DefOf move {mem}: no DefOf fact (rerun defof_facts.py on the post-R tree)", s["_file"])
                    continue
                checked += 1
                feats = {feature_of(t) for t in targets}
                if d["defName"] in split_of and split_of[d["defName"]][0]:
                    owner, feature = split_of[d["defName"]]
                    ok = feats == {feature} if feature else feats <= set(OWNER_FEATURES.get(owner, []))
                    why = f"defined in an R2 split target of {owner} ({feature})"
                elif d["definedIn"]:
                    # A central writer can retain a shared XML file for load-order
                    # parity without taking semantic ownership of every Def in it.
                    owners = {m for f in d["definedIn"] for m, _ in own.direct(f, include_preserved=False)}
                    if len(owners) != 1:
                        F("L15", "error", f"R2 DefOf move {mem}: defining file owner unclear {sorted(owners)} ({d['definedIn']})", s["_file"])
                        continue
                    owner = owners.pop()
                    ok = len(feats) == 1 and feats <= set(OWNER_FEATURES.get(owner, []))
                    why = f"defined by {owner} ({', '.join(d['definedIn'])})"
                else:
                    readers = {r for r in d["readers"] if r not in ("Tests", "DefOf", "PublicApi")} or {"Core"}
                    ok = feats == readers
                    why = f"not a Mugirl def: read by {sorted(readers)}"
                if not ok:
                    F("L15", "error", f"R2 DefOf move {mem} -> {sorted(targets)}: C04 placement violated ({why})", s["_file"])
    F("L15", "info", f"C04 placement checked for {checked} DefOf fields")

# ---------------------------------------------------------------- L16 ownership of the post-R2 tree
if POST_R2 is not None:
    applied = load_json(R2_APPLIED) if R2_APPLIED else None
    own = Ownership(POST_R2, {s["module"]: s for s in R}, {s["_file"]: s for s in R2}, applied)
    files, problems = own.resolve()
    for p in problems:
        F("L16", "error", f"{p['file']}: {p['problem']}")
    pub_path = SPEC_ROOT / "ownership-postR2.json"
    computed = {k: v["module"] for k, v in files.items()}
    if pub_path.exists():
        pub = load_json(pub_path)["files"]
        for k in sorted(set(pub) | set(computed)):
            if pub.get(k) != computed.get(k):
                F("L16", "error", f"ownership-postR2.json: {k} is {pub.get(k)!r}, recomputed {computed.get(k)!r} (regenerate with gen_ownership.py)")
    elif SPEC_ROOT == FINAL:
        F("L16", "error", "spec/final/ownership-postR2.json is missing (run gen_ownership.py)")
    F("L16", "info", f"{len(computed)} post-R2 files, {len(problems)} without exactly one owner")

# ---------------------------------------------------------------- write
errors = [x for x in findings if x["severity"] == "error"]
save_json(REPORT, {"tree": str(TREE), "treeR2": str(TREE_R2) if TREE_R2 else None, "postR2": str(POST_R2) if POST_R2 else None,
                   "rules": [l.strip() for l in __doc__.split("Rules (ids in the report):")[1].strip().splitlines()],
                   "counts": {"types": len(types), "defs": len(defs), "abstractNames": len(abstract), "createTypes": len(created)},
                   "errors": len(errors), "findings": findings})
for x in findings:
    print(f"{x['severity'].upper():7} {x['rule']} {x['message']}" + (f"  [{x['where']}]" if x.get("where") else ""))
print(f"lint: {len(errors)} error(s), {len(findings) - len(errors)} other finding(s); types {len(types)}, defs {len(defs)}, abstract {len(abstract)}, created {len(created)}")
sys.exit(1 if errors else 0)
