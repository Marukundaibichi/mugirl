using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Abilities;

// 投掷与扣篮共用的游戏状态查询：举起力量与质量、举起条件与控制器查找。数值公式见 ThrowBalance，落点搜索见 PayloadPlacement。
internal static class ThrowUtility
{
    internal static float ThrowStrength(Pawn? pawn)
    {
        return pawn is null ? 0f : UnityMathf.Max(0f, pawn.GetStatValue(StatDefOf.CarryingCapacity));
    }

    internal static float EffectiveMass(Thing? thing)
    {
        if (thing is null)
        {
            return 0f;
        }

        float mass = UnityMathf.Max(0.1f, thing.GetStatValue(StatDefOf.Mass));
        if (thing is not Pawn)
        {
            mass *= Math.Max(1, thing.stackCount);
        }

        if (thing is Mineable)
        {
            // 山体始终有完整耐久度形成的重量下限；显式 Mass 较高时仍保留原值。
            mass = ThrowBalance.MineableMass(mass, thing.MaxHitPoints);
        }
        else if (thing.def.category == ThingCategory.Building && !thing.def.statBases.StatListContains(StatDefOf.Mass))
        {
            // 墙等无重量建筑也按完整耐久度赋予吨级重量；材料和占地仍是下限。
            mass = ThrowBalance.UnweightedBuildingMass(mass, thing.MaxHitPoints, thing.def.Size.x, thing.def.Size.z, thing.def.costStuffCount);
        }

        return mass;
    }

    internal static float MaxThrowDistance(Pawn? pawn, float mass)
    {
        return ThrowBalance.MaxThrowDistance(ThrowStrength(pawn), mass);
    }

    internal static bool CanPickUp([NotNullWhen(true)] Pawn? caster, [NotNullWhen(true)] Thing? target, out string? reason)
    {
        reason = null;
        // Def 负责禁用按钮；这里兜底拒绝已排队或直接调用的举起请求。
        if (caster is not null && caster.WorkTagIsDisabled(WorkTags.Violent))
        {
            reason = "IsIncapableOfViolence".Translate(caster.LabelShort, caster).ToString();
            return false;
        }

        if (caster is null || target is null || target == caster || target.Destroyed || !target.Spawned || target.Map != caster.Map)
        {
            reason = "Mugirl.Throw.InvalidTarget".Translate().ToString();
            return false;
        }

        if (target is Blueprint or Frame or Mote or Projectile or Skyfaller or Explosion or Fire or ThrowController or DunkController)
        {
            reason = "Mugirl.Throw.InvalidTarget".Translate().ToString();
            return false;
        }

        ThingCategory category = target.def.category;
        if (category != ThingCategory.Item && category != ThingCategory.Pawn && category != ThingCategory.Building && category != ThingCategory.Plant)
        {
            reason = "Mugirl.Throw.InvalidTarget".Translate().ToString();
            return false;
        }

        if (!target.def.destroyable)
        {
            reason = "Mugirl.Throw.IndestructibleTarget".Translate(target.LabelCap).ToString();
            return false;
        }

        if (category == ThingCategory.Building && IsConnectedBuilding(target))
        {
            reason = "Mugirl.Throw.ConnectedTarget".Translate(target.LabelCap).ToString();
            return false;
        }

        if (FindHeldController(caster) is not null || FindDunkController(caster) is not null)
        {
            reason = "Mugirl.Throw.AlreadyHolding".Translate().ToString();
            return false;
        }

        float mass = EffectiveMass(target);
        float strength = ThrowStrength(caster);
        if (mass > strength)
        {
            reason = "Mugirl.Throw.TooHeavy".Translate(mass.ToString("0.#"), strength.ToString("0.#")).ToString();
            return false;
        }

        return true;
    }

    private static bool IsConnectedBuilding(Thing target)
    {
        Map map = target.Map;
        CellRect occupied = GenAdj.OccupiedRect(target);
        foreach (IntVec3 cell in occupied.Cells)
        {
            if (HasOtherBuildingAt(target, cell, map))
            {
                return true;
            }
        }

        foreach (IntVec3 cell in occupied.AdjacentCellsCardinal)
        {
            if (HasOtherBuildingAt(target, cell, map))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOtherBuildingAt(Thing target, IntVec3 cell, Map map)
    {
        if (!cell.InBounds(map))
        {
            return false;
        }

        List<Thing> neighbours = map.thingGrid.ThingsListAtFast(cell);
        for (int i = 0; i < neighbours.Count; i++)
        {
            Thing other = neighbours[i];
            if (other != target && other.Spawned && other.def.category == ThingCategory.Building)
            {
                return true;
            }
        }

        return false;
    }

    internal static ThrowController? FindHeldController(Pawn? pawn)
    {
        return FindController(pawn, includeFlying: false);
    }

    // 投掷进行中（含物体飞行阶段）的控制器查询；投掷期间持物者禁止开枪。
    internal static ThrowController? FindThrowingController(Pawn? pawn)
    {
        return FindController(pawn, includeFlying: true);
    }

    // 开火锁在每次 Verb 施放时调用：ThingsOfDef 是一次按 Def 的字典查找，地图上没有控制器时返回空列表。
    private static ThrowController? FindController(Pawn? pawn, bool includeFlying)
    {
        Map? map = pawn?.MapHeld;
        ThingDef? controllerDef = AbilitiesDefOf.Mugirl_ThrowController;
        if (map is null || controllerDef is null)
        {
            return null;
        }

        List<Thing> controllers = map.listerThings.ThingsOfDef(controllerDef);
        for (int i = 0; i < controllers.Count; i++)
        {
            if (controllers[i] is ThrowController controller && controller.Carrier == pawn
                && (includeFlying ? controller.IsThrowInProgress : controller.IsHeld))
            {
                return controller;
            }
        }

        return null;
    }

    internal static DunkController? FindDunkController(Pawn? pawn)
    {
        Map? map = pawn?.MapHeld;
        ThingDef? controllerDef = AbilitiesDefOf.Mugirl_DunkProp;
        if (map is null || controllerDef is null)
        {
            return null;
        }

        List<Thing> controllers = map.listerThings.ThingsOfDef(controllerDef);
        for (int i = 0; i < controllers.Count; i++)
        {
            if (controllers[i] is DunkController controller && controller.Carrier == pawn && controller.HasPayload)
            {
                return controller;
            }
        }

        return null;
    }
}
