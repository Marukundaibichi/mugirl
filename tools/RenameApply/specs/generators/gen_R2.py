"""Generate the FINAL Wave R2 specs (spec/final/R2/*.json) against the POST-R tree.

R2 (decisions.md section 3): DefOf dissolution by the owner-of-the-Def rule (C04), the closed PatchModule enum, shared
XML/Keyed file splits by feature, date-named content file renames, cross-owner member moves/renames (C06, C19, C21).
Usage: gen_R2.py <post-R tree> <R applied report (rename-applied json)>
Needs defof_facts.py output (defof-facts.json) for the same tree. Writes R2/*.json and _gen/r2-defof-decisions.txt.
"""
import fnmatch
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import GEN, FINAL, SPEC, load_json, save_json, parse_xml, walk_files

TREE = Path(sys.argv[1])
MAIN = TREE / "Source" / "MugirlRace"
facts = load_json(GEN / "defof-facts.json")
cmm = load_json(SPEC / "core-member-moves.json")
merged = load_json(SPEC / "merged.json")
keyed_split = load_json(SPEC / "content-keyed-split.json")
r_specs = {f.stem: load_json(f) for f in sorted((FINAL / "R").glob("*.json"))}
applied = load_json(Path(sys.argv[2]))
applied = applied.get("maps", applied)
decisions = []

defof_renames = {e["old"]: e["new"] for e in applied["defOfFields"] + applied["members"]}
type_renames = {t["old"]: t["new"] for t in applied["types"]}
def_renames = {(d.get("rootDefType") or d["defType"], d["old"]): d["new"] for d in applied["defs"]}
def_new = {d["old"]: d["new"] for d in applied["defs"] if not d.get("derived")}


def post_r_member(m):
    m = defof_renames.get(m, m)
    cls, field = m.rsplit(".", 1)
    return type_renames.get(cls, cls) + "." + field


def nd(name):
    """post-R defName of a pre-R name (identity when not renamed)."""
    return def_new.get(name, name)


# =================================================================================================================
# 1. Shared XML files split by feature (decisions R2; C09, C10, C11, milk-plan S4-2/S4-3, restraints-plan S4-6)
# =================================================================================================================
D = "1.7/Defs/"
L = "1.7/Languages/"
XML_SPLITS = [
    {"source": D + "JobDefs/JobDefs.xml", "module": "content", "reason": "C09 / content-plan S2-1: one JobDef file per feature; JobDefs.xml is dissolved.",
     "targets": [
         {"file": D + "JobDefs/JobDefs_Roping.xml", "owner": "restraints", "feature": "Features/Roping",
          "defs": [nd("JobDriver_RopeMoo"), nd("JobDriver_RemoveRopeMoo"), nd("RopeToBuild"), nd("Job_FollowRoper")]},
         {"file": D + "JobDefs/JobDefs_Restraints.xml", "owner": "restraints", "feature": "Features/Restraints",
          "defs": ["GiveSlaveApparelGear", nd("UnlockSlaveApparel"), nd("ForceUnlockSlaveApparel"), nd("CrackBondageGear")]},
         {"file": D + "JobDefs/JobDefs_Social.xml", "owner": "incidents", "feature": "Features/Social", "defs": [nd("Job_Nuzzle")]},
         {"file": D + "JobDefs/JobDefs_Milk.xml", "owner": "milk", "feature": "Features/Milk",
          "defs": [nd("Job_Breastfeed"), nd("Job_DrinkMilkFromMugirl"), nd("Job_FeedMilkToDowned"), nd("Job_MugirlMilkInteractionWait")]},
     ]},
    {"source": D + "ThoughtDefs/Thoughts_Traits.xml", "module": "incidents", "reason": "C10 / incidents-plan S2-15: thoughts split by topic.",
     "targets": [
         {"file": D + "ThoughtDefs/Thoughts_Mugirl.xml", "owner": "incidents", "feature": "Features/Social",
          "defs": ["Mugirl_SingleshotWeaponAversionUnhappy", "Mugirl_AteHay", "Mugirl_AteNutrient", "Mugirl_Nuzzled"]},
         {"file": D + "ThoughtDefs/Thoughts_Restraints.xml", "owner": "restraints", "feature": "Features/Restraints",
          "defs": ["Mugirl_BrainwashHappy", "Mugirl_BrainWashing_End"]},
         {"file": D + "ThoughtDefs/Thoughts_Roping.xml", "owner": "restraints", "feature": "Features/Roping", "defs": ["Mugirl_Roper", "Mugirl_Roped"]},
     ]},
    {"source": D + "SoundsDefs/Bondage_Sounds.xml", "module": "milk", "reason": "restraints-plan S4-6 / milk-plan S2-17: milking sounds belong to the milk feature.",
     "targets": [{"file": D + "SoundsDefs/Milk_Sounds.xml", "owner": "milk", "feature": "Features/Milk", "defs": ["Mugirl_Milking_Sound", "Mugirl_Milking_Power_Sound"]}]},
    {"source": D + "Apparel/Apparel_Bondage/BondageApparel_Spacer.xml", "module": "milk", "reason": "milk-plan S4-3: milking devices out of the restraint file.",
     "targets": [{"file": D + "Apparel/Apparel_MilkingDevice.xml", "owner": "milk", "feature": "Features/Milk", "defs": ["Mugirl_MilkingDevice", "Mugirl_MilkingDeviceHidden"]}]},
    # C04 needs the defining file to belong to the feature: shared race-content files are split where their defs are feature defs.
    # LifeStages.xml stays whole: its LifeStageDefs are part of the race definition (Mugirl_Race.xml lifeStageAges, C11 content).
    {"source": D + "TraitDefs/TraitDefs.xml", "module": "content", "reason": "C04/C09: feature traits leave the race-wide trait file (Mugirl_SingleshotWeaponAversion stays: race trait).",
     "targets": [
         {"file": D + "TraitDefs/TraitDefs_Corporate.xml", "owner": "corporate", "feature": "Features/Corporate", "defs": ["Mugirl_CorporateDiehard"]},
         {"file": D + "TraitDefs/TraitDefs_Restraints.xml", "owner": "restraints", "feature": "Features/Restraints", "defs": ["Mugirl_BrainwashPeace", "Mugirl_BrainwashObey"]},
         {"file": D + "TraitDefs/TraitDefs_Milk.xml", "owner": "milk", "feature": "Features/Milk", "defs": ["Mugirl_NurturedByMugirl"]},
     ]},
    {"source": D + "ThingDefs_Misc/Items_Filth.xml", "module": "content", "reason": "C04/C09: milk filth and the brainwash head effects belong to their features; the file is dissolved.",
     "targets": [
         {"file": D + "ThingDefs_Misc/Milk_Filth.xml", "owner": "milk", "feature": "Features/Milk",
          "defs": ["MugirlMilkFilth", nd("MugirlMilkStain"), nd("MugirlWaterFilth"), "Mugirl_MilkSpray"]},
         {"file": D + "ThingDefs_Misc/Restraints_BrainwashEffects.xml", "owner": "restraints", "feature": "Features/Restraints",
          "defs": [nd("MugirlWashMentalEyesInHead"), nd("MugirlWashMentalEyes")]},
     ]},
    # date-named DefInjected file whose defs land in two renamed Defs files (MeleeWeapons_0920 / RangedWeapons_0920)
    {"source": L + "ChineseSimplified/DefInjected/ThingDef/Weapons_0920.xml", "module": "combat", "reason": "Date-named file: entries follow their Defs files.",
     "targets": [
         {"file": L + "ChineseSimplified/DefInjected/ThingDef/MeleeWeapons_Lances.xml", "owner": "combat", "defs": ["Mugirl_NormanLance", "Mugirl_HeavyKnightLance", "Mugirl_SteamKnightLance"]},
         {"file": L + "ChineseSimplified/DefInjected/ThingDef/RangedWeapons_Autocannon.xml", "owner": "combat", "defs": ["Mugirl_Bullet_AT23_Autocannon", "Mugirl_AT23Autocannon"]},
     ]},
]
# Patches/MugirlPatch.xml (content-plan S2-2): operation indices verified by fingerprints (the first xpath of each operation).
patch = parse_xml(TREE / "1.7/Patches/MugirlPatch.xml")
ops = [(i, c, next((n.text.strip() for n in c.iter() if n.tag == "xpath"), "")) for i, c in enumerate(patch.children, 1)]


