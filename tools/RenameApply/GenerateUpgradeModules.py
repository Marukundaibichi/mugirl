"""Compile reviewed applied-to-manifest JSON into per-module C# registrations.

Run after RenameApply and applied_to_manifest.py, with --rows and --output.
Output is deterministic and contains no runtime JSON dependency.
Generated files belong in SaveUpgrade/Modules; custom structural upgrades use
the optional RegisterStructure partial method in a separate file.
"""

import argparse
import json
import re
from pathlib import Path


MODULES = {"core": 100, "incidents": 200, "corporate": 300, "milk": 400,
           "restraints": 500, "combat": 600, "content": 700}
TYPE_NAME = re.compile(r"^[A-Za-z_][\w]*(?:[.+][A-Za-z_][\w]*)*$")


def literal(value):
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"Expected a nonempty string, got {value!r}")
    return json.dumps(value, ensure_ascii=False)


def target_type(name):
    if not isinstance(name, str) or not TYPE_NAME.fullmatch(name):
        raise ValueError(f"Unsupported target type: {name!r}")
    parts = name.split("+")
    expression = f"typeof(global::{parts[0]})"
    for nested in parts[1:]:
        expression = f"SaveTypeNames.RequireNested({expression}, {literal(nested)})"
    return expression


def compose_def_rows(rows):
    """Collapse historical -> intermediate -> current names within one Def root."""
    result = [dict(row) for row in rows]
    by_old = {(row["root"], row["old"]): dict(row) for row in result}
    if len(by_old) != len(result):
        raise ValueError("Duplicate Def rows must be resolved before projection")
    for row in result:
        seen = {row["old"]}
        traversed = []
        while row.get("new") is not None:
            target = row["new"]
            next_row = by_old.get((row["root"], target))
            if next_row is None or next_row.get("new") == target:
                break
            if target in seen:
                raise ValueError(f"Cyclic Def mapping: {row['root']}/{row['old']}")
            seen.add(target)
            traversed.append(target)
            row["new"] = next_row.get("new")
            row["disposition"] = next_row["disposition"]
            row["policy"] = next_row.get("policy")
        if traversed:
            row["reason"] += " (composed through " + ", ".join(traversed) + ")"
    return result


def generate(rows):
    if rows.get("errors"):
        raise ValueError("Cannot compile an applied manifest with errors")
    statements = {name: [] for name in MODULES}

    def add(row, statement):
        module = row.get("module", "").lower()
        if module not in statements:
            raise ValueError(f"Unknown module {module!r}: {row}")
        statements[module].append(statement)

    seen = set()
    for row in rows.get("types", []):
        old = row["old"]
        key = ("type", old)
        if key in seen:
            raise ValueError(f"Duplicate type row: {old}")
        seen.add(key)
        reason = literal(row["reason"])
        if row["disposition"] == "rename":
            if old == row["new"]:
                continue
            add(row, f"module.RenameType({literal(old)}, {target_type(row['new'])}, {reason});")
        elif row["disposition"] == "drop":
            policy = (row.get("policy") or "").split(" ")[0]
            if policy not in ("RemoveElement", "NullOut"):
                raise ValueError(f"Unreviewed type drop policy: {row}")
            add(row, f"module.DropType({literal(old)}, TypeDropPolicy.{policy}, {reason});")
        else:
            raise ValueError(f"Unknown type disposition: {row}")

    for row in compose_def_rows(rows.get("defs", [])):
        root, old = row["root"], row["old"]
        key = (root, old)
        if key in seen:
            raise ValueError(f"Duplicate Def row: {root}/{old}")
        seen.add(key)
        reason = literal(row["reason"])
        legacy_root = f", legacyRoot: {literal(row['legacyRoot'])}" if row.get("legacyRoot") else ""
        if row["disposition"] == "rename":
            if old == row["new"]:
                continue
            add(row, f"module.RenameDef({literal(root)}, {literal(old)}, {literal(row['new'])}, {reason}{legacy_root});")
        elif row["disposition"] == "drop":
            # The runtime policies are typed by database root; descriptive text
            # from the reviewed hand rows is retained verbatim as the reason.
            policy = {"ThingDef": "RemoveThings", "RecipeDef": "RemoveBills",
                      "HediffDef": "RemoveHediffs"}.get(root, "RemoveReferences")
            add(row, f"module.DropDef({literal(root)}, {literal(old)}, DefDropPolicy.{policy}, {reason}{legacy_root});")
        else:
            raise ValueError(f"Unknown Def disposition: {row}")

    settings = rows.get("settings")
    if settings:
        target = settings.get("fileClass")
        if target:
            add(settings, f"module.RenameSettingsClass({literal(target['old'])}, {literal(target['new'])}, 1);")
        for row in settings.get("labels", []):
            if row.get("new") is None:
                add(settings, f"module.DropSetting({literal(row['old'])}, 1);")
            else:
                add(settings, f"module.RenameSetting({literal(row['old'])}, {literal(row['new'])}, 1);")

    # Unsupported-era detection is owned by the framework's audited
    # MooGirlEraNames table, generated separately by GenerateEraMarkers.py.

    result = {}
    for module, order in MODULES.items():
        name = module.capitalize()
        body = "\n".join("        " + statement for statement in statements[module])
        result[f"{name}/{name}UpgradeModule.Generated.cs"] = (
            "// <auto-generated />\n"
            "// 由已审阅的改名日志生成；旧名只在升级层登记，结构步骤写在同名分部类中。\n"
            "namespace Mugirl.SaveUpgrade;\n\n"
            f"[SaveUpgradeModule(\"{name}\", {order})]\n"
            f"internal sealed partial class {name}UpgradeModule : ISaveUpgradeModule\n"
            "{\n"
            "    public void Register(UpgradeModuleBuilder module)\n"
            "    {\n"
            f"{body}\n"
            "        RegisterStructure(module);\n"
            "    }\n\n"
            "    static partial void RegisterStructure(UpgradeModuleBuilder module);\n"
            "}\n")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rows", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--check", action="store_true", help="Verify existing output without changing it")
    args = parser.parse_args()
    rows = json.loads(args.rows.read_text(encoding="utf-8-sig"))
    files = generate(rows)
    for relative, content in files.items():
        target = args.output / relative
        if args.check:
            if not target.exists() or target.read_text(encoding="utf-8-sig") != content:
                raise SystemExit(f"Generated registration differs: {target}")
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding="utf-8", newline="\n")
    print(f"{'Verified' if args.check else 'Generated'} {len(files)} module registrations")


if __name__ == "__main__":
    main()
