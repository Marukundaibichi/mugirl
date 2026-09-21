using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Mugirl
{
    internal static class MugirlBootstrap
    {
        internal const string HarmonyId = "MugirlMod.Mod";
        // StaticCacheLifecycle: 进程级 Harmony 启动审计数据；Harmony 初始化只在进程内执行一次。
        private static readonly List<string> patchedClassNames = new List<string>();

        internal static Harmony Harmony { get; private set; }
        internal static IReadOnlyList<string> PatchedClassNames => patchedClassNames;

        internal static void Initialize()
        {
            if (Harmony != null)
            {
                return;
            }

            Harmony = new Harmony(HarmonyId);
            patchedClassNames.Clear();
            RegisterAttributePatches(Harmony);
            MugirlPatchRegistry.RegisterManualPatches(Harmony);
            // 等延迟的 UI 补丁注册完毕，再统计实际成功注册的补丁。
            LongEventHandler.ExecuteWhenFinished(() =>
                MugirlPatchCatalog.LogDevSummary(patchedClassNames, MugirlPatchRegistry.ManualPatchNames));
        }

        private static void RegisterAttributePatches(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }

            List<Type> patchTypes = GetHarmonyPatchTypes();
            for (int i = 0; i < patchTypes.Count; i++)
            {
                if (patchTypes[i] == typeof(Harmony_StylingStationRefresh))
                {
                    // Harmony 编译目标方法时可能触发 HAR StylingStation 的静态构造，
                    // 其中会加载 UI 贴图。仅此补丁等加载长事件结束后在主线程注册。
                    Type patchClass = patchTypes[i];
                    LongEventHandler.ExecuteWhenFinished(() => TryPatchClass(harmony, patchClass));
                    continue;
                }
                TryPatchClass(harmony, patchTypes[i]);
            }
        }

        private static List<Type> GetHarmonyPatchTypes()
        {
            List<Type> patchTypes = new List<Type>();
            Assembly assembly = Assembly.GetExecutingAssembly();
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types ?? new Type[0];
                WarnPatchFailure(
                    "Bootstrap.PatchClassDiscoveryPartial",
                    assembly.GetName().Name, ex);
            }
            catch (Exception ex)
            {
                WarnPatchFailure(
                    "Bootstrap.PatchClassDiscoveryFailed",
                    assembly.GetName().Name, ex);
                return patchTypes;
            }

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (HasHarmonyPatchAttribute(type))
                {
                    patchTypes.Add(type);
                }
            }

            return patchTypes;
        }

        private static bool HasHarmonyPatchAttribute(Type type)
        {
            if (type == null)
            {
                return false;
            }

            try
            {
                return type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0;
            }
            catch (Exception ex)
            {
                string typeName = type.FullName ?? type.Name;
                WarnPatchFailure(
                    "Bootstrap.PatchClassAttributeFailed." + typeName,
                    typeName, ex);
                return false;
            }
        }

        private static void TryPatchClass(Harmony harmony, Type patchClass)
        {
            if (patchClass == null)
            {
                return;
            }

            string patchClassName = patchClass.FullName ?? patchClass.Name;
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
                patchedClassNames.Add(patchClassName);
            }
            catch (Exception ex)
            {
                WarnPatchFailure(
                    "Bootstrap.PatchClassFailed." + patchClassName,
                    patchClassName, ex);
            }
        }

        private static void WarnPatchFailure(string warningKey, string targetName, Exception exception)
        {
            // Harmony 的外层异常通常只有目标信息；保留内部异常和堆栈才能定位失败原因。
            string detail = exception.ToString();
            MugirlLog.StartupWarningOnce(warningKey,
                () => "Mugirl.Bootstrap.HarmonyPatchFailed".Translate(targetName, detail).ToString(),
                "Harmony patch skipped: " + targetName + ". " + detail);
        }
    }
}