def ops_where(pred):
    return [(i, x) for i, c, x in ops if pred(x)]


GOV = {
    "FoodThoughts_PreceptAndHay_Patch.xml": ("原版 Ideology 戒律 NutrientPasteEating_Disgusting 的 comp 与原版干草 ThingDef。",
                                             "1.7 根补丁；为雪牛娘追加营养膏与干草的进食心情。",
                                             "追加前用 PatchOperationConditional 检查目标中是否已有同一节点；干草已有任意 tasteThought 时不再追加，避免 1.7 加载器报告重复字段。",
                                             "缺少可选 Ideology 目标可以接受；干草目标缺失时 nomatch 中的 PatchOperationAdd 失败，游戏记录 Patch operation failed。"),
    "Roping_CaravanPackingSpot_Patch.xml": ("原版商队打包点 CaravanPackingSpot 的 comps。",
                                            "1.7 根补丁；追加把雪牛娘牵到建筑的可用 comp。",
                                            "已有同一 compClass 的 comp 时不重复追加。",
                                            "原版目标缺失时 nomatch 中的 PatchOperationAdd 失败，游戏记录 Patch operation failed。"),
    "Slaves_TraderStock_Patch.xml": ("原版海盗轨道商、海盗商队与外来者基地的 TraderKindDef 库存生成器。",
                                     "1.7 根补丁；追加雪牛娘奴隶库存生成器。",
                                     "商人已有同类库存生成器时不重复追加。",
                                     "原版商人缺失时 nomatch 中的 PatchOperationAdd 失败，游戏记录 Patch operation failed。"),
    "Restraints_TraderStock_Patch.xml": ("原版异域商队与轨道异域商人的 TraderKindDef 库存生成器。",
                                              "1.7 根补丁；追加洗脑头盔库存。",
                                              "商人已有生成该 thingDef 的库存生成器时不重复追加。",
                                              "原版商人缺失时 nomatch 中的 PatchOperationAdd 失败，游戏记录 Patch operation failed。"),
    "Restraints_ElectricSmelter_Patch.xml": ("原版电熔炉 ElectricSmelter 的配方列表。",
                                             "1.7 根补丁；追加束具熔炼配方。",
                                             "电熔炉已有该配方时不重复追加。",
                                             "原版目标缺失时 nomatch 中的 PatchOperationAdd 失败，游戏记录 Patch operation failed。"),
}


