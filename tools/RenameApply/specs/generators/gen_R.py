"""Generate the FINAL Wave R specs (spec/final/R/<module>.json) from spec/merged.json + decisions.md.

Wave R = identity-preserving renames only (decisions.md section 3): namespaces, types, C# file moves incl. cross-module moves and
multi-type file splits, defNames + DefInjected keys/handles + XML references + abstract Names, translated baselines.
Everything else found in the module specs is routed to R2 (gen_R2.py) or to Wave S (spec/final/deferred-S.json).
Run with the repo snapshot path as argument (the tree the specs are applied to): gen_R.py <tree root>
"""
import copy
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import REPO, GEN, SPEC, FINAL, load_json, save_json, parse_xml, walk_files

TREE = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO
MAIN = TREE / "Source" / "MugirlRace"
merged = load_json(SPEC / "merged.json")
api = load_json(GEN / "public-api.partial.json")
log = []


def note(msg):
    log.append(msg)


# ---------------------------------------------------------------------------------------------------------------
# Frozen public names (decisions.md section 5, D5 = keep dependents working)
# ---------------------------------------------------------------------------------------------------------------
frozen_defs = {(e["name"], e.get("defRoot")) for e in api["entries"] if e["kind"] == "defName"}
frozen_def_names = {n for n, _ in frozen_defs}
frozen_abstract = {e["name"] for e in api["entries"] if e["kind"] == "abstractName"}
frozen_xml_types = {e["name"] for e in api["entries"] if e["kind"] == "xmlClass"}
frozen_types = {e["name"] for e in api["entries"] if e["kind"] in ("type", "xmlClass")}
# D4: these three keep working through thin public alias subclasses (PublicApi/XmlAliases.cs); their implementation may move.
ALIASED = {
    "Mugirl.StockGenerator_Mugirl_Slaves": "Mugirl.Features.Trade.StockGenerator_MugirlSlaves",
    "Mugirl.CompProperties_MugirlMount": "Mugirl.Features.Mounting.CompProperties_Mount",
    "Mugirl.CompProperties_MooMilkable": "Mugirl.Features.Milk.CompProperties_MilkProduction",
}
# Kept by name and moved into PublicApi/ (decisions section 5): the Def element <Mugirl.SlaveApparelDef> and the patchable
# extension point Mugirl.MountedPawnUtility.IsMugirl.
KEEP_IN_PUBLIC_API = {
    "Mugirl.SlaveApparelDef": ("restraints", "Source/MugirlRace/PublicApi/SlaveApparelDef.cs"),
    "Mugirl.MountedPawnUtility": ("combat", "Source/MugirlRace/PublicApi/MountedPawnUtility.cs"),
}
# Declared public by decisions (D7, design section 2) although no installed dependent uses them today.
DECLARED_FROZEN_DEFS = {"Ears", "Job_Charge", "MugirlLike", "Mugirl", "MugirlBody", "Mugirl_Colony", "Mugirl_Milk", "Mugirl_Wool",
                        "Mugirl_Hair", "Mugirl_Horn", "Mugirl_Tail", "Mugirl_OnSkin", "Mugirl_Shout", "Mugirl_NobelPrize",
                        "Mugirl_Charge", "Mugirl_Body_bondage"}
DECLARED_FROZEN_DEF_PREFIX = ("Mugirl_SlaveApparelKey_",)

# ---------------------------------------------------------------------------------------------------------------
# Repository facts
# ---------------------------------------------------------------------------------------------------------------
TOP_TYPE_RX = re.compile(r"^(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|unsafe|file|ref|new)\s+)*"
                         r"(class|struct|interface|enum|record(?:\s+struct|\s+class)?|delegate)\s+(?:[\w<>\[\],.? ]+?\s+)?(\w+)(?:\s*<([^>]*)>)?", re.M)


def top_level_types(path):
    """Top-level type declarations of a file-scoped-namespace file (column 0)."""
    text = path.read_text(encoding="utf-8-sig")
    ns = re.search(r"^namespace\s+([\w.]+)\s*;", text, re.M)
    out = []
    for m in TOP_TYPE_RX.finditer(text):
        name = m.group(2)
        arity = len(m.group(3).split(",")) if m.group(3) else 0
        out.append(((ns.group(1) + "." if ns else "") + name + (f"`{arity}" if arity else ""), name))
    return out


