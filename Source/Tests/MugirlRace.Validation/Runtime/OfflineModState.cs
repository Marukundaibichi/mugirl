using System.Reflection;
using System.Runtime.CompilerServices;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using HarmonyLib;
using Verse;

namespace MugirlRace.Validation.Runtime;

/// <summary>
/// 让 <see cref="ModLister"/> 与 <see cref="ModsConfig"/> 在离线时按验证程序的完整配置（<see cref="ModCatalog.Full"/>）回答“某 Mod 是否启用”。
/// </summary>
/// <remarks>
/// <para>
/// Mugirl 的兼容补丁在 <c>Prepare()</c> 中调用 <c>ModLister.GetActiveModWithIdentifier</c>。离线时 ModLister 的类型构造函数会扫描本机
/// 全部 Mod 目录并读取未初始化的 Prefs（抛 NullReferenceException，类型从此不可用）；ModsConfig 的类型构造函数会读取并可能
/// <em>改写</em>玩家的 ModsConfig.xml。这里在任何 Mod 代码运行之前打补丁：ModLister 不扫描目录，ModsConfig 读一个不存在的
/// 配置文件且从不保存，查询改由完整配置回答。只影响本验证进程。
/// </para>
/// <para>返回的 <see cref="ModMetaData"/> 是未初始化的占位对象：Mod 代码只用它判断非 null。</para>
/// </remarks>
internal static class OfflineModState
{
    private const string HarmonyId = "mugirl.validation.offlinemods";

    // StaticCacheLifecycle: 进程级的离线 Mod 配置与占位对象；验证进程只安装一次。
    private static readonly Dictionary<string, ModMetaData> Placeholders = new(StringComparer.Ordinal);
    private static ActiveMods? active;

    public static void Install(ActiveMods activeMods)
    {
        active = activeMods;
        var harmony = new Harmony(HarmonyId);
        Patch(harmony, typeof(GenFilePaths), "get_ModsConfigFilePath", [], nameof(ModsConfigFilePath));
        Patch(harmony, typeof(ModsConfig), nameof(ModsConfig.Save), [], nameof(Skip));
        Patch(harmony, typeof(ModsConfig), nameof(ModsConfig.SaveFromList), [typeof(List<string>)], nameof(Skip));
        Patch(harmony, typeof(ModsConfig), nameof(ModsConfig.Reset), [], nameof(Skip));
        Patch(harmony, typeof(ModLister), nameof(ModLister.RebuildModList), [], nameof(Skip));
        Patch(harmony, typeof(ModLister), nameof(ModLister.GetActiveModWithIdentifier), [typeof(string), typeof(bool)], nameof(ModWithIdentifier));
        Patch(harmony, typeof(ModLister), nameof(ModLister.GetModWithIdentifier), [typeof(string), typeof(bool)], nameof(ModWithIdentifier));
        Patch(harmony, typeof(ModLister), nameof(ModLister.HasActiveModWithName), [typeof(string)], nameof(HasActiveModWithName));
        Patch(harmony, typeof(ModsConfig), nameof(ModsConfig.IsActive), [typeof(string)], nameof(IsActive));
        // 每个 DLC 的 XxxActive 与 XxxInstalled 都由同一个前缀 DlcXxx 回答。
        foreach (string dlc in new[] { "Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey" })
        {
            Patch(harmony, typeof(ModsConfig), $"get_{dlc}Active", [], "Dlc" + dlc);
            Patch(harmony, typeof(ModLister), $"get_{dlc}Installed", [], "Dlc" + dlc);
        }
    }

    private static void Patch(Harmony harmony, Type type, string method, Type[] parameters, string prefix)
    {
        MethodInfo original = AccessTools.DeclaredMethod(type, method, parameters)
            ?? throw new EnvironmentException($"{type.FullName}.{method} does not exist in this game build (offline mod state).");
        harmony.Patch(original, prefix: new HarmonyMethod(typeof(OfflineModState), prefix) { priority = Priority.First });
    }

    private static bool Skip() => false;

    private static bool ModsConfigFilePath(ref string __result)
    {
        // 不存在的文件：ModsConfig 以空配置初始化，不读玩家的配置。
        __result = Path.Combine(Path.GetTempPath(), "mg17-validation-no-modsconfig", "ModsConfig.xml");
        return false;
    }

    private static bool ModWithIdentifier(string identifier, ref ModMetaData? __result)
    {
        __result = Active(identifier) ? Placeholder(identifier) : null;
        return false;
    }

    private static bool HasActiveModWithName(string name, ref bool __result)
    {
        __result = active?.HasModNamed(name) == true;
        return false;
    }

    private static bool IsActive(string id, ref bool __result)
    {
        __result = Active(id);
        return false;
    }

    private static bool DlcRoyalty(ref bool __result) => Answer(ref __result, "ludeon.rimworld.royalty");

    private static bool DlcIdeology(ref bool __result) => Answer(ref __result, "ludeon.rimworld.ideology");

    private static bool DlcBiotech(ref bool __result) => Answer(ref __result, "ludeon.rimworld.biotech");

    private static bool DlcAnomaly(ref bool __result) => Answer(ref __result, "ludeon.rimworld.anomaly");

    private static bool DlcOdyssey(ref bool __result) => Answer(ref __result, "ludeon.rimworld.odyssey");

    private static bool Answer(ref bool result, string packageId)
    {
        result = Active(packageId);
        return false;
    }

    private static bool Active(string? packageId) => packageId is not null && active?.IsActive(packageId) == true;

    private static ModMetaData Placeholder(string identifier)
    {
        string key = ActiveMods.Normalize(identifier);
        lock (Placeholders)
        {
            if (!Placeholders.TryGetValue(key, out ModMetaData? placeholder))
            {
                placeholder = (ModMetaData)RuntimeHelpers.GetUninitializedObject(typeof(ModMetaData));
                Placeholders[key] = placeholder;
            }

            return placeholder;
        }
    }
}