def governance(file):
    t, sc, dg, fa = GOV[file.split("/")[-1]]
    return ("    <!-- PatchGovernance:\n    Targets: " + t + "\n    Scope: " + sc + "\n    Duplicate guard: " + dg + "\n    Failure: " + fa + "\n    -->\n")


PATCH_TARGETS = [
    ("1.7/Patches/FoodThoughts_PreceptAndHay_Patch.xml", "incidents", lambda x: "NutrientPasteEating_Disgusting" in x or 'defName="Hay"' in x),
    ("1.7/Patches/Roping_CaravanPackingSpot_Patch.xml", "restraints", lambda x: "CaravanPackingSpot" in x),
    ("1.7/Patches/Slaves_TraderStock_Patch.xml", "incidents", lambda x: "StockGenerator_MugirlSlaves" in x),
    # Named to sort after MilkFood_TraderStock_Patch.xml: both append to the exotic traders' stockGenerators (patch order = file order).
    ("1.7/Patches/Restraints_TraderStock_Patch.xml", "restraints", lambda x: "Mugirl_BrainwashHelmet" in x),
    ("1.7/Patches/Restraints_ElectricSmelter_Patch.xml", "restraints", lambda x: "ElectricSmelter" in x),
]
patch_split = {"source": "1.7/Patches/MugirlPatch.xml", "module": "content", "reason": "C09 / content-plan S2-2: patch operations split by feature.", "targets": []}
covered = set()
for file, owner, pred in PATCH_TARGETS:
    sel = ops_where(pred)
    covered |= {i for i, _ in sel}
    patch_split["targets"].append({"file": file, "owner": owner, "header": governance(file), "operations": [i for i, _ in sel],
                                   "operationFingerprints": [x.split("[")[0] + "[" + x.split("[")[1].split("]")[0] + "]" if "[" in x else x for _, x in sel]})
if covered != {i for i, _, _ in ops}:
    raise SystemExit(f"MugirlPatch.xml operations not covered: {sorted({i for i,_,_ in ops} - covered)}")
XML_SPLITS.append(patch_split)

# Def -> (owner module, feature) after the splits
split_owner = {}
for s in XML_SPLITS:
    for t in s["targets"]:
        for dname in t.get("defs", []):
            if "feature" in t:
                split_owner[dname] = (t["owner"], t["feature"])

# Date-named content renames (decisions R2): contentFileMoves (DefInjected files of the same name follow automatically).
DATE_RENAMES = [m for m in merged["contentFileMoves"] if re.search(r"_09\d\d\.xml$", m["old"]) and "/Languages/" not in m["old"]]
content_moves = [{"old": m["old"], "new": m["new"], "module": m["modules"][0]} for m in DATE_RENAMES]
deferred_content_moves = [m for m in merged["contentFileMoves"] if m not in DATE_RENAMES and not ("/Languages/" in m["old"] and re.search(r"_09\d\d\.xml$", m["old"]))]

# =================================================================================================================
# 2. DefOf dissolution (owner-of-the-Def rule, C04)
# =================================================================================================================
FEATURE_DEFOF = {
    "Core": ("Mugirl.Core.MugirlDefOf", "Source/MugirlRace/Core/MugirlDefOf.cs", "core", "种族本体与内容共享目录（特性、生长阶段、污物等）中的雪牛娘 Def。"),
    "Rendering": ("Mugirl.Rendering.RenderingDefOf", "Source/MugirlRace/Rendering/RenderingDefOf.cs", "core", "渲染层使用的 Def：原生着色器材质与需要补刷图形的 DLC Hediff。"),
    "Features/Incidents": ("Mugirl.Features.Incidents.IncidentsDefOf", None, "incidents", None),
    "Features/Xenotype": ("Mugirl.Features.Xenotype.MugirlXenotypeDefOf", "Source/MugirlRace/Features/Xenotype/MugirlXenotypeDefOf.cs", "milk",
                          "异种功能的 Def（类名带 Mugirl：RimWorld.XenotypeDefOf 已存在）。"),
}
MODULE_OF_FEATURE = {"Corporate": "corporate", "Incidents": "incidents", "Social": "incidents", "Trade": "incidents", "LifeStages": "incidents",
                     "Grazing": "incidents", "Milk": "milk", "Abilities": "milk", "Xenotype": "milk", "Appearance": "milk", "AdvancedArmor": "milk",
                     "Restraints": "restraints", "Roping": "restraints", "Mounting": "combat", "Lances": "combat", "WeaponWheel": "combat", "Weapons": "combat"}
