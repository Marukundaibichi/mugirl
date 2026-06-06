# Behavior Map Baseline

Generated: 2026-06-06

This file records player-visible behavior that must be preserved unless a behavior-change proposal is approved. It is a baseline for refactoring, not a redesign document.

## Global Settings

Refactor update 2026-06-07:

- User confirmed that the R18 button must be removed and R18 content must be permanently enabled.
- The historical `enableAdultContent` baseline below is no longer protected behavior for the refactor.

Source examples:

- `1.6/Source/WRace/bondageclothes束具的编程/Mod.cs`

Current behavior:

- The mod settings category is `牛牛模组控制`.
- `enableStructuralCrashEvent` defaults to `true`.
- `enableAdultContent` defaults to `false`.
- `enableFastMilking` defaults to `false`.
- Turning adult content off immediately calls adult-content cleanup with worn apparel removal.

Protected behavior:

- Setting names, defaults, and visible effects remain unchanged unless confirmed.
- The adult-content setting is explicitly confirmed as changed: no visible button, no saved setting, no adult-content cleanup, and no runtime filtering.

## MooGirl Identity

Source examples:

- `1.6/Source/WRace/Defof/MooGirl_DefOf.cs`
- `1.6/Source/WRace/Mounting骑乘系统的编程/MountedPawnUtility.cs`
- `1.6/Source/WRace/NewBron/MooGirlJuvenileGraphicUtility.cs`

Current behavior:

- Some systems identify MooGirl pawns by `pawn.def == MooGirl_DefOf.MooGirl`.
- Some systems identify them by `RaceProps.body == MooGirl_DefOf.MooGirlBody`.
- Some systems accept either check.

Protected behavior:

- Refactoring must not silently widen or narrow the affected pawn set.
- Any unified identity rule needs explicit review against all previous call sites.

## Milk And Nurture

Source examples:

- `1.6/Source/WRace/MooGirlMilked 挤奶相关的编程/CompMooHasBodyResource.cs`
- `1.6/Source/WRace/MooGirlMilked 挤奶相关的编程/CompMooMilkable.cs`
- `1.6/Source/WRace/MooGirlMilked 挤奶相关的编程/CompProperties_MilkingDevice.cs`
- `1.6/Source/WRace/MooGirlMilked 挤奶相关的编程/Harmony_MooGirlBabyFeeding.cs`
- `1.6/Source/WRace/MooGirlMilked 挤奶相关的编程/MooGirlNurtureUtility.cs`

Current behavior:

- Milk fullness grows over time while the component is active.
- A pawn is active for milk if base active checks pass, the pawn is humanlike, reproductive, and passes the configured female-only rule.
- Fullness is inspected as a percentage in the pawn inspect string.
- Manual gathering consumes milk and may waste product based on `AnimalGatherYield`.
- Fixed gathering consumes at most 20 percent fullness per use.
- Default automatic milking threshold is 80 percent.
- Active milkable pawns auto-receive `MooGirl_Lactation` if the hediff exists.
- Milking device integration can accept full milk automatically.
- Milk can be drunk, fed to downed pawns, and used for baby/child nurture flows.
- DevMode exposes milk-fill gizmos.

Protected behavior:

- Fullness math, thresholds, item output, hediff effects, thought effects, and DevMode commands must remain value-identical unless confirmed.
- The current effective behavior of breast-size multipliers must be preserved first, even where comments imply a different intent.

## Milk Visuals

Source examples:

- `1.6/Source/WRace/MooGirlMilked 挤奶相关的编程/MooGirlMilkingAnimation.cs`
- `1.6/Source/WRace/MooGirlMilked 挤奶相关的编程/CompMooMilkable.cs`

Current behavior:

- Milking has custom draw offsets/transforms.
- Milk effects can spawn filth, text motes, flecks, and sounds.

Protected behavior:

- Animation timing, draw positions, fleck/sound timing, and spawned filth behavior are considered player-visible.

## Restraints And Bondage Apparel

Source examples:

- `1.6/Source/WRace/bondageclothes束具的编程/奴隶服装的自定义属性类SlaveApperalDef.cs`
- `1.6/Source/WRace/bondageclothes束具的编程/SlaveApperal_extensions.cs`
- `1.6/Source/WRace/bondageclothes束具的编程/JobDriver_UnlockSlaveApperalGear.cs`
- `1.6/Source/WRace/bondageclothes束具的编程/JobDriver_CrackBondageGear.cs`
- `1.6/Source/WRace/bondageclothes束具的编程/AdultContentControl.cs`

