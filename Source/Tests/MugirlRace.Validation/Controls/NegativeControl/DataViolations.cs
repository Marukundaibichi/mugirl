namespace Mugirl;

/// <summary>C.defof：不存在的 Def、没有 [MayRequire] 的 Royalty 专属 Def，且静态构造函数不调用 EnsureInitializedInCtor。</summary>
[DefOf]
public static class ControlDefOf
{
    public static ThingDef NoSuchThing_Control;

    public static ThingDef Throne;
}

/// <summary>代码中的 C.def-lookups、C.translation-keys、C.textures、C.shaders 与 B.architecture 违规。</summary>
public static class ControlLookups
{
    // 同时违反 B.architecture defdatabase-getnamed。
    public static ThingDef MissingDef() => DefDatabase<ThingDef>.GetNamed("NoSuchThingDef_Control");

    // 外部乳房 HediffDef 字面量出现在 ExternalBreastHediffDefs 之外（B.architecture），且该 Def 不存在（C.def-lookups）。
    public static HediffDef ExternalBreastOutsideCache() => DefDatabase<HediffDef>.GetNamedSilentFail("HugeBreasts");

    public static string MissingKey() => "Mugirl.Control.NoSuchKey".Translate();

    public static string DynamicKey(string suffix) => ("Mugirl.Control." + suffix).Translate();

    public static UnityEngine.Texture2D MissingTexture() => ContentFinder<UnityEngine.Texture2D>.Get("Control/NoSuchTexture");

    public static UnityEngine.Shader MissingShader() => ShaderDatabase.LoadShader("Map/NoSuchShaderControl");

    public static bool IsMugirlBody(Pawn pawn) => pawn.RaceProps.body == Core.MugirlDefOf.MugirlBody || pawn.RaceProps.body.defName == "MugirlBody";
}

/// <summary>
/// B.reflection-names：不存在的成员（直接与经辅助方法参数）、字段类型不符的 FieldRefAccess、只找公开成员的查找、类型在运行时才算出；
/// 另有兼容层之外的 TypeByName（B.architecture third-party-type-lookup）。
/// </summary>
public static class ControlReflection
{
    public static System.Reflection.FieldInfo MissingField() => HarmonyLib.AccessTools.Field(typeof(Pawn), "noSuchField_Control");

    public static System.Reflection.FieldInfo MissingFieldThroughHelper() => FieldOfPawn("noSuchHelperField_Control");

    public static HarmonyLib.AccessTools.FieldRef<Pawn, int> WrongFieldType() => HarmonyLib.AccessTools.FieldRefAccess<Pawn, int>("story");

    public static System.Reflection.FieldInfo PrivateFieldPublicLookup() => typeof(Pawn_HealthTracker).GetField("pawn");

    public static System.Reflection.MethodInfo ComputedType(object instance) => HarmonyLib.AccessTools.Method(instance.GetType(), "Tick");

    public static Type TypeLookupOutsideCompatibility() => HarmonyLib.AccessTools.TypeByName("Verse.Pawn");

    private static System.Reflection.FieldInfo FieldOfPawn(string name) => HarmonyLib.AccessTools.Field(typeof(Pawn), name);
}