HEADERS = {
    "Corporate": "巨企功能的 Def。", "Social": "社交（蹭蹭、进食心情）功能的 Def。", "Trade": "奴隶交易的 Def。", "LifeStages": "成长阶段功能的 Def。",
    "Grazing": "放牧功能的 Def。", "Milk": "产奶、喂奶与奶制品功能的 Def。", "Abilities": "冲撞、投掷、扣篮等能力的 Def。",
    "Appearance": "外观（发型、服装、皮肤覆盖层）功能的 Def。", "AdvancedArmor": "神经战斗护甲功能的 Def。", "Restraints": "束具功能的 Def。",
    "Roping": "牵绳功能的 Def。", "Mounting": "骑乘功能的 Def。", "Lances": "骑枪冲锋功能的 Def。", "WeaponWheel": "武器轮盘功能的 Def。", "Weapons": "武器功能的 Def。",
}


def defof_for(feature):
    if feature in FEATURE_DEFOF:
        return FEATURE_DEFOF[feature]
    m = re.match(r"Features/(\w+)$", feature)
    if m:
        f = m.group(1)
        return (f"Mugirl.Features.{f}.{f}DefOf", f"Source/MugirlRace/Features/{f}/{f}DefOf.cs", MODULE_OF_FEATURE[f], HEADERS[f])
    m = re.match(r"Compatibility/(\w+)$", feature)
    if m:
        f = m.group(1)
        return (f"Mugirl.Compatibility.{f}.{f}DefOf", f"Source/MugirlRace/Compatibility/{f}/{f}DefOf.cs", "core", f"{f} 兼容代码读取的 Def。")
    raise SystemExit(f"no DefOf for feature {feature}")


cmm_target = {}
for mv in cmm["memberMoves"]:
    for mem in mv["members"]:
        cmm_target[post_r_member(mem)] = mv["to"]


def feature_of_class(full):
    if not full:
        return None
    if full.startswith("Mugirl.Core."):
        return "Core"
    if full.startswith("Mugirl.Rendering."):
        return "Rendering"
    m = re.match(r"Mugirl\.Features\.(\w+)\.", full)
    return f"Features/{m.group(1)}" if m else None


owned = [(p, s["module"]) for s in r_specs.values() for p in s.get("ownedContentFiles", [])]
moves_map = {m["old"]: m["new"] for m in merged["contentFileMoves"]}


def owner_of_content(rel):
    for cand in (rel, moves_map.get(rel, rel)):
        hits = sorted([(p, m) for p, m in owned if fnmatch.fnmatch(cand, p)], key=lambda x: -len(x[0]))
        if hits:
            return hits[0][1]
    return None


from ownership import OWNER_FEATURES  # module -> features whose DefOf may hold its defs (C04)


def milk_feature_by_file(files):
    f = " ".join(files)
    if re.search(r"/Apparel/|/HairDefs/", f):
        return "Features/Appearance"
    return "Features/Milk"


placements = []
for d in facts:
    full = f"{d['class']}.{d['field']}"
    if d["class"].endswith("TrainingFacilityDefOf") or (d["class"].endswith(".IncidentsDefOf") and not d["definedIn"]):
        continue  # unchanged DefOfs (core-member-moves unchangedDefOfs)
    readers = sorted(r for r in d["readers"] if r not in ("Tests", "DefOf", "PublicApi"))
    if not d["readers"]:
        placements.append((full, [], "delete", "no C# reader"))
        continue
    if d["defName"] in split_owner:
        mod, feature = split_owner[d["defName"]]
        placements.append((full, [defof_for(feature)[0]], "move", f"defined by {mod} after the R2 split ({feature})"))
        continue
    if d["definedIn"]:
        owners = sorted({o for o in (d["owner"] or [owner_of_content(x) for x in d["definedIn"]]) if o})
        if len(owners) != 1:
            raise SystemExit(f"{full}: owner unclear {owners} {d['definedIn']}")
        mod = owners[0]
        allowed = OWNER_FEATURES[mod]
        tf = feature_of_class(cmm_target.get(full))
        if tf in allowed:
            feature, why = tf, f"defined by {mod} ({', '.join(x.split('/')[-1] for x in d['definedIn'])})"
        elif len(allowed) == 1:
            feature, why = allowed[0], f"owner rule: defined by {mod}; core-member-moves had {cmm_target.get(full)}"
        elif mod == "milk":
            feature, why = milk_feature_by_file(d["definedIn"]), f"owner rule: defined by milk ({d['definedIn'][0].split('/')[-1]}); core-member-moves had {cmm_target.get(full)}"
        else:
            feature, why = allowed[0], f"owner rule: defined by {mod}; core-member-moves had {cmm_target.get(full)}"
        placements.append((full, [defof_for(feature)[0]], "move", why))
    else:
        feats = readers or ["Core"]
        targets = sorted({defof_for(f)[0] for f in feats})
        placements.append((full, targets, "copy" if len(targets) > 1 else "move", f"not a Mugirl def: declared by each reading feature ({', '.join(feats)})"))

create_types = {}
moves_by_target = defaultdict(list)
copies = []
deletes = []
for full, targets, kind, why in placements:
    decisions.append(f"{kind:6} {full:72} -> {', '.join(targets) or '-':56} {why}")
    if kind == "delete":
        deletes.append(full)
        continue
    for t in targets:
        for feat in list(FEATURE_DEFOF) + [f"Features/{x}" for x in MODULE_OF_FEATURE]:
            cls, file, module, header = defof_for(feat)
            if cls == t:
                if file and not (TREE / file).exists():
                    create_types[cls] = {"fullName": cls, "file": file, "kind": "defOf", "accessibility": "internal", "header": header, "module": module}
                break
    if kind == "copy":
        copies.append((full, targets))
    else:
        moves_by_target[targets[0]].append(full)

