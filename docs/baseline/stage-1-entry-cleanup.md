# Stage 1 Entry Cleanup Log

Generated: 2026-06-06

This file records the first Stage 1 refactoring pass. The pass focused on project structure and initialization cleanup. It intentionally did not change XML Def values, gameplay numbers, event timings, item stats, hediff values, or player-facing balance.

## Changes Made

### Single Main Bootstrap

- Added `1.6/Source/WRace/Core/MooGirlBootstrap.cs`.
- Moved the main Harmony creation and `PatchAll()` call behind `MooGirlBootstrap.Initialize()`.
- Preserved the existing Harmony ID: `MooGirlMod.Mod`.
- Preserved `MooGirlMod.harmony` by assigning it to `MooGirlBootstrap.Harmony`, so existing manual patch code still has the same static access path.
- Added `1.6/Source/WRace/Core/MooGirlLog.cs` as the future logging wrapper.
- Added `1.6/Source/WRace/UI/MooGirlSettingsWindow.cs` and moved settings UI drawing out of `Mod.cs`.
- Kept the existing settings labels, descriptions, default values, and cleanup behavior unchanged.

### Removed Duplicate/Dead Initialization

- Deleted `1.6/Source/WRace/RopingAndRoped牵引系统的编程/Harmoney_Mod.cs`.
    - It declared a second `Mod` subclass, `RopedMooMod`.
    - It called `new Harmony("RopedMoo").PatchAll()`.
    - It logged `Roped loaded`.
    - It was not included in the project file, but keeping it in source risked future accidental recompilation.
- Deleted `1.6/Source/WRace/OtherFunction/AiGenerated_Init.cs`.
    - It only emitted a load log and had no gameplay responsibility.

### Removed Backup Source From Production

- Removed from `.csproj` and deleted:
    - `电击项圈备份(不能用,太难特有的条件判断方法几乎不工作，只能自己用计时器检查)CompProperties_ShockCollar.cs`
    - `电击项圈备份备份CompProperties_ShockCollar.cs`
- The active shock collar implementation remains compiled:
    - `电击项圈CompProperties_ShockCollar.cs`

### Removed Old-Save Repair Paths

- Removed legacy-save upgrade state from `MooGirl_GameComp`:
    - `courierRaidLegacyFixApplied`
    - `legacySaveUpgradeApplied`
- Removed old-save repair methods:
    - `RunLegacySaveUpgradeOnce`
    - `PrepareLegacyRescuePawns`
    - `IsLegacyRescuePawn`
    - `TryRecoverLegacyCourierRaidQuest`
    - `RepairCourierRaidQuestSignals`
    - related quest signal helper methods used only by legacy recovery
- Removed `MooGirl_ConvertComp.cs`.
    - It existed to repeatedly normalize old-save `MooGirl_EscapeWildSlave` player pawns.
    - Old-save compatibility is explicitly out of scope.

## Behavior Preserved

- Main mod settings still load through `MooGirlMod`.
- Settings defaults are unchanged.
- The main Harmony patch pass still happens once through the same Harmony ID.
- Structural crash timer values are unchanged.
- Courier raid timer values are unchanged.
- New-game structural crash and courier quest trigger logic are unchanged.
- Rescue-join hediff logic remains in place.
- Juvenile graphic normalization still runs on `FinalizeInit`, `StartedNewGame`, and `LoadedGame`.

## Validation

- Residual search for removed legacy/init symbols found no active references.
- Debug rebuild completed successfully with MSBuild.
- Output assembly was regenerated at `1.6/Assemblies/MooGirlRace.dll`.

## Follow-Up

- Move remaining compatibility one-off patching, such as the HAR swaddle patch, under the future patch registry.
- Introduce `MooGirlLog` and replace normal load-time message spam in later cleanup passes.
- Continue Stage 1 by separating settings UI from bootstrap and preparing Stage 2 core infrastructure.
