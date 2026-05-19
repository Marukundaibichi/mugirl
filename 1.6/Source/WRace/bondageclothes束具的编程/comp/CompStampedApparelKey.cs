using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class CompStampedApparelKey : CompUsable
    {
        protected string make_label(Pawn pawn, Pawn other)
        {
            return FloatMenuOptionLabel(pawn)
                   + " " + ((other == null) ? "MooGirl.SelfLabel".Translate().ToString() : PawnBool.get_pawnname(other));

        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            // ============================
            // 1️⃣ 检查使用者是否可以预占物品 parent
            // ============================
            if (!pawn.CanReserve(parent))
            {
                // 如果物品已被占用，则生成一个禁用的菜单项
                yield return new FloatMenuOption(
                    FloatMenuOptionLabel(pawn) + " (" + "MooGirl.Reserved".Translate() + ")",
                    null, // 没有操作
                    MenuOptionPriority.DisabledOption
                );
            }
            // ============================
            // 2️⃣ 检查 pawn 是否可以到达物品 parent
            // ============================
            else if (pawn.CanReach(parent, PathEndMode.Touch, Danger.Some))
            {
                // ============================
                // 2a️⃣ 检查自己是否穿了束具
                // ============================
                if (pawn.IsWearingSlaveApperal())
                {
                    if (!pawn.IsHandsBlocked())
                    {
                        yield return this.make_option(make_label(pawn, pawn), pawn, pawn, null);
                    }
                    else
                    {
                        yield return new FloatMenuOption( make_label(pawn, pawn) 
                            + " (" + "MooGirl.HandsBlocked".Translate() + ")", 
                            null,MenuOptionPriority.DisabledOption);
                    }
                }

                // 2b️⃣ 检查当前地图是否有效，并且是当前地图
                if ((pawn.Map != null) && (pawn.Map == Find.CurrentMap))
                {
                    // 2b-1 自由殖民者
                    foreach (var other in pawn.Map.mapPawns.FreeColonists)
                    {
                        // 排除自己，并且只显示穿束具的人
                        if ((other != pawn) && other.IsWearingSlaveApperal())
                            yield return this.make_option(make_label(pawn, other), pawn, other, null);
                    }

                    // 2b-2 囚犯
                    foreach (var prisoner in pawn.Map.mapPawns.PrisonersOfColony)
                    {
                        // 只显示穿束具的囚犯
                        if (prisoner.IsWearingSlaveApperal())
                            yield return this.make_option(make_label(pawn, prisoner), pawn, prisoner, WorkTypeDefOf.Warden);
                        // WorkTypeDefOf.Warden 指定 Warden 工作类型，用于游戏内部判定权限
                    }

                    // 2b-3 尸体中的 Pawn
                    foreach (var q in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
                    {
                        var corpse = q as Corpse; // 强制转换为 Corpse 类型
                                                  // 如果尸体内部的 Pawn 穿着束具，则生成菜单项
                        if (corpse?.InnerPawn?.IsWearingSlaveApperal() == true)
                            yield return this.make_option(make_label(pawn, corpse.InnerPawn), pawn, corpse, null);
                    }
                }
            }
        }

    }
}
