using System.Linq;
using System.Reflection;

namespace Mugirl.SaveUpgrade;

/// <summary>按正在运行的接口识别读取契约；游戏构建号、MVID 和元数据编号的变化不禁用迁移。</summary>
internal static class ReviewedSaveEngine
{
    // StaticCacheLifecycle: 只缓存当前引擎的接口形状与方法编号，进程内不可变；不保存游戏或存档对象。
    private static readonly Module EngineModule = typeof(Scribe_Collections).Module;
    private static readonly Dictionary<int, int> Readers = FindReaders();
    private static readonly bool ThinkTreeAvailable = typeof(ThinkTreeKeyAssigner)
        .GetField("assignedKeys", BindingFlags.Static | BindingFlags.NonPublic)?.FieldType == typeof(HashSet<int>);

    /// <summary>当前引擎中找到了已审阅的读取入口。</summary>
    internal static bool ReadersAvailable => Readers.Count > 0;

    internal static bool ThinkTreeApiAvailable => ThinkTreeAvailable;

    /// <summary>
    /// 引擎方法对应的读取契约 ID；不是引擎模块中已审阅的读取入口时为 0。
    /// 旧编号仅作为内部契约 ID，实际入口按名称、参数与泛型类型找到，不按版本猜 token 偏移。
    /// </summary>
    internal static int CanonicalReaderToken(MethodBase method)
        => method.Module == EngineModule && Readers.TryGetValue(method.MetadataToken, out int contract) ? contract : 0;

    internal static int ReaderContract(MethodInfo method)
    {
        if (method.ReturnType != typeof(void)) { return 0; }
        Type[] parameters = [.. method.GetParameters().Select(parameter => parameter.ParameterType)];
        if (method.DeclaringType == typeof(LoadIDsWantedBank) && !method.IsStatic
            && method.Name == nameof(LoadIDsWantedBank.RegisterLoadIDListReadFromXml))
        {
            if (parameters.SequenceEqual([typeof(List<string>), typeof(string), typeof(IExposable)])) { return 0x060026DD; }
            return parameters.SequenceEqual([typeof(List<string>), typeof(string)]) ? 0x060026DE : 0;
        }
        if (method.DeclaringType != typeof(Scribe_Collections) || !method.IsStatic || !method.IsGenericMethodDefinition
            || method.Name != nameof(Scribe_Collections.Look)) { return 0; }
        Type[] generic = method.GetGenericArguments();
        if (generic.Length == 1)
        {
            Type list = typeof(List<>).MakeGenericType(generic[0]).MakeByRefType();
            if (parameters.SequenceEqual([list, typeof(string), typeof(bool), typeof(LookMode), typeof(object[])])) { return 0x0600271C; }
            if (parameters.SequenceEqual([list, typeof(string), typeof(LookMode), typeof(object[])])) { return 0x0600271D; }
            if (parameters.SequenceEqual([list, typeof(bool), typeof(string), typeof(LookMode), typeof(object[])])) { return 0x0600271F; }
        }
        if (generic.Length == 2)
        {
            Type dictionary = typeof(Dictionary<,>).MakeGenericType(generic).MakeByRefType();
            if (parameters.SequenceEqual([dictionary, typeof(string), typeof(LookMode), typeof(LookMode)])) { return 0x06002720; }
            if (parameters.SequenceEqual([dictionary, typeof(string), typeof(LookMode), typeof(LookMode),
                typeof(List<>).MakeGenericType(generic[0]).MakeByRefType(), typeof(List<>).MakeGenericType(generic[1]).MakeByRefType(),
                typeof(bool), typeof(bool), typeof(bool)])) { return 0x06002722; }
        }
        return 0;
    }

    private static Dictionary<int, int> FindReaders()
    {
        var result = new Dictionary<int, int>();
        foreach (Type type in new[] { typeof(LoadIDsWantedBank), typeof(Scribe_Collections) })
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                int contract = ReaderContract(method);
                if (contract != 0) { result.Add(method.MetadataToken, contract); }
            }
        }
        return result;
    }
}
