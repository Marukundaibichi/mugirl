using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Legacy;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. <c>Scribe_Values.Look</c> 的默认值等于字段的常量初始值（没有初始值时为类型默认值），按 IL 检查：节点缺失时（旧存档没有这个
/// 标签、或值等于默认值而没写）读到的值必须与新建对象相同。默认值不是常量（取自 Props/Def）的，或字段在构造函数、Initialize、
/// PostMake 一类方法中由非常量赋值的（动态默认值），必须 <c>forceSave: true</c>，否则缺失的节点会读成另一个值。
/// </summary>
/// <remarks>
/// 只看 <c>ref this.field</c> 形式的调用；经辅助方法转交的字段读不到（报告为信息）。现有违规列在 <c>Baselines/scribe-defaults.allow.txt</c>，
/// 键为 <c>Owner::field (label)</c>，只能减少。
/// </remarks>
internal static class ScribeDefaultsCheck
{
    public const string AllowFile = "scribe-defaults.allow.txt";

    private static readonly string[] InitializerMethods = ["Initialize", "PostMake", "PostPostMake", "CompPostMake", "PostAdd", "CompPostPostAdd", "PostSpawnSetup", "SpawnSetup", "Init"];

    public static void Run(Report report, ValidationOptions options, ModuleStringFlow flow)
        => report.Run("B.scribe-defaults", "Scribe_Values.Look defaults equal the field initializer; dynamic defaults use forceSave", recorder => Check(recorder, options, flow));

    private static void Check(CheckRecorder recorder, ValidationOptions options, ModuleStringFlow flow)
    {
        AllowList allow = AllowList.Load(options.BaselineDir, AllowFile);
        ScribeSchema schema = ScribeSchema.Analyze(flow, xml: null);
        Dictionary<string, FieldDefinition> fields = CecilNames.AllTypes(flow.Module).SelectMany(t => t.Fields).Where(f => !f.IsStatic)
            .ToDictionary(CecilNames.MemberShort, f => f, StringComparer.Ordinal);
        var dynamicFields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (FieldDefinition field in fields.Values)
        {
            if (DynamicAssignment(field) is { } where)
            {
                dynamicFields[CecilNames.MemberShort(field)] = where;
            }
        }

        int checkedCalls = 0;
        int indirect = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (LabelFact fact in schema.Labels.Where(f => f.Kind == "Values"))
        {
            if (fact.Field is null || !fields.TryGetValue(fact.Field, out FieldDefinition? field))
            {
                indirect++;
                continue;
            }

            string key = $"{fact.Field} ({fact.Label})";
            if (!seen.Add(key + "|" + fact.Default + "|" + fact.ForceSave))
            {
                continue;
            }

            checkedCalls++;
            string? problem = null;
            bool forced = fact.ForceSave == "True";
            if (fact.Default is "dynamic" or "def-dependent")
            {
                problem = forced ? null : $"Default value is computed ({fact.Default}); a missing node would load that run-time value. Pass forceSave: true.";
            }
            else if (dynamicFields.TryGetValue(fact.Field, out string? where))
            {
                problem = forced ? null : $"The field is assigned a computed value in {where}; a save that omitted the node loads the Scribe default {fact.Default} instead. Pass forceSave: true.";
            }
            else
            {
                string initializer = Initializer(field);
                problem = initializer == fact.Default || forced ? null
                    : $"Scribe default {fact.Default} differs from the field initializer {initializer}: a missing node (older save, or a value equal to the default) loads {fact.Default} while a new object starts at {initializer}.";
            }

            if (problem is not null && !allow.Allows(key))
            {
                recorder.Fail(key, problem, fact.Location);
            }
        }

        allow.ReportProblems(recorder);
        if (indirect > 0)
        {
            recorder.Info("(indirect)", $"{indirect} Scribe_Values call(s) save a value that is not a field of the owner (local, property or helper parameter); not checked.");
        }

        recorder.Summary = $"{checkedCalls} Scribe_Values.Look call(s) on fields checked";
    }

    // 字段在构造函数之外的初始化方法中，或在构造函数中由非常量赋值（参数、Props、Def）的位置。
    private static string? DynamicAssignment(FieldDefinition field)
    {
        foreach (MethodDefinition method in field.DeclaringType.Methods.Where(m => m.HasBody && (m.IsConstructor && !m.IsStatic || InitializerMethods.Contains(m.Name, StringComparer.Ordinal))))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code == Code.Stfld && instruction.Operand is FieldReference stored && stored.Name == field.Name
                    && CecilNames.TryResolve(stored.DeclaringType) == field.DeclaringType)
                {
                    if (!method.IsConstructor || IlArguments.Constant(method, instruction.Previous, field.FieldType) is null)
                    {
                        return method.Name;
                    }
                }
            }
        }

        return null;
    }

    // 字段的常量初始值（构造函数中的 ldarg.0; 常量; stfld），没有时为类型默认值的 Scribe 文本。
    private static string Initializer(FieldDefinition field)
    {
        foreach (MethodDefinition ctor in field.DeclaringType.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
        {
            foreach (Instruction instruction in ctor.Body.Instructions)
            {
                if (instruction.OpCode.Code == Code.Stfld && instruction.Operand is FieldReference stored && stored.Name == field.Name
                    && IlArguments.Constant(ctor, instruction.Previous, field.FieldType) is { } value)
                {
                    return value;
                }
            }
        }

        return DefaultText(field.FieldType);
    }

    private static string DefaultText(TypeReference type) => type.FullName switch
    {
        "System.Boolean" => "False",
        "System.Single" or "System.Double" => "0",
        "System.Int32" or "System.Int64" or "System.Int16" or "System.Byte" or "System.UInt32" or "System.UInt64" or "System.UInt16" or "System.SByte" or "System.Char" => "0",
        _ when CecilNames.TryResolve(type) is { IsEnum: true } enumType => IlArguments.EnumMember(enumType, 0) ?? "0",
        _ when type.IsValueType => "default",
        _ => "null",
    };
}
