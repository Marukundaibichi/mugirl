"""Wave S file ownership of the post-R2 tree (decisions.md section 3: "After R2 every file has exactly one Wave S owner").

One rule engine for the generator (gen_ownership.py -> spec/final/ownership-postR2.json) and the spec linter (L16).
A file's candidate owners come from, in order of authority:
  1. HAND: explicit rows (path or glob) with a reason - shared files the module specs did not claim (C09-C11, C24);
  2. SPLITS: the owner of each R2 xmlSplits target and its DefInjected files (same stem, every language);
  3. CONTENT: module specs' ownedContentFiles, planned Wave S names mapped back to the post-R2 name of the same file;
  4. SOURCE: ownedSourceFolders globs (most specific pattern wins) plus the Wave A infrastructure owners;
  5. FOLLOW: DefInjected files follow the Defs file of the same stem (same content root) or the owner of the defs they
     translate; a Keyed file in one language follows the same file in another language.
The first level that yields owners decides; a level that yields two different modules is a conflict (error).
"""
import fnmatch
import re
from collections import defaultdict
from pathlib import Path

from common import FINAL, SPEC, load_json, parse_xml, walk_files

# C04 owner-of-the-Def rule: a DefOf field lives in the DefOf of a feature its defining file's owner owns.
OWNER_FEATURES = {
    "core": ["Core", "Rendering"], "content": ["Core"], "corporate": ["Features/Corporate"],
    "incidents": ["Features/Incidents", "Features/Social", "Features/Trade", "Features/LifeStages", "Features/Grazing"],
    "milk": ["Features/Milk", "Features/Abilities", "Features/Xenotype", "Features/Appearance", "Features/AdvancedArmor"],
    "restraints": ["Features/Restraints", "Features/Roping"],
    "combat": ["Features/Mounting", "Features/Lances", "Features/WeaponWheel", "Features/Weapons"],
}

MODULES = ["core", "content", "corporate", "incidents", "milk", "restraints", "combat", "upgrader", "validator", "gamevalidation"]
SAVE_UPGRADE_MODULES = {"Core": "core", "Content": "content", "Corporate": "corporate", "Incidents": "incidents", "Milk": "milk",
                        "Restraints": "restraints", "Combat": "combat"}

# Shared or unclaimed files after R2 (path globs relative to the mod root) -> owner, with the decision that assigns them.
HAND = [
    ("1.7/Defs/ThingDefs_Races/Mugirl_Race.xml", "content", "C11: race-wide file"),
    ("1.7/Defs/Bodies/BodyDef.xml", "content", "C11: race-wide file"),
    ("1.7/Defs/BackStoryDefs/BackstoryDef.xml", "content", "C11: race-wide file"),
    ("1.7/Defs/ThinkTreeDefs/Mugirl_ThinkTreeDefs.xml", "content", "C11: race-wide file"),
    ("1.7/Defs/RecipeDefs/Recipes_Mugirl.xml", "restraints", "every recipe in it makes or melts restraints (A3: not split)"),
    ("1.7/Defs/Misc/Corporate*.xml", "corporate", "corporate configuration defs (CorporateTradeSettings etc.)"),
    ("1.7/Defs/TraitDefs/TraitDefs.xml", "content", "race trait file after the R2 split (Mugirl_SingleshotWeaponAversion)"),
    ("1.7/Defs/SoundsDefs/Bondage_Sounds.xml", "restraints", "restraint sounds after the R2 split (milking sounds left)"),
    ("1.7/Defs/Apparel/Apparel_Bondage/BondageApparel_Spacer.xml", "restraints", "restraint file after the R2 split (milking devices left)"),
    ("1.7/Languages/*/Strings/**", "content", "race name lists"),
    ("1.7/Languages/*/Keyed/SaveUpgrade.xml", "upgrader", "shared save-upgrade notices"),
    ("1.7/Shaders/**", "core", "rendering shaders (core Rendering)"),
    ("1.7/Textures/Things/Apparel/**", "milk", "1.7-only apparel masks of Appearance apparel defs (Apparel_Shell/SpecializedGear/ThemedOutfits)"),
    ("1.7/Textures/Things/Item/Equipment/MugirlWeapon/**", "combat", "1.7-only lance masks (MeleeWeapons_Lances.xml)"),
    ("1.7/Textures/UI/Mugirl_MilkGauge*", "milk", "milk gauge texture (Features/Milk/Gauge)"),
    ("1.7/Assemblies/**", "core", "build output (A4 deploy target)"),
]