file_types = {}
for f in walk_files(MAIN, (".cs",)):
    rel = str(f.relative_to(TREE)).replace("\\", "/")
    file_types[rel] = top_level_types(f)
type_file = defaultdict(list)
for rel, ts in file_types.items():
    for full, _ in ts:
        type_file[full].append(rel)

# ---------------------------------------------------------------------------------------------------------------
# Types
# ---------------------------------------------------------------------------------------------------------------
types = []
for r in merged["types"]:
    r = copy.deepcopy(r)
    mods = r.pop("modules")
    r["module"] = mods[0]
    if len(mods) > 1:
        r["alsoListedBy"] = mods[1:]
    r.pop("conflict", None)
    r.pop("reasonsByModule", None)
    types.append(r)

# C01: corporate wins (CorporateForeignBehavior); core's two rows go; the test class follows its subject (types row, not a file move).
types = [t for t in types if not (t["old"] in ("Mugirl.CorporatePricingCompatibility", "Mugirl.Tests.Compatibility.CorporatePricingCompatibilityTests") and t["module"] == "core")]
types.append({"old": "Mugirl.Tests.Compatibility.CorporatePricingCompatibilityTests", "new": "Mugirl.Tests.Corporate.CorporateForeignBehaviorTests",
              "oldFile": "Source/Tests/MugirlRace.Tests/Compatibility/CorporatePricingCompatibilityTests.cs",
              "newFile": "Source/Tests/MugirlRace.Tests/Corporate/CorporateForeignBehaviorTests.cs", "saveVisible": False, "xmlVisible": False,
              "module": "corporate", "reason": "C01/C28: the test class follows its subject (CorporateForeignBehavior)."})
# C28: the raid-tier test becomes a corporate test by name and namespace.
types.append({"old": "Mugirl.Tests.Incidents.GiantCorpRaidTierRulesTests", "new": "Mugirl.Tests.Corporate.CorporateRaidTierRulesTests",
              "oldFile": "Source/Tests/MugirlRace.Tests/Incidents/GiantCorpRaidTierRulesTests.cs",
              "newFile": "Source/Tests/MugirlRace.Tests/Corporate/CorporateRaidTierRulesTests.cs", "saveVisible": False, "xmlVisible": False,
              "module": "corporate", "reason": "C28: class and namespace follow the moved file (corporate raid tiers)."})

# C02/C03: no DefOf type rows in R (the DefOf split is R2). DefOf classes that share a file with types that move are extracted
# into their own file under their current name (identity extraction) so the file's other types can change namespace.
DEFOF_ROWS = {"Mugirl.CorporateFinanceDefOf", "Mugirl.CorporateIntroductionDefOf", "Mugirl.CorporateQuestDefOf",
              "Mugirl.CorporatePeopleDefOf", "Mugirl.CorporateTradeDefOf"}
types = [t for t in types if t["old"] not in DEFOF_ROWS]

# C18: talk-to-pawn bases live in Mugirl.Core (Core/Interaction).
for t in types:
    if t["old"] == "Mugirl.Features.Incidents.FloatMenuProvider_MugirlTalkToPawn":
        t.update(new="Mugirl.Core.FloatMenuOptionProvider_TalkToEventPawn", newFile="Source/MugirlRace/Core/Interaction/FloatMenuOptionProvider_TalkToEventPawn.cs",
                 reason=t.get("reason", "") + " C18: shared talk-to-pawn bases live in Mugirl.Core (Core/Interaction); corporate derives from them.")
    if t["old"] == "Mugirl.Features.Incidents.JobDriver_MugirlTalkToPawn":
        t.update(new="Mugirl.Core.JobDriver_TalkToEventPawn", newFile="Source/MugirlRace/Core/Interaction/JobDriver_TalkToEventPawn.cs",
                 reason=t.get("reason", "") + " C18: shared talk-to-pawn bases live in Mugirl.Core (Core/Interaction).")

