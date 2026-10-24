using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using HarmonyLib;

namespace Mugirl.SaveUpgrade;

/// <summary>一次旧档读取的不可变身份映射。只能用于已知身份或实际 Scribe 引用，不能扫描任意 XML 文字。</summary>
internal sealed class LoadIdRemapper
{
    private readonly LoadIdScanner references;
    private readonly LoadIdScanner identities;
    private readonly Dictionary<string, string> recipes;
    private readonly HashSet<string> removedRecipes;

    internal LoadIdRemapper(
        Dictionary<string, string> thingRenames,
        Dictionary<string, string> recipeRenames,
        IEnumerable<string> removedLoadIds,
        IEnumerable<string> removedRecipes)
    {
        var things = new Dictionary<string, string>(thingRenames, StringComparer.Ordinal);
        var removed = new HashSet<string>(removedLoadIds, StringComparer.Ordinal);
        references = new LoadIdScanner(things, removed);
        identities = new LoadIdScanner(things, new HashSet<string>(StringComparer.Ordinal));
        recipes = new Dictionary<string, string>(recipeRenames, StringComparer.Ordinal);
        this.removedRecipes = new HashSet<string>(removedRecipes, StringComparer.Ordinal);
    }

    /// <summary>不存在的引用用 Scribe 的 null 哨兵表示；普通字符串从不调用此方法。</summary>
    internal string? Reference(string? loadId)
    {
        if (string.IsNullOrEmpty(loadId) || loadId == "null")
        {
            return loadId;
        }

        // 删除先于改名，尤其是已删除的账单或包含已删除物品的组件/动词身份。
        LoadIdScanner.Result result = references.Scan(loadId, out string? renamed);
        if (result == LoadIdScanner.Result.ReferencesRemoved)
        {
            return "null";
        }

        if (LoadIdTokens.TryParseBillId(loadId, out string recipe, out string number))
        {
            if (removedRecipes.Contains(recipe))
            {
                return "null";
            }

            if (recipes.TryGetValue(recipe, out string? newRecipe))
            {
                return "Bill_" + newRecipe + "_" + number;
            }
        }

        return result == LoadIdScanner.Result.Rewritten ? renamed : loadId;
    }

    /// <summary>按需复制，保留 null/空列表、顺序和条目数量；这里也承载字典键值与并行列表，不能删除条目。</summary>
    internal List<string>? References(List<string>? loadIds)
    {
        if (loadIds is null)
        {
            return null;
        }

        List<string>? rewritten = null;
        for (int i = 0; i < loadIds.Count; i++)
        {
            string? value = Reference(loadIds[i]);
            if (!string.Equals(value, loadIds[i], StringComparison.Ordinal))
            {
                rewritten ??= new List<string>(loadIds);
                rewritten[i] = value!;
            }
        }

        return rewritten ?? loadIds;
    }

