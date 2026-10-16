"""Scan every installed mod that depends on HAR.MugirlRace for names it takes from Mugirl Race.

Output: <spec root>/generators/public-api.partial.json and dependents-raw.json.
Read-only on every mod folder. Classification: a use is 'production' when it is part of what the dependent ships and loads
(Defs, Patches, Languages, load folders, About, C# compiled into the shipped assembly) and 'validation' when it only lives in
test/validation code or tooling (WhiteFluff Source/Validation/** - compiled only with EnableValidation=true - and Tools/**).
"""
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import (GEN, REPO, MODS, WORKSHOP, FINAL, SPEC, parse_xml, walk_files, save_json, def_root)

# ---------------------------------------------------------------------------------------------------------------
# Universe of Mugirl names (1.7 tree + frozen 1.6 trees; dependents were written against the 1.6 build)
# ---------------------------------------------------------------------------------------------------------------
CONTENT_ROOTS = ["1.7", "1.6", "Bio_1.6", "Odyssey_1.6", "Versions/1.6"]
defs = defaultdict(set)          # defName -> {root}
def_where = defaultdict(set)     # defName -> {rel file (1.7 preferred)}
abstract = set()
keyed = set()
tags = set()            # string tags carried by Mugirl defs (apparel.tags, tradeTags, weaponTags, ...)
for cr in CONTENT_ROOTS:
    base = REPO / cr
    if not base.exists():
        continue
    for f in walk_files(base, (".xml",), skip_dirs=("obj", "bin", "Source", "Textures", "Sounds")):
        r = str(f.relative_to(REPO)).replace("\\", "/")
        root = parse_xml(f)
        if root is None:
            continue
        if "/Languages/" in "/" + r:
            if "/Keyed/" in r:
                keyed.update(c.tag for c in root.children)
            continue
        if root.tag == "Defs":
            tops = root.children
        else:
            tops = [n for n in root.iter() if n.tag == "value" for n in n.children]
        for n in root.iter():
            if n.tag == "li" and n.parent is not None and (n.parent.tag.endswith("Tags") or n.parent.tag == "tags") and n.text.strip():
                tags.add(n.text.strip())
        for el in tops:
            if "Name" in el.attrs:
                abstract.add(el.attrs["Name"])
            for c in el.children:
                if c.tag == "defName" and c.text.strip():
                    dn = c.text.strip()
                    defs[dn].add(def_root(el.tag, el.attrs.get("Class")))
                    def_where[dn].add(r)

# C# types of the mod (1.7 source + frozen 1.6 source).
types = {}                          # full -> {"file": [...], "text": str}
short_to_full = defaultdict(set)
for src in [REPO / "Source" / "MugirlRace", REPO / "1.6" / "Source"]:
    for f in walk_files(src, (".cs",)):
        text = f.read_text(encoding="utf-8-sig", errors="replace")
        m = re.search(r"^\s*namespace\s+([\w.]+)", text, re.M)
        ns = m.group(1) if m else ""
        for tm in re.finditer(r"(?<![\w.])(?:class|struct|interface|enum|record)\s+(\w+)", text):
            ls = text.rfind("\n", 0, tm.start()) + 1
            pre = text[ls:tm.start()]
            if "//" in pre or "where" in pre or "new()" in pre:
                continue
            full = (ns + "." if ns else "") + tm.group(1)
            e = types.setdefault(full, {"files": set(), "text": ""})
            e["files"].add(str(f.relative_to(REPO)).replace("\\", "/"))
            e["text"] += "\n" + text
            short_to_full[tm.group(1)].add(full)


TEX_EXT = (".png", ".psd", ".jpg", ".dds")
textures = set()
for troot in [REPO / "Textures", REPO / "1.7" / "Textures"] + [p / "Textures" for p in (REPO / "1.7" / "Mods").glob("*")]:
    if troot.exists():
        for f in walk_files(troot, TEX_EXT):
            t = str(f.relative_to(troot)).replace("\\", "/").rsplit(".", 1)[0]
            textures.add(t)
            textures.add(re.sub(r"_(north|south|east|west|m|south_m|north_m|east_m|west_m)$", "", t))