# C21: Core provides the equipment-transfer flag that WeaponWheel writes and Mounting reads (combat-plan S2-12).
for old, new, file in (("Mugirl.Features.WeaponWheel.WeaponWheelTransferScope", "Mugirl.Core.EquipmentTransferScope", "Source/MugirlRace/Core/Equipment/EquipmentTransferScope.cs"),
                       ("Mugirl.Features.WeaponWheel.WeaponWheelTransferMode", "Mugirl.Core.EquipmentTransferMode", "Source/MugirlRace/Core/Equipment/EquipmentTransferMode.cs")):
    types = [t for t in types if t["old"] != old]
    types.append({"old": old, "new": new, "oldFile": "Source/MugirlRace/Features/WeaponWheel/Core/WeaponWheelTransferScope.cs", "newFile": file,
                  "saveVisible": False, "xmlVisible": False, "module": "core",
                  "reason": "C21: core provides the equipment-transfer scope (written by WeaponWheel, read by Mounting); breaks the Mounting<->WeaponWheel cycle."})

# D5: frozen names. Three XML classes keep their old names through alias subclasses; the others are not renamed at all.
public_api_moves = []
for t in list(types):
    if t["old"] in KEEP_IN_PUBLIC_API:
        types.remove(t)
        module, target = KEEP_IN_PUBLIC_API[t["old"]]
        public_api_moves.append({"old": t["oldFile"], "new": target, "module": module,
                                 "reason": f"D5/decisions section 5: {t['old']} is public API (used by dependents); its name stays, the file moves to PublicApi/ (root namespace allowed only there)."})
        note(f"removed type rename {t['old']} -> {t['new']} (frozen public API); file moves to {target}")
    elif t["old"] in ALIASED:
        assert t["new"] == ALIASED[t["old"]], (t["old"], t["new"])
        t["publicAlias"] = {"file": "Source/MugirlRace/PublicApi/XmlAliases.cs", "name": t["old"],
                            "frozenXmlFields": sorted(e["name"] for e in api["entries"] if e["kind"] == "xmlField" and e.get("owner") == t["old"])}
    elif t["old"] in frozen_types:
        raise SystemExit(f"type {t['old']} is frozen but has a rename row")

# C23: machine-readable transitional markers.
HAND = {h["old"]: h for h in merged["handTypeDispositions"] if h["old"].startswith("Mugirl.")}
for t in types:
    if not t.get("transitional"):
        t.pop("transitional", None)
        continue
    reason = t.get("reason", "")
    rl = reason.lower()
    if "merged into" in rl or "merge" in rl and "wave s" in rl:
        waves = "merge"
    elif "delete" in rl or "deletes" in rl or "replaced by" in rl:
        waves = "delete"
    else:
        waves = "keep"
    h = HAND.get(t["old"])
    if h:
        upgrade = h["disposition"] + (f":{h['target']}" if h.get("target") else "") + (f" ({h['policy']})" if h.get("policy") else "") + (f" step={h['step']}" if h.get("step") else "")
    elif t.get("saveVisible"):
        upgrade = "Map"
    else:
        upgrade = "none (not save-visible)"
    t["transitional"] = {"waveS": waves, "upgrade": upgrade}

# C25: no namespace segment "Apparel".
for t in types:
    assert ".Apparel." not in t["new"] + "." or t["new"].startswith("Mugirl.Tests"), t["new"]

# C27/one type per file: every production file that keeps more than one top-level type gets split by identity rows.
by_old = {t["old"]: t for t in types}


def final_target(rel):
    stem = Path(rel).name.split(".")[0]
    for full, short in file_types.get(rel, []):
        if short == stem and full in by_old and by_old[full].get("newFile") and (by_old[full].get("oldFile") in (None, rel)):
            return by_old[full]["newFile"]
    for m in merged["fileMoves"]:
        if m["old"] == rel:
            return m["new"]
    for m in public_api_moves:
        if m["old"] == rel:
            return m["new"]
    return rel


