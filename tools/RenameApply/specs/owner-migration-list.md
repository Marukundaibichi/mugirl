# Owner migration list: names WhiteFluff / Subspecies validation code uses that change

Source: `tools/RenameApply/specs/public-api.json#validationOnly` (dependent scan 2026-10-15; actual post-R2/S targets checked 2026-10-16).
These names are used only by the dependents' own validation code (WhiteFluff `Source/Validation/**`, compiled only with
`EnableValidation=true`, and `Tools/**`). They are not public API (decisions.md section 5). The names below have changed;
update dependent validation code when porting to 1.7. Production name contracts remain frozen; dependent 1.6 assemblies still need their own 1.7 compatibility verification.

`typeNameLiteral` rows are string literals compared with `GetType().Name`: they keep compiling and silently stop matching.

| kind | old name | new name | used at (validation only) |
|---|---|---|---|
| member | `Mugirl.Comp_MugirlMount.CanMount` | `Mugirl.Features.Mounting.CompMount.CanMount` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:34` |
| member | `Mugirl.Comp_MugirlMount.HasMountedPawn` | `Mugirl.Features.Mounting.CompMount.HasMountedPawn` (retained facade) | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:46` |
| member | `Mugirl.Comp_MugirlMount.MountedPawn` | `Mugirl.Features.Mounting.CompMount.MountedPawn` (retained facade) | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:40`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:50` |
| member | `Mugirl.Comp_MugirlMount.TryDismount` | `Mugirl.Features.Mounting.CompMount.TryDismount` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:44`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:50` |
| member | `Mugirl.Comp_MugirlMount.TryEmergencyDismountNear` | `Mugirl.Features.Mounting.CompMount.TryEmergencyDismountNear` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:51` |
| member | `Mugirl.Comp_MugirlMount.TryMount` | `Mugirl.Features.Mounting.CompMount.TryMount` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:38` |
| member | `Mugirl.Comp_MugirlMount.innerContainer` | Use `CompMount.GetDirectlyHeldThings()`; the owned container is now private `mugirlMountRiders` and remains `ThingOwner<Thing>` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:40`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:53`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:55` |
| member | `Mugirl.Harmony_MugirlGrazeIngest.Prefix` | `Mugirl.Features.Grazing.Harmony_JobDriver_Ingest_PrepareToIngestToils_Graze.Prefix` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:104`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:115` |
| member | `Mugirl.Harmony_MugirlGrazeIngest.TargetMethod` | `Mugirl.Features.Grazing.Harmony_JobDriver_Ingest_PrepareToIngestToils_Graze.TargetMethod` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:107` |
| member | `Mugirl.MountedPawnUtility.IsMounted` | `Mugirl.Features.Mounting.MountUtility.IsMounted` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:41`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:46` |
| type | `Mugirl.Comp_MugirlMount` | `Mugirl.Features.Mounting.CompMount` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:23`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:41`; MRK.WhiteFluff: `Source/Validation/RuntimeChecks.cs:207` |
| type | `Mugirl.Harmony_MugirlGrazeIngest` | `Mugirl.Features.Grazing.Harmony_JobDriver_Ingest_PrepareToIngestToils_Graze` | MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:104`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:107`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:109`; MRK.WhiteFluff: `Source/Validation/InheritanceChecks.cs:115` |
| typeNameLiteral | `"CompMooMilkable" (Mugirl.CompMooMilkable)` | `"CompMilkProduction"` | MRK.WhiteFluff: `Source/Validation/RuntimeChecks.cs:206` |
| typeNameLiteral | `"Comp_WeaponWheel" (Mugirl.Features.WeaponWheel.Comp_WeaponWheel)` | `"CompWeaponWheel"` | MRK.WhiteFluff: `Source/Validation/RuntimeChecks.cs:206` |

Unchanged validation-only names (listed for completeness, nothing to do): `Mugirl_CorporateRepresentative`, `Mugirl_CorporateSupport`, `cold_tight2`, `hungry1`, `hungry2`, `pain2`.

Related changes the owner may want to know about (production code keeps working):

- `Mugirl.CompProperties_MooMilkable`, `Mugirl.CompProperties_MugirlMount`, `Mugirl.StockGenerator_Mugirl_Slaves` stay valid XML class names (public alias subclasses in `PublicApi/XmlAliases.cs`); their XML field names are frozen.
- `Mugirl.MountedPawnUtility.IsMugirl(Pawn)` stays the Harmony-patchable decision point; the other members of that class move to `Mugirl.Features.Mounting.MountUtility` (R2).
- `<Mugirl.SlaveApparelDef>` keeps loading (PublicApi); its business fields now populate `RestraintExtension` during ResolveReferences. An explicitly supplied new extension retains its own configuration.
- A 1.7 build of WhiteFluff must reference the 1.7 `MugirlRace.dll` (its csproj points at `1.6/Assemblies/MugirlRace.dll`).

The old combat-plan proposals to rename `MooPawn` / `MountedPawn` / `HasMountedPawn` again were not applied. Check the current facades rather than mechanically following those plan names. Public API coverage is the complete 151-entry authority file, read directly by the validator.
