using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>观察窗口的补丁证据。方法体指纹解析元数据成员身份，不能仅凭 Harmony owner 或原始 token 字节授权。</summary>
internal static class ObservationPatchEvidence
{
    private const string GuardedRestraintPostfix = "94C5AD6877728E056D753C4CD56AEAB2A625514889774630EE1AAAAB96BE1136";
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).GroupBy(o => o.Value).ToDictionary(g => g.Key, g => g.First());

    public static JsonObject Describe(MethodBase target, Patch patch, string role, string check, string? proof)
        => new()
        {
            ["check"] = check, ["target"] = Signature(target), ["targetMvid"] = target.Module.ModuleVersionId.ToString(),
            ["targetToken"] = target.MetadataToken.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
            ["owner"] = patch.owner, ["role"] = role, ["priority"] = patch.priority, ["index"] = patch.index,
            ["before"] = new JsonArray([.. (patch.before ?? []).Select(s => (JsonNode?)JsonValue.Create(s))]),
            ["after"] = new JsonArray([.. (patch.after ?? []).Select(s => (JsonNode?)JsonValue.Create(s))]),
            ["method"] = Signature(patch.PatchMethod), ["declaringType"] = patch.PatchMethod.DeclaringType?.FullName,
            ["assembly"] = patch.PatchMethod.Module.Assembly.GetName().Name, ["assemblyPath"] = Safe(() => patch.PatchMethod.Module.Assembly.Location),
            ["mvid"] = Safe(() => patch.PatchMethod.Module.ModuleVersionId.ToString()),
            ["token"] = Safe(() => patch.PatchMethod.MetadataToken.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)),
            ["semanticIlSha256"] = Safe(() => Fingerprint(patch.PatchMethod)), ["proof"] = proof,
            ["patchMethodHooks"] = DescribeNestedHooks(patch.PatchMethod),
        };

    public static string? GuardedProductionProof(MethodBase target, Patch patch, string role)
    {
        MethodInfo method = patch.PatchMethod;
        if (target != typeof(RimWorld.Pawn_ApparelTracker).GetMethod(nameof(RimWorld.Pawn_ApparelTracker.ExposeData))
            || target.Module.ModuleVersionId != ReviewedEngine.Mvid
            || patch.owner != "MugirlMod.Mod" || role != "postfix" || patch.priority != Priority.Normal
            || (patch.before?.Length ?? 0) != 0 || (patch.after?.Length ?? 0) != 0
            || method.Module.Assembly.GetName().Name != "MugirlRace" || method.DeclaringType?.FullName != "Mugirl.Features.Restraints.Harmony_Pawn_ApparelTracker_ExposeData"
            || method.Name != "Postfix" || !method.IsStatic || method.ReturnType != typeof(void)
            || method.IsGenericMethod || method.DeclaringType.IsGenericType || method.DeclaringType.TypeInitializer is not null
            || method.GetParameters() is not [{ Name: "__instance", ParameterType: var parameter }] || parameter != typeof(RimWorld.Pawn_ApparelTracker)
            || method.GetMethodBody() is not { ExceptionHandlingClauses.Count: 0, LocalVariables.Count: 0 }
            || HasPatches(method) || Safe(() => Fingerprint(method)) != GuardedRestraintPostfix)
        {
            return null;
        }
        // 此已复读方法仅在 PostLoadInit 调用补锁服务。它不是无写入补丁；只在本观察器读取/写出证据的两个窗口直接返回。
        return "verified exact PostLoadInit-only restraint postfix; no effects in ResolvingCrossRefs/Saving; its own method has no Harmony patches; final saved empty list is still required";
    }

    private static bool HasPatches(MethodBase method)
        => Harmony.GetPatchInfo(method) is { } patches && patches.Prefixes.Count + patches.Postfixes.Count + patches.Transpilers.Count + patches.Finalizers.Count
            + patches.InnerPrefixes.Count + patches.InnerPostfixes.Count + patches.InnerFinalizers.Count != 0;

    private static JsonArray DescribeNestedHooks(MethodBase method)
    {
        var entries = new JsonArray();
        if (Harmony.GetPatchInfo(method) is not { } patches) { return entries; }
        foreach ((string role, IEnumerable<Patch> group) in new[]
            {
                ("prefix", (IEnumerable<Patch>)patches.Prefixes), ("postfix", patches.Postfixes),
                ("transpiler", patches.Transpilers), ("finalizer", patches.Finalizers),
                ("innerPrefix", patches.InnerPrefixes), ("innerPostfix", patches.InnerPostfixes), ("innerFinalizer", patches.InnerFinalizers),
            })
        {
            foreach (Patch hook in group)
            {
                entries.Add(new JsonObject { ["role"] = role, ["owner"] = hook.owner, ["priority"] = hook.priority,
                    ["method"] = Signature(hook.PatchMethod), ["mvid"] = Safe(() => hook.PatchMethod.Module.ModuleVersionId.ToString()),
                    ["token"] = Safe(() => hook.PatchMethod.MetadataToken.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)) });
            }
        }
        return entries;
    }

    private static string Safe(Func<string> read)
    {
        try { return read(); }
        catch (Exception ex) { return "unavailable: " + ex.GetType().Name + ": " + ex.Message; }
    }

    public static string Fingerprint(MethodInfo method)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SemanticBody(method))));

    public static string SerializationFingerprint(MethodBase method)
    {
        MethodBody body = method.GetMethodBody() ?? throw new InvalidOperationException("A serializer has no inspectable body.");
        var text = new StringBuilder(SemanticBody(method)).Append("attributes:").Append(method.Attributes).Append('|').Append(method.GetMethodImplementationFlags())
            .Append("\ntype:").Append(method.DeclaringType!.Attributes).Append("\ninitLocals:").Append(body.InitLocals);
        foreach (LocalVariableInfo local in body.LocalVariables)
        {
            text.Append("\nlocal:").Append(local.LocalType.Assembly.GetName().Name).Append('|').Append(ReviewedEngine.CanonicalTypeName(local.LocalType)).Append('|').Append(local.IsPinned);
        }
        foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
        {
            text.Append("\neh:").Append(clause.Flags).Append('|').Append(clause.TryOffset).Append('|').Append(clause.TryLength)
                .Append('|').Append(clause.HandlerOffset).Append('|').Append(clause.HandlerLength);
            if (clause.Flags == ExceptionHandlingClauseOptions.Filter) { text.Append('|').Append(clause.FilterOffset); }
            if (clause.Flags == ExceptionHandlingClauseOptions.Clause) { text.Append('|').Append(ReviewedEngine.CanonicalTypeName(clause.CatchType, qualified: true)); }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public static string SemanticBody(MethodBase method)
    {
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? throw new InvalidOperationException("A patch method has no inspectable IL body.");
        var result = new StringBuilder(Signature(method)).Append('\n');
        int offset = 0;
        while (offset < bytes.Length)
        {
            short number = bytes[offset++];
            if (number == 0xFE) { number = unchecked((short)(0xFE00 | bytes[offset++])); }
            OpCode opcode = Opcodes[number];
            int size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4,
            };
            string operand = Convert.ToHexString(bytes.AsSpan(offset, size));
            if (opcode.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok)
            {
                int token = BitConverter.ToInt32(bytes, offset);
                MemberInfo member = method.Module.ResolveMember(token, method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null)
                    ?? throw new InvalidOperationException("Unresolved patch IL token.");
                operand = member switch
                {
                    MethodBase called => Signature(called),
                    FieldInfo field => field.Module.Assembly.GetName().Name + "|" + ReviewedEngine.CanonicalTypeName(field.DeclaringType) + "::" + field.Name + ":" + ReviewedEngine.CanonicalTypeName(field.FieldType),
                    Type type => type.Assembly.GetName().Name + "|" + (ReviewedEngine.CanonicalTypeName(type) ?? type.Name),
                    _ => throw new InvalidOperationException("Unsupported patch IL metadata operand."),
                };
            }
            else if (opcode.OperandType == OperandType.InlineString)
            {
                operand = JsonValue.Create(method.Module.ResolveString(BitConverter.ToInt32(bytes, offset)))!.ToJsonString();
            }
            else if (opcode.OperandType == OperandType.InlineSig)
            {
                throw new InvalidOperationException("An indirect call signature cannot be authorized by a known patch proof.");
            }
            result.Append(opcode.Name).Append(' ').Append(operand).Append('\n');
            offset += size;
        }
        return result.ToString();
    }

    private static string Signature(MethodBase method)
        => method.Module.Assembly.GetName().Name + "|" + (ReviewedEngine.CanonicalTypeName(method.DeclaringType) ?? "<dynamic>") + "::" + method.Name + "(" + string.Join(",", method.GetParameters().Select(p => ReviewedEngine.CanonicalTypeName(p.ParameterType))) + ")"
            + (method is MethodInfo info ? ":" + ReviewedEngine.CanonicalTypeName(info.ReturnType) : string.Empty);
}