split_rows = 0
for rel, ts in sorted(file_types.items()):
    if len(ts) < 2:
        continue
    target = final_target(rel)
    tdir = target.rsplit("/", 1)[0]
    stem = Path(target).name.split(".")[0]
    for full, short in ts:
        row = by_old.get(full)
        new_full = row["new"] if row else full
        new_short = new_full.rsplit(".", 1)[-1].split("`")[0]
        if row and row.get("newFile") and row.get("newFile") != target:
            continue  # already leaves the file
        if new_short == stem:
            continue  # the file's primary type
        if full in DEFOF_ROWS or (row is None and full.endswith("DefOf")):
            # DefOf classes stay put until R2 unless they share a file with moving types.
            pass
        new_file = f"{tdir}/{new_short}.cs"
        if row:
            row["newFile"] = new_file
            row.setdefault("oldFile", rel)
            row["reason"] = row.get("reason", "") + " One type per file (C27): split out of " + Path(rel).name + "."
        else:
            r = {"old": full, "new": full, "oldFile": rel, "newFile": new_file, "saveVisible": False, "xmlVisible": False,
                 "module": None, "reason": f"One type per file (C27): identity extraction out of {Path(rel).name}."}
            if full in DEFOF_ROWS:
                r["reason"] += " DefOf class: keeps its name until the R2 DefOf split dissolves it."
                r["transitional"] = {"waveS": "n/a (dissolved in R2)", "upgrade": "none (not save-visible)"}
            types.append(r)
            by_old[full] = r
        split_rows += 1
note(f"one-type-per-file: {split_rows} extraction/newFile adjustments")

# Module of identity rows: owner of the target folder.
OWN = [(o["path"], o["module"]) for o in merged["ownedSourceFolders"]]


def owner_of(path):
    best = None
    for pat, mod in OWN:
        p = pat.rstrip("*").rstrip("/")
        if path == pat or path.startswith(p + "/"):
            if best is None or len(p) > len(best[0]):
                best = (p, mod)
    return best[1] if best else "core"


for t in types:
    if not t.get("module"):
        t["module"] = owner_of(t["newFile"])

# ---------------------------------------------------------------------------------------------------------------
# File moves (C# only; content file moves are R2/S)
# ---------------------------------------------------------------------------------------------------------------
file_moves = []
for m in merged["fileMoves"]:
    if m["old"] in ("Source/Tests/MugirlRace.Tests/Incidents/GiantCorpRaidTierRulesTests.cs",
                    "Source/Tests/MugirlRace.Tests/Compatibility/CorporatePricingCompatibilityTests.cs"):
        continue  # replaced by types rows (C01, C28)
    file_moves.append({"old": m["old"], "new": m["new"], "module": m["module"]})
file_moves += public_api_moves

# ---------------------------------------------------------------------------------------------------------------
# Defs
# ---------------------------------------------------------------------------------------------------------------
ROOT_SHORT = {
    "Mugirl.Features.Corporate.CorporateFinanceConfigDef": "CorporateFinanceConfigDef",
    "Mugirl.Features.Corporate.CorporatePeopleConfigDef": "CorporatePeopleConfigDef",
    "Mugirl.Features.Corporate.CorporateTradeConfigDef": "CorporateTradeSettingsDef",
    "Mugirl.Features.Corporate.CorporateMissionConfigDef": "CorporateQuestConfigDef",
    "Mugirl.Features.Corporate.CorporateIntroductionConfigDef": "CorporateIntroductionDef",
}
# C13: the NEW name equals a def of another type (informational: the upgrader keys on (root, name)); the tool's own
# 'sharedNameWithOtherDefTypes' keeps meaning "the OLD name is shared" (selects explicitRefs).
NEW_SHARED = {("MaterialDef", "Mugirl_ExtraOutline"), ("MaterialDef", "Mugirl_LanceMotionBlur"), ("JobDef", "Mugirl_Nuzzle"),
              ("ThinkTreeDef", "Mugirl_Punisher"), ("JobDef", "Mugirl_Dunk"), ("JobDef", "Mugirl_GatherMilk"), ("WorkGiverDef", "Mugirl_GatherMilk")}
