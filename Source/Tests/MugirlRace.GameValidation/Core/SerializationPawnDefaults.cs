using System.Reflection;
using System.Text.Json.Nodes;

namespace Mugirl.GameValidation.Core;

/// <summary>当前实际 Def 的 Pawn 类型、组件次数及 Mount 配置输入；不读取重存档或已加载的开火状态。</summary>
internal sealed record SerializationPawnDefaults(string[] Classes, IReadOnlyDictionary<string, int> Comps, bool? MountDefault, string? MountPropsType)
{
    public SerializationWheelDefaults? Wheel { get; init; }
    public static Dictionary<string, SerializationPawnDefaults> Read(IEnumerable<ThingDef> defs, Assembly production)
    {
        Type mount = production.GetType(CurrentSerializationDefaults.MountType, throwOnError: true)!;
        Type propsType = production.GetType("Mugirl.Features.Mounting.CompProperties_Mount", throwOnError: true)!;
        FieldInfo field = propsType.GetField("turretFireAtWillDefault", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        if (field.FieldType != typeof(bool)) { throw new InvalidOperationException("The Mount default field is no longer the verified Boolean configuration."); }
        var result = new Dictionary<string, SerializationPawnDefaults>(StringComparer.Ordinal);
        foreach (ThingDef def in defs.Where(d => d.thingClass is not null && typeof(Pawn).IsAssignableFrom(d.thingClass)))
        {
            var counts = def.comps.Where(p => p.compClass?.Assembly == production).GroupBy(p => p.compClass.FullName!)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            if (counts.Count == 0) { continue; }
            CompProperties[] mounts = [.. def.comps.Where(p => p.compClass == mount)];
            CompProperties? props = mounts.Length == 1 ? mounts[0] : null;
            bool? value = props is not null && propsType.IsInstanceOfType(props) ? (bool)field.GetValue(props)! : null;
            string[] classes = def.thingClass.Namespace is "Verse" or "RimWorld" ? [def.thingClass.FullName!, def.thingClass.Name] : [def.thingClass.FullName!];
            CompProperties[] wheels = [.. def.comps.Where(p => p.compClass?.FullName == CurrentSerializationDefaults.WheelType && p.compClass.Assembly == production)];
            result.Add(def.defName, new SerializationPawnDefaults(classes, counts, value, props?.GetType().FullName)
            {
                Wheel = wheels.Length == 1 ? SerializationWheelDefaults.Read(wheels[0], production) : null,
            });
        }
        return result;
    }

    public static JsonObject ToJson(IReadOnlyDictionary<string, SerializationPawnDefaults> rows)
        => new([.. rows.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, new JsonObject
        {
            ["classes"] = new JsonArray([.. p.Value.Classes.Select(c => (JsonNode?)JsonValue.Create(c))]),
            ["componentCounts"] = new JsonObject([.. p.Value.Comps.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new KeyValuePair<string, JsonNode?>(c.Key, JsonValue.Create(c.Value)))]),
            ["mountDefault"] = p.Value.MountDefault, ["mountPropsType"] = p.Value.MountPropsType,
            ["wheel"] = p.Value.Wheel?.ToJson(),
        }))]);
}
