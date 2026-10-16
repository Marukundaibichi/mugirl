using System.Threading;
using Verse.AI;

namespace Mugirl.Features.WeaponWheel;

// 对外组件门面：保留存档回调、每 tick 调度和 IThingHolder 身份；四个内部对象分别维护存储、战斗、剑舞和渲染状态。
// 外部模组通过 WeaponWheelHooks 的内部边界接线，不补丁组件自身的方法。
public sealed partial class CompWeaponWheel : ThingComp, IThingHolder
{
    internal readonly WeaponWheelStorage storage;
    internal readonly WeaponWheelCombat combat;
    internal readonly WeaponWheelSwordDance swordDance;
    internal readonly WeaponWheelRenderCache renderCache;
    private const int MaintenanceIntervalTicks = 60;
    private int nextMaintenanceTick = -1;
    private bool normalizeAfterLoad;
    public CompWeaponWheel()
    {
        storage = new WeaponWheelStorage(this);
        combat = new WeaponWheelCombat(this);
        swordDance = new WeaponWheelSwordDance(this);
        renderCache = new WeaponWheelRenderCache(this);
        storage.reserveWeapons = new ThingOwner<ThingWithComps>(this);
    }

    public CompProperties_WeaponWheel Props => (CompProperties_WeaponWheel)props;
    public Pawn? Pawn => parent as Pawn;
    public int MaxSlots => Math.Max(1, Props?.maxSlots ?? 6);
    public int ActiveSlotIndex => storage.activeSlotIndex;
    public bool IsBusy => combat.combatState is WeaponWheelCombatState.EquippingPrimary or WeaponWheelCombatState.Switching or WeaponWheelCombatState.SwordDanceSwitching or WeaponWheelCombatState.CycleCooldown or WeaponWheelCombatState.Firing;
    internal WeaponWheelCombatState CombatState => combat.combatState;
    internal ThingOwner<ThingWithComps> ReserveWeapons => storage.reserveWeapons;

    // 只在调试窗口的同步离屏绘制期间启用，不保存、不影响普通肖像。
    internal bool BackLayoutPreviewActive
    {
        get
        {
            return renderCache.BackLayoutPreviewActive;
        }

        set
        {
            renderCache.BackLayoutPreviewActive = value;
        }
    }

    IThingHolder? IThingHolder.ParentHolder => Pawn;

    public override void Initialize(CompProperties props)
    {
        base.Initialize(props);
        storage.EnsureCollections();
    }