# C26: projectile category after the prefix.
C26 = {
    "Bullet_PMC_Carbine": "Mugirl_Bullet_PMC_Carbine", "Bullet_PMC_Assault_Rifle": "Mugirl_Bullet_PMC_AssaultRifle",
    "Bullet_PMC_DMR": "Mugirl_Bullet_PMC_DMR", "Bullet_PMC_LMG": "Mugirl_Bullet_PMC_LMG",
    "Bullet_OPC_Tranquilizer_Pistol": "Mugirl_Bullet_OPC_TranquilizerPistol", "Bullet_OPC_Defense_Rifle": "Mugirl_Bullet_OPC_DefenseRifle",
    "Bullet_OPC_Tactical_Shotgun": "Mugirl_Bullet_OPC_TacticalShotgun", "Bullet_OPC_Assault_Rifle": "Mugirl_Bullet_OPC_AssaultRifle",
}
C26_EXISTING = {"Bullet_Mugirl_AR_Standard": "Mugirl_Bullet_AR_Standard", "Bullet_Mugirl_MP_Standard": "Mugirl_Bullet_MP_Standard",
                "Bullet_Mugirl_Militia_Rifle": "Mugirl_Bullet_Militia_Rifle", "Bullet_Mugirl_AT23_Autocannon": "Mugirl_Bullet_AT23_Autocannon"}

# Sites that carry an old shared name but mean something else (C# has no schema; XML sites are typed by the tool).
NOT_REFS = {
    ("ThingCategoryDef", "Mugirl_Slave"): ["Source/MugirlRace/Features/Incidents/Shared/MugirlGeneratedPawnUtility.cs:8"],  # backstory category string
}
defs = []
for r in merged["defs"]:
    r = copy.deepcopy(r)
    mods = r.pop("modules")
    r["module"] = mods[0]
    if len(mods) > 1:
        r["alsoListedBy"] = mods[1:]
    r.pop("conflict", None)
    if "reasonsByModule" in r:
        rb = r.pop("reasonsByModule")
        r.setdefault("reason", " | ".join(f"{k}: {v}" for k, v in rb.items()))
    if "saveVisibleByModule" in r:
        r.pop("saveVisibleByModule")
    r["defType"] = ROOT_SHORT.get(r["defType"], r["defType"])
    if r["old"] in frozen_def_names or r["old"] in DECLARED_FROZEN_DEFS or r["old"].startswith(DECLARED_FROZEN_DEF_PREFIX):
        note(f"removed def rename {r['defType']} {r['old']} -> {r['new']} (frozen public API, D5)")
        continue
    if r["old"] == "MugirlWashMentalEyesInHead":
        # C12 (binding): motes stay saveVisible=true (the map entry is harmless); the upgrader's thing-ID rewrite excludes motes.
        r["saveVisible"] = True
        r["mote"] = True
    if (r["defType"], r["new"]) in NEW_SHARED:
        r["newNameSharedWithOtherDefTypes"] = True
    if r["old"] in C26:
        r["new"] = C26[r["old"]]
        r["reason"] = (r.get("reason") or "") + " C26: def category after the Mugirl_ prefix (Mugirl_Bullet_*)."
    if r.get("explicitRefs"):
        # "path:LINE" only (some module specs appended prose in parentheses)
        r["explicitRefs"] = [re.match(r"^\s*([^:(\s]+:\d+)", x).group(1) for x in r["explicitRefs"]]
    if not r.get("explicitRefs"):
        r.pop("explicitRefs", None)
    if (r["defType"], r["old"]) in NOT_REFS:
        r["notRefs"] = NOT_REFS[(r["defType"], r["old"])]
    defs.append(r)
for old, new in C26_EXISTING.items():
    defs.append({"defType": "ThingDef", "old": old, "new": new, "saveVisible": True, "sharedNameWithOtherDefTypes": False, "module": "combat",
                 "reason": "C26: def category after the Mugirl_ prefix (projectiles in flight are saved: upgrader def map + thing-ID rewrite)."})

abstract = []
for r in merged["abstractNames"]:
    r = copy.deepcopy(r)
    mods = r.pop("modules")
    r["module"] = mods[0]
    if "reasonsByModule" in r:
        rb = r.pop("reasonsByModule")
        r.setdefault("reason", " | ".join(f"{k}: {v}" for k, v in rb.items()))
    if r["old"] in frozen_abstract:
        note(f"removed abstract rename {r['old']} (frozen)")
        continue
    abstract.append(r)

# ---------------------------------------------------------------------------------------------------------------
# DefInjected class-name handles for every type whose SHORT name changes (linter rule: Comp_X -> CompX lists them)
# ---------------------------------------------------------------------------------------------------------------
handle_index = defaultdict(list)
for f in walk_files(TREE / "1.7", (".xml",)):
    rel = str(f.relative_to(TREE)).replace("\\", "/")
    if "/DefInjected/" not in rel:
        continue
    root = parse_xml(f)
    if root is None:
        continue
    for c in root.children:
        for seg in c.tag.split(".")[1:]:
            handle_index[re.sub(r"-\d+$", "", seg)].append(f"{rel}:{c.line}")