dissolve = sorted({d["class"] for d in facts if not (d["class"].endswith("TrainingFacilityDefOf") or d["class"].endswith(".IncidentsDefOf"))})
defof_spec = {
    "module": "core", "wave": "R2",
    "createTypes": sorted(create_types.values(), key=lambda x: x["fullName"]),
    "memberMoves": [{"to": t, "members": sorted(ms), "reason": "C04 owner-of-the-Def rule: the DefOf of the feature that defines the def; consumers reference it."}
                    for t, ms in sorted(moves_by_target.items())]
                   + [{"to": ts[0], "members": [f], "referenceRules": [{"pathPrefix": "Source/MugirlRace/" + feature_of_class(t) + "/", "to": t} for t in ts],
                       "reason": "C04 memberCopies rule: a vanilla/DLC def is declared by each feature that reads it."} for f, ts in copies],
    "memberDeletes": [{"members": sorted(deletes), "reason": "DefOf fields without a C# reader (design 3.1: no unused DefOf fields; existence checks belong to the validator's content contracts)."}],
    "dissolveTypes": [{"type": t, "reason": "R2 DefOf dissolution (C02/C03): every field moved to its feature DefOf or deleted."} for t in dissolve],
}

# =================================================================================================================
# 3. Cross-owner member moves / renames (C06, C19, C21; core-member-moves PlayerFactionUtility; decisions section 5)
# =================================================================================================================
member_spec = {
    "module": "core", "wave": "R2",
    "createTypes": [
        {"fullName": "Mugirl.Core.PlayerFactionUtility", "file": "Source/MugirlRace/Core/PlayerFactionUtility.cs", "kind": "staticClass", "accessibility": "internal",
         "header": "玩家派系判断；没有游戏或玩家派系时安全返回（Faction.OfPlayerSilentFail）。", "module": "core"},
        {"fullName": "Mugirl.Core.PawnPredicates", "file": "Source/MugirlRace/Core/PawnPredicates.cs", "kind": "staticClass", "accessibility": "internal",
         "header": "跨功能共用的小人状态判断（类人、能力、清醒）。", "module": "core"},
        {"fullName": "Mugirl.Features.Mounting.MountUtility", "file": "Source/MugirlRace/Features/Mounting/MountUtility.cs", "kind": "staticClass", "accessibility": "internal",
         "header": "骑乘关系查询（坐骑组件、骑手所在坐骑、是否在骑乘）与骑乘绘制距离。", "module": "combat"},
        {"fullName": "Mugirl.Features.Incidents.EventPawnRequestUtility", "file": "Source/MugirlRace/Features/Incidents/Shared/EventPawnRequestUtility.cs", "kind": "staticClass",
         "accessibility": "internal", "header": "事件小人的生成请求（无派系成人、访客）与奴隶背景判断。", "module": "incidents"},
    ],
    "memberMoves": [
        {"to": "Mugirl.Core.PlayerFactionUtility", "accessibility": "internal", "order": "spec",
         "members": ["Mugirl.Features.Incidents.WildMugirlUtility.PlayerFaction", "Mugirl.Features.Incidents.WildMugirlUtility.IsPlayerFaction",
                     "Mugirl.Features.Incidents.WildMugirlUtility.IsHostileToPlayer(RimWorld.Faction)", "Mugirl.Features.Incidents.WildMugirlUtility.IsHostileToPlayer(Verse.Pawn)",
                     "Mugirl.Features.Incidents.WildMugirlUtility.TrySetPlayerFaction"],
         "reason": "core-member-moves PlayerFactionUtility: player-faction helpers used by Corporate, Abilities, Incidents and Restraints live in Core."},
        {"to": "Mugirl.Features.Mounting.CompMount@Source/MugirlRace/Features/Mounting/Comp/CompMount.Ticking.cs", "accessibility": "private",
         "members": ["Mugirl.Core.TickUtility.Add", "Mugirl.Core.TickUtility.ConsumeReady"],
         "reason": "C06: the only caller is CompMount ticking; Core must not host one feature's counter contract."},
        {"to": "Mugirl.Features.Incidents.EventPawnRequestUtility", "accessibility": "internal",
         "members": ["Mugirl.Core.GeneratedPawnUtility.CreateFactionlessAdultRequest", "Mugirl.Core.GeneratedPawnUtility.CreateVisitorRequest",
                     "Mugirl.Core.GeneratedPawnUtility.HasMugirlSlaveBackstories", "Mugirl.Core.GeneratedPawnUtility.BackstoryHasCategory"],
         "reason": "C19: event-only pawn builders leave the core-owned GeneratedPawnUtility (internal class: members internal)."},
        {"to": "Mugirl.Features.Incidents.EventPawnRequestUtility",
         "members": ["Mugirl.Core.GeneratedPawnUtility.MugirlSlaveBackstoryCategory"],
         "reason": "C19: the backstory category constant moves with its reader and stays private."},
        {"to": "Mugirl.Core.PawnPredicates", "accessibility": "internal",
         "members": ["Mugirl.MountedPawnUtility.IsHumanlike", "Mugirl.MountedPawnUtility.HasCapacity", "Mugirl.MountedPawnUtility.IsAwake"],
         "reason": "C21: generic pawn predicates get a Core home (combat-plan S4.1)."},
        {"to": "Mugirl.Features.Mounting.MountUtility", "accessibility": "internal",
         "members": ["Mugirl.MountedPawnUtility.GetMountComp", "Mugirl.MountedPawnUtility.GetMountForRider", "Mugirl.MountedPawnUtility.IsMounted",
                     "Mugirl.MountedPawnUtility.EquipmentDrawDistanceFactor"],
         "reason": "Decisions section 5: Mugirl.MountedPawnUtility keeps only the public extension point IsMugirl (PublicApi); mount queries move to the feature."},
    ],
    "memberRenames": [{"member": "Mugirl.Core.MugirlSettings." + r["member"].rsplit(".", 1)[1], "new": r["new"],
                       "reason": "core-member-moves memberRenames: C# symbol renames whose readers are in incidents/corporate; the Scribe label strings stay (SettingsUpgrade in S)."}
                      for r in cmm["memberRenames"]["renames"]],
}