# Abstract Names that the frozen 1.6 build also declared but that belong to the base game (1.6 shadowed vanilla "Berserk";
# 1.7 renamed it). A dependent's ParentName="Berserk" means the vanilla node.
VANILLA_NAMES = {"Berserk"}
# Owner decision (decisions.md section 5, D5): names the owner listed explicitly as frozen even though the scan finds them only in
# WhiteFluff validation code (FA part types are built as "Mugirl_" + part + "Normal*").
OWNER_FROZEN = {"MugirlFaceSizeAndPositionDef"}
OWNER_FROZEN_RX = re.compile(r"^Mugirl_(Brow|Emotion|Eye|Head|Lid|LidOption|Mouth)Normal\d*$")
PACKAGE_ID = "har.mugirlrace"
HARMONY_ID = "MugirlMod.Mod"
DISPLAY_NAME = "Mugirl Race"
NON_REF_ELEMENTS = {"label", "labelShort", "description", "jobString", "reportString", "labelNoun", "customLabel", "text",
                    "baseDesc", "title", "titleShort", "letterText", "letterLabel", "gerund", "verb", "labelMale", "labelFemale"}


def declared_member(full, member):
    t = types.get(full)
    return t is not None and re.search(r"\b" + re.escape(member) + r"\b", t["text"]) is not None


def declared_field(full, field):
    t = types.get(full)
    if t is None:
        return False
    return re.search(r"\bpublic\s+(?:static\s+|readonly\s+)*[\w<>\[\],.? ]+\s+" + re.escape(field) + r"\s*[;=]", t["text"]) is not None


# ---------------------------------------------------------------------------------------------------------------
# Dependents
# ---------------------------------------------------------------------------------------------------------------
WF = MODS / "乳牛娘 白绒物语"
DEPENDENTS = [
    {"mod": "MRK.WhiteFluff", "label": "Mods/乳牛娘 白绒物语 (local, 1.6)", "root": WF,
     "include": ["1.6", "Source", "About", "Tools", "LoadFolders.xml"],
     "validation": ["Source/Validation/", "Tools/"]},
    {"mod": "har.mugirlrace.subspecies", "label": "Mods/MooGirl_Subspecies (local, 1.6)", "root": MODS / "MooGirl_Subspecies",
     "include": None, "validation": []},
    {"mod": "KindSeal.LOL", "label": "workshop 3555662938 (1.6+1.7)", "root": WORKSHOP / "3555662938", "include": None, "validation": []},
    {"mod": "Kindseal.UFPC", "label": "workshop 3698316804 (1.6+1.7)", "root": WORKSHOP / "3698316804", "include": None, "validation": []},
    {"mod": "ZuoYao.RavenRace", "label": "workshop 3781005562 (1.6)", "root": WORKSHOP / "3781005562", "include": None, "validation": []},
    {"mod": "ZuoYao.RavenRace", "label": "Mods/ZuoYao_RavenRace (local copy, 1.6)", "root": MODS / "ZuoYao_RavenRace", "include": None, "validation": []},
    {"mod": "Berserk.poisonless.milk", "label": "workshop 3601148404 (1.6)", "root": WORKSHOP / "3601148404", "include": None, "validation": []},
]
SKIP_DIRS = ("obj", "bin", ".git", ".vs", "Textures", "Sounds", "TMP", "docs", "Releases", "贴图", "__pycache__", "Assemblies")

uses = []   # dicts: kind, name, defRoot, owner, mod, file, line, use, note


def add(kind, name, mod, rel, line, use, defRoot=None, owner=None, note=None):
    uses.append({"kind": kind, "name": name, "defRoot": defRoot, "owner": owner, "mod": mod, "file": rel, "line": line,
                 "use": use, "note": note})


DECLARED_PUBLIC = {"MugirlBody", "MugirlLike", "Ears"}
TOKEN = re.compile(r"[A-Za-z_][\w\-]*(?:\.[A-Za-z_][\w\-]*)*")


def classify_token(tok, ctx_def_roots=None):
    """Return (kind, name, defRoot) candidates for a raw token."""
    out = []
    if tok in types:
        out.append(("type", tok, None))
    if tok in defs:
        for r in sorted(defs[tok]):
            out.append(("defName", tok, r))
    return out


