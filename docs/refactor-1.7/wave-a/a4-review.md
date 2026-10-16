# A4 adversarial review (tag a4r, 2026-10-15)

Scope: build policy (K3), SDK pin, explicit Harmony order (C08/F6), Validate -WorkRoot, SmokeTest lock (K5), release script,
bootstrap files. Reviewed the ACTUAL files against `DevData/Backups/PreRefactor-1.7-20261015/tree.zip` (extracted to
`scratchpad/a4r/base`). No repo file was edited.

## Verification done by the reviewer

- **Ownership:** diff vs tree.zip. All A4 edits are inside the owned set. The 17 patch files differ ONLY by added
  `[HarmonyPriority]` lines (21 lines). Other changed files (`SaveUpgrade/**`, `Tests/MugirlRace.Validation/**`,
  `Tests/MugirlRace.GameValidation/**`, `tools/Test-InGame.ps1`, `tools/Export-LegacySchema.ps1`) belong to A1/A2/A5
  (timestamps after A4's report or A5/A2-owned). No edits outside ownership found.
- **Build:** `agent-build.ps1 -Tag a4r -Project MugirlRace.slnx -AllWarnings` on the LIVE tree fails with 19 CS0246 in
  A1's in-progress `SaveUpgrade/Engine/**` (not A4). So I built an isolated tree = baseline Source + exactly the 25
  A4-modified files (`%TEMP%\mg17-agent\a4r-iso`, deleted afterwards). From the tree ROOT (outside Source/):
  `dotnet build Source/MugirlRace.slnx -c Release -p:RimWorldDir=… -p:WorkshopDir=…` → **0 warnings, 0 errors**,
  all 6 projects to their own bin/, no `1.7` folder created.
- **Unit tests:** `MugirlRace.Tests.exe` from that build: **473/473 passed**.
- **Deploy:** `-p:DeployToMod=true` → `1.7\Assemblies\MugirlRace.dll/.pdb`, sha256 identical to bin. Plain build:
  no 1.7. `-p:MugirlDeployDir=<dir without trailing slash>` works.
- **SDK guard:** pin changed to 10.0.402 and run from outside Source/ → `Directory.Build.props(84,5): error : Source/global.json pins …`.
  Installed SDKs: 8.0.303, 9.0.315, 10.0.401. `dotnet --version` in Source/ = 10.0.401.
- **Patch order:** re-ran the implementer's `PatchOrderProbe` on MY build (`scratchpad/a4r/iso-{default,all}[-reverse].txt`).
  - TARGET/ORDER lines are identical to the snapshot DLL (`a4/before-*.txt`) in both modes.
  - They are identical under reversed registration.
  - The per-patch priority list is identical to the implementer's `a4/after-*.txt`. 13 rows changed vs the snapshot (399/398/401/0); 12 multi-patch groups; no multi-transpiler targets.
  - Union of modes `default` + `all` covers all 125 classes. `ITab_Pawn_Gear_DrawThingRow_Transpiler` is skipped only in `all`.
- **Harmony 3 `MethodCreatorTools.AffectsOriginal`:** decompiled (context `harmony17-review`). It confirms the G-group rationale: a bool return, out/ref, or a reference-type argument makes a prefix skippable; `__state`/`__instance` do not.
- **Load order:** VEF (loaded before Mugirl) only has a Postfix on `Notify_EquipmentRemoved`, and HAR has none. The Normal+1 move has no effect on the owner's current mod list.
- **Script parsing:** Validate/SmokeTest/New-ReleasePackage parse with 0 errors.

## Findings

1. **MAJOR — K5 lock protocol diverges between SmokeTest (A4) and Test-InGame (A5).**
   - **Evidence, SmokeTest:** `tools/SmokeTest.ps1:102,135` writes one line, `SmokeTest pid=N started=…`.
   - **Evidence, Test-InGame:** `tools/Test-InGame.ps1:277` detects stale locks with `(?m)^pid=(\d+)`, which does not match a `pid=` that is not at the start of a line (checked: `'SmokeTest pid=1234 …' -match '(?m)^pid=(\d+)'` → False).
   - **Evidence, token:** Test-InGame also passes ownership down through `MUGIRL_INGAME_LOCK_TOKEN` (`Test-InGame.ps1:255-258`); SmokeTest ignores that variable.
   - **Failure scenario 1:** a SmokeTest that was killed leaves its lock. Test-InGame never treats it as stale, waits its full timeout, and then fails.
   - **Failure scenario 2:** a parent that holds the lock and runs SmokeTest makes SmokeTest wait 3600 s, then exit 2.
   - **Fix:** SmokeTest adopts A5's format exactly (lines `label=SmokeTest`, `pid=$PID`, `token=<guid>`, `started=…`). It also honours the inherited token, sets it while holding the lock, and releases by token. Better still, keep ONE implementation that both scripts dot-source; the integrator decides where it lives.

2. **MINOR — Race in stale-lock takeover (both scripts).**
   - **Evidence:** `SmokeTest.ps1:145-149` and `Test-InGame.ps1:276-281`. The script reads the holder, finds its PID dead, then `Remove-Item` deletes whatever file is there at that moment.
   - **Failure scenario:** two waiters see the same stale lock. Waiter B deletes it and creates its own. Waiter A then deletes B's fresh lock and creates its own, so both runners start RimWorld.
   - **Fix:** open the lock with `FileShare.None` (and `FileOptions.DeleteOnClose`), re-read it, and delete it only if the content still equals what was judged stale. Alternatively, `File.Move` it to a unique name and verify the content before discarding it.

3. **MINOR — SmokeTest checks the DLL before taking the lock.**
   - **Evidence:** `SmokeTest.ps1:226-234` (existence, SHA-256, staleness) runs before `Enter-InGameLock` (`:236`).
   - **Failure scenario:** while SmokeTest waits, the lock holder (for example `Test-InGame -Deploy`) replaces `1.7/Assemblies/MugirlRace.dll`. The logged SHA-256 is then not the DLL that was booted, and that hash is the evidence record.
   - **Fix:** move the DLL block after `Enter-InGameLock`, next to the RimWorld-running check.

4. **MINOR — The deploy command bypasses the SDK pin.**
   - **Evidence:** the command `dotnet build Source/MugirlRace/MugirlRace.csproj -c Release -p:DeployToMod=true` appears in `MugirlRace.csproj:9`, `SmokeTest.ps1:18,103` and `New-ReleasePackage.ps1:21,68`. It is relative to the repo root, so dotnet does not read `Source/global.json`.
   - **Inconsistency:** this contradicts `Validate.ps1` (`Invoke-DotnetBuild` uses Push-Location Source). It works today only because 10.0.401 is the newest SDK installed. Once SDK 11 is installed, the advertised command, and K3's literal `dotnet build Source/MugirlRace.slnx` from the root, fail on `MugirlCheckSdkPin`.
   - **Also:** `New-ReleasePackage.ps1:172` records `sdk = dotnet --version` from the caller's working directory, not the SDK the pin selects.
   - **Fix:**
     - Advertise the command as "in Source/: `dotnet build MugirlRace/MugirlRace.csproj -c Release -p:DeployToMod=true`".
     - Run `dotnet --version` inside `Push-Location Source`.
     - Document in Wave D that K3's "anywhere" means "from Source/ or with the pinned SDK being the newest".

5. **MINOR — The stale-comment list is incomplete, and one comment contradicts the code.**
   - **Missed comment:** `a4-patch-order.md §6` lists five stale comments but misses `Features/WeaponWheel/Patches/Harmony_WeaponWheel_Verbs.cs:6-7`. It says "补丁类名决定 HarmonyBootstrap 的应用顺序（FullName 序），改名前复查同目标补丁", which is now false.
   - **Contradicting comment:** `Features/Restraints/Patches/Harmony_PawnGenerator_Patch.cs:6` ("类型名排在…之后，仍按 1.6 的顺序最后执行") sits directly above the new `[HarmonyPriority(Priority.Last)]` and contradicts it.
   - **Fix:** add the missed file to §6 and to the REQUESTS. The integrator or Wave S rewords both comments to "order pinned by [HarmonyPriority]".

6. **MINOR — The spec conflict is not raised, so the priorities are at risk in Wave S.**
   - **Evidence:** `spec/combat-plan.md:228-236` (S2-8) says "no gratuitous priorities". It lists Attribute "none" for `DynamicDrawPhaseAt`, `get_WarmupTime`, `Notify_EquipmentRemoved` and `GeneratePawn`, and documents that GeneratePawn order changes the RNG draw order.
   - **Precedence:** decisions C08/F6 win, but the plan is not amended. A Wave S combat implementer following S2-8 would delete A4's attributes.
   - **Fix:** a REQUEST to the orchestrator to amend combat-plan S2-8 ("keep A4's priorities; golden = a4/after-all.txt"). A REQUEST to A2 that `B.patch-order` also asserts that the restraint lock has the minimum priority on `GeneratePawn` (C08). Until that rule lands, nothing in the repo guards the order; the probe exists only in the scratchpad.

7. **MINOR — A relative `-WorkRoot` resolves against the process directory.**
   - **Evidence:** `Validate.ps1:105` uses `[IO.Path]::GetFullPath($WorkRoot)`. That resolves against `[Environment]::CurrentDirectory`, not `$PWD`.
   - **Failure scenario:** in an interactive pwsh session after `cd`, `-WorkRoot .\vroot` points elsewhere, and the script creates and clears `mg17-*` folders there.
   - **Fix:** use `$PSCmdlet.GetUnresolvedProviderPathFromPSPath($WorkRoot)`.
   - **Nit, same check:** the "inside the mod folder" test compares strings only, so a junction into the mod folder passes.

8. **MINOR — The K4 REQUEST omits a required ordering.**
   - **Requirement:** decisions §2 requires a startup check that `Harmony.GetPatchInfo` shows the LoadGame/FinalizeLoading/FinalizeInit/BackCompatibleDefName patches owned by `MugirlMod.Mod`.
   - **Conflict:** K4 places `SaveUpgradeBootstrap.Initialize(this)` before `GetSettings`, which is before `HarmonyBootstrap.ApplyAtStartup`. A check inside `Initialize` would always fail.
   - **Fix:** the REQUEST to the integrator and A1 states the order: Initialize (settings upgrade only) → GetSettings → ApplyAtStartup → installation check. The check runs after patching, through a second entry point or `ExecuteWhenFinished`. The comment at `MugirlMod.cs:14-15` is updated with the call.

9. **NIT — The SDK guard is lenient and fragile.**
   - **Prerelease:** `Directory.Build.props:80` strips `-preview…` before comparing, so a 10.0.5xx-preview SDK passes although global.json says `allowPrerelease:false`.
   - **Parsing:** `:75` takes the first `"version"` anywhere in global.json.
   - **Fix:** error when `NETCoreSdkVersion` contains `-`, and anchor the regex on the `"sdk"` object.

10. **NIT — An empty folder appeared in the live tree.** `Source/MugirlRace/bin/Debug` was created at 12:26 during A4's work (gitignored, harmless). Remove it if this session's tooling created it (AGENTS cleanup rule).

11. **NIT — The registration order is implemented twice.** The "non-deferred, then deferred" partition is coded separately in `HarmonyBootstrap.cs:61` (`Apply`) and `:94-104` (`ApplyAtStartup`). One private helper would keep the offline and in-game orders from drifting.

## Accepted as correct

- K3 csproj and deploy target.
- The `HarmonyAssembliesDir` fix: `-HarmonyDir` was previously ignored by the build.
- The `-WorkRoot` plumbing: all `$temp` uses are replaced, the default paths are unchanged, and Reset-TempDirectory is confined to the work root.
- Validate starts dotnet from Source/.
- The step-down priority scheme and the G exception, with sound reasoning.
- C08: the restraint lock is at Last.
- Doc-comment cleanup in HarmonyBootstrap, MugirlMod and MugirlPatchAttribute: no history comments.
- K4 left as a REQUEST, which is correct because `SaveUpgradeBootstrap` still does not exist.
- The REQUESTS to A5 about the GameValidation guard, the bin path and the deploy flags are accurate.
