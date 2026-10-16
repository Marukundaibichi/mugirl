using Mugirl.Features.Mounting;
using Verse.AI;
using Verse.Sound;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.WeaponWheel;

internal sealed class WeaponWheelRenderCache
{
    private readonly CompWeaponWheel owner;
    internal WeaponWheelRenderCache(CompWeaponWheel owner)
    {
        this.owner = owner;
    }

    internal readonly List<PawnRenderNode_BackWeapon> backWeaponNodes = [];
    internal readonly List<PawnRenderNode_AutoloadingWeapon> autoloadingWeaponNodes = [];
    internal int backWeaponVisibilityMask = -1;
    internal bool BackLayoutPreviewActive { get; set; }

    internal ThingWithComps? GetBackWeapon(int displayIndex)
    {
        return displayIndex >= 0 && displayIndex < 2 && displayIndex < owner.storage.slots.Count && owner.storage.slots[displayIndex].Unlocked ? owner.storage.slots[displayIndex].Weapon : null;
    }

    internal bool IsBackWeaponVisible(int displayIndex)
    {
        ThingWithComps? weapon = GetBackWeapon(displayIndex);
        if (weapon == null || weapon == owner.combat.animationOutgoingWeapon || weapon == owner.combat.animationIncomingWeapon)
        {
            return false;
        }

        Pawn? pawn = owner.Pawn;
        if (pawn == null || weapon != pawn.equipment?.Primary)
        {
            return true;
        }

        if (owner.combat.TryGetAimHandoff(out ThingWithComps? handoff, out _) && handoff == weapon)
        {
            return false;
        }

        if (owner.swordDance.TryGetSwordDanceDashHeldWeapon(out ThingWithComps? dash, out _) && dash == weapon)
        {
            return false;
        }

        // 主武器平时在 equipment 中，但未亮出时同样属于背负；瞄准/征召/工作亮枪时隐藏。
        if (pawn.CurJob?.def?.neverShowWeapon != false)
        {
            return true;
        }

        return !(pawn.stances?.curStance is Stance_Busy busy && !busy.neverAimWeapon && busy.focusTarg.IsValid) && !PawnRenderUtility.CarryWeaponOpenly(pawn);
    }

    internal void RefreshBackWeaponVisibility()
    {
        // 只比较两个固定槽位；Job/瞄准变化不一定改变 PawnDrawParms，需显式刷新绘制请求。
        int mask = (IsBackWeaponVisible(0) ? 1 : 0) | (IsBackWeaponVisible(1) ? 2 : 0);
        if (mask == backWeaponVisibilityMask)
        {
            return;
        }

        backWeaponVisibilityMask = mask;
        for (int i = 0; i < backWeaponNodes.Count; i++)
        {
            backWeaponNodes[i].requestRecache = true;
        }
    }

    internal ThingWithComps? GetAutoloadingWeapon(int displayIndex)
    {
        if (displayIndex < 0 || displayIndex >= 4 || !owner.combat.HasAutoloadingSystem)
        {
            return null;
        }

        return WeaponVisibleInStorageSlot(displayIndex + 2);
    }

    private ThingWithComps? WeaponVisibleInStorageSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= owner.storage.slots.Count || !owner.storage.slots[slotIndex].Unlocked)
        {
            return null;
        }

        ThingWithComps? weapon = owner.storage.slots[slotIndex].Weapon;
        if (weapon == null || weapon == owner.Pawn?.equipment?.Primary || weapon == owner.combat.animationOutgoingWeapon || weapon == owner.combat.animationIncomingWeapon)
        {
            return null;
        }

        return weapon;
    }

    internal void RegisterBackWeaponNode(PawnRenderNode_BackWeapon node)
    {
        if (!backWeaponNodes.Contains(node))
        {
            backWeaponNodes.Add(node);
        }
    }

    internal void RegisterAutoloadingWeaponNode(PawnRenderNode_AutoloadingWeapon node)
    {
        if (!autoloadingWeaponNodes.Contains(node))
        {
            autoloadingWeaponNodes.Add(node);
        }
    }

    internal void RequestBackWeaponRecache()
    {
        for (int i = backWeaponNodes.Count - 1; i >= 0; i--)
        {
            backWeaponNodes[i].requestRecache = true;
        }

        for (int i = autoloadingWeaponNodes.Count - 1; i >= 0; i--)
        {
            autoloadingWeaponNodes[i].requestRecache = true;
        }
    }

    private const int BackWeaponNodeCount = 2;
    private const int AutoloadingWeaponNodeCount = 4;
    internal List<PawnRenderNode>? CompRenderNodes()
    {
        Pawn? pawn = owner.Pawn;
        PawnRenderTree? tree = pawn?.Drawer?.renderer?.renderTree;
        if (pawn == null || tree == null)
        {
            return null;
        }

        backWeaponNodes.Clear();
        autoloadingWeaponNodes.Clear();
        List<PawnRenderNode> nodes = new(WeaponWheelRenderCache.BackWeaponNodeCount + WeaponWheelRenderCache.AutoloadingWeaponNodeCount);
        for (int i = 0; i < WeaponWheelRenderCache.BackWeaponNodeCount; i++)
        {
            PawnRenderNodeProperties nodeProps = WeaponWheelRenderCache.NodeProperties("Mugirl weapon wheel back weapon " + i.ToStringCached(), typeof(PawnRenderNode_BackWeapon), typeof(PawnRenderNodeWorker_BackWeapon));
            nodes.Add(new PawnRenderNode_BackWeapon(pawn, nodeProps, tree, owner, i));
        }

        for (int i = 0; i < WeaponWheelRenderCache.AutoloadingWeaponNodeCount; i++)
        {
            PawnRenderNodeProperties nodeProps = WeaponWheelRenderCache.NodeProperties("Mugirl autoloading groove weapon " + i.ToStringCached(), typeof(PawnRenderNode_AutoloadingWeapon), typeof(PawnRenderNodeWorker_AutoloadingWeapon));
            nodes.Add(new PawnRenderNode_AutoloadingWeapon(pawn, nodeProps, tree, owner, i));
        }

        return nodes;
    }

    private static PawnRenderNodeProperties NodeProperties(string debugLabel, Type nodeClass, Type workerClass)
    {
        return new PawnRenderNodeProperties
        {
            debugLabel = debugLabel,
            nodeClass = nodeClass,
            workerClass = workerClass,
            pawnType = PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly,
            useGraphic = true,
            baseLayer = 0f,
            drawSize = new UVector2(1f, 1f)
        };
    }
}
