# Stage 0 Baseline Summary

Generated: 2026-06-06

Stage 0 is complete. This stage froze the current state before functional refactoring. No C# business logic, XML Def values, textures, sounds, or gameplay data were changed during baseline capture.

## Produced Files

- `file-inventory.md`: full scanned file inventory, directory/file-type counts, largest source/XML files.
- `def-values.csv`: machine-readable XML leaf/attribute value snapshot.
- `def-values-summary.md`: XML snapshot summary.
- `code-values.csv`: machine-readable C# literals/defaults/Def lookups/messages snapshot.
- `code-values.md`: C# snapshot summary.
- `harmony-patches.csv`: machine-readable Harmony/reflection entry snapshot.
- `harmony-patches.md`: Harmony snapshot summary.
- `scribe-fields.csv`: machine-readable save-state/Scribe snapshot.
- `scribe-fields.md`: Scribe snapshot summary.
- `behavior-map.md`: player-visible behavior map to preserve during refactoring.

## Snapshot Counts

- Scanned files from `rg --files`: 1,104 at capture time.
- Effective C# source files, excluding `bin`/`obj`: 143.
- Effective C# source lines: 13,474.
- XML files parsed: 190.
- XML values captured: 8,723.
- Numeric-looking XML values captured: 2,260.
- XML parse errors: 0.
- C# value/default/message entries captured: 2,432.
- Runtime `DefDatabase.GetNamed*` string lookups captured: 66.
- Harmony/reflection entries captured: 141.
- Harmony entry-point related entries captured: 4.
- Reflection access entries captured: 26.
- Save-state/Scribe entries captured: 176.
- Scribe call entries captured: 98.

## Immediate Stage 1 Risks

- Multiple mod initialization entry points are present.
- Harmony patching is currently spread across more than one bootstrap path.
- Some backup or explicitly unusable C# source files are still included in the project file.
- A static initialization class only logs an initialization message and has no business responsibility.
- Legacy-save repair logic exists in runtime components, even though old-save compatibility is not required.

## Next Step

Proceed to Stage 1: project and entry cleanup. The first Stage 1 changes should remove duplicate/noise bootstraps and exclude backup source files from compilation while preserving current gameplay behavior and all values.

