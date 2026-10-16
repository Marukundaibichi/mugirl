using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl.ParityControl;

// “改名后”的构建（见 ../A/ParityControl.cs）。

public static class Rebind
{
    public static Pawn Oldest(List<Pawn> pawns) => Enumerable.MaxBy(pawns, pawn => pawn.ageTracker.AgeBiologicalTicks);
}

public static class Numbers
{
    public static int Limit() => 6;
}

public class SavedThing : IExposable
{
    private int amount;

    public void ExposeData() => Scribe_Values.Look(ref amount, nameof(amount));
}

public class UnmappedNewName
{
    public int Value;
}

public class MappedNew
{
    public int Value;
}

public static class Startup
{
    public static int Value;
}

[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
public static class Harmony_A_First
{
    public static void Prefix()
    {
    }
}

[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
public static class Harmony_0_Second
{
    public static void Prefix()
    {
    }
}

public static class Keys
{
    public static string Key() => "Mugirl.Control.KeyB";
}

public class ProviderTwo : FloatMenuOptionProvider
{
    protected override bool Drafted => true;

    protected override bool Undrafted => true;

    protected override bool Multiselect => false;
}

public class ProviderOne : FloatMenuOptionProvider
{
    protected override bool Drafted => true;

    protected override bool Undrafted => true;

    protected override bool Multiselect => false;
}

/// <summary>A 的 OldDefOf 的成员移到这里（见 ../A/ParityControl.cs）。</summary>
public static class NewDefOf
{
    public static ThingDef Moved;

    public static PawnKindDef Retyped;
}

/// <summary>A 的 OldUtility.Helper 移到这里。</summary>
public static class NewUtility
{
    public static int Helper(int value) => value + 1;
}

/// <summary>调用被移动的方法与字段。</summary>
public static class MoveCaller
{
    public static int Call() => NewUtility.Helper(1);

    public static ThingDef Read() => NewDefOf.Moved;
}
