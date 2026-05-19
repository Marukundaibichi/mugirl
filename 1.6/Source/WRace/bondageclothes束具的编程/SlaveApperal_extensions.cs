using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public static class SlaveApperal_extensions
    {
        public static bool IsSlaveApparel(this Apparel apparel)
        {
            return apparel is SlaveApparel;
        }

        public static bool SatifiedKey(this Apparel apparel, Thing key)
        {
            return apparel is SlaveApparel && apparel.def is SlaveApparelDef def && (def.keytype == null || def.keytype == key.def);
        }

        public static bool IsAdvancedApperal(this Apparel apparel)
        {
            return (apparel is AdvancedSlaveApparel || apparel is BrainWashSlaveApparel);
        }

        public static bool IsUnlockAdvancedApperal(this Apparel apparel)
        {
            if (apparel is AdvancedSlaveApparel advanced)
            {
                return advanced.IsCracked();
            }

            if (apparel is BrainWashSlaveApparel brainwashed)
            {
                return brainwashed.IsCracked();
            }

            return false;
        }

        public static bool IsWearingSlaveApperal(this Pawn pawn)
        {
            return pawn.apparel?.WornApparel.Any(app => app is SlaveApparel) ?? false;
        }

        public static bool IsWearingAdvancedApperal(this Pawn pawn)
        {
            return pawn.apparel?.WornApparel.Any(app =>
                app is AdvancedSlaveApparel || app is BrainWashSlaveApparel
            ) ?? false;
        }


        public static bool IsWearingUncrackedBrainwashApparel(this Pawn pawn)
        {
            return pawn.apparel?.WornApparel.Any(app =>
                app is BrainWashSlaveApparel slaveApparel &&
                !slaveApparel.IsCracked()
            ) ?? false;
        }

        public static bool IsWearingCrackedBrainwashApparel(this Pawn pawn)
        {
            return pawn.apparel?.WornApparel.Any(app =>
                app is BrainWashSlaveApparel slaveApparel &&
                slaveApparel.IsCracked()
            ) ?? false;
        }


        public static bool IsHandsBlocked(this Pawn pawn)
        {
            foreach (var apparel in pawn.apparel.WornApparel)
            {
                if (apparel is SlaveApparel sa && sa.SlaveDef.blocks_hands)
                {
                    return true; // 找到一个阻塞双手的束具就返回 true
                }
            }
            return false; // 没有阻塞双手的束具
        }

        public static void start_job(this CompUsable usa, Pawn p, LocalTargetInfo tar, Apparel apparel)
        {
            if (p.CanReserveAndReach(usa.parent, PathEndMode.Touch, Danger.Some) &&
                ((tar == null) || p.CanReserveAndReach(tar, PathEndMode.Touch, Danger.Some)))
            {
                var comfor = usa.parent.GetComp<CompForbiddable>();
                if (comfor != null)
                    comfor.Forbidden = false;
                var job = JobMaker.MakeJob(((CompProperties_Usable)usa.props).useJob, usa.parent, tar, apparel);
                p.jobs.TryTakeOrderedJob(job);
            }
        }


        public static FloatMenuOption make_option(
            this CompUsable usa,       // 这是调用此方法的物品组件
            string label,              // 菜单显示文本
            Pawn p,                    // 操作的 Pawn（使用物品的人）
            LocalTargetInfo tar,       // 目标，可以是 Pawn 或 Corpse，也可以是 null 表示自己
            WorkTypeDef required_work  // 可选工作类型，用于限制操作（比如 Warden）
        )
        {
            // ==============================
            // 1️⃣ 检查目标是否可预占
            // ==============================
            if ((tar != null) && (!p.CanReserve(tar)))
            {
                // 目标已被其他 Pawn 占用
                string text = "MooGirl.Reserved".Translate();
                // 返回禁用的菜单项
                return new FloatMenuOption($"{label} ({text})", null, MenuOptionPriority.DisabledOption);
            }

            // ==============================
            // 2️⃣ 检查目标是否可到达
            // ==============================
            else if ((tar != null) && (!p.CanReach(tar, PathEndMode.Touch, Danger.Some)))
            {
                string text = "MooGirl.NoPath".Translate();
                return new FloatMenuOption($"{label} ({text})", null, MenuOptionPriority.DisabledOption);
            }

            // ==============================
            // 3️⃣ 检查是否有权限执行指定工作
            // ==============================
            else if ((required_work != null) && p.WorkTagIsDisabled(required_work.workTags))
            {
                string text = "MooGirl.CannotPrioritizeWorkTypeDisabled".Translate(required_work.gerundLabel);
                return new FloatMenuOption($"{label} ({text})", null, MenuOptionPriority.DisabledOption);
            }

            // ==============================
            // 4️⃣ 生成实际可用菜单项
            // ==============================
            else
                return new FloatMenuOption(label, delegate
                {
                    Pawn target = null;

                    // ------------------------------
                    // 4a️⃣ 确定目标 Pawn
                    // ------------------------------
                    if (tar == null)
                    {
                        target = p; // 没有指定目标，则目标是自己
                    }
                    else
                    {
                        if (tar.Thing is Pawn var)
                        {
                            target = var; // 如果目标是 Pawn
                        }
                        if (tar.Thing is Corpse cor)
                        {
                            target = cor.InnerPawn; // 如果目标是尸体，取尸体内的 Pawn
                        }
                    }

                    // ------------------------------
                    // 4b️⃣ 查找目标身上所有可解锁的束具
                    // ------------------------------
                    List<FloatMenuOption> ops = new List<FloatMenuOption>();
                    foreach (var item in target.apparel.LockedApparel)
                    {
                        if (item.SatifiedKey(usa.parent))
                        {
                            // ✅ 钥匙匹配，生成可解锁菜单
                            ops.Add(new FloatMenuOption(item.Label, () =>
                            {
                                usa.start_job(p, tar, item); // 点击后启动 Job 执行解锁
                            }));
                        }
                        else
                        {
                            // ❌ 钥匙类型不匹配，生成提示项（不可操作）
                            ops.Add(new FloatMenuOption(
                                "MooGirl.WrongKeyType".Translate(item.Label), // 翻译并传装备名
                                null,
                                MenuOptionPriority.Default,
                                null,
                                null,
                                0f,
                                null,
                                null,
                                true // 灰色不可用
                            ));
                        }
                    }

                    // ------------------------------
                    // 4c️⃣ 弹出菜单（即使全部是提示项也会显示）
                    // ------------------------------
                    if (ops.Any())
                    {
                        Find.WindowStack.Add(new FloatMenu(ops));
                    }
                },
                MenuOptionPriority.Default);



        }

    }
}