    public override void PostPostMake()
    {
        base.PostPostMake();
        storage.EnsureCollections();
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Deep.Look(ref storage.reserveWeapons, "mugirlWheelReserveWeapons", this);
        Scribe_Collections.Look(ref storage.slots, "mugirlWheelSlotRecords", LookMode.Deep);
        Scribe_Values.Look(ref swordDance.swordDanceFailureStreak, "mugirlWheelSwordDanceFailureStreak", 0);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            swordDance.swordDanceFailureStreak = UnityMathf.Clamp(swordDance.swordDanceFailureStreak, 0, WeaponWheelSwordDance.SwordDanceGuaranteeFailures - 1);
            storage.collectionsReady = false;
            storage.EnsureCollections();
            storage.ValidateSlotReferences();
            normalizeAfterLoad = true;
            ResetRuntimeState();
        }
    }

    public override void CompTick()
    {
        TickCore();
        // 与原 Postfix 相同：包括 TickCore 的提前返回；异常时不执行。
        WeaponWheelHooks.Current.TickCompleted(this);
    }

    private void TickCore()
    {
        base.CompTick();
        storage.EnsureCollections();
        Pawn? pawn = Pawn;
        if (pawn == null || pawn.Destroyed || pawn.Dead)
        {
            return;
        }

        if (normalizeAfterLoad)
        {
            normalizeAfterLoad = false;
            storage.NormalizeToPrimarySlot();
        }

        int currentTick = WeaponWheelCombat.CurrentTick;
        if (ShouldRunMaintenance(pawn, currentTick))
        {
            storage.RefreshResearchUnlocks();
            storage.TrackWeaponsRemovedFromReserve();
            storage.TrackExternalPrimaryIfNeeded();
            // 穿脱通知覆盖正常路径；维护周期再兜底重算一次，防止其他模组绕过 Notify_Equipped。
            combat.autoloadingDirty = true;
        }

        if (combat.autoloadingDirty)
        {
            combat.RefreshAutoloadingCache();
        }

        storage.TickReserveVerbs();
        combat.TickMountedState(currentTick);
        combat.TickWeaponOpenState(currentTick);
        swordDance.TickSwordDanceDashAnimation(currentTick);
        swordDance.TickPendingSwordDanceCompletion();
        combat.TickCombatState(currentTick);
        renderCache.RefreshBackWeaponVisibility();
    }

    public override void PostDestroy(DestroyMode mode, Map previousMap)
    {
        base.PostDestroy(mode, previousMap);
        storage.reserveWeapons?.ClearAndDestroyContents(mode);
        storage.slots?.Clear();
        swordDance.ClearSwordDanceDashAnimation();
        renderCache.backWeaponNodes.Clear();
        renderCache.autoloadingWeaponNodes.Clear();
        storage.reserveVerbTickers.Clear();
        renderCache.backWeaponVisibilityMask = -1;
        combat.autoloadingDirty = true;
        storage.reserveMassDirty = true;
    }

    public ThingOwner GetDirectlyHeldThings()
    {
        storage.EnsureCollections();
        return storage.reserveWeapons;
    }

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
    }

    public ThingWithComps? WeaponAt(int index)
    {
        return storage.WeaponAt(index);
    }

    public bool IsSlotUnlocked(int index)
    {
        return storage.IsSlotUnlocked(index);
    }

    public bool UnlockSlotDeveloper(int index)
    {
        return storage.UnlockSlotDeveloper(index);
    }

    public int OccupiedUnlockedSlotCount
    {
        get
        {
            return storage.OccupiedUnlockedSlotCount;
        }
    }

    public float ReserveWeaponMass
    {
        get
        {
            return storage.ReserveWeaponMass;
        }
    }

    internal bool ContainsWeapon(ThingWithComps? weapon)
    {
        return storage.ContainsWeapon(weapon);
    }

    internal int IndexOfWeapon(ThingWithComps? weapon)
    {
        return storage.IndexOfWeapon(weapon);
    }

    private bool ShouldRunMaintenance(Pawn pawn, int currentTick)
    {
        if (nextMaintenanceTick < 0)
        {
            // 按 thingIDNumber 错开各 Pawn 的维护 tick，避免大批 Pawn 同一 tick 集中扫描。
            int stagger = Math.Abs(pawn.thingIDNumber % CompWeaponWheel.MaintenanceIntervalTicks);
            nextMaintenanceTick = currentTick + stagger;
        }

        if (currentTick < nextMaintenanceTick)
        {
            return false;
        }

        nextMaintenanceTick = currentTick + CompWeaponWheel.MaintenanceIntervalTicks;
        return true;
    }

    private void ResetRuntimeState()
    {
        combat.combatState = WeaponWheelCombatState.Holstered;
        combat.ClearTransitionAnimation();
        combat.cooldownUntilTick = -1;
        combat.accumulatedCooldownTicks = 0;
        combat.ClearCycle();
        combat.ClearAimHandoff();
        combat.ClearPendingCast();
        swordDance.ClearPendingSwordDance();
        swordDance.ClearSwordDanceDashAnimation();
        combat.wasWeaponOpenlyHeld = false;
        combat.wasMountedDisabled = false;
        nextMaintenanceTick = -1;
        combat.mountStateCacheTick = int.MinValue;
        combat.autoloadingDirty = true;
        storage.reserveVerbTickersDirty = true;
        renderCache.backWeaponVisibilityMask = -1;
        storage.reserveMassDirty = true;
    }

    internal void RegisterBackWeaponNode(PawnRenderNode_BackWeapon node)
    {
        renderCache.RegisterBackWeaponNode(node);
    }

    internal void RegisterAutoloadingWeaponNode(PawnRenderNode_AutoloadingWeapon node)
    {
        renderCache.RegisterAutoloadingWeaponNode(node);
    }

    internal void MarkBackWeaponsDirty()
    {
        renderCache.backWeaponVisibilityMask = -1;
        storage.reserveVerbTickersDirty = true;
        storage.reserveMassDirty = true;
        renderCache.RequestBackWeaponRecache();
    }

    // 只让轮盘自己的渲染节点重建绘制请求（网格、可见性），用于背负布局调试改动。
    internal void RequestBackWeaponRecache()
    {
        renderCache.RequestBackWeaponRecache();
    }

    // DrawPos 补丁的快速门：没有任何冲刺动画时直接返回，不查组件。
    internal static bool AnySwordDanceDashActive => WeaponWheelSwordDance.AnySwordDanceDashActive;
}