# =================================================================================================================
# 4. PatchModule closed enum (design 3.2, C25: no 'Apparel' member)
# =================================================================================================================
RULES = [
    ("Source/MugirlRace/Core/VanillaFixes/", "VanillaFixes"), ("Source/MugirlRace/Core/", "Core"), ("Source/MugirlRace/Rendering/", "Rendering"),
    ("Source/MugirlRace/SaveUpgrade/", "SaveUpgrade"), ("Source/MugirlRace/Compatibility/Savegames/", "SaveUpgrade"),
    ("Source/MugirlRace/Compatibility/", "{folder}"), ("Source/MugirlRace/Features/", "{folder}"),
    ("Source/MugirlRace/PublicApi/", "Core"), ("Source/Tests/", "Core"),
]
used = set()
for f in walk_files(TREE / "Source", (".cs",)):
    rel = str(f.relative_to(TREE)).replace("\\", "/")
    if "[MugirlPatch(" not in f.read_text(encoding="utf-8-sig"):
        continue
    for prefix, val in RULES:
        if rel.startswith(prefix):
            v = rel[len(prefix):].split("/")[0] if val == "{folder}" else val
            if not re.match(r"^[A-Z]\w*$", v):
                raise SystemExit(f"PatchModule value '{v}' from {rel}")
            used.add(v)
            break
    else:
        raise SystemExit(f"no PatchModule rule for {rel}")
compat = sorted(v for v in used if (MAIN / "Compatibility" / v).exists())
features = sorted(v for v in used if (MAIN / "Features" / v).exists())
members = ["Core", "VanillaFixes", "Rendering", "SaveUpgrade"] + features + compat
patch_spec = {
    "module": "core", "wave": "R2",
    "createTypes": [{"fullName": "Mugirl.Core.PatchModule", "file": "Source/MugirlRace/Core/Patching/PatchModule.cs", "kind": "enum", "accessibility": "public",
                     "header": "Harmony 补丁所属模块的封闭集合：Core、原版修复、渲染、存档升级，以及 Features/ 与 Compatibility/ 下的文件夹名。\n新增功能或兼容文件夹时在这里加成员。",
                     "members": members, "module": "core", "linkWith": ["Source/MugirlRace/Core/Patching/MugirlPatchAttribute.cs"]}],
    "attributeArgumentRewrites": [{"attribute": "Mugirl.Core.MugirlPatchAttribute", "argument": 0, "valueFormat": "PatchModule.{0}",
                                   "rules": [{"pathPrefix": p, "value": v} for p, v in RULES], "requireNamespace": "Mugirl.Core",
                                   "reason": "Design 3.2: the module of [MugirlPatch] is a member of a closed set, derived from the class's folder."}],
    "textEdits": [
        {"file": "Source/MugirlRace/Core/Patching/MugirlPatchAttribute.cs", "old": "(string module, PatchRisk risk, string failureBehavior)",
         "new": "(PatchModule module, PatchRisk risk, string failureBehavior)", "reason": "PatchModule enum replaces the free string."},
        {"file": "Source/MugirlRace/Core/Patching/MugirlPatchAttribute.cs", "old": "public string Module { get; } = module;",
         "new": "public PatchModule Module { get; } = module;", "reason": "PatchModule enum replaces the free string."},
        {"file": "Source/MugirlRace/Core/Patching/MugirlPatchAttribute.cs", "old": "<param name=\"module\">功能模块名，例如 <c>Milk</c>、<c>WeaponWheel</c>、<c>YayoCombatCompat</c>。</param>",
         "new": "<param name=\"module\">补丁所属模块（<see cref=\"PatchModule\"/>，与补丁类所在的功能或兼容文件夹一致）。</param>", "reason": "PatchModule enum replaces the free string."},
        # dissolved Rendering DefOf: ShaderCheck reads the DefOf file by path, and two XML comments name the class
        {"file": "Source/Tests/MugirlRace.ShaderCheck/CheckPaths.cs", "old": "\"MugirlRenderDefOf.cs\"", "new": "\"RenderingDefOf.cs\"", "reason": "R2 DefOf split: Rendering/RenderingDefOf.cs."},
        {"file": "Source/Tests/MugirlRace.ShaderCheck/CSharpReferenceChecks.cs", "old": "<c>Mugirl.Rendering.MugirlRenderDefOf</c>", "new": "<c>Mugirl.Rendering.RenderingDefOf</c>", "reason": "R2 DefOf split."},
        {"file": "Source/Tests/MugirlRace.ShaderCheck/CSharpReferenceChecks.cs", "old": "{file}: MugirlRenderDefOf.{name}", "new": "{file}: RenderingDefOf.{name}", "reason": "R2 DefOf split."},
        {"file": "Source/Tests/MugirlRace.ShaderCheck/CSharpReferenceChecks.cs", "old": "$\"MugirlRenderDefOf: {fields}", "new": "$\"RenderingDefOf: {fields}", "reason": "R2 DefOf split."},
    ],
}
# Comments the moves make false (C31 style: comments state the current reason). Each old text must be in the post-R tree.
COMMENT_EDITS = [
    ("Source/MugirlRace/Features/Incidents/Shared/IncidentsDefOf.cs",
     "// 本模块用到、但原版 DefOf 没有收录的 Core Def；全部来自原版 Core，无条件绑定。",
     "// 事件功能（快递、逃跑牧场、惩戒者等）定义的 Def，以及本模块用到、原版 DefOf 没有收录的原版 Core Def。"),
    ("Source/MugirlRace/PublicApi/MountedPawnUtility.cs",
     "// 骑乘关系查询与骑手状态判断。其他模块（企业、基因、长枪、武器轮盘等）通过这里查询骑乘状态；\n"
     "// tick、下骑落点、Gizmo、绘制偏移已分别移到 RiderTickDriver、DismountCellFinder、MountedRiderGizmos、MountDrawOffsets。\n",
     "// 公共 API：其他 Mod（如白绒物语）用 Harmony 补丁 IsMugirl 扩展雪牛娘身份判断；类名、方法名与参数名 pawn 都不能改。\n"),
    ("Source/MugirlRace/PublicApi/MountedPawnUtility.cs", "using Verse.AI;\n", ""),
]
for file, old, new in COMMENT_EDITS:
    text = (TREE / file).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    if text.count(old) != 1:
        raise SystemExit(f"comment edit: {old!r} occurs {text.count(old)} times in {file} (post-R tree changed? update COMMENT_EDITS)")
    patch_spec["textEdits"].append({"file": file, "old": old, "new": new, "reason": "R2 moves make the old comment false (members/defs moved in or out)."})
