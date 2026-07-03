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
            MugirlPatchCatalog.LogDevSummary(patchedClassNames, MugirlPatchRegistry.ManualPatchNames);
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
                MugirlLog.WarningOnce(
                    "Bootstrap.PatchClassDiscoveryPartial",
                    "Mugirl.Bootstrap.HarmonyPatchFailed".Translate(assembly.GetName().Name, ExceptionSummary(ex)).ToString());
            }
            catch (Exception ex)
            {
                MugirlLog.WarningOnce(
                    "Bootstrap.PatchClassDiscoveryFailed",
                    "Mugirl.Bootstrap.HarmonyPatchFailed".Translate(assembly.GetName().Name, ExceptionSummary(ex)).ToString());
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
                MugirlLog.WarningOnce(
                    "Bootstrap.PatchClassAttributeFailed." + typeName,
                    "Mugirl.Bootstrap.HarmonyPatchFailed".Translate(typeName, ExceptionSummary(ex)).ToString());
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
                MugirlLog.WarningOnce(
                    "Bootstrap.PatchClassFailed." + patchClassName,
                    "Mugirl.Bootstrap.HarmonyPatchFailed".Translate(patchClassName, ExceptionSummary(ex)).ToString());
            }
        }

        private static string ExceptionSummary(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }
}