    /// <summary>
    /// 真实 Reference 列表的登记边界。普通列表保留下标；只有已证明正在读取原版字典的 keys，才连同尚未读取的 values 成对删除。
    /// </summary>
    internal List<string>? RegisteredReferences(List<string>? loadIds, XmlNode? node, SaveUpgradeSession session)
    {
        List<string>? rewritten = References(loadIds);
        session.ReadTrace?.References(node, loadIds, rewritten);
        if (loadIds is null || ReferenceEquals(rewritten, loadIds)
            || node is not XmlElement { Name: "keys", ParentNode: XmlElement dictionary } keys
            || dictionary["values"] is not { } values)
        {
            return rewritten;
        }

        List<int> removed = [];
        for (int i = 0; i < loadIds.Count; i++)
        {
            if (rewritten![i] == "null" && !string.IsNullOrEmpty(loadIds[i]) && loadIds[i] != "null")
            {
                removed.Add(i);
            }
        }
        if (removed.Count == 0)
        {
            return rewritten;
        }

        const string Step = "Framework.LoadIds.DictionaryReferences";
        DictionaryReadKind kind;
        try
        {
            kind = DictionaryReadEvidence.Current();
        }
        catch (Exception exception)
        {
            session.Report.Fail(Step, "The typed reference dictionary reader could not be verified: " + exception);
            return rewritten;
        }
        if (kind == DictionaryReadKind.List)
        {
            return rewritten;
        }

        if (kind != DictionaryReadKind.Dictionary)
        {
            session.Report.Fail(Step, "A removed reference occurs in a keys/values structure, but the active typed Scribe reader could not be verified. The save schema is not upgraded.");
            return rewritten;
        }

        // The keys reader has already collected this list; the verified dictionary reader reads values next.
        // Validate every node before changing either list, including any earlier third-party changes to the IDs.
        if (keys.ChildNodes.Count != loadIds.Count || values.ChildNodes.Count != loadIds.Count)
        {
            session.Report.Fail(Step, "The verified reference dictionary has mismatched key/value counts. No pair was removed.");
            return rewritten;
        }
        for (int i = 0; i < loadIds.Count; i++)
        {
            if (keys.ChildNodes[i] is not XmlElement { Name: "li" } key
                || values.ChildNodes[i] is not XmlElement { Name: "li" }
                || DomEditor.HasElementChild(key)
                || key.InnerText != loadIds[i])
            {
                session.Report.Fail(Step, "The verified reference dictionary no longer matches its registered key list. No pair was removed.");
                return rewritten;
            }
        }
        foreach (int index in removed)
        {
            if (DomEditor.HasElementChild((XmlElement)values.ChildNodes[index]!))
            {
                // A deep value may own independently referenced objects. Dropping it here could orphan references
                // registered before this reader. Such a container needs an explicit DOM migration with cascade tracking.
                session.Report.Fail(Step, "A removed dictionary key owns a deep value. Its object lifetime and earlier references require an explicit structural migration; no pair was removed.");
                return rewritten;
            }
        }

        var retained = new List<string>(loadIds.Count - removed.Count);
        int nextRemoved = 0;
        for (int i = 0; i < loadIds.Count; i++)
        {
            if (nextRemoved < removed.Count && removed[nextRemoved] == i)
            {
                nextRemoved++;
            }
            else
            {
                retained.Add(rewritten![i]);
            }
        }

        bool applied = session.RunAction(Step, UpgradePhase.Dom, () =>
        {
            session.ReadTrace?.DictionaryPairs(dictionary, keys, values, removed, rewritten!);
            for (int i = removed.Count - 1; i >= 0; i--)
            {
                keys.RemoveChild(keys.ChildNodes[removed[i]]!);
                values.RemoveChild(values.ChildNodes[removed[i]]!);
            }
            return removed.Count * 2;
        });
        return applied ? retained : rewritten;
    }

    /// <summary>动词身份须与新 owner 生成的 ID 一致；不重建动词，也不改变 burst 状态。</summary>
    internal string? VerbIdentity(string? loadId) =>
        loadId is not null && identities.Scan(loadId, out string? renamed) == LoadIdScanner.Result.Rewritten ? renamed : loadId;

    /// <summary>会话和 XML 文档都必须属于当前旧档；嵌套读取其它文件、失败、新档和未来版本都不适用。</summary>
    internal static bool TryForDocument(
        SaveUpgradeSession? session,
        XmlNode? currentNode,
        [NotNullWhen(true)] out LoadIdRemapper? remapper)
    {
        remapper = null;
        if (session is not { IsLegacyRun: true, Completed: false, LoadIds: { } aliases }
            || session.Report.Failed
            || currentNode?.OwnerDocument is not { } document
            || !ReferenceEquals(document, session.Document?.Xml))
        {
            return false;
        }

        remapper = aliases;
        return true;
    }

    private enum DictionaryReadKind { Unknown, List, Dictionary }

    /// <summary>
    /// 实际方法签名和调用点共同证明读取边界，不依赖构建编号。未知包装器或不可用调用点不能猜成字典。
    /// 调用者的 IL 区分普通列表与被内联的字典，仅凭标签不能决定成对删除。
    /// </summary>
    private static class DictionaryReadEvidence
    {
        // StaticCacheLifecycle: immutable engine metadata and opcode widths, valid for the process lifetime.
        private static readonly Module EngineModule = typeof(Scribe_Collections).Module;
        private static readonly Dictionary<short, OpCode> Opcodes = BuildOpcodes();

