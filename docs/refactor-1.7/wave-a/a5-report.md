A5 report: in-game framework and legacy fixtures (tag a5)

BUILT (root: Source\Tests\MugirlRace.GameValidation\)
- Fix: Core\DrawProbe.cs patches the method's declaring type. UiSmokeSuite waits for the close fade.
- Framework (Core\): MugirlTarget finds the target by assembly name, with a configurable packageId. ValidationOptions/EntryMode/FixtureRef add load:<save> entry, boots, fixtures, token set, offline report and suite options. LogPolicy/SuiteLogPolicy/LogVerdict add an AnyError mode, a reviewed allowlist and a foreign classification. The vanilla-XML readers are SaveXml, SaveFacts, SaveFactQueries, SaveNameMap (K1), SaveDiff and SaveDifference. Also new: DialogAnswers and ArtifactJson; ValidationSession loads the entry save from the main menu.
- Suites: Fixture\FixtureGateSuite, Fixture\ContentSweepSuite (works with any build), Mod\UpgradeSuite (critique §4.3 boot-1/boot-2 checks, K1/K2 read by reflection; not in default or all).
- tools\Test-InGame.ps1: K5 lock, -Fixture <id|all> (two fresh-process boots), -TargetModDir, -TokenSet, -SuiteOption, K3 deploy. tools\New-SaveFixture.ps1 is new.
- DevData\Evidence\SaveFixtures\P-1004, P-0927, P-0920, P-0.5.0, P-0.4.7: manifest.json plus .rws.gz; all feasible.

VERIFICATION
- agent-build -Tag a5, for the mod and for GameValidation (-AllWarnings): succeeded, 0 warnings, 0 errors. The plumbing-only build is also 0/0.
- Test-InGame.ps1 (default): ok=78 failed=0 skipped=14, PASS (TMP\InGameValidation\20261015-144507). Baseline was 65/1/14. Only ui/corporate-comms changed, from FAIL to ok (+13 page checks).
- New-SaveFixture.ps1 -Build all -Force: 5/5 built.
- Test-InGame.ps1 -Fixture all on the deployed DLL, which has no K1/K2 (TMP\InGameValidation\20261015-143755):
  - Boot 2 round trip: 0 differences on all 5.
  - Boot 1 fails as expected where K1/K2 are missing.
  - Boot 1 also finds real legacy losses: milkFullness_lastResourceUpdateTick, MagneticShackles tickCounter, MechanicBackpack quality, apparel color#2, lastJobGiverKey, EleShock/BrainWashing hediff fields (0.5.0), and in 0920 the defs NeuralCombatShoulders/MechanoidBulldog and the type Verb_MeleeAttack_SegmentedTailAssist.
- The K1/K2 names and JSON fields in A1's current source match UpgradeSuite's reflection.

DEVIATIONS
- Fixture ids are P-<build>; catalog keys are r1004, r0927, r0920, v050, v047; temp packageId is mugirl.legacy.<key>.
- The sweep answers dialog trees with the first enabled option, because a quest dialog reopened every tick. Quests given by the mod's incidents are not generated again.
- Facts are read in-game from the original written XML. A test-only Harmony postfix pauses after Game.LoadGame.
- Boot-1 diff rules:
  - Additions and null/empty swaps are info only.
  - Two vanilla first-load normalizations (lastLightTick, inventoryStock defaults) count as load resets.
  - Anything missing or changed needs a K1 explanation.
- Test-InGame tests only the DLL in 1.7/Assemblies, so the upgrade suite has not run against A1's build (that needs a deploy).

REQUESTS
- Integrator / Wave D: document Test-InGame -Fixture/-TargetModDir/-TokenSet, New-SaveFixture and SaveFixtures in the root README and docs/validation-runbook.md. After K4, run `tools\Test-InGame.ps1 -BuildMod -Deploy -Fixture all`. Decide whether fixtures are versioned (DevData/ is gitignored).
- A1: keep these names exactly: SaveUpgradeManifest.ToJson(), SaveUpgradeDiagnostics.LastReportJson, GameComponent_Mugirl.SaveSchema/SourceSchema/LastUpgradeReportJson, stamp mugirlSaveSchema, jobPolicies "endInFlight". Add K1 rows for the losses listed above.
- A2: provide the legacy token set (-TokenSet) and the offline --save-corpus reports as SaveFixtures\<id>\offline-report.json; Test-InGame checks parity with them automatically.

OPEN RISKS
- Corporate network state is almost empty in the fixtures (the representative had not arrived).
- Legacy-build log errors (load errors, missing textures in 0920, Eye hediffs in 0.5.0) are recorded in the manifests, not treated as failures.
- The allowlist is uncalibrated (1 entry). No CoreOnly, X-alias or X-settings fixtures exist; X-newer is generated at boot 2.
- The diff normalizations come from P fixtures only. lastJobGiverKey may need classifying as vanilla.
- The "seeded Mugirls stay alive" check failed once on P-1004 (one Mugirl died during the 2500 ticks), which may not be an upgrade fault.
- The game is 1.7.4979.
- Cleanup is done: my work dirs and superseded results are removed; no lock and no __Mugirl* mods remain.
