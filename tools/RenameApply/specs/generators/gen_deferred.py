"""spec/final/deferred-S.json (module-spec items that are NOT identity-preserving renames, routed to Wave S) and
spec/final/upgrade-hand-rows.json (hand dispositions the upgrader tables need besides the applied logs; MooGirl era excluded, D3)."""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import GEN, FINAL, SPEC, load_json, save_json

merged = load_json(SPEC / "merged.json")
keyed_split = load_json(SPEC / "content-keyed-split.json")
r_specs = {f.stem: load_json(f) for f in sorted((FINAL / "R").glob("*.json"))}
deferred_moves = load_json(GEN / "r2-deferred-content-moves.json")

transitional = []
for mod, s in r_specs.items():
    for t in s.get("types", []):
        if isinstance(t.get("transitional"), dict):
            transitional.append({"module": mod, "type": t["new"], "legacyName": t["old"], **t["transitional"], "reason": t.get("reason")})

deferred = {
    "_doc": "Items of the module specs that Wave R/R2 deliberately do NOT apply (F section 2.1, F16, F8, decisions section 3). Each Wave S owner "
            "applies its rows; keyed renames need a saved-value map entry when the key is stored as data (A.6.3) and a release-notes line.",
    "keyedRenames": [{"module": k["module"], "old": k["old"], "new": k["new"]} for k in merged["keyed"]],
    "keyedDeletes": [{"key": d["key"], "file": d["file"], "note": "no usage site (content-keyed-split.json); Misc_Gameplay keys were moved by R2 to the file of their prefix"}
                     for d in keyed_split["deleteKeys"]],
    "contentFileRenames": [{"module": m["modules"][0], "old": m["old"], "new": m["new"],
                            "note": "XML file rename that is not date-named (F8: per module in Wave S; check load order and C.content-shadowing)"} for m in deferred_moves],
    "transitionalTypes": sorted(transitional, key=lambda x: (x["module"], x["type"])),
}
save_json(FINAL / "deferred-S.json", deferred)

hand = {
    "_doc": "Hand dispositions for the upgrader tables (K1 manifest) that the rename-applied logs cannot express. MooGirl-era rows are excluded "
            "(D3: MooGirl era not supported; see moogirl-era-markers.json). Transitional types: their Wave R target is temporary; the upgrade "
            "column of R/<module>.json#types[].transitional is authoritative for the legacy (old) name.",
    "types": [h for h in merged["handTypeDispositions"] if h["old"].startswith("Mugirl.")],
    "defs": [h for h in merged["handDefDispositions"] if not re.match(r"^(MooGirl|MuGirl)", h["old"])],
    "retiredDefs": merged["legacyDefs"],
}
# Owner module of each hand row (K1 rows carry a module; the Wave S owner maintains the row).
HAND_TYPE_MODULE = {"Mugirl.ChoiceLetter_MugirlFusionInvestment": "incidents", "Mugirl.Mugirl_XenotypeFix_GameComp": "milk",
                    "Mugirl.BrainWashSlaveApparel": "restraints", "Mugirl.JobDriver_UseItemOn": "restraints", "Mugirl.MapRopingIndex": "restraints",
                    "Mugirl.CorporateRuntimeValidation": "corporate", "Mugirl.CorporateVisualValidation": "corporate",
                    "Mugirl.StockGenerator_Mugirl_Slaves": "incidents", "Mugirl.CompProperties_MugirlMount": "combat",
                    "Mugirl.CompProperties_MooMilkable": "milk"}
HAND_DEF_MODULE = {"Mugirl_CourierRaid": "incidents", "Mugirl_WildManWandersIn": "core", "Mugirl_NeuralCombatShoulders": "core",
                   "Make_Mugirl_NeuralCombatShoulders": "core", "Mugirl_EscapeWildSlave": "incidents", "Mugirl_PreEscapeWildSlave": "incidents",
                   "GiveSlaveApperalGear": "restraints"}
RETIRED_OWNER = {"content": "content", "AdvancedArmor/core (reference only)": "core", "Weapons (reference only)": "combat",
                 "content (file owner); C# types by Restraints": "restraints", "Restraints (file owner)": "restraints",
                 "Thoughts_Traits.xml owner": "restraints"}
for h in hand["types"] + hand["defs"]:
    h.pop("conflict", None)
for h in hand["types"]:
    h["module"] = HAND_TYPE_MODULE[h["old"]]
for h in hand["defs"]:
    h["module"] = HAND_DEF_MODULE[h["old"]]
for r in hand["retiredDefs"]:
    r["module"] = RETIRED_OWNER[r["owner"]]
# Settings file (decisions 2 "Settings"; K1 settings.fileClass): R moves the settings class to Mugirl.Core.
hand["settings"] = {"fileClass": {"old": "Mugirl.MugirlSettings", "new": "Mugirl.Core.MugirlSettings"}, "labels": [], "module": "core",
                    "source": "R/core.json types (Mugirl.MugirlSettings -> Mugirl.Core.MugirlSettings); the Class attribute of every "
                              "Config/Mod_<folder>_MugirlMod.xml names the old class",
                    "lands": "with Wave R (the settings upgrade must map the old Class value from the first build that has the renamed class)"}
save_json(FINAL / "upgrade-hand-rows.json", hand)
print(f"deferred: keyed renames {len(deferred['keyedRenames'])}, keyed deletes {len(deferred['keyedDeletes'])}, content renames {len(deferred['contentFileRenames'])}, "
      f"transitional types {len(transitional)}; hand rows: types {len(hand['types'])}, defs {len(hand['defs'])}, retired {len(hand['retiredDefs'])}")