for xml in ("1.7/Defs/MaterialDefs/Mugirl_Materials.xml", "1.7/Defs/ShaderDefs/Mugirl_Shaders.xml"):
    if "Mugirl.Rendering.MugirlRenderDefOf" in (TREE / xml).read_text(encoding="utf-8-sig"):
        patch_spec["textEdits"].append({"file": xml, "old": "Mugirl.Rendering.MugirlRenderDefOf", "new": "Mugirl.Rendering.RenderingDefOf", "reason": "R2 DefOf split (XML comment)."})

# =================================================================================================================
# 5. Keyed split (content-keyed-split.json; dead keys go to the file of their prefix and are deleted by its owner in S)
# =================================================================================================================
split_map = dict(keyed_split["miscGameplaySplit"])
prefix_target = {}
for k, f in split_map.items():
    prefix_target.setdefault(".".join(k.split(".")[:2]), f)
dead = [d["key"] for d in keyed_split["deleteKeys"] if d["file"] == "Misc_Gameplay.xml"]
dead_targets = {}
for k in dead:
    p2 = ".".join(k.split(".")[:2])
    t = prefix_target.get(p2) or {"Mugirl.CrackBondageGear_NoCrackableType": "Restraints.xml", "Mugirl.RopedByPawn": "Roping.xml", "Mugirl.RopedToHitchingPost": "Roping.xml",
                                  "Mugirl.RopedToSpot": "Roping.xml", "Mugirl.FloatMenu": "Restraints.xml", "Mugirl.FeralChild": "Incidents.xml", "Mugirl.Child": "Incidents.xml",
                                  "Mugirl.Person": "Incidents.xml", "Mugirl.PatchRegistry": "Core.xml"}.get(p2) or {"Mugirl.CrackBondageGear_NoCrackableType": "Restraints.xml"}.get(k, "Core.xml")
    dead_targets[k] = t
keyed_spec = {"module": "content", "wave": "R2", "keyedSplits": [{
    "source": "Misc_Gameplay.xml", "contentRoot": "1.7", "keys": {**split_map, **dead_targets},
    "reason": "C09 / content-plan S2-3: Keyed/Misc_Gameplay.xml split by the post-move area of each key's users; the 19 dead keys go to the file of their "
              "prefix and are deleted there by its Wave S owner (deletions are not R2)."}]}

xml_spec = {"module": "content", "wave": "R2",
            "xmlSplits": [{"source": s["source"], "module": s["module"], "reason": s["reason"],
                           "targets": [{k: v for k, v in t.items() if k in ("file", "defs", "operations", "operationFingerprints", "owner", "feature", "header")} for t in s["targets"]]}
                          for s in XML_SPLITS],
            "contentFileMoves": content_moves}

