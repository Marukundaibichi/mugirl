using MugirlRace.Validation.Content;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. 每个 <c>[DefOf]</c> 字段都能绑定（<c>DefOfHelper.BindDefsFor</c> 的复刻）：Def 存在且类型可赋值；没有 <c>[MayRequire…]</c>
/// 的字段只靠 Core 与 Mod 的硬依赖就必须满足。DefOf 类的静态构造函数必须调用 <c>DefOfHelper.EnsureInitializedInCtor</c>，
/// 过早使用（例如 Mod 构造期间的补丁代码）才会被报告，而不是静默得到 null。
/// </summary>
/// <remarks>
/// DefOf 类只经元数据（Mono.Cecil）读取，绝不反射类本身：碰到 DefOf 类型会运行其静态构造函数，离线时记录
/// “Tried to use an uninitialized DefOf”。
/// </remarks>
internal static class DefOfCheck
{
    public static void Run(Report report, MetadataWorld world, ContentModel content)
        => report.Run("C.defof", "Every [DefOf] field has its Def (with MayRequire for optional content)", recorder => Check(recorder, world.Mod, content));

    private static void Check(CheckRecorder recorder, ModuleDefinition module, ContentModel content)
    {
        int fields = 0;
        int classes = 0;
        foreach (TypeDefinition type in CecilNames.AllTypes(module).Where(t => HasAttribute(t, "RimWorld.DefOf")).OrderBy(CecilNames.Type, StringComparer.Ordinal))
        {
            classes++;
            string className = CecilNames.Type(type);
            if (content.Types.GetTypeInAnyAssembly(className) is { } runtimeType && typeof(Def).IsAssignableFrom(runtimeType))
            {
                recorder.Fail(className, "A [DefOf] class must not itself be a Def (DefOfHelper warns \"Possible typo\").");
            }

            if (!CallsEnsureInitialized(type))
            {
                recorder.Fail(className, "The static constructor must call DefOfHelper.EnsureInitializedInCtor(typeof(...)), so early use is reported instead of silently returning null.");
            }

            foreach (FieldDefinition field in type.Fields.Where(f => f.IsStatic && f.IsPublic))
            {
                fields++;
                CheckField(recorder, content, className, field);
            }
        }

        recorder.Summary = $"{fields} fields in {classes} [DefOf] classes checked";
    }

    private static void CheckField(CheckRecorder recorder, ContentModel content, string className, FieldDefinition field)
    {
        string subject = className + "." + field.Name;
        Type? fieldType = content.Types.GetTypeInAnyAssembly(CecilNames.Type(field.FieldType));
        if (fieldType is null || !typeof(Def).IsAssignableFrom(fieldType))
        {
            recorder.Fail(subject, $"Field type {CecilNames.Type(field.FieldType)} is not a Def (DefOfHelper logs an error).");
            return;
        }

        string defName = AttributeString(field, "RimWorld.DefAlias") ?? field.Name;
        (List<string> requiredAll, List<string> requiredAny) = MayRequire(field, content);
        IReadOnlyList<DefEntry> found = content.Defs.Find(fieldType, defName);
        string what = $"{fieldType.Name} '{defName}'";
        if (found.Count == 0)
        {
            string? notInstalled = requiredAll.Concat(requiredAny).FirstOrDefault(id => !content.Catalog.Full.IsActive(id));
            if (notInstalled is not null)
            {
                recorder.Info(subject, $"{what} is guarded by MayRequire({notInstalled}), which is not installed here; not verifiable.");
                return;
            }

            IReadOnlyList<DefEntry> otherType = content.Defs.FindAny(defName);
            recorder.Fail(subject, otherType.Count > 0
                ? $"{what} does not exist; '{defName}' is a {string.Join(", ", otherType.Select(e => e.Type.Name).Distinct())}."
                : $"{what} does not exist in Core, the DLCs, the dependencies or this mod.");
            return;
        }

        bool guarded = requiredAll.Count > 0 || requiredAny.Count > 0;
        if (!guarded && !found.Any(entry => entry.InMinimal))
        {
            string providers = string.Join(", ", found.Select(entry => entry.Pack.PackageId).Distinct());
            recorder.Fail(subject, $"{what} comes only from optional content ({providers}); without it DefOfHelper logs an error. Add [MayRequire…] and handle null.");
        }
    }

    // [MayRequire("id")] 或其子类（如 [MayRequireBiotech]）；[MayRequireAnyOf("a,b")]。
    private static (List<string> All, List<string> Any) MayRequire(FieldDefinition field, ContentModel content)
    {
        var all = new List<string>();
        var any = new List<string>();
        foreach (CustomAttribute attribute in field.CustomAttributes)
        {
            string name = CecilNames.Type(attribute.AttributeType);
            if (name == "RimWorld.MayRequireAnyOfAttribute")
            {
                // 构造函数接受一个逗号分隔的包 id 字符串。
                if (attribute.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is string ids)
                {
                    any.AddRange(ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }

                continue;
            }

            Type? runtime = content.Types.GetTypeInAnyAssembly(name);
            if (runtime is null || !typeof(MayRequireAttribute).IsAssignableFrom(runtime))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is string modId)
            {
                all.Add(modId);
            }
            else if (Activator.CreateInstance(runtime) is MayRequireAttribute fixedId)
            {
                // 无参子类（MayRequireBiotech 等）把自己的包 id 传给基类构造函数。
                all.Add(fixedId.modId);
            }
        }

        return (all, any);
    }

    private static bool CallsEnsureInitialized(TypeDefinition type)
        => type.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic && m.HasBody) is { } cctor
            && cctor.Body.Instructions.Any(i => i.OpCode.Code == Code.Call && i.Operand is MethodReference { Name: "EnsureInitializedInCtor" } m
                && m.DeclaringType.FullName == "RimWorld.DefOfHelper");

    private static bool HasAttribute(ICustomAttributeProvider provider, string fullName)
        => provider.HasCustomAttributes && provider.CustomAttributes.Any(a => a.AttributeType.FullName == fullName || a.AttributeType.FullName == fullName + "Attribute");

    private static string? AttributeString(ICustomAttributeProvider provider, string fullName)
        => provider.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == fullName || a.AttributeType.FullName == fullName + "Attribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;
}