def scan_xml(dep, f, rel, use):
    root = parse_xml(f)
    if root is None:
        return
    mod = dep["mod"]
    is_definj = "/DefInjected/" in rel
    is_keyed = "/Keyed/" in rel
    own_textures = dep.setdefault("_tex", None)
    for n in root.iter():
        tag = n.tag
        # Def subclass element names (<Mugirl.SlaveApparelDef>) and their fields.
        if tag in types:
            add("xmlClass", tag, mod, rel, n.line, use, note="Def element name")
            for c in n.children:
                if declared_field(tag, c.tag):
                    add("xmlField", c.tag, mod, rel, c.line, use, owner=tag)
        if is_definj and n.parent is root:
            segs = tag.split(".")
            folder = rel.split("/DefInjected/")[1].split("/")[0]
            if segs[0] in defs:
                add("defName", segs[0], mod, rel, n.line, use, defRoot=sorted(defs[segs[0]])[0], note="DefInjected key of a Mugirl def")
            for s in segs[1:]:
                s0 = re.sub(r"-\d+$", "", s)
                if s0 in short_to_full and any(f.startswith("Mugirl") for f in short_to_full[s0]):
                    add("typeHandle", s0, mod, rel, n.line, use, note="DefInjected class-name handle")
        elif not is_keyed and root.tag in ("Defs", "Patch") and n.parent is not None and tag in defs and not tag.startswith("li"):
            add("defName", tag, mod, rel, n.line, use, defRoot=sorted(defs[tag])[0], note="element name (dictionary key)")
        for a, v in n.attrs.items():
            if a == "Class":
                if v in types:
                    add("xmlClass", v, mod, rel, n.line, use, note="Class attribute")
                    for c in n.children:
                        if declared_field(v, c.tag):
                            add("xmlField", c.tag, mod, rel, c.line, use, owner=v)
            elif a in ("Name", "ParentName"):
                if v in abstract and a == "ParentName" and v not in VANILLA_NAMES:
                    add("abstractName", v, mod, rel, n.line, use)
            elif a in ("MayRequire", "MayRequireAnyOf", "IfModActive", "IfModActiveAll"):
                if any(p.strip().lower() in (PACKAGE_ID, PACKAGE_ID + "_steam") for p in v.split(",")):
                    add("packageId", "HAR.MugirlRace", mod, rel, n.line, use, note=f"{a} attribute")
        text = n.text.strip()
        if not text or n.children:
            continue
        line = n.text_line or n.line
        if tag in ("packageId", "li") and text.lower() in (PACKAGE_ID, PACKAGE_ID + "_steam") and rel.endswith("About.xml"):
            add("packageId", "HAR.MugirlRace", mod, rel, line, use, note="About.xml dependency")
            continue
        if text.lower() in (PACKAGE_ID, PACKAGE_ID + "_steam"):
            add("packageId", "HAR.MugirlRace", mod, rel, line, use, note=f"<{tag}>")
            continue
        if text == DISPLAY_NAME:
            add("displayName", DISPLAY_NAME, mod, rel, line, use, note=f"<{tag}> (PatchOperationFindMod)")
            continue
        if tag == "xpath" or "[" in text and "/" in text:
            # Structural dependency: the element path a dependent patches below one of our defs.
            for sm in re.finditer(r"""\[\s*defName\s*=\s*["']([^"']+)["']\s*\]((?:/[A-Za-z_][\w.]*(?:\[[^\]]*\])?)+)""", text):
                if sm.group(1) in defs:
                    path = re.sub(r"\[[^\]]*\]", "", sm.group(2))
                    add("xpathStructure", sm.group(1) + path, mod, rel, line, use, defRoot=sorted(defs[sm.group(1)])[0],
                        note="patched element path below the def (renaming or moving these fields breaks the patch)")
            for tok in TOKEN.findall(text):
                for part in [tok] + tok.split("."):
                    if part in defs:
                        add("defName", part, mod, rel, line, use, defRoot=sorted(defs[part])[0], note="xpath")
                    if part in abstract:
                        add("abstractName", part, mod, rel, line, use, note="xpath")
                if tok in types:
                    add("xmlClass", tok, mod, rel, line, use, note="xpath")
            continue
        if tag in NON_REF_ELEMENTS or is_keyed or is_definj:
            continue
        if tag == "li" and n.parent is not None and (n.parent.tag.endswith("Tags") or n.parent.tag == "tags") and text in tags and "mugirl" in text.lower():
            add("tag", text, mod, rel, line, use, note=f"<{n.parent.tag}><li>")
            continue
        if text in types:
            add("xmlClass", text, mod, rel, line, use, note=f"<{tag}> text")
            continue
        if text in defs:
            add("defName", text, mod, rel, line, use, defRoot=sorted(defs[text])[0], note=f"<{tag}>")
            continue
        if (tag.lower().endswith("path") or tag.lower().endswith("texpath")) and "/" in text:
            own = (dep["root"] / "Textures").exists() and any((dep["root"] / "Textures" / (text + e)).exists() for e in ("", ".png"))
            if text in textures and not own:
                add("texturePath", text, mod, rel, line, use, note=f"<{tag}>")


