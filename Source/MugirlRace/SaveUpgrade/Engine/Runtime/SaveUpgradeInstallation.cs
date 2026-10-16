using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 启动检查：用 <see cref="Harmony.GetPatchInfo"/> 确认读档钩子都由本 Mod 的 Harmony 实例（<c>MugirlMod.Mod</c>）安装。
/// 补丁按类隔离应用，单个类失败只会记录警告。
/// </summary>
/// <remarks>
/// 必需钩子（读档前缀/后缀/终结器、阶段 A、阶段 B、Def 改名后缀）缺失时，旧档会不经升级读入，第一次保存就会被错误地标成当前版本：
/// 弹出启动错误，并让游戏组件不再为读入的存档写当前版本戳。备份文件名入口同样必需；可选钩子（新游戏生命周期、已删除 Def 的静默处理）
/// 缺失只降低体验，记录警告。
/// </remarks>
internal static class SaveUpgradeInstallation
{
    // StaticCacheLifecycle: 进程级启动结果，只在加载长事件结束时写入一次。
    private static int status = (int)InstallationStatus.Unverified;

    internal static InstallationStatus Status => (InstallationStatus)Volatile.Read(ref status);

    /// <summary>检查全部钩子；在补丁应用之后（加载长事件结束时）调用。</summary>
    internal static void Verify()
    {
        List<string> missing = [];
        MethodBase? loadGame = AccessTools.Method(typeof(Game), nameof(Game.LoadGame));
        Require(loadGame, PatchKind.Prefix, typeof(Harmony_Game_LoadGame), missing);
        Require(loadGame, PatchKind.Postfix, typeof(Harmony_Game_LoadGame), missing);
        Require(loadGame, PatchKind.Finalizer, typeof(Harmony_Game_LoadGame), missing);
        Require(AccessTools.Method(typeof(ScribeLoader), nameof(ScribeLoader.FinalizeLoading)), PatchKind.Postfix, typeof(Harmony_ScribeLoader_FinalizeLoading), missing);
        Require(AccessTools.Method(typeof(Game), nameof(Game.FinalizeInit)), PatchKind.Prefix, typeof(Harmony_Game_FinalizeInit), missing);
        Require(AccessTools.Method(typeof(BackCompatibility), nameof(BackCompatibility.BackCompatibleDefName)), PatchKind.Postfix, typeof(Harmony_BackCompatibility_BackCompatibleDefName), missing);
        Require(AccessTools.Method(typeof(SavedGameLoaderNow), nameof(SavedGameLoaderNow.LoadGameFromSaveFileNow)), PatchKind.Prefix, typeof(Harmony_SavedGameLoaderNow_LoadGameFromSaveFileNow), missing);
        Require(AccessTools.Method(typeof(LoadIDsWantedBank), nameof(LoadIDsWantedBank.RegisterLoadIDReadFromXml), [typeof(string), typeof(Type), typeof(string), typeof(IExposable)]), PatchKind.Prefix, typeof(Harmony_LoadIDsWantedBank_RegisterLoadIDReadFromXml), missing);
        Require(AccessTools.Method(typeof(LoadIDsWantedBank), nameof(LoadIDsWantedBank.RegisterLoadIDListReadFromXml), [typeof(List<string>), typeof(string), typeof(IExposable)]), PatchKind.Prefix, typeof(Harmony_LoadIDsWantedBank_RegisterLoadIDListReadFromXml), missing);
        Require(AccessTools.Method(typeof(Verb), nameof(Verb.ExposeData)), PatchKind.Postfix, typeof(Harmony_Verb_ExposeData_LoadId), missing);
        Require(AccessTools.Method(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.Reset)), PatchKind.Postfix, typeof(Harmony_ThinkTreeKeyAssigner_Reset), missing);
        Require(AccessTools.Method(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.AssignKeys)), PatchKind.Prefix, typeof(Harmony_ThinkTreeKeyAssigner_AssignKeys), missing);
        Require(AccessTools.Method(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.AssignSingleKey)), PatchKind.Prefix, typeof(Harmony_ThinkTreeKeyAssigner_AssignSingleKey), missing);
        Require(AccessTools.Method(typeof(ThinkNode), nameof(ThinkNode.DeepCopy), [typeof(bool)]), PatchKind.Prefix, typeof(Harmony_ThinkNode_DeepCopy_LegacyLayout), missing);
        Require(AccessTools.Method(typeof(ThinkNode), nameof(ThinkNode.DeepCopy), [typeof(bool)]), PatchKind.Postfix, typeof(Harmony_ThinkNode_DeepCopy_LegacyLayout), missing);
        Require(AccessTools.Method(typeof(ThinkNode), nameof(ThinkNode.DeepCopy), [typeof(bool)]), PatchKind.Finalizer, typeof(Harmony_ThinkNode_DeepCopy_LegacyLayout), missing);
        Require(AccessTools.Method(typeof(ThinkTreeDef), nameof(ThinkTreeDef.ResolveReferences)), PatchKind.Prefix, typeof(Harmony_ThinkTreeDef_ResolveReferences), missing);
        Require(AccessTools.Method(typeof(ThinkTreeDef), nameof(ThinkTreeDef.ResolveReferences)), PatchKind.Finalizer, typeof(Harmony_ThinkTreeDef_ResolveReferences), missing);
        Require(AccessTools.Method(typeof(Job), nameof(Job.ExposeData)), PatchKind.Prefix, typeof(Harmony_Job_ExposeData_ThinkTreeIdentity), missing);
        Require(AccessTools.Method(typeof(Job), nameof(Job.ExposeData)), PatchKind.Postfix, typeof(Harmony_Job_ExposeData_ThinkTreeIdentity), missing);
        Require(AccessTools.Method(typeof(Job), nameof(Job.ExposeData)), PatchKind.Finalizer, typeof(Harmony_Job_ExposeData_ThinkTreeIdentity), missing);
        Require(AccessTools.Method(typeof(Job), nameof(Job.Clear)), PatchKind.Postfix, typeof(Harmony_Job_Clear_ThinkTreeIdentity), missing);
        Require(AccessTools.Method(typeof(Job), nameof(Job.Clone)), PatchKind.Postfix, typeof(Harmony_Job_Clone_ThinkTreeIdentity), missing);
        Require(AccessTools.Method(typeof(ThinkTreeDef), nameof(ThinkTreeDef.TryGetThinkNodeWithSaveKey)), PatchKind.Prefix, typeof(Harmony_ThinkTreeDef_TryGetThinkNodeWithSaveKey), missing);
        Require(AccessTools.Method(typeof(ThinkTreeDef), nameof(ThinkTreeDef.TryGetThinkNodeWithSaveKey)), PatchKind.Postfix, typeof(Harmony_ThinkTreeDef_TryGetThinkNodeWithSaveKey), missing);
        Require(AccessTools.Method(typeof(ThinkTreeDef), nameof(ThinkTreeDef.TryGetThinkNodeWithSaveKey)), PatchKind.Finalizer, typeof(Harmony_ThinkTreeDef_TryGetThinkNodeWithSaveKey), missing);
        // 捕获布局失败只影响 AI 来源；已核验的 writer 仍能保留原键，不应禁用整个升级器。
        if (LegacyThinkTreeKeys.Problem is { } thinkTreeProblem
            && (!LegacyThinkTreeKeys.AlgorithmVerified || !UnresolvedThinkTreeKeys.ProtocolVerified))
        {
            missing.Add(thinkTreeProblem);
        }

        List<string> degraded = [];
        Require(AccessTools.Method(typeof(Game), nameof(Game.InitNewGame)), PatchKind.Prefix, typeof(Harmony_Game_InitNewGame), degraded);
        Require(AccessTools.Method(typeof(BackCompatibility), nameof(BackCompatibility.WasDefRemoved)), PatchKind.Postfix, typeof(Harmony_BackCompatibility_WasDefRemoved), degraded);
        if (degraded.Count > 0)
        {
            MugirlLog.Warning("Optional save upgrade hooks are missing: " + string.Join(", ", degraded)
                + ". Saves still upgrade, but new games may keep per-game state or removed defs log load errors.");
        }

        if (missing.Count == 0)
        {
            Volatile.Write(ref status, (int)InstallationStatus.Installed);
            return;
        }

        Volatile.Write(ref status, (int)InstallationStatus.Broken);
        string list = string.Join(", ", missing);
        MugirlLog.Error("Save upgrade hooks are missing: " + list + ". Saves from older Mugirl versions will not be upgraded and keep their schema stamp.");
        SaveUpgradeNotices.NotInstalled(list);
    }

    private static void Require(MethodBase? target, PatchKind kind, Type patchClass, List<string> missing)
    {
        Patches? info = target is null ? null : Harmony.GetPatchInfo(target);
        IEnumerable<Patch> patches = info is null ? [] : kind switch
        {
            PatchKind.Prefix => info.Prefixes,
            PatchKind.Postfix => info.Postfixes,
            _ => info.Finalizers,
        };

        if (!patches.Any(patch => patch.owner == HarmonyBootstrap.HarmonyId && patch.PatchMethod?.DeclaringType == patchClass))
        {
            missing.Add(patchClass.Name + "." + kind);
        }
    }

    private enum PatchKind
    {
        Prefix,
        Postfix,
        Finalizer,
    }
}