# OWNER 2026-10-15: player text and behaviour stay unchanged. The engine does
# not sort XML files; physical splits/moves alter Def order and RNG consumers.
# Keep the per-Def ownership calculation above for DefOf placement, but retain
# loaded files and their element order. One content owner coordinates edits to
# these shared files after R2; feature owners request changes rather than race.
retained = {s["source"] for s in XML_SPLITS}
retained.update(move["old"] for move in content_moves)
xml_spec["logicalContentOwnership"] = xml_spec["xmlSplits"]
xml_spec["preservedSharedFiles"] = [
    {"file": path, "owner": "content", "reason": "OWNER behaviour parity: preserve loaded XML path and element order"}
    for path in sorted(retained)
]
xml_spec["xmlSplits"] = []
xml_spec["contentFileMoves"] = []
keyed_spec["keyedSplits"] = []
keyed_spec["preservedSharedFiles"] = [
    {"file": "1.7/Languages/*/Keyed/Misc_Gameplay.xml", "owner": "content", "reason": "OWNER text parity: retain original shared translations"}
]

# Validator negative control: it mimics the mod's DefOf owner of MugirlBody; the architecture rule now names Mugirl.Core.MugirlDefOf.
NL = "\n"
CONTROL_DEFOF = NL.join([
    "namespace Mugirl.Core;",
    "",
    "/// <summary>B.architecture mugirl-identity：与正式 Mod 同名的 DefOf 字段在 MugirlIdentity 之外被读取。</summary>",
    "[DefOf]",
    "public static class MugirlDefOf",
    "{",
    "    public static BodyDef MugirlBody;",
    "",
    "    static MugirlDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MugirlDefOf));",
    "}",
    ""])
OLD_CONTROL = NL.join([
    "/// <summary>B.architecture mugirl-identity：与正式 Mod 同名的 DefOf 字段在 MugirlIdentity 之外被读取。</summary>",
    "[DefOf]",
    "public static class Mugirl_DefOf",
    "{",
    "    public static BodyDef MugirlBody;",
    "",
    "    static Mugirl_DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(Mugirl_DefOf));",
    "}",
    "",
    ""])
controls_spec = {"module": "validator", "wave": "R2",
    "createFiles": [{"path": "Source/Tests/MugirlRace.Validation/Controls/NegativeControl/MugirlDefOf.cs", "content": CONTROL_DEFOF,
                     "reason": "The negative control mimics the DefOf that owns MugirlBody after the R2 split."}],
    "textEdits": [
        {"file": "Source/Tests/MugirlRace.Validation/Controls/NegativeControl/DataViolations.cs", "old": OLD_CONTROL, "new": "",
         "reason": "moved to Controls/NegativeControl/MugirlDefOf.cs (namespace Mugirl.Core)"},
        {"file": "Source/Tests/MugirlRace.Validation/Controls/NegativeControl/DataViolations.cs",
         "old": "pawn.RaceProps.body == Mugirl_DefOf.MugirlBody", "new": "pawn.RaceProps.body == Core.MugirlDefOf.MugirlBody",
         "reason": "the control reads the moved DefOf field"},
        {"file": "Source/Tests/MugirlRace.Validation/Controls/expected-failures.txt",
         "old": "B.architecture | mugirl-identity: Mugirl.Mugirl_DefOf::MugirlBody @ Mugirl.ControlLookups",
         "new": "B.architecture | mugirl-identity: Mugirl.Core.MugirlDefOf::MugirlBody @ Mugirl.ControlLookups",
         "reason": "architecture-rules.json names the moved field"},
    ]}

# Validator debt paid by the DefOf dissolution: "root: <DefOf class>" lines of structure-debt.allow.txt (see gen_R.py).
DEBT = "Source/Tests/MugirlRace.Validation/Baselines/structure-debt.allow.txt"
if (TREE / DEBT).exists():
    present = {l.split("\t")[0].strip() for l in (TREE / DEBT).read_text(encoding="utf-8-sig").splitlines()}
    keys = [f"root: {t}" for t in dissolve if t.count(".") == 1 and f"root: {t}" in present]
    if keys:
        defof_spec["baselineRemovals"] = [{"file": DEBT, "keys": keys, "reason": "B.namespaces debt paid by the R2 DefOf dissolution."}]

out = FINAL / "R2"
out.mkdir(parents=True, exist_ok=True)
for old in out.glob("*.json"):
    old.unlink()
save_json(out / "defof.json", defof_spec)
save_json(out / "members.json", member_spec)
save_json(out / "patchmodule.json", patch_spec)
save_json(out / "content-splits.json", xml_spec)
save_json(out / "keyed-split.json", keyed_spec)
save_json(out / "controls.json", controls_spec)
Path(GEN / "r2-defof-decisions.txt").write_text("\n".join(decisions) + "\n", encoding="utf-8")
save_json(GEN / "r2-deferred-content-moves.json", deferred_content_moves)
print(f"R2: defof moves {sum(len(v) for v in moves_by_target.values())}, copies {len(copies)}, deletes {len(deletes)}, created DefOfs {len(create_types)}, "
      f"dissolve {len(dissolve)}; PatchModule members {members}; loaded XML splits/moves 0; retained shared files {len(retained)}")