for t in types:
    o, n = t["old"].rsplit(".", 1)[-1].split("`")[0], t["new"].rsplit(".", 1)[-1].split("`")[0]
    if o != n and handle_index.get(o):
        t["definjectedHandles"] = sorted(set(handle_index[o]))

# ---------------------------------------------------------------------------------------------------------------
# C05: JobDef Job_Nuzzle -> Mugirl_Nuzzle needs the InteractionDef field of the same name re-bound through [DefAlias]
# (DefOfHelper.BindDefsFor honours DefAliasAttribute in 1.7.4977, verified by decompiling RimWorld.DefOfHelper).
# ---------------------------------------------------------------------------------------------------------------
member_renames = [{
    "member": "Mugirl.Mugirl_DefOf.Mugirl_Nuzzle", "new": "Mugirl_NuzzleInteraction", "module": "incidents",
    "addAttributes": ['DefAlias("Mugirl_Nuzzle")'],
    "reason": "C05: JobDef Job_Nuzzle is renamed to Mugirl_Nuzzle; the InteractionDef field of the same defName keeps binding through [DefAlias].",
}, {
    "member": "Mugirl.Mugirl_DefOf.Mugirl_Dunk", "new": "Mugirl_DunkAbility", "module": "milk",
    "addAttributes": ['DefAlias("Mugirl_Dunk")'],
    "reason": "Same pattern as C05: JobDef Job_MugirlDunk is renamed to Mugirl_Dunk; the AbilityDef field of that defName keeps binding through [DefAlias] (both fields have no C# reader and are deleted by the R2 DefOf split).",
}]

# D4: thin public alias subclasses, one file.
ALIAS_FILE = """namespace Mugirl;

// 公共 API：其他 Mod 的 XML 以这些旧类名引用本 Mod 的类（KindSeal.LOL 的商人库存、Mugirl Race Subspecies 与白绒物语的种族组件）。
// 实现位于各功能的命名空间；这里只保留名字，字段名由基类保持不变。新增或改名前先看 spec/final/public-api.json。

/// <summary>旧类名 <c>Mugirl.StockGenerator_Mugirl_Slaves</c>；字段 <c>slaveCountRange</c>、<c>slaveKindDef</c> 不变。</summary>
public class StockGenerator_Mugirl_Slaves : Features.Trade.StockGenerator_MugirlSlaves
{
}

/// <summary>旧类名 <c>Mugirl.CompProperties_MugirlMount</c>。</summary>
public class CompProperties_MugirlMount : Features.Mounting.CompProperties_Mount
{
}

/// <summary>旧类名 <c>Mugirl.CompProperties_MooMilkable</c>；字段 <c>displayString</c>、<c>milkDef</c>、<c>milkIntervalDays</c>、<c>milkAmount</c> 不变。</summary>
public class CompProperties_MooMilkable : Features.Milk.CompProperties_MilkProduction
{
}
"""
create_files = [{"path": "Source/MugirlRace/PublicApi/XmlAliases.cs", "module": "core", "content": ALIAS_FILE,
                 "reason": "D4: thin public alias subclasses keep the three XML class names used by other mods working."}]
# The settings class moves to Mugirl.Core (decisions 2 "Settings"); the comment stating it must never be renamed becomes false.
# The settings file keeps loading through the K1 settings.fileClass row (applied_to_manifest.py) that lands with R.
text_edits = [{"file": "Source/MugirlRace/Core/MugirlSettings.cs", "module": "core",
               "old": "// 类名与 Scribe 标签都是设置文件的一部分，不能改名。",
               "new": "// 类名与 Scribe 标签写入设置文件；改动时同时在存档升级层登记设置升级行（RenameSettingsClass / RenameSetting）。",
               "reason": "R moves Mugirl.MugirlSettings to Mugirl.Core.MugirlSettings; the settings upgrade maps the old Class value."}]
