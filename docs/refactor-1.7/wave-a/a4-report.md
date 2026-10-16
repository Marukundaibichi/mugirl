# A4 report: build, bootstrap and patch order (tag a4)

## What I built
- **K3.** `Source/MugirlRace/MugirlRace.csproj` now outputs only to `Source/MugirlRace/bin/<Config>/`. A new target, `MugirlDeployToMod`, runs only when `-p:DeployToMod=true` is set. It copies the dll and pdb to `$(MugirlDeployDir)`, which defaults to `<mod>\1.7\Assemblies\` and can be overridden. If no pdb was built, it deletes the stale pdb in the target folder.
- `Source/Directory.Build.props`:
  - Adds a `HarmonyAssembliesDir` input. Before this change, `Validate.ps1 -HarmonyDir` had no effect on the build.
  - Adds a `MugirlCheckSdkPin` error. `global.json` only applies when dotnet starts inside `Source/`, so the check reads the pin from that file.
- `Source/global.json`: pins SDK 10.0.401 with `rollForward latestFeature` and `allowPrerelease false`. The test runner setting is kept.
- Fixed the stale "not in slnx" comments in the Tests and ShaderCheck csproj files. The slnx itself needed no change.
- **Patch order.** I added 21 method-level `[HarmonyPriority]` lines in 17 patch files. The restraint lock gets `Priority.Last` (C08). Derivation: `scratchpad/waveA/a4-patch-order.md`.
- **Core files:**
  - `HarmonyBootstrap.cs`: `Apply` now registers deferred classes last, the same as in game. The doc comments now state the priority-only rule and no longer describe history.
  - `MugirlPatchAttribute.cs`, `MugirlMod.cs`: comment fixes only.
- **`tools/Validate.ps1`:** new `-WorkRoot` parameter. The default `%TEMP%` keeps today's paths. All temp folders go under the work root, and the script refuses a work root inside the mod folder. dotnet now starts from `Source/` so the SDK pin applies.
- **`tools/SmokeTest.ps1`:** adds the K5 lock (`CreateNew`, contents `SmokeTest pid=N`, stale lock removed when that process no longer exists, `-LockTimeoutSeconds`). Its messages give the deploy command.
- **`tools/New-ReleasePackage.ps1`:** its messages give the deploy command.

## Verification
- **Solution build:** `agent-build.ps1 -Tag a4 -Project MugirlRace.slnx -AllWarnings`. All projects except GameValidation build with 0 warnings; GameValidation fails with 4 errors from A5's in-progress work. MugirlRace goes to `bin\Release`, and no `1.7` folder is created.
- **Unit tests:** `agent-build.ps1 -Tag a4 -Project Tests/MugirlRace.Tests/MugirlRace.Tests.csproj -RunTests` → 473/473 passed.
- **Deploy and SDK guard, in a temp copy:**
  - A plain build writes nothing to `1.7`.
  - `-p:DeployToMod=true` puts the dll and pdb into `1.7\Assemblies`.
  - A custom deploy folder without a trailing slash, built with `DebugType=none`, ends with the stale pdb removed.
  - A pin of 10.0.402, built from outside `Source/`, fails with the guard error.
- **Live-folder simulation** (`About.xml` present): `dotnet build MugirlRace.slnx` fails only on GameValidation's `GuardLiveModFolder`.
- **Patch order:** I wrote an offline probe (`scratchpad/a4/PatchOrderProbe`) that applies the classes in the in-game order and runs Harmony's own `PatchSorter`.
  - The effective order before (snapshot DLL `220E5D1A…`) and after is identical: 7 target/kinds in mode default, 12 in mode all.
  - Registering the classes in reverse order gives the same result on the new DLL. On the snapshot DLL, reversal flips 10 of 12 target/kinds, so the probe does detect order dependence.
  - Only the 13 intended patch rows changed priority.
- **`tools/Validate.ps1 -WorkRoot %TEMP%\mg17-a4\vroot`:** 42 of 43 checks pass. Only A.about fails, the same as the Wave 0 baseline. Both runtimes agree, unit tests and the shader check pass, and B.harmony-apply passes. `-SelfTest` with its own work root passes all 114 rules.
- **Lock functions:** tested on their own (`scratchpad/a4/locktest.ps1`): free lock, stale lock, timeout on a held lock, and a foreign lock left in place.

## Deviations
- Priorities are on the patch method, not the class, so no other target or kind changes.
- Each group keeps its first patch's priority and the later patches step down (−1, −2). Patches never move ahead of same-priority patches from mods loaded before Mugirl.
- **Exception, `Notify_EquipmentRemoved`:** the wheel's bool prefix keeps Normal and the mounted prefix gets Normal+1. This keeps unchanged which later prefixes Harmony 3 skips during wheel transfers.
- Patches that already ran at the default now carry an explicit `Priority.Normal`. This changes nothing at runtime.
- `MugirlMod.cs` does not call the K4 entry point yet, because `SaveUpgradeBootstrap` does not exist (see REQUESTS).

## REQUESTS
- **A5, GameValidation csproj:** delete `GuardLiveModFolder` and the comments saying the project is not in the slnx or that the referenced project writes to `1.7/Assemblies`. Also fix the current compile errors (`ValidationSession.cs:101/428/432`, `StartupSuite.cs:54`).
- **A5, `Test-InGame -BuildMod`:**
  - The built DLL is now at `<copy>\Source\MugirlRace\bin\Release\MugirlRace.dll`.
  - For `-Deploy`, use `dotnet build …MugirlRace.csproj -c Release -p:DeployToMod=true -p:MugirlDeployDir=<ModRoot>\1.7\Assemblies\`.
  - For the lock, create it with `CreateNew` and include `pid=<n>` in the contents.
- **Integrator (K4):** in the `MugirlMod` constructor, add `Mugirl.SaveUpgrade.SaveUpgradeBootstrap.Initialize(this);` before `GetSettings`. `SaveUpgrade/` currently holds only `SaveSchema.cs` and `Engine/Names/SaveTypeNames.cs`.
- **A2:** `B.patch-order` can read the order from `Apply`, which now matches in-game registration. My reference output is `scratchpad/a4/after-all.txt`.
- **Wave S owners:** five comments about patch order are now stale (listed in a4-patch-order.md §6).
- **Docs (Wave D):** document that a plain build no longer deploys, the deploy command, starting dotnet from `Source/`, `-WorkRoot`, and the lock.

## Open risks
- Against other mods' patches at priority 400, the patches moved to 399, 398 or 401 change position, and the restraint lock now runs after every other mod's GeneratePawn postfix (documented in a4-patch-order.md §4).
- The SDK guard turns a build started outside `Source/` on another SDK band (for example SDK 11 from the repo root) into a build error.
- The IDE created an empty `Source/MugirlRace/bin/Debug`. It is gitignored.

---

# A4 fixer report (tag a4, 2026-10-15)

## Fixes

- **#1 SmokeTest lock:** writes A5's format (`label=`/`pid=`/`token=`/`started=`).
  - Honours `MUGIRL_INGAME_LOCK_TOKEN`: a parent's token means no waiting and no release.
  - Releases by token, with retries; also reads the old one-line lock format.
- **#2 Takeover race:** a stale lock is opened `FileShare.None`, re-read, and rewritten in place only if unchanged; it is never deleted.
- **#3:** the DLL hash and age are checked after the lock is taken.
- **#4:** the deploy command (csproj, SmokeTest, New-ReleasePackage) and the manifest's `dotnet --version` now run in `Source/`.
- **#5/#6:** `a4-patch-order.md` §6 adds `Harmony_WeaponWheel_Verbs.cs:6-7`, the contradicting restraint comment, and requests for the S2-8 amendment and the C08 assertion.
- **#7:** `-WorkRoot` resolves against the PowerShell location; the mod-folder check compares real paths, so junctions are caught.
- **#8 K4:** `SaveUpgradeBootstrap` now exists, so `MugirlMod` calls it before `GetSettings`. Its `Verify` (queued with `ExecuteWhenFinished`) runs after the constructor-applied, non-deferred hooks. A comment documents the order.
- **#9 SDK guard:** reads only `sdk.version`, needs a release pin, and rejects prerelease SDKs.
- **#11:** the immediate/deferred split is computed once in `Discover`, shared by `Apply` and `ApplyAtStartup`.

## Rejected

- **#10:** `bin/Debug` and `obj/Debug` came from the IDE's design-time build (same timestamp), not agent tools. They are gitignored and get recreated.

## Verification

- **agent-build (slnx, `-AllWarnings`):** 6 projects, 0 warnings, 0 errors. Later runs fail only on A1's in-progress `TypeTableTests.cs:25`.
- **Temp tree, from its root:** `dotnet build Source/MugirlRace.slnx` with warnings as errors (A1's usings shimmed in the copy only).
  - Result: 0 warnings, 0 errors, no `1.7`.
  - Deploy from `Source/`: hashes equal `bin/`.
- **Tests:** 473/473 existing pass. A1's 43 new tests: 2 fail (theirs).
- **SDK guard, 7 cases correct:**
  - Builds: pinned SDK; `test.version` placed first; later feature band.
  - Fails: 10.0.402 pin; prerelease pin; prerelease SDK; SDK 11.
- **Patch order:** matches the snapshot in both modes (7 and 12 groups), also via `HarmonyBootstrap.Apply` itself and in reversed registration. Priorities are unchanged; A1's 8 hooks have their own targets.
- **`a4f/locktest.ps1`:** 20/20, including a 4-process race over 4 rounds with no overlap.
- **`Validate.ps1`:**
  - A relative path or a junction into the mod folder is refused.
  - `-WorkRoot %TEMP%\mg17-a4\vroot`: 15/45 fail, identically on both runtimes, all from A1/A2 in-progress work (new rules, stale baselines, missing golden).
  - `B.harmony-apply` passes; `B.patch-order` fails only on the missing golden; shader check passes; unit-test build blocked by A1.
  - Log: `a4f/validate.log`. Temp trees deleted.

## REQUESTS

- **A5:** adopt the exclusive re-check takeover in `Test-InGame.ps1` and `New-SaveFixture.ps1`.
- **Integrator:** one shared lock implementation.
- **A1:** add the missing usings in `Tests/SaveUpgrade`; fix `RemovingTheSameNodeTwiceAppliesOnce` and `DroppingAThingDefDropsItsImpliedDefs`.
- **A2:** create `Baselines/patch-order.txt` (check against `a4/after-all.txt`); add the C08 rule and the 8 hooks to patch-targets.
- **Orchestrator:** amend S2-8.
- **Wave S:** reword the §6 comments.
- **Docs:** deploy from `Source/`.

## Risks

- **SDK guard:** repo-root builds fail once a newer SDK major is installed; building from `Source/` works.
- **A5 lock race:** remains until A5 adopts the takeover.
