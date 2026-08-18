using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl.Features.WeaponWheel
{
    // Targets: 火力满载轮射的 mod 冲突排查。
    // 在设置中开启后，雪牛娘每次射击、轮盘每次战斗决策都会输出诊断日志，
    // 并附带一份轮盘关键原版方法的 Harmony 补丁归属报告。
    // 所有入口先检查 Enabled，未开启时不产生任何字符串构建开销。
    internal static class WeaponWheelDevLog
    {
        private const string Prefix = "[Mugirl][WeaponWheelDev] ";

        // StaticCacheLifecycle: 进程级去重与补丁报告状态；关闭开关时由 Enabled 检查清空，
        // 重新开启后补丁报告会再打印一次。
        private static bool patchReportPrinted;
        private static bool lastEnabledState;
        private static readonly Dictionary<int, string> lastMessageByPawn = new Dictionary<int, string>();
        private static readonly Dictionary<int, int> suppressedByPawn = new Dictionary<int, int>();

        internal static bool Enabled
        {
            get
            {
                bool enabled = MugirlMod.Settings?.enableWeaponWheelDevLog == true;
                if (!enabled && lastEnabledState)
                {
                    patchReportPrinted = false;
                    lastMessageByPawn.Clear();
                    suppressedByPawn.Clear();
                }
                lastEnabledState = enabled;
                return enabled;
            }
        }

        // 战斗决策日志。等待类状态（冷却、切枪动画）会逐 tick 触发相同分支，
        // 因此对同一 Pawn 连续重复的消息做去重，只在内容变化时输出。
        internal static void CastDecision(Comp_WeaponWheel comp, Verb verb, string message)
        {
            if (!Enabled)
            {
                return;
            }
            Pawn pawn = comp?.Pawn;
            if (pawn == null)
            {
                return;
            }

            string body = message + " | " + DescribeVerb(verb, pawn) + " | " + comp.DevDescribeCombat();
            int pawnId = pawn.thingIDNumber;
            if (lastMessageByPawn.TryGetValue(pawnId, out string lastMessage) && lastMessage == body)
            {
                suppressedByPawn[pawnId] = suppressedByPawn.TryGetValue(pawnId, out int count) ? count + 1 : 1;
                return;
            }
            lastMessageByPawn[pawnId] = body;
            string repeatNote = string.Empty;
            if (suppressedByPawn.TryGetValue(pawnId, out int suppressed) && suppressed > 0)
            {
                repeatNote = " (previous line repeated " + suppressed + "x)";
                suppressedByPawn[pawnId] = 0;
            }

            EnsurePatchReport();
            MugirlLog.DiagnosticMessage(Prefix + Stamp(pawn) + body + repeatNote);
        }

        // TryCastNextBurstShot Postfix 专用：说明本次 burst 射击是否会触发轮盘接续，
        // 不去重，逐发输出，便于观察其他 mod 篡改 burst 节奏的情况。
        internal static void BurstShotPostfix(Verb verb)
        {
            if (!Enabled || verb == null)
            {
                return;
            }
            Pawn pawn = verb.CasterPawn;
            Comp_WeaponWheel comp = pawn?.TryGetComp<Comp_WeaponWheel>();
            if (comp == null || verb.EquipmentSource == null || !comp.ContainsWeapon(verb.EquipmentSource))
            {
                return;
            }
            if (verb.verbProps?.IsMeleeAttack == true)
            {
                return;
            }

            bool tickAvailable = MugirlTickUtility.TryGetCurrentGameTick(out int currentTick);
            string verdict;
            if (verb.state != VerbState.Idle)
            {
                verdict = "mid-burst, wheel continues after the last shot";
            }
            else if (!tickAvailable)
            {
                verdict = "SKIPPED: game tick unavailable";
            }
            else if (verb.LastShotTick != currentTick)
            {
                verdict = "SKIPPED: LastShotTick=" + verb.LastShotTick + " != tick=" + currentTick
                    + " (another mod may have deferred/blocked the final shot, wheel cycling will NOT trigger)";
            }
            else
            {
                verdict = "final shot this tick, NotifyBurstCompleted will run";
            }

            EnsurePatchReport();
            MugirlLog.DiagnosticMessage(Prefix + Stamp(pawn) + "TryCastNextBurstShot postfix: state=" + verb.state
                + " lastShotTick=" + verb.LastShotTick + " -> " + verdict + " | " + DescribeVerb(verb, pawn));
        }

        // 攻击入口选出的 Verb 诊断。重点捕获“返回了轮盘中的非主武器 Verb”：
        // 这种选择发生在 TryStartCastOn 之前，轮盘会因其并非当前主武器而忽略，
        // 最终表现为手持武器贴图正确、攻击行为来自另一把武器且不触发轮射。
        internal static void AttackVerbSelected(
            Pawn pawn,
            Thing target,
            bool allowManualCastWeapons,
            bool allowTurrets,
            Verb selectedResult,
            Verb finalResult,
            bool corrected)
        {
            if (!Enabled || pawn == null)
            {
                return;
            }

            Comp_WeaponWheel comp = pawn.TryGetComp<Comp_WeaponWheel>();
            if (comp == null)
            {
                return;
            }

            ThingWithComps source = selectedResult?.EquipmentSource;
            bool isWheelVerb = source != null && comp.ContainsWeapon(source);
            bool isNonPrimaryWheelVerb = isWheelVerb && source != pawn.equipment?.Primary;
            string verdict = isNonPrimaryWheelVerb
                ? corrected
                    ? "CORRECTED: blocked a non-primary weapon-wheel verb"
                    : "MISMATCH: selected a non-primary weapon-wheel verb"
                : isWheelVerb
                    ? "selected current weapon-wheel primary verb"
                    : "selected verb is not owned by the weapon wheel";

            EnsurePatchReport();
            string message = Prefix + Stamp(pawn)
                + "Pawn.TryGetAttackVerb postfix: " + verdict
                + " | target=" + (target?.ToString() ?? "null")
                + " allowManual=" + allowManualCastWeapons
                + " allowTurrets=" + allowTurrets
                + " job=" + (pawn.CurJob?.def?.defName ?? "none")
                + " primary=" + (pawn.equipment?.Primary?.def?.defName ?? "none")
                + " | selected: " + DescribeVerb(selectedResult, pawn);
            if (corrected)
            {
                message += " | final: " + DescribeVerb(finalResult, pawn);
            }
            if (isNonPrimaryWheelVerb)
            {
                message += "\nSelection call stack:\n" + new System.Diagnostics.StackTrace(1, true);
            }
            MugirlLog.DiagnosticMessage(message);
        }

        // 逐槽位描述火力满载可用性，只使用 comp 的公开接口。
        internal static string DescribeSlots(Comp_WeaponWheel comp, LocalTargetInfo target)
        {
            if (comp == null)
            {
                return string.Empty;
            }
            Pawn pawn = comp.Pawn;
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < comp.MaxSlots; i++)
            {
                builder.Append("\n  slot").Append(i);
                if (!comp.IsSlotUnlocked(i))
                {
                    builder.Append(": locked");
                    continue;
                }
                ThingWithComps weapon = comp.WeaponAt(i);
                if (weapon == null)
                {
                    builder.Append(": empty");
                    continue;
                }
                Verb verb = weapon.GetComp<CompEquippable>()?.PrimaryVerb;
                builder.Append(": ").Append(weapon.def.defName)
                    .Append(weapon == pawn?.equipment?.Primary ? " [primary]" : string.Empty)
                    .Append(" ranged=").Append(weapon.def.IsRangedWeapon)
                    .Append(" verb=").Append(verb == null ? "null" : verb.GetType().FullName);
                if (verb != null)
                {
                    builder.Append(" available=").Append(verb.Available());
                    if (target.IsValid)
                    {
                        builder.Append(" canHit=").Append(verb.CanHitTarget(target));
                    }
                }
            }
            return builder.ToString();
        }

        // 输出轮盘依赖的原版方法上所有 Harmony 补丁的归属，用于定位冲突 mod。
        // 设置页按钮可随时触发；开启开关后的第一条日志也会自动附带一次。
        internal static void PrintPatchReport()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(Prefix).Append("Harmony patch report (own id: ").Append(MugirlBootstrap.HarmonyId).Append(')');
            builder.Append("\nKnown compatibility layers: DualWield=").Append(DualWieldCompatibility.Active)
                .Append(" RunAndGun=").Append(RunAndGunCompatibility.Active)
                .Append(" YayoCombat=").Append(YayoCombatCompatibility.Active)
                .Append(" TrainingFacility=").Append(TrainingFacilityCompatibility.Active);
            AppendPatches(builder, AccessTools.Method(typeof(Verb), "TryStartCastOn", new[]
            {
                typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
            }), "Verb.TryStartCastOn(6 args)");
            AppendPatches(builder, AccessTools.Method(typeof(Verb), "TryStartCastOn", new[]
            {
                typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
            }), "Verb.TryStartCastOn(5 args)");
            AppendPatches(builder, AccessTools.Method(typeof(Pawn), nameof(Pawn.TryGetAttackVerb), new[]
            {
                typeof(Thing), typeof(bool), typeof(bool)
            }), "Pawn.TryGetAttackVerb");
            AppendPatches(builder, AccessTools.Method(typeof(Verb), "TryCastNextBurstShot"), "Verb.TryCastNextBurstShot");
            AppendPatches(builder, AccessTools.PropertyGetter(typeof(Verb), "WarmupTime"), "Verb.WarmupTime (getter)");
            AppendPatches(builder, AccessTools.Method(typeof(Verb), "WarmupComplete"), "Verb.WarmupComplete");
            AppendPatches(builder, AccessTools.Method(typeof(Verb), "Available"), "Verb.Available");
            AppendPatches(builder, AccessTools.Method(typeof(Verb_MeleeAttack), "TryCastShot"), "Verb_MeleeAttack.TryCastShot");
            AppendPatches(builder, AccessTools.Method(typeof(Pawn_EquipmentTracker), "Notify_EquipmentAdded"), "Pawn_EquipmentTracker.Notify_EquipmentAdded");
            AppendPatches(builder, AccessTools.Method(typeof(Pawn_EquipmentTracker), "Notify_EquipmentRemoved"), "Pawn_EquipmentTracker.Notify_EquipmentRemoved");
            AppendPatches(builder, AccessTools.Method(typeof(Pawn_EquipmentTracker), "TryTransferEquipmentToContainer"), "Pawn_EquipmentTracker.TryTransferEquipmentToContainer");
            AppendPatches(builder, AccessTools.Method(typeof(Pawn_EquipmentTracker), "AddEquipment"), "Pawn_EquipmentTracker.AddEquipment");
            AppendPatches(builder, AccessTools.Method(typeof(Pawn_StanceTracker), "SetStance"), "Pawn_StanceTracker.SetStance");
            AppendPatches(builder, AccessTools.Method(typeof(PawnRenderUtility), "DrawEquipmentAiming"), "PawnRenderUtility.DrawEquipmentAiming");
            MugirlLog.DiagnosticMessage(builder.ToString());
        }

        private static void EnsurePatchReport()
        {
            if (!patchReportPrinted)
            {
                patchReportPrinted = true;
                PrintPatchReport();
            }
        }

        private static void AppendPatches(StringBuilder builder, MethodBase method, string label)
        {
            builder.Append("\n").Append(label).Append(": ");
            if (method == null)
            {
                builder.Append("method not found (game version mismatch?)");
                return;
            }

            Patches patches = Harmony.GetPatchInfo(method);
            if (patches == null || (patches.Prefixes.Count == 0 && patches.Postfixes.Count == 0
                && patches.Transpilers.Count == 0 && patches.Finalizers.Count == 0))
            {
                builder.Append("no patches");
                return;
            }
            AppendPatchGroup(builder, "prefix", patches.Prefixes);
            AppendPatchGroup(builder, "postfix", patches.Postfixes);
            AppendPatchGroup(builder, "transpiler", patches.Transpilers);
            AppendPatchGroup(builder, "finalizer", patches.Finalizers);
        }

        private static void AppendPatchGroup(StringBuilder builder, string kind, IReadOnlyList<Patch> patches)
        {
            if (patches == null || patches.Count == 0)
            {
                return;
            }
            for (int i = 0; i < patches.Count; i++)
            {
                Patch patch = patches[i];
                bool foreign = patch.owner != MugirlBootstrap.HarmonyId;
                builder.Append("\n  ").Append(kind).Append(": ").Append(patch.owner)
                    .Append(" [").Append(patch.PatchMethod?.DeclaringType?.Assembly?.GetName()?.Name ?? "unknown assembly").Append(']');
                if (foreign)
                {
                    builder.Append(" <- FOREIGN, possible conflict source");
                }
            }
        }

        private static string Stamp(Pawn pawn)
        {
            return "T" + MugirlTickUtility.CurrentGameTickOrFallback(-1) + " " + pawn.LabelShort + "#" + pawn.thingIDNumber + " | ";
        }

        private static string DescribeVerb(Verb verb, Pawn pawn)
        {
            if (verb == null)
            {
                return "verb=null";
            }
            ThingWithComps equipment = verb.EquipmentSource;
            return "verb=" + verb.GetType().FullName
                + " [" + (verb.GetType().Assembly?.GetName()?.Name ?? "unknown assembly") + "]"
                + " state=" + verb.state
                + " source=" + (equipment?.def?.defName ?? "none")
                + (equipment != null && equipment == pawn?.equipment?.Primary ? " [primary]" : string.Empty);
        }
    }
}