CS_LIT = re.compile(r'@?"((?:[^"\\\n]|\\.)*)"')


def strip_comments(text):
    # keep line structure; remove // and /* */ comments outside string literals (approximation good enough here)
    out, i, n = [], 0, len(text)
    in_str = False
    while i < n:
        c = text[i]
        if not in_str and text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if not in_str and text.startswith("/*", i):
            j = text.find("*/", i + 2)
            seg = text[i:(n if j < 0 else j + 2)]
            out.append("\n" * seg.count("\n"))
            i = n if j < 0 else j + 2
            continue
        if c == '"' and (i == 0 or text[i - 1] != "\\"):
            in_str = not in_str
        out.append(c)
        i += 1
    return "".join(out)


def resolve_type(name, usings):
    """Full name of a Mugirl type written as 'Mugirl.X.Y' or as a short name under 'using Mugirl*;'."""
    name = name.rstrip("?")
    if name in types and name.startswith("Mugirl."):
        return name
    for u in sorted(usings):
        if f"{u}.{name}" in types:
            return f"{u}.{name}"
    return None


def code_text_for_types(text):
    return CS_LIT.sub('""', text)


def local_variable_types(code, usings):
    """Locals, parameters and pattern variables whose declared type is a Mugirl type (best effort, per file)."""
    out = {}
    for m in re.finditer(r"(?<![\w.])((?:Mugirl\.)?[\w.]+\??)\s+(\w+)\s*(?=[=;,)])", code):
        full = resolve_type(m.group(1), usings)
        if full:
            out[m.group(2)] = full
    for m in re.finditer(r"\b(?:var|[\w.]+\??)\s+(\w+)\s*=\s*[^;]*?\b(?:TryGetComp|GetComp|GetComps)\s*<\s*([\w.]+)\s*>", code):
        full = resolve_type(m.group(2), usings)
        if full:
            out[m.group(1)] = full
    for m in re.finditer(r"\b(?:is|out)\s+([\w.]+\??)\s+(\w+)\b", code):
        full = resolve_type(m.group(1), usings)
        if full:
            out[m.group(2)] = full
    return out