Current behavior:

- Slave apparel can be locked/unlocked.
- Advanced slave apparel has separate crack/unlock behavior.
- Keys and targetable use effects can interact with worn slave apparel.
- Some apparel applies or removes hediffs on target body parts.
- Historical baseline: adult content could be filtered from maps, caravans, world objects, trader stock, and worn apparel.
- Refactor update 2026-06-07: R18 content is now permanent, so filtering and cleanup behavior is deliberately removed.
- DevMode exposes developer unlock actions.

Protected behavior:

- Lock counts, key behavior, crack behavior, equipped hediffs, apparel cleanup scope, and DevMode commands must remain unchanged unless confirmed.
- C# type spelling can be fixed in the new architecture, but XML class mappings and defName changes need separate review.

## Advanced Restraint Devices

Source examples:

- `1.6/Source/WRace/bondageclothes束具的编程/comp/电击项圈CompProperties_ShockCollar.cs`
- `1.6/Source/WRace/bondageclothes束具的编程/comp/磁力镣铐CompProperties_MagneticShackles.cs`
- `1.6/Source/WRace/bondageclothes束具的编程/comp/脑控头盔组件的属性类CompProperties_BrainwashHelmet.cs`
- `1.6/Source/WRace/bondageclothes束具的编程/HediffCompProperties_PerformanceEffect.cs`

Current behavior:

- Shock collar has random and manual shock behavior, cooldowns, power-shock chance, sounds, messages, and saved timers.
- Magnetic shackles can activate/deactivate, add/remove bind hediffs, play sounds, send messages, and save cycle state.
- Brainwash helmet can trigger conversion/effect behavior, ideology adoption when active, performance effects, sounds, messages, and saved timers.
- Brainwash performance can run through a GameComponent and through hediff comp state.

Protected behavior:

- Device state machines must preserve timings, random chances, messages, hediff application, and manual command availability.

## Roping

Source examples:

- `1.6/Source/WRace/RopingAndRoped牵引系统的编程/JobDriver_RopeMoo.cs`
- `1.6/Source/WRace/RopingAndRoped牵引系统的编程/JobDriver_RemoveRopeMoo.cs`
- `1.6/Source/WRace/RopingAndRoped牵引系统的编程/JobDriver_RopeToWallRopeHitch.cs`
- `1.6/Source/WRace/RopingAndRoped牵引系统的编程/Harmony_RopingTick.cs`
- `1.6/Source/WRace/RopingAndRoped牵引系统的编程/Harmony_RopingDraw.cs`
- `1.6/Source/WRace/RopingAndRoped牵引系统的编程/Harmony_PreventEscape.cs`
- `1.6/Source/WRace/RopingAndRoped牵引系统的编程/Harmony_Door_PawnCanOpen.cs`

Current behavior:

- MooGirl pawns can be roped to another pawn or to a wall rope hitch.
- Roped pawns follow a roper through a custom follow job.
- Roping affects escape behavior, door opening, restraints info, gizmos, thoughts, and drawing.
- Some roping logic derives state from current jobs and map pawn scans.

Protected behavior:

- Roping interactions, restrictions, thoughts, and rope drawing remain behaviorally identical until a relationship-state redesign is confirmed.

## Mounting

Source examples:

- `1.6/Source/WRace/Mounting骑乘系统的编程/Comp_MooGirlMount.cs`
- `1.6/Source/WRace/Mounting骑乘系统的编程/MountedPawnUtility.cs`
- `1.6/Source/WRace/Mounting骑乘系统的编程/MountedPawnCombatTurret.cs`
- `1.6/Source/WRace/Mounting骑乘系统的编程/MountedPawnMeleeSupport.cs`
- `1.6/Source/WRace/Mounting骑乘系统的编程/Harmony_MountRendering.cs`

Current behavior:

- Humanlike riders can mount eligible MooGirl pawns.
- Ropes are broken before mounting.
- Mounted riders are stored in a one-stack ThingOwner container.
- Riders can be selected or dismounted through gizmos.
- Auto-dismount and safety checks run periodically.
- Mounted riders can tick physiology and use ranged/nearby melee support.
- Ranged support temporarily uses carrier stance/caster behavior and target selection from the carrier position.
- Rendering draws rider and equipment at offsets derived from carrier rotation and head offset.

Protected behavior:

