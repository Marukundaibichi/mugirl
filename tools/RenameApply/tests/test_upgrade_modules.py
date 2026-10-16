"""Regression checks for the applied-log -> compiled upgrade-table boundary."""

import importlib.util
import unittest
from pathlib import Path


spec = importlib.util.spec_from_file_location("upgrade_modules", Path(__file__).resolve().parents[1] / "GenerateUpgradeModules.py")
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


class UpgradeModuleGenerationTests(unittest.TestCase):
    def test_preserves_database_roots_nested_types_and_settings(self):
        rows = {
            "types": [{"old": "Mugirl.Old+State", "new": "Mugirl.Features.New+State", "module": "core", "disposition": "rename", "reason": "same state"}],
            "defs": [{"root": "NewConfigDef", "legacyRoot": "OldConfigDef", "old": "OldConfig", "new": "Mugirl_Config", "module": "corporate", "disposition": "rename", "reason": "same config"}],
            "settings": {"module": "core", "fileClass": {"old": "Mugirl.Settings", "new": "Mugirl.Core.Settings"}, "labels": [{"old": "oldKey", "new": "newKey"}]},
        }
        output = generator.generate(rows)
        self.assertEqual(7, len(output))
        self.assertIn('SaveTypeNames.RequireNested(typeof(global::Mugirl.Features.New), "State")', output["Core/CoreUpgradeModule.Generated.cs"])
        self.assertIn('legacyRoot: "OldConfigDef"', output["Corporate/CorporateUpgradeModule.Generated.cs"])
        self.assertNotIn('legacyRoot:', output["Core/CoreUpgradeModule.Generated.cs"])
        self.assertIn('module.RenameSetting("oldKey", "newKey", 1);', output["Core/CoreUpgradeModule.Generated.cs"])
        self.assertNotIn('MooGirlDefPattern', "".join(output.values()))
        self.assertEqual(output, generator.generate(rows))

    def test_refuses_incomplete_or_unreviewed_inputs(self):
        with self.assertRaises(ValueError):
            generator.generate({"errors": ["unresolved target"]})
        for row in (
            {"old": "Old", "new": "New", "module": "unassigned", "disposition": "rename", "reason": "rename"},
            {"old": "Old", "new": None, "module": "core", "disposition": "drop", "policy": "CustomMagic", "reason": "drop"},
        ):
            with self.assertRaises(ValueError):
                generator.generate({"types": [row]})

    def test_does_not_merge_same_def_name_in_different_databases(self):
        rows = {"defs": [
            {"root": root, "old": "Shared", "new": "Mugirl_" + root, "module": "content", "disposition": "rename", "reason": "root-specific"}
            for root in ("ThingDef", "HediffDef")
        ]}
        output = generator.generate(rows)["Content/ContentUpgradeModule.Generated.cs"]
        self.assertIn('RenameDef("ThingDef", "Shared", "Mugirl_ThingDef"', output)
        self.assertIn('RenameDef("HediffDef", "Shared", "Mugirl_HediffDef"', output)
        rows["defs"].append(rows["defs"][0])
        with self.assertRaises(ValueError):
            generator.generate(rows)

    def test_composes_retired_names_with_the_current_rename(self):
        rows = [dict(root="ThingDef", old=old, new=new, disposition="rename", module="combat", reason="history")
                for old, new in (("Bullet_Old", "Bullet_Intermediate"), ("Bullet_Intermediate", "Mugirl_Bullet_Current"))]
        folded = generator.compose_def_rows(rows)
        self.assertEqual("Mugirl_Bullet_Current", folded[0]["new"])
        self.assertEqual("Bullet_Intermediate", rows[0]["new"])
        output = generator.generate({"defs": rows})["Combat/CombatUpgradeModule.Generated.cs"]
        self.assertIn('RenameDef("ThingDef", "Bullet_Old", "Mugirl_Bullet_Current"', output)

    def test_composition_propagates_removal_and_rejects_cycles(self):
        rows = [dict(root="ThingDef", old=old, new=new, disposition="rename" if new else "drop", policy=None if new else "RemoveThings", module="content", reason="history")
                for old, new in (("A", "B"), ("B", None))]
        self.assertEqual("drop", generator.compose_def_rows(rows)[0]["disposition"])
        rows[1].update(new="A", disposition="rename", policy=None)
        with self.assertRaises(ValueError):
            generator.compose_def_rows(rows)


if __name__ == "__main__":
    unittest.main()