def scan_cs(dep, f, rel, use):
    mod = dep["mod"]
    raw = f.read_text(encoding="utf-8-sig", errors="replace")
    text = strip_comments(raw)
    usings = set(re.findall(r"^\s*using\s+(Mugirl[\w.]*)\s*;", text, re.M))
    lines = text.split("\n")
    local_types = local_variable_types(code_text_for_types(text), usings)
    for ln, line in enumerate(lines, 1):
        lits = [m.group(1) for m in CS_LIT.finditer(line)]
        code = CS_LIT.sub('""', line)
        # Instance members used through locals typed as a Mugirl type (Comp_MugirlMount mount = ...; mount.TryMount(...)).
        for mm in re.finditer(r"(?<![\w.])(\w+)\??\.(\w+)\b", code):
            full = local_types.get(mm.group(1))
            if full and declared_member(full, mm.group(2)):
                add("member", f"{full}.{mm.group(2)}", mod, rel, ln, use, note=f"member access through a local of type {full.rsplit('.', 1)[-1]}")
        # qualified names and short names under using Mugirl*
        for m in re.finditer(r"(?<![\w.])([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)", code):
            tok = m.group(1)
            parts = tok.split(".")
            found = None
            for k in range(len(parts), 0, -1):
                cand = ".".join(parts[:k])
                if cand.startswith("Mugirl.") and cand in types:
                    found = (cand, parts[k:])
                    break
                if k == 1 and usings:
                    for u in usings:
                        if f"{u}.{cand}" in types:
                            found = (f"{u}.{cand}", parts[1:])
                            break
                if found:
                    break
            if found:
                full, rest = found
                add("type", full, mod, rel, ln, use)
                if rest and declared_member(full, rest[0]):
                    add("member", f"{full}.{rest[0]}", mod, rel, ln, use)
        for lit in lits:
            if lit == HARMONY_ID:
                add("harmonyId", HARMONY_ID, mod, rel, ln, use)
            elif lit.lower() in (PACKAGE_ID, PACKAGE_ID + "_steam"):
                add("packageId", "HAR.MugirlRace", mod, rel, ln, use, note="C# literal")
            elif lit in defs:
                add("defName", lit, mod, rel, ln, use, defRoot=sorted(defs[lit])[0], note="C# string literal")
            elif lit in keyed:
                add("keyedKey", lit, mod, rel, ln, use)
            elif lit in types:
                add("type", lit, mod, rel, ln, use, note="C# string literal (reflection by name)")
            elif lit in short_to_full and any(x.startswith("Mugirl.") for x in short_to_full[lit]):
                for full in sorted(x for x in short_to_full[lit] if x.startswith("Mugirl.")):
                    add("typeNameLiteral", full, mod, rel, ln, use, note=f'C# string literal "{lit}" compared with a type short name (GetType().Name)')
            elif "[" in lit or "/" in lit:
                for tok in TOKEN.findall(lit):
                    for part in [tok] + tok.split("."):
                        if part in defs:
                            add("defName", part, mod, rel, ln, use, defRoot=sorted(defs[part])[0], note="xpath in C# literal")
        # Harmony patches of Mugirl methods bind the target's parameter names (Prefix/Postfix parameters without __).
        hp = re.search(r"\[HarmonyPatch\(typeof\((Mugirl[\w.]*)\)\s*,\s*(?:nameof\([\w.]*?(\w+)\)|\"(\w+)\")", line)
        if hp:
            target = f"{hp.group(1)}.{hp.group(2) or hp.group(3)}"
            # the patch class: from the attribute to the next [HarmonyPatch] attribute
            end = next((k for k in range(ln, len(lines)) if "[HarmonyPatch" in lines[k]), len(lines))
            body = "\n".join(lines[ln:end])
            for pm in re.finditer(r"\b(?:Prefix|Postfix|Finalizer)\s*\(([^)]*)\)", body):
                for prm in pm.group(1).split(","):
                    pn = prm.strip().split()[-1] if prm.strip() else ""
                    if pn and not pn.startswith("__"):
                        add("harmonyParameter", f"{target}({pn})", mod, rel, ln, use, owner=target, note="parameter name bound by a Harmony patch")
        # dynamically composed def names: "A" + x + "B"
        for m in re.finditer(r'"([A-Za-z_]\w*)"\s*\+\s*([^";]+?)\s*\+\s*"(\w*)"', line):
            a, b = m.group(1), m.group(3)
            rx = re.compile("^" + re.escape(a) + r"\w*" + re.escape(b) + r"\w*$")
            hits = sorted(d for d in defs if rx.match(d))
            if hits and a.startswith("Mugirl"):
                add("defNamePattern", f"{a}*{b}*", mod, rel, ln, use, note="composed in C#: " + ", ".join(hits[:12]) + (" ..." if len(hits) > 12 else ""))
                for h in hits:
                    add("defName", h, mod, rel, ln, use, defRoot=sorted(defs[h])[0], note=f"via pattern {a}*{b}*")


def is_validation(dep, rel):
    return any(rel.startswith(p) for p in dep["validation"])


