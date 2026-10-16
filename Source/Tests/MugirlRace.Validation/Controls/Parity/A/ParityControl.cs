using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl.ParityControl;

// “改名前”的构建。B 中每个类型对应一条应当报告的差异（../expected.txt），MappedOld 是映射解释的改名（不报告）。

/// <summary>B 把 GenCollection.MaxBy 换成同名的 Enumerable.MaxBy（静默重绑定）。</summary>
public static class Rebind
{
    public static Pawn Oldest(List<Pawn> pawns) => GenCollection.MaxBy(pawns, pawn => pawn.ageTracker.AgeBiologicalTicks);
}

/// <summary>B 改了常量。</summary>
public static class Numbers
{
    public static int Limit() => 5;
}

/// <summary>B 把字段改名（映射中有），标签经 nameof 一起改变：存档结构变化。</summary>
public class SavedThing : IExposable
{
    private int count;

    public void ExposeData() => Scribe_Values.Look(ref count, nameof(count));
}

/// <summary>B 把它改名，但映射中没有。</summary>
public class OldName
{
    public int Value;
}

/// <summary>B 改名（映射中有）：不应报告。</summary>
public class MappedOld
{
    public int Value;
}

/// <summary>B 去掉了 [StaticConstructorOnStartup]。</summary>
[StaticConstructorOnStartup]
public static class Startup
{
    public static int Value;
}

/// <summary>同一目标上没有优先级的两个前缀：顺序由类名决定；B 的改名（映射中有）把它们的先后颠倒。</summary>
[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
public static class Harmony_A_First
{
    public static void Prefix()
    {
    }
}

/// <summary>见 <see cref="Harmony_A_First"/>。</summary>
[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
public static class Harmony_B_Second
{
    public static void Prefix()
    {
    }
}

/// <summary>B 改了翻译键字面量。</summary>
public static class Keys
{
    public static string Key() => "Mugirl.Control.KeyA";
}

/// <summary>FloatMenu 提供者按元数据顺序注册；B 交换了两者的声明顺序。</summary>
public class ProviderOne : FloatMenuOptionProvider
{
    protected override bool Drafted => true;

    protected override bool Undrafted => true;

    protected override bool Multiselect => false;
}

/// <summary>见 <see cref="ProviderOne"/>。</summary>
public class ProviderTwo : FloatMenuOptionProvider
{
    protected override bool Drafted => true;

    protected override bool Undrafted => true;

    protected override bool Multiselect => false;
}

/// <summary>B 解散它（映射：dissolve）：Moved 移到 NewDefOf（映射：move，不报告），Retyped 移过去时字段类型变了（报告），Deleted 删除（映射：delete）。</summary>
public static class OldDefOf
{
    public static ThingDef Moved;

    public static ThingDef Retyped;

    public static ThingDef Deleted;
}

/// <summary>B 把 Helper 移到 NewUtility 并且调用方跟着改（映射：move，不报告）。</summary>
public static class OldUtility
{
    public static int Helper(int value) => value + 1;
}

/// <summary>调用被移动的方法与字段：映射后与 B 相同。</summary>
public static class MoveCaller
{
    public static int Call() => OldUtility.Helper(1);

    public static ThingDef Read() => OldDefOf.Moved;
}