def _literal_len(pattern):
    m = re.search(r"[*?\[]", pattern)
    return len(pattern) if m is None else m.start()


def _match(pattern, rel):
    if pattern.endswith("/**"):
        return rel.startswith(pattern[:-2])
    if "**" in pattern:
        rx = "^" + re.escape(pattern).replace(r"\*\*/", "(?:.*/)?").replace(r"\*\*", ".*").replace(r"\*", "[^/]*").replace(r"\?", "[^/]") + "$"
        return re.match(rx, rel) is not None
    return fnmatch.fnmatchcase(rel, pattern) and rel.count("/") == pattern.count("/")


def tree_files(tree):
    out = []
    for root in ("Source", "1.7"):
        for f in walk_files(Path(tree) / root, ("",), skip_dirs=("obj", "bin", ".git", ".vs")):
            out.append(str(f.relative_to(tree)).replace("\\", "/"))
    return sorted(out)


def content_root(rel):
    m = re.match(r"^(1\.7(?:/Mods/[^/]+)?)/", rel)
    return m.group(1) if m else None


class Ownership:
    def __init__(self, tree, r_specs, r2_specs, r2_applied):
        self.tree = Path(tree)
        self.files = tree_files(tree)
        self.fileset = set(self.files)
        self.r_specs, self.r2_specs = r_specs, r2_specs
        merged = load_json(SPEC / "merged.json")
        # planned Wave S names -> current names (R2 renamed some of them already: its applied 'files' map old -> new)
        r2_moves = {m["old"]: m["new"] for m in (r2_applied.get("files") or [])} if r2_applied else {}
        planned_to_old = {m["new"]: m["old"] for m in merged["contentFileMoves"]}
        self.content = []          # (path, module, origin)
        self.stale = []
        for s in r_specs.values():
            for p in s.get("ownedContentFiles", []):
                if "*" in p:
                    self.content.append((p, s["module"], "ownedContentFiles"))
                    continue
                cands = [p, r2_moves.get(p), planned_to_old.get(p), r2_moves.get(planned_to_old.get(p, ""), None)]
                hit = next((c for c in cands if c and c in self.fileset), None)
                if hit:
                    self.content.append((hit, s["module"], "ownedContentFiles" + ("" if hit == p else f" (planned {p})")))
                else:
                    self.stale.append({"module": s["module"], "path": p, "note": "planned Wave S name; no post-R2 file maps to it"})
        self.splits = []           # (path, module)
        self.preserved = []        # OWNER behaviour parity: physically unchanged shared content
        for s in r2_specs.values():
            for retained in s.get("preservedSharedFiles", []):
                path = retained["file"]
                self.preserved.append((path, retained["owner"]))
                if "/Defs/" in path:
                    self.preserved.append((content_root(path) + "/Languages/*/DefInjected/*/" + Path(path).name, retained["owner"]))
            for x in s.get("xmlSplits", []):
                for t in x["targets"]:
                    if t.get("owner"):
                        self.splits.append((t["file"], t["owner"]))
        self.source = []
        for s in r_specs.values():
            for p in s.get("ownedSourceFolders", []):
                self.source.append((p, s["module"]))
        self.source += [
            ("Source/*", "core"), ("Source/Facades/**", "core"), ("Source/Build/**", "core"),
            ("Source/MugirlRace/*.props", "core"), ("Source/MugirlRace/TypeOrder.txt", "core"),
            ("Source/MugirlRace/SaveUpgrade/**", "upgrader"),
            ("Source/Tests/MugirlRace.Tests/**", "core"),
            ("Source/Tests/MugirlRace.Tests/SaveUpgrade/**", "upgrader"),
            ("Source/Tests/MugirlRace.Validation/**", "validator"),
            ("Source/Tests/MugirlRace.GameValidation/**", "gamevalidation"),
        ]
        for folder, mod in SAVE_UPGRADE_MODULES.items():
            self.source.append((f"Source/MugirlRace/SaveUpgrade/Modules/{folder}/**", mod))
            self.source.append((f"Source/Tests/MugirlRace.Tests/SaveUpgrade/Modules/{folder}/**", mod))
            self.source.append((f"Source/Tests/MugirlRace.Tests/SaveUpgrade/Golden/{folder}.*/**", mod))
        self._defs_owner_cache = None

    # ---------------------------------------------------------------------------------------------------- levels
    def _hand(self, rel):
        return {(m, f"hand: {why}") for p, m, why in HAND if _match(p, rel)}

    def _preserved(self, rel):
        return {(m, "OWNER behaviour parity: retained shared content") for p, m in self.preserved if _match(p, rel)}

    def _split(self, rel):
        out = set()
        for p, m in self.splits:
            if rel == p:
                out.add((m, "R2 split target"))
            elif "/DefInjected/" in rel and Path(rel).name == Path(p).name and content_root(rel) == content_root(p):
                out.add((m, "DefInjected of an R2 split target"))
        return out

    def _content(self, rel):
        return {(m, why) for p, m, why in self.content if (p == rel or ("*" in p and _match(p, rel)))}

    def _source(self, rel):
        best, out = -1, set()
        for p, m in self.source:
            if _match(p, rel):
                n = _literal_len(p)
                if n > best:
                    best, out = n, {(m, f"source glob {p}")}
                elif n == best:
                    out.add((m, f"source glob {p}"))
        return out

    def _defs_owner(self):
        """defName -> owner of its defining Defs file (levels 1-4 only)."""
        if self._defs_owner_cache is None:
            self._defs_owner_cache = {}
            for rel in self.files:
                if not rel.endswith(".xml") or "/Defs/" not in rel:
                    continue
                owners = self.direct(rel)
                if len({m for m, _ in owners}) != 1:
                    continue
                root = parse_xml(self.tree / rel)
                if root is None:
                    continue
                for el in root.children:
                    for c in el.children:
                        if c.tag == "defName" and c.text.strip():
                            self._defs_owner_cache[c.text.strip()] = next(iter(owners))[0]
        return self._defs_owner_cache

    def _follow(self, rel):
        if "/Languages/" in rel and "/DefInjected/" in rel:
            cr = content_root(rel)
            stem = Path(rel).name
            same = [f for f in self.files if f.startswith(cr + "/Defs/") and Path(f).name == stem]
            mods = {m for f in same for m, _ in self.direct(f)}
            if len(mods) == 1:
                return {(mods.pop(), f"DefInjected follows {same[0]}")}
            root = parse_xml(self.tree / rel)
            if root is not None:
                owners = self._defs_owner()
                mods = {owners.get(c.tag.split(".")[0]) for c in root.children} - {None}
                if mods:
                    return {(m, "DefInjected follows the owner of the defs it translates") for m in mods}
        m = re.match(r"^(.*?/Languages/)[^/]+(/Keyed/[^/]+)$", rel)
        if m:
            out = set()
            for f in self.files:
                mm = re.match(r"^(.*?/Languages/)[^/]+(/Keyed/[^/]+)$", f)
                if f != rel and mm and mm.group(1) == m.group(1) and mm.group(2) == m.group(2):
                    out |= {(o, f"Keyed follows {f}") for o, _ in self.direct(f)}
            return out
        return set()

    def direct(self, rel, include_preserved=True):
        levels = (self._hand, self._split, self._content, self._source)
        if include_preserved:
            levels = (self._preserved,) + levels
        for level in levels:
            got = level(rel)
            if got:
                return got
        return set()

    def owners(self, rel):
        got = self.direct(rel)
        return got if got else self._follow(rel)

    def resolve(self):
        files, problems = {}, []
        for rel in self.files:
            got = self.owners(rel)
            mods = sorted({m for m, _ in got})
            if len(mods) == 1:
                files[rel] = {"module": mods[0], "by": sorted(w for _, w in got)[0]}
            elif not mods:
                problems.append({"file": rel, "problem": "no owner"})
            else:
                problems.append({"file": rel, "problem": "several owners: " + "; ".join(sorted(f"{m} ({w})" for m, w in got))})
        return files, problems


def load_specs(final=FINAL):
    r = {f.stem: load_json(f) for f in sorted((final / "R").glob("*.json"))}
    r2 = {f.stem: load_json(f) for f in sorted((final / "R2").glob("*.json"))} if (final / "R2").exists() else {}
    return r, r2
