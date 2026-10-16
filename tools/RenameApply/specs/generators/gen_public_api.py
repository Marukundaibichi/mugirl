"""<spec root>/public-api.json and <spec root>/owner-migration-list.md.

Inputs: scan_dependents.py output (public-api.partial.json), decisions.md (D4-D7, section 5), and the applied logs of R and R2
(new names for the validation-only names). Usage: gen_public_api.py <R applied json> <R2 applied json>
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import GEN, FINAL, SPEC, load_json, save_json

partial = load_json(GEN / "public-api.partial.json")
r = load_json(Path(sys.argv[1]))
r2 = load_json(Path(sys.argv[2]))
r = r.get("maps", r)     # accepts the applied log or the full report
r2 = r2.get("maps", r2)
type_map = {t["old"]: t["new"] for t in r["types"]}
member_map = {}
for m in r.get("members", []) + r2.get("members", []) + r.get("defOfFields", []) + r2.get("defOfFields", []):
    if m.get("new"):
        member_map[m["old"]] = m["new"]
        member_map.setdefault(m["old"].split("(")[0], m["new"].split("(")[0])   # methods: doc ids carry parameter lists
def_map = {d["old"]: d["new"] for d in r["defs"] if not d.get("derived")}

ALIASED = {t["publicAlias"]: t["new"] for t in r["types"] if t.get("publicAlias")}
KEPT = {"Mugirl.SlaveApparelDef": "Source/MugirlRace/PublicApi/SlaveApparelDef.cs (restraints; Wave S turns it into the alias Def subclass that maps the legacy fields into RestraintExtension, D6)",
        "Mugirl.MountedPawnUtility": "Source/MugirlRace/PublicApi/MountedPawnUtility.cs (combat; keeps only IsMugirl after R2: public static, Wave S adds [MethodImpl(NoInlining)] and makes it the decision point mounting code calls)"}

entries = []
for e in partial["entries"]:
    e = dict(e)
    name = e["name"]
    if e["kind"] in ("xmlClass", "type"):
        if name in ALIASED:
            e["treatment"] = f"public alias subclass in Source/MugirlRace/PublicApi/XmlAliases.cs (D4); implementation {ALIASED[name]}"
        elif name in KEPT:
            e["treatment"] = "name kept; " + KEPT[name]
        else:
            e["treatment"] = "name kept"
    elif e["kind"] == "xmlField":
        owner = e.get("owner", "")
        e["treatment"] = f"XML field name frozen on {ALIASED.get(owner, owner)} (no field renames on PublicApi classes)"
    elif e["kind"] == "member":
        e["treatment"] = "kept in Mugirl.MountedPawnUtility (PublicApi); Harmony-patchable extension point, public static, NoInlining"
        bound = sorted(x["name"].split("(")[1].rstrip(")") for x in partial["entries"] if x["kind"] == "harmonyParameter" and x.get("owner") == name)
        if bound:
            e["harmonyBoundParameters"] = bound
    elif e["kind"] == "harmonyParameter":
        e["treatment"] = "parameter name of a Harmony-patched public method: dependents' Prefix/Postfix bind it by name, so it never changes"
    elif e["kind"] == "xpathStructure":
        e["treatment"] = "element path that dependents patch below this def: the elements on the path keep their names (XML structure frozen)"
    else:
        e["treatment"] = "unchanged"
    entries.append(e)

DECLARED = [
    ("packageId", "HAR.MugirlRace", None, "design 2"),
    ("harmonyId", "MugirlMod.Mod", None, "design 2; WhiteFluff [HarmonyAfter]"),
    ("displayName", "Mugirl Race", None, "D7; KindSeal.LOL PatchOperationFindMod"),
    ("typeShortName", "MugirlMod", None, "design 2: settings file Mod_<folder>_MugirlMod.xml"),
    ("type", "Mugirl.Core.GameComponent_Mugirl", None, "design 5.8 / K2: hub type name frozen forever (saved by class name)"),
    ("scribeLabel", "mugirlSaveSchema", None, "design 5.1 / K1: save schema stamp label frozen forever"),
    ("defName", "Ears", "BodyPartGroupDef", "D7 / C30: shared with other mods' ear apparel"),
    ("defName", "Job_Charge", "JobDef", "D7: Kindseal.UFPC and Raven FA targetJobs"),
    ("defName", "MugirlLike", "ThinkTreeDef", "D7 / design 2"),
    ("defName", "Mugirl", "ThingDef", "design 2 (race)"), ("defName", "MugirlBody", "BodyDef", "design 2 (+ its body-part order: part indices are saved)"),
    ("defName", "Mugirl_Colony", "FactionDef", "design 2"), ("defName", "Mugirl_Milk", "ThingDef", "design 2"), ("defName", "Mugirl_Wool", "ThingDef", "design 2"),
    ("defName", "Mugirl_Hair", "StyleItemCategoryDef", "design 2"), ("defName", "Mugirl_Horn", "BodyPartDef", "design 2"), ("defName", "Mugirl_Tail", "BodyPartDef", "design 2"),
    ("defName", "Mugirl_OnSkin", "ThingCategoryDef", "design 2"), ("defNamePattern", "Mugirl_SlaveApparelKey_*", "ThingDef", "design 2"),
    ("defName", "Mugirl_Shout", "AbilityDef", "design 2"), ("defName", "Mugirl_NobelPrize", "GeneDef", "design 2"),
    ("defName", "Mugirl_Charge", "AbilityDef", "design 2 (Ability, Gene and Hediff share the name)"),
    ("defName", "Mugirl_Body_bondage", "ApparelLayerDef", "design 2"),
    ("structure", "Mugirl race bodyAddons order (HAR addonVariants are saved by index; grow only at the end)", None, "design 2"),
    ("structure", "texture paths under Textures/ (dependents reference Mugirl textures by path)", None, "design 2"),
]
have = {(e["kind"], e["name"]) for e in entries}
for kind, name, root, why in DECLARED:
    if (kind, name) in have:
        for e in entries:
            if (e["kind"], e["name"]) == (kind, name):
                e.setdefault("frozenBy", why)
        continue
    e = {"kind": kind, "name": name, "frozen": True, "frozenBy": why, "usedBy": [], "treatment": "unchanged"}
    if root:
        e["defRoot"] = root
    entries.append(e)
entries.sort(key=lambda e: (e["kind"], e["name"], e.get("defRoot") or "", e.get("owner") or ""))


def new_name(e):
    n = e["name"]
    if e["kind"] == "type":
        return type_map.get(n, n)
    if e["kind"] == "typeNameLiteral":
        return type_map.get(n, n).rsplit(".", 1)[-1]
    if e["kind"] == "member":
        t, m = n.rsplit(".", 1)
        moved = member_map.get(n) or member_map.get(type_map.get(t, t) + "." + m)
        return moved or type_map.get(t, t) + "." + m
    if e["kind"] == "defName":
        return def_map.get(n, n)
    return n


# Wave S renames already planned by the module plans (the owner should port to the final name directly).
PLANNED_S = {"Mugirl.Comp_MugirlMount.HasMountedPawn": "combat-plan: HasMountedPawn -> HasRider in Wave S",
             "Mugirl.Comp_MugirlMount.MountedPawn": "combat-plan: MountedPawn -> Rider in Wave S"}
validation_only = []
for e in partial["validationOnly"]:
    e = dict(e)
    e["newName"] = new_name(e)
    e["changes"] = e["newName"] != (e["name"].rsplit(".", 1)[-1] if e["kind"] == "typeNameLiteral" else e["name"])
    if e["name"] in PLANNED_S:
        e["plannedWaveS"] = PLANNED_S[e["name"]]
    validation_only.append(e)

doc = {
    "_doc": "Frozen-forever public API of Mugirl Race (decisions.md section 5, D5 = keep dependents working). 'entries' are frozen: no spec may rename "
            "them (spec linter L05/L06) and the validator reads this file as frozen-forever input; 'treatment' says how each stays available. "
            "'validationOnly' lists names that only dependents' validation code uses: they are NOT frozen; their new names go to the owner "
            "(owner-migration-list.md). usedBy.file is '<path in the dependent>:<line>'; use = production | validation. Kinds: packageId, harmonyId, "
            "displayName, typeShortName, type, xmlClass (Class= value or Def element name), xmlField (field of an xmlClass, see owner), member, defName "
            "(defRoot = Def database root), defNamePattern, abstractName, tag (apparel/trade tag string), texturePath, scribeLabel, structure, "
            "harmonyParameter (parameter name a dependent's Harmony patch binds; also listed as harmonyBoundParameters on its member), "
            "xpathStructure (<defName>/<element path> a dependent's XPath patches), typeNameLiteral (validationOnly: a string compared with a "
            "type short name). usedBy lines of compiled assemblies are ':0'.",
    "formatVersion": 1,
    "generated": "2026-10-15",
    "generator": "spec/final/_gen/scan_dependents.py + gen_public_api.py",
    "scanned": partial["scanned"],
    "entries": entries,
    "validationOnly": validation_only,
}
save_json(FINAL / "public-api.json", doc)
# RenameApply consumes this single document via --frozen-names.

lines = ["# Owner migration list: names WhiteFluff / Subspecies validation code uses that change", "",
         "Source: `spec/final/public-api.json#validationOnly` (scan of `Mods/乳牛娘 白绒物语` and `Mods/MooGirl_Subspecies`, 2026-10-15).",
         "These names are used only by the dependents' own validation code (WhiteFluff `Source/Validation/**`, compiled only with",
         "`EnableValidation=true`, and `Tools/**`). They are not public API (decisions.md section 5) and change in Wave R/R2; update the",
         "validation code when porting the mod to 1.7. Production references of both mods are frozen and keep working unchanged.", "",
         "`typeNameLiteral` rows are string literals compared with `GetType().Name`: they keep compiling and silently stop matching.", "",
         "| kind | old name | new name | used at (validation only) |", "|---|---|---|---|"]
for e in validation_only:
    if not e["changes"]:
        continue
    where = "; ".join(sorted({f"{u['mod']}: `{u['file']}`" for u in e["usedBy"]}))
    old = e["name"] if e["kind"] != "typeNameLiteral" else f'"{e["name"].rsplit(".", 1)[-1]}" ({e["name"]})'
    new = e["newName"] if e["kind"] != "typeNameLiteral" else f'"{e["newName"]}"'
    lines.append(f"| {e['kind']} | `{old}` | `{new}`{' (' + e['plannedWaveS'] + ')' if e.get('plannedWaveS') else ''} | {where} |")
lines += ["", "Unchanged validation-only names (listed for completeness, nothing to do): " +
          ", ".join(f"`{e['name']}`" for e in validation_only if not e["changes"]) + ".", "",
          "Related changes the owner may want to know about (production code keeps working):", "",
          "- `Mugirl.CompProperties_MooMilkable`, `Mugirl.CompProperties_MugirlMount`, `Mugirl.StockGenerator_Mugirl_Slaves` stay valid XML class names "
          "(public alias subclasses in `PublicApi/XmlAliases.cs`); their XML field names are frozen.",
          "- `Mugirl.MountedPawnUtility.IsMugirl(Pawn)` stays the Harmony-patchable decision point; the other members of that class move to "
          "`Mugirl.Features.Mounting.MountUtility` (R2).",
          "- `<Mugirl.SlaveApparelDef>` keeps loading (PublicApi); in Wave S its legacy fields are mapped into `RestraintExtension`.",
          "- A 1.7 build of WhiteFluff must reference the 1.7 `MugirlRace.dll` (its csproj points at `1.6/Assemblies/MugirlRace.dll`).", ""]
(FINAL / "owner-migration-list.md").write_text("\n".join(lines), encoding="utf-8")
print(f"entries {len(entries)}, validationOnly {len(validation_only)} ({sum(1 for e in validation_only if e['changes'])} change)")