- Mount eligibility, dismount cell search, auto-dismount reasons, weapon behavior, draw positions, and selection behavior must not change silently.
- `Verb.caster` cleanup may be hardened internally, but combat outcome changes require confirmation.

## Genes, Birth, And Life Stages

Source examples:

- `1.6/Source/WRace/GeneAndAbility/MooGirl_XenotypeFix_GameComp.cs`
- `1.6/Source/WRace/NewBron/MooGirlJuvenileGraphicUtility.cs`
- `1.6/Source/WRace/NewBron/LifeStageWorker_HumanlikeAdult_MooGirl.cs`

Current behavior:

- Biotech-gated xenotype correction can apply MooGirl xenotype to related female offspring.
- A GameComponent scans and corrects xenotype in some cases.
- Juvenile body type and backstory can be normalized for MooGirl pawns.

Protected behavior:

- Biotech-disabled games remain silent.
- Existing xenogenes or unique xenotypes are not cleared.
- Birth and life-stage visual behavior must be preserved unless confirmed.

## Abilities

Source examples:

- `1.6/Source/WRace/GeneAndAbility/JobDriver_CastCharge.cs`
- `1.6/Source/WRace/GeneAndAbility/CompProperties_AbilityEffect_ForceJob.cs`

Current behavior:

- Charge/jump ability can dismount a mounted rider before forcing jump behavior.
- It applies existing random damage, smoke, and movement behavior.
- Ability effect can force a configured job on targets.

Protected behavior:

- Damage ranges, smoke counts, jump behavior, target rules, and job forcing behavior remain unchanged.

## Incidents, Quests, And Factions

Source examples:

- `1.6/Source/WRace/MooGirlsJoins加入方式的编程/用于管理与坠机任务相关的游戏状态和逻辑MooGirl_GameCom.cs`
- `1.6/Source/WRace/MooGirlsJoins加入方式的编程/QuestNode_Root_MooGirl_OpeningPodCrash.cs`
- `1.6/Source/WRace/MooGirlsJoins加入方式的编程/QuestNode_Root_MooGirl_WandererJoin_WalkIn.cs`
- `1.6/Source/WRace/MooGirlsJoins加入方式的编程/QuestNode_MooGirl_RefugeePodCrash.cs`
- `1.6/Source/WRace/MooGirlsJoins加入方式的编程/QuestPart_CourierRaid.cs`
- `1.6/Source/WRace/MooGirlsJoins加入方式的编程/StockGenerator_MooGirl_Slaves.cs`

Current behavior:

- Structural crash event is controlled by the mod setting.
- Opening pod crash, wanderer join, refugee pod crash, wild slave, and slave stock generation use custom pawn kinds.
- Generated pawns may have ideology, apparel, faction, backstory, and hediff normalization.
- Giant corporation courier raid has dialog, item drops, compensation/fight outcomes, and quest signals.
- Some legacy faction/quest recovery logic exists in the current GameComponent.

Protected behavior:

- New-game event behavior, pawn generation results, rewards, inventory, and faction outcomes remain unchanged.
- Legacy save recovery is not protected because old-save compatibility is not required.

## Rendering And Other Cross-Cutting Patches

Source examples:

- `1.6/Source/WRace/OtherFunction/Harmony_GhoulRenderingRefresh.cs`
- `1.6/Source/WRace/OtherFunction/Harmony_Apparel_DrawColor.cs`
- `1.6/Source/WRace/OtherFunction/Harmoney_GridsUtility_IsPolluted.cs`
- `1.6/Source/WRace/OtherFunction/Harmoney_DrugAdministerDefs.cs`
- `1.6/Source/WRace/OtherFunction/Harmoney_SetForbidden.cs`

Current behavior:

- Some hediff changes refresh rendering for MooGirl pawns.
- Apparel draw color can be overridden.
- Pollution and plant ticking behavior are patched.
- Milk administer recipe generation is patched.
- Forbidden setting behavior is patched.

Protected behavior:

- Cross-cutting patches must be narrowed to their intended cases but cannot change visible behavior without review.

## Compatibility Content

Source examples:

- `1.6/FacialAnimation`
- `Versions/1.6/Integrations/SearchAndDestroy`
- `Versions/1.6/Integrations/VCookE`

Current behavior:

- Facial Animation XML provides face/shape/animation content.
- Search and Destroy and VCookE integration content lives under `Versions/1.6/Integrations`.

Protected behavior:

- Optional integrations must remain optional.
- Missing integration mods must not produce load errors.
