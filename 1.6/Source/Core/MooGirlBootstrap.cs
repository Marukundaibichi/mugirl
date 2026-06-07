using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace MooGirl
{
    internal static class MooGirlBootstrap
    {
        internal const string HarmonyId = "MooGirlMod.Mod";
        // StaticCacheLifecycle: process-level bootstrap audit data; filled once because Harmony initialization is process-level.
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
            MooGirlPatchRegistry.RegisterManualPatches(Harmony);
            MooGirlPatchCatalog.LogDevSummary(patchedClassNames, MooGirlPatchRegistry.ManualPatchNames);
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
                MooGirlLog.WarningOnce(
                    "Bootstrap.PatchClassDiscoveryPartial",
                    "MooGirl.Bootstrap.HarmonyPatchFailed".Translate(assembly.GetName().Name, ExceptionSummary(ex)).ToString());
            }
            catch (Exception ex)
            {
                MooGirlLog.WarningOnce(
                    "Bootstrap.PatchClassDiscoveryFailed",
                    "MooGirl.Bootstrap.HarmonyPatchFailed".Translate(assembly.GetName().Name, ExceptionSummary(ex)).ToString());
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
                MooGirlLog.WarningOnce(
                    "Bootstrap.PatchClassAttributeFailed." + typeName,
                    "MooGirl.Bootstrap.HarmonyPatchFailed".Translate(typeName, ExceptionSummary(ex)).ToString());
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
                MooGirlLog.WarningOnce(
                    "Bootstrap.PatchClassFailed." + patchClassName,
                    "MooGirl.Bootstrap.HarmonyPatchFailed".Translate(patchClassName, ExceptionSummary(ex)).ToString());
            }
        }

        private static string ExceptionSummary(Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }
}