baseline_appends = [{"file": "Source/Tests/MugirlRace.Validation/Baselines/empty-types.allow.txt", "module": "core",
                     "lines": [f"{a}\tpublic XML alias (decisions D4/D5): other mods name this class in their Defs; behaviour and XML fields live in {ALIASED[a]}." for a in sorted(ALIASED)],
                     "reason": "D4: the alias subclasses are intentionally empty."}]

# Validator debt the renames pay off: "root: <type>" lines of structure-debt.allow.txt whose type leaves the root namespace
# (or moves into PublicApi/, where the root namespace is allowed for public-api.json types). The validator reports such
# lines as stale; its header says "delete this line when it does".
DEBT = "Source/Tests/MugirlRace.Validation/Baselines/structure-debt.allow.txt"
debt_keys = []
if (TREE / DEBT).exists():
    renamed_ns = {t["old"]: t["new"].rsplit(".", 1)[0] for t in types}
    for line in (TREE / DEBT).read_text(encoding="utf-8-sig").splitlines():
        key = line.split("\t")[0].strip()
        if not key.startswith("root: "):
            continue
        name = key[len("root: "):]
        if (name in renamed_ns and renamed_ns[name] != "Mugirl") or name in KEEP_IN_PUBLIC_API:
            debt_keys.append(key)
baseline_removals = [{"file": DEBT, "module": "core", "keys": debt_keys,
                      "reason": "B.namespaces debt paid by Wave R: these types leave the root namespace (or move into PublicApi/)."}] if debt_keys else []
note(f"structure-debt root lines removed by R: {len(debt_keys)}")

# ---------------------------------------------------------------------------------------------------------------
# Ownership after R (C09/C10/C11/C24 and PublicApi)
# ---------------------------------------------------------------------------------------------------------------
owned_src = defaultdict(list)
for o in merged["ownedSourceFolders"]:
    owned_src[o["module"]].append(o["path"])
owned_src["core"].append("Source/MugirlRace/PublicApi/XmlAliases.cs")
owned_src["restraints"].append("Source/MugirlRace/PublicApi/SlaveApparelDef.cs")
owned_src["combat"].append("Source/MugirlRace/PublicApi/MountedPawnUtility.cs")
owned_src["core"].append("Source/MugirlRace/Core/Interaction/**")
owned_content = defaultdict(list)
for o in merged["ownedContentFiles"]:
    owned_content[o["module"]].append(o["path"])

# ---------------------------------------------------------------------------------------------------------------
# Write
# ---------------------------------------------------------------------------------------------------------------
out_dir = FINAL / "R"
out_dir.mkdir(parents=True, exist_ok=True)
for old in out_dir.glob("*.json"):
    old.unlink()
modules = sorted({t["module"] for t in types} | {d["module"] for d in defs} | {a["module"] for a in abstract} | {m["module"] for m in file_moves} | {"core"})
for mod in modules:
    spec = {
        "module": mod,
        "wave": "R",
        "ownedSourceFolders": owned_src.get(mod, []),
        "ownedContentFiles": owned_content.get(mod, []),
        "types": sorted([t for t in types if t["module"] == mod], key=lambda t: t["old"]),
        "fileMoves": sorted([m for m in file_moves if m["module"] == mod], key=lambda m: m["old"]),
        "defs": sorted([d for d in defs if d["module"] == mod], key=lambda d: (d["defType"], d["old"])),
        "abstractNames": sorted([a for a in abstract if a["module"] == mod], key=lambda a: a["old"]),
        "memberRenames": [m for m in member_renames if m["module"] == mod],
        "createFiles": [c for c in create_files if c["module"] == mod],
        "textEdits": [{k: v for k, v in t.items() if k != "module"} for t in text_edits if t["module"] == mod],
        "baselineAppends": [b for b in baseline_appends if b["module"] == mod],
        "baselineRemovals": [{k: v for k, v in b.items() if k != "module"} for b in baseline_removals if b["module"] == mod],
    }
    spec = {k: v for k, v in spec.items() if v or k in ("module", "wave", "types", "defs")}
    save_json(out_dir / f"{mod}.json", spec)
save_json(GEN / "gen_R.log.json", log)
print(f"types {len(types)}, fileMoves {len(file_moves)}, defs {len(defs)}, abstract {len(abstract)}, modules {modules}")
for m in log:
    print(" -", m)
