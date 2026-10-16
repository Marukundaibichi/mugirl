using System.Reflection;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. <c>StaticConstructorOnStartupUtility.ReportProbablyMissingAttributes</c> 的复刻（游戏只在开发者模式下运行它）：静态字段持有
/// Texture、Material、Shader、Graphic、GameObject 或 MaterialPropertyBlock（或其数组）的类型必须标 <c>[StaticConstructorOnStartup]</c>，
/// 资源才会在内容加载后于主线程创建。按 hk 的约定把 Mesh 也算作静态资源。只读字段元数据，不运行任何静态构造函数。
/// </summary>
internal static class StaticAssetFieldCheck
{
    private static readonly Type[] AssetTypes =
    [
        typeof(UnityEngine.Texture), typeof(UnityEngine.Material), typeof(UnityEngine.Shader), typeof(Graphic),
        typeof(UnityEngine.GameObject), typeof(UnityEngine.MaterialPropertyBlock), typeof(UnityEngine.Mesh),
    ];

    public static void Run(Report report, GameRuntime runtime)
        => report.Run("B.static-assets", "Static asset fields only in [StaticConstructorOnStartup] types", recorder => Check(recorder, runtime));

    private static void Check(CheckRecorder recorder, GameRuntime runtime)
    {
        int holders = 0;
        foreach (Type type in runtime.ModTypes.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (TypeLoadException exception)
            {
                recorder.Fail(RuntimeNames.Type(type), "Static fields cannot be read: " + exception.Message);
                continue;
            }

            // 游戏中的 HasAttribute<T> 即 Attribute.IsDefined(type, T, inherit: true)。
            bool marked = Attribute.IsDefined(type, typeof(StaticConstructorOnStartup), inherit: true);
            foreach (FieldInfo field in fields)
            {
                Type fieldType = field.FieldType.IsArray ? field.FieldType.GetElementType()! : field.FieldType;
                if (!AssetTypes.Any(asset => asset.IsAssignableFrom(fieldType)))
                {
                    continue;
                }

                if (marked)
                {
                    holders++;
                }
                else
                {
                    recorder.Fail(RuntimeNames.Type(type), $"Static field {field.Name} ({RuntimeNames.Type(field.FieldType)}) holds an asset, "
                        + "but the type is not [StaticConstructorOnStartup] (the game warns in developer mode; the asset may be created off the main thread or before content is loaded).");
                }
            }
        }

        recorder.Summary = $"{holders} static asset field(s) in [StaticConstructorOnStartup] types";
    }
}