scanned = []
for dep in DEPENDENTS:
    root = dep["root"]
    if not root.exists():
        continue
    files = []
    tops = dep["include"] or [p.name for p in root.iterdir()]
    for t in tops:
        p = root / t
        if p.is_file():
            files.append(p)
        elif p.is_dir() and p.name not in SKIP_DIRS:
            files.extend(walk_files(p, (".xml", ".cs", ".ps1", ".py"), skip_dirs=SKIP_DIRS))
    # Compiled assemblies (e.g. RavenRace ships only a DLL): user strings (UTF-16) and metadata names (UTF-8).
    # Only assemblies the game loads (<load folder>/Assemblies/*.dll); build/temp copies (TMP, obj, bin) are skipped.
    for f in sorted(walk_files(root, (".dll",), skip_dirs=tuple(d for d in SKIP_DIRS if d != "Assemblies"))):
        rel = str(f.relative_to(root)).replace("\\", "/")
        if f.name.lower() in ("0harmony.dll", "alienrace.dll", "mugirlrace.dll") or not re.match(r"^(?:[\w.]+/)?Assemblies/[^/]+$", rel):
            continue
        data = f.read_bytes()
        strings = {m.group(0).decode("utf-16-le") for m in re.finditer(rb"(?:[\x20-\x7e]\x00){3,}", data)}
        names = {m.group(0).decode("ascii") for m in re.finditer(rb"[\x21-\x7e]{3,}", data)}
        seen = set()
        for st in strings:
            for tok in TOKEN.findall(st):
                if tok in defs and tok not in seen and (tok.startswith(("Mugirl", "Job_")) or tok in DECLARED_PUBLIC):
                    seen.add(tok)
                    add("defName", tok, dep["mod"], rel, 0, "production", defRoot=sorted(defs[tok])[0], note="string in a compiled assembly")
        if "Mugirl" in names:
            for full in types:
                ns_, sh = full.rsplit(".", 1) if "." in full else ("", full)
                if ns_.startswith("Mugirl") and sh in names and ns_ in names and full not in seen:
                    seen.add(full)
                    add("type", full, dep["mod"], rel, 0, "production", note="type reference in a compiled assembly (namespace and name in metadata)")
    for u in uses:
        u.setdefault("source", dep["label"])
    n = 0
    for f in sorted(files):
        rel = str(f.relative_to(root)).replace("\\", "/")
        use = "validation" if is_validation(dep, rel) else "production"
        before = len(uses)
        if f.suffix == ".xml":
            scan_xml(dep, f, rel, use)
        elif f.suffix == ".cs":
            scan_cs(dep, f, rel, use)
        else:
            # scripts: literal tokens only
            text = f.read_text(encoding="utf-8-sig", errors="replace")
            for ln, line in enumerate(text.split("\n"), 1):
                for tok in TOKEN.findall(line):
                    if tok in types and tok.startswith("Mugirl."):
                        add("type", tok, dep["mod"], rel, ln, "validation")
                    elif tok in defs and tok.startswith(("Mugirl", "Job_")):
                        add("defName", tok, dep["mod"], rel, ln, "validation", defRoot=sorted(defs[tok])[0], note="script")
        n += 1
        for u in uses[before:]:
            u["source"] = dep["label"]
    scanned.append({"mod": dep["mod"], "location": dep["label"], "files": n,
                    "validationPaths": dep["validation"]})

# Merge into entries
entries = {}
for u in uses:
    key = (u["kind"], u["name"], u["defRoot"] or "", u["owner"] or "")
    e = entries.setdefault(key, {"kind": u["kind"], "name": u["name"]})
    if u["defRoot"]:
        e["defRoot"] = u["defRoot"]
    if u["owner"]:
        e["owner"] = u["owner"]
    ub = e.setdefault("usedBy", [])
    rec = {"mod": u["mod"], "where": u["source"], "file": f"{u['file']}:{u['line']}", "use": u["use"]}
    if u.get("note"):
        rec["note"] = u["note"]
    if rec not in ub:
        ub.append(rec)

frozen, validation_only = [], []
for key in sorted(entries, key=lambda k: (k[0], k[1], k[2], k[3])):
    e = entries[key]
    prod = any(x["use"] == "production" for x in e["usedBy"])
    if not prod and (e["name"] in OWNER_FROZEN or OWNER_FROZEN_RX.match(e["name"]) or e["name"] == "Mugirl_*Normal*"):
        prod = True
        e["frozenBy"] = "owner decision (decisions.md section 5, D5): listed explicitly"
    e["frozen"] = prod
    (frozen if prod else validation_only).append(e)

save_json(GEN / "dependents-raw.json", {"scanned": scanned, "uses": uses})
print(f"defs {len(defs)}, abstract {len(abstract)}, types {len(types)}, keyed {len(keyed)}, textures {len(textures)}")
print(f"uses {len(uses)}; frozen entries {len(frozen)}; validation-only entries {len(validation_only)}")
from collections import Counter
print(Counter(e["kind"] for e in frozen))
print(Counter(e["kind"] for e in validation_only))
save_json(GEN / "public-api.partial.json", {"entries": frozen, "validationOnly": validation_only, "scanned": scanned})
