using System.Reflection;

namespace Mugirl.GameValidation.Core;

/// <summary>观察器用实际接口绑定历史契约；MVID 与 token 仅记录来源，不作为游戏小版本白名单。</summary>
internal static class ReviewedEngine
{
    // StaticCacheLifecycle: 进程级接口描述和解析结果，不持有世界或存档。
    private static readonly Dictionary<int, ReaderSymbol> Symbols = new()
    {
        [0x06000595] = new("Verse.GenTypes", "AllSubclassesNonAbstract", "List", "Type", true, 0),
        [0x06000DD2] = new("Verse.Game", "ExposeSmallComponents", "Void", "", false, 0),
        [0x06000DD3] = new("Verse.Game", "FillComponents", "Void", "", false, 0),
        [0x06000DD9] = new("Verse.Game", "GetComponent", "GameComponent", "Type", false, 0),
        [0x06000DEF] = new("Verse.GameComponentUtility", "LoadedGame", "Void", "", true, 0),
        [0x06001458] = new("Verse.Map", "FillComponents", "Void", "", false, 0),
        [0x0600145B] = new("Verse.Map", "ExposeComponents", "Void", "", false, 0),
        [0x06001460] = new("Verse.Map", "GetComponent", "MapComponent", "Type", false, 0),
        [0x060026DD] = new("Verse.LoadIDsWantedBank", "RegisterLoadIDListReadFromXml", "Void", "List,String,IExposable", false, 0),
        [0x060026DE] = new("Verse.LoadIDsWantedBank", "RegisterLoadIDListReadFromXml", "Void", "List,String", false, 0),
        [0x060026E6] = new("Verse.ScribeExtractor", "ValueFromNode", "T", "XmlNode,T", true, 1),
        [0x06002705] = new("Verse.ScribeSaver", "WriteElement", "Void", "String,String", false, 0),
        [0x0600271C] = new("Verse.Scribe_Collections", "Look", "Void", "List&,String,Boolean,LookMode,Object[]", true, 1),
        [0x0600271D] = new("Verse.Scribe_Collections", "Look", "Void", "List&,String,LookMode,Object[]", true, 1),
        [0x0600271F] = new("Verse.Scribe_Collections", "Look", "Void", "List&,Boolean,String,LookMode,Object[]", true, 1),
        [0x06002720] = new("Verse.Scribe_Collections", "Look", "Void", "Dictionary&,String,LookMode,LookMode", true, 2),
        [0x06002722] = new("Verse.Scribe_Collections", "Look", "Void", "Dictionary&,String,LookMode,LookMode,List&,List&,Boolean,Boolean,Boolean", true, 2),
        [0x06002742] = new("Verse.Scribe_Values", "Look", "Void", "T&,String,T,Boolean", true, 1),
        [0x06002769] = new("Verse.CompColorable", "PostExposeData", "Void", "", false, 0),
        [0x060027A2] = new("Verse.ThingComp", "PostExposeData", "Void", "", false, 0),
        [0x06002A98] = new("Verse.Thing", "ExposeData", "Void", "", false, 0),
        [0x06002B79] = new("Verse.ThingWithComps", "GetComp", "T", "", false, 1),
        [0x06002B7C] = new("Verse.ThingWithComps", "InitializeComps", "Void", "", false, 0),
        [0x06002B7D] = new("Verse.ThingWithComps", "RebuildCompsByType", "Void", "", false, 0),
        [0x06002B7F] = new("Verse.ThingWithComps", "ExposeData", "Void", "", false, 0),
        [0x060034A1] = new("Verse.BackCompatibility", "PostExposeData", "Void", "Object", true, 0),
        [0x060034B1] = new("Verse.BackCompatibility", "CheckSaveIdenticalToCurrentEnvironment", "Boolean", "", true, 0),
        [0x060068A7] = new("RimWorld.Quest", "ExposeData", "Void", "", false, 0),
        [0x060068C2] = new("RimWorld.QuestManager", "ExposeData", "Void", "", false, 0),
        [0x060068D8] = new("RimWorld.QuestPart_Delay", "ExposeData", "Void", "", false, 0),
        [0x06008731] = new("RimWorld.Pawn_ApparelTracker", "ExposeData", "Void", "", false, 0),
        [0x06008881] = new("RimWorld.Pawn_PsychicEntropyTracker", "SetInitialPsyfocusLevel", "Void", "", false, 0),
        [0x06008889] = new("RimWorld.Pawn_PsychicEntropyTracker", "ExposeData", "Void", "", false, 0),
        [0x06009D8E] = new("RimWorld.CompForbiddable", "PostExposeData", "Void", "", false, 0),
        [0x06009D9F] = new("RimWorld.ForbidUtility", "IsForbidden", "Boolean", "Thing,Faction", true, 0),
        [0x0600A4BF] = new("RimWorld.CompStyleable", "PostExposeData", "Void", "", false, 0),
        [0x0600B1C1] = new("RimWorld.Apparel", "ExposeData", "Void", "", false, 0),
        [0x0600E52D] = new("RimWorld.Planet.WorldPawns", "ExposeData", "Void", "", false, 0),
    };
    private static readonly Dictionary<int, MethodBase> Methods = Bind();

    public static Guid Mvid => typeof(Game).Module.ModuleVersionId;
    internal static bool IsReviewed(Guid mvid) => mvid == Mvid;

    // 泛型 FullName 含程序集版本；统一诊断显示身份，原始 MVID/token 仍记录在证据中。
    public static string? CanonicalTypeName(Type? type, bool qualified = false)
    {
        string? name = qualified ? type?.AssemblyQualifiedName : type?.FullName;
        return name?.Replace(typeof(Game).Assembly.FullName!,
            "Assembly-CSharp, Version=1.7.9784.3272, Culture=neutral, PublicKeyToken=null", StringComparison.Ordinal);
    }

    public static int Token(int contract) => Token(Mvid, contract);
    internal static int Token(Guid mvid, int contract)
    {
        if (!IsReviewed(mvid)) { throw new InvalidOperationException("Observation contract belongs to a different loaded module."); }
        return ResolveMethod(contract).MetadataToken;
    }

    public static MethodBase ResolveMethod(int contract)
        => Methods.TryGetValue(contract, out MethodBase? method) ? method
            : throw new InvalidOperationException("Unknown observation contract: " + contract);

    private static Dictionary<int, MethodBase> Bind()
    {
        var result = new Dictionary<int, MethodBase>();
        foreach ((int contract, ReaderSymbol symbol) in Symbols)
        {
            Type type = typeof(Game).Assembly.GetType(symbol.Type, throwOnError: true)!;
            MethodInfo[] matches = [.. type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name == symbol.Name && method.IsStatic == symbol.Static
                    && method.GetGenericArguments().Length == symbol.Arity && Shape(method.ReturnType) == symbol.Return
                    && string.Join(",", method.GetParameters().Select(parameter => Shape(parameter.ParameterType))) == symbol.Parameters)];
            if (matches.Length != 1) { throw new InvalidOperationException("Observation interface changed: " + symbol.Type + "." + symbol.Name); }
            result.Add(contract, matches[0]);
        }
        return result;
    }

    private static string Shape(Type type) => type.IsByRef ? Shape(type.GetElementType()!) + "&"
        : type.IsArray ? Shape(type.GetElementType()!) + "[]"
        : type.IsGenericParameter ? "T" : type.IsGenericType ? type.Name.Split('`')[0] : type.Name;

    private sealed record ReaderSymbol(string Type, string Name, string Return, string Parameters, bool Static, int Arity);
}
