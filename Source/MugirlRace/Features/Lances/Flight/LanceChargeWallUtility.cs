namespace Mugirl.Features.Lances;

/// <summary>冲锋途中的墙体处理：按剩余额度扣减墙体耐久，判断该格之后能否继续前进。</summary>
internal static class LanceChargeWallUtility
{
    internal static bool TryClearCell(IntVec3 cell, Map map, Pawn? attacker, ref int remaining)
    {
        if (!cell.InBounds(map))
        {
            return false;
        }

        Building? building = cell.GetEdifice(map);
        if (building?.def.IsWall == true)
        {
            if (!building.def.destroyable || !building.def.useHitPoints || remaining <= 0)
            {
                return false;
            }

            int damage = Math.Min(remaining, building.HitPoints);
            remaining -= damage;
            // 额度代表实际墙体耐久，直接扣减以免建筑伤害倍率或施放者近战倍率突破总上限。
            building.HitPoints -= damage;
            if (building.HitPoints <= 0)
            {
                building.Kill(new DamageInfo(DamageDefOf.Blunt, damage, instigator: attacker));
            }

            if (building.Spawned && !building.Destroyed)
            {
                return false;
            }
        }

        // 销毁可能留下另一层阻挡物；门、岩石等非墙建筑不消耗破墙额度，但不能穿透。
        building = cell.GetEdifice(map);
        return building is null || (building.def.passability != Traversability.Impassable
            && !(building is Building_Door door && !door.Open));
    }
}