        internal static DictionaryReadKind Current()
        {
            if (!ReviewedSaveEngine.ReadersAvailable)
            {
                return DictionaryReadKind.Unknown;
            }

            bool sawBank = false;
            foreach (StackFrame frame in new StackTrace(fNeedFileInfo: false).GetFrames())
            {
                MethodBase? method = Harmony.GetOriginalMethodFromStackframe(frame);
                if (!sawBank)
                {
                    sawBank = IsBankMethod(method, 0x060026DD, [typeof(List<string>), typeof(string), typeof(IExposable)]);
                    continue;
                }
                if (IsBankMethod(method, 0x060026DE, [typeof(List<string>), typeof(string)]))
                {
                    continue;
                }
                if (method?.DeclaringType == typeof(Scribe_Collections))
                {
                    DictionaryReadKind reader = Reader(method);
                    if (reader == DictionaryReadKind.Dictionary)
                    {
                        return reader;
                    }
                    if (reader != DictionaryReadKind.List)
                    {
                        return DictionaryReadKind.Unknown;
                    }
                }
                else
                {
                    // Do not look through a nested ExposeData/Deep call to an unrelated outer dictionary.
                    return ReaderAtCallSite(frame);
                }
            }
            return DictionaryReadKind.Unknown;
        }

        private static bool IsBankMethod(MethodBase? method, int token, Type[] parameters)
        {
            if (method is not MethodInfo info || info.Module != EngineModule
                || ReviewedSaveEngine.CanonicalReaderToken(info) != token
                || info.DeclaringType != typeof(LoadIDsWantedBank) || info.IsStatic || info.ReturnType != typeof(void))
            {
                return false;
            }
            ParameterInfo[] actual = info.GetParameters();
            if (actual.Length != parameters.Length)
            {
                return false;
            }
            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i].ParameterType != parameters[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static DictionaryReadKind Reader(MethodBase method)
        {
            if (method is not MethodInfo { IsStatic: true, IsGenericMethod: true } info
                || info.Module != EngineModule || info.DeclaringType != typeof(Scribe_Collections)
                || info.Name != nameof(Scribe_Collections.Look) || info.ReturnType != typeof(void))
            {
                return DictionaryReadKind.Unknown;
            }
            if (Harmony.GetPatchInfo(info) is { } patches
                && (patches.Prefixes.Count != 0 || patches.Postfixes.Count != 0 || patches.Transpilers.Count != 0 || patches.Finalizers.Count != 0))
            {
                return DictionaryReadKind.Unknown;
            }
            info = info.GetGenericMethodDefinition();
            ParameterInfo[] parameters = info.GetParameters();
            Type[] generic = info.GetGenericArguments();
            Type first = parameters[0].ParameterType;
            if (!first.IsByRef || first.GetElementType() is not { IsGenericType: true } collection)
            {
                return DictionaryReadKind.Unknown;
            }

            int token = ReviewedSaveEngine.CanonicalReaderToken(info);
            bool dictionary = token is 0x06002720 or 0x06002722;
            if (dictionary && generic.Length == 2 && collection.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                && parameters.Length == (token == 0x06002720 ? 4 : 9)
                && parameters[1].ParameterType == typeof(string)
                && parameters[2].ParameterType == typeof(LookMode) && parameters[3].ParameterType == typeof(LookMode))
            {
                if (parameters.Length == 4
                    || (parameters[4].ParameterType == typeof(List<>).MakeGenericType(generic[0]).MakeByRefType()
                        && parameters[5].ParameterType == typeof(List<>).MakeGenericType(generic[1]).MakeByRefType()
                        && parameters[6].ParameterType == typeof(bool) && parameters[7].ParameterType == typeof(bool)
                        && parameters[8].ParameterType == typeof(bool)))
                {
                    return DictionaryReadKind.Dictionary;
                }
            }
            if (token is 0x0600271C or 0x0600271D or 0x0600271F
                && generic.Length == 1 && collection.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type[] expected = token switch
                {
                    0x0600271C => [first, typeof(string), typeof(bool), typeof(LookMode), typeof(object[])],
                    0x0600271D => [first, typeof(string), typeof(LookMode), typeof(object[])],
                    _ => [first, typeof(bool), typeof(string), typeof(LookMode), typeof(object[])],
                };
                if (parameters.Length == expected.Length)
                {
                    for (int i = 0; i < expected.Length; i++)
                    {
                        if (parameters[i].ParameterType != expected[i])
                        {
                            return DictionaryReadKind.Unknown;
                        }
                    }
                    return DictionaryReadKind.List;
                }
            }
            return DictionaryReadKind.Unknown;
        }

        private static DictionaryReadKind ReaderAtCallSite(StackFrame frame)
        {
            MethodBase? caller = frame.GetMethod();
            int offset = frame.GetILOffset();
            if (caller is null || offset < 0)
            {
                return DictionaryReadKind.Unknown;
            }
            try
            {
                byte[]? il = caller.GetMethodBody()?.GetILAsByteArray();
                if (il is null)
                {
                    return DictionaryReadKind.Unknown;
                }
                DictionaryReadKind onlyReader = DictionaryReadKind.Unknown;
                bool hasReader = false;
                bool opaqueCall = false;
                for (int position = 0; position < il.Length;)
                {
                    int start = position;
                    short code = il[position++] == 0xFE ? unchecked((short)(0xFE00 | il[position++])) : il[start];
                    if (!Opcodes.TryGetValue(code, out OpCode opcode))
                    {
                        return DictionaryReadKind.Unknown;
                    }
                    int size = OperandSize(opcode.OperandType, il, position);
                    if (size < 0 || position + size > il.Length)
                    {
                        return DictionaryReadKind.Unknown;
                    }
                    int end = position + size;
                    if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt || opcode == OpCodes.Newobj)
                    {
                        MethodBase? called = caller.Module.ResolveMethod(BitConverter.ToInt32(il, position), caller.DeclaringType?.GetGenericArguments(), caller.IsGenericMethod ? caller.GetGenericArguments() : null);
                        DictionaryReadKind reader = called is null ? DictionaryReadKind.Unknown : Reader(called);
                        if ((offset == start || offset == end) && reader != DictionaryReadKind.Unknown)
                        {
                            return reader;
                        }
                        if (reader != DictionaryReadKind.Unknown)
                        {
                            opaqueCall |= hasReader && onlyReader != reader;
                            hasReader = true;
                            onlyReader = reader;
                        }
                        else if (called is not MethodInfo { IsStatic: true, IsGenericMethod: true, DeclaringType: { } declaring } arrayEmpty
                            || declaring != typeof(Array) || arrayEmpty.Name != nameof(Array.Empty) || arrayEmpty.GetParameters().Length != 0)
                        {
                            opaqueCall = true;
                        }
                    }
                    opaqueCall |= opcode == OpCodes.Calli;
                    position = end;
                }
                // Optimized .NET frames can report IL offset zero. A body containing only one reader family and
                // Array.Empty calls still proves the family; any helper, constructor or other call makes it unknown.
                return hasReader && !opaqueCall ? onlyReader : DictionaryReadKind.Unknown;
            }
            catch (Exception)
            {
                // Dynamic wrappers and unsupported reflection metadata cannot establish a reader contract.
            }
            return DictionaryReadKind.Unknown;
        }

        private static int OperandSize(OperandType type, byte[] il, int offset) => type switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch when offset + 4 <= il.Length => checked(4 + 4 * BitConverter.ToInt32(il, offset)),
            OperandType.InlineSwitch => -1,
            _ => 4,
        };

        private static Dictionary<short, OpCode> BuildOpcodes()
        {
            var result = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode opcode)
                {
                    result[opcode.Value] = opcode;
                }
            }
            return result;
        }
    }
}
