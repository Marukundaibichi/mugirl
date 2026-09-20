param([string]$CscPath)
$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CscPath) {
    $CscPath = Join-Path (Split-Path (Get-Command MSBuild.exe).Source -Parent) 'Roslyn\csc.exe'
}

# 用可控随机数和游戏 tick 执行生产代码，验证生成来源、Lovin 临时覆盖和到期恢复。
$harness = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using AlienRace;
using HarmonyLib;
using Mugirl.Features.Appearance;
using RimWorld;
using Verse;

namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string methodName) { } }
    public sealed class HarmonyPostfix : Attribute { }
}

namespace RimWorld
{
    public class BackstoryDef { }
    public class JobDriver_Lovin { }
    public static class PortraitsCache
    {
        public static int DirtyCount;
        public static void SetDirty(Verse.Pawn pawn) { DirtyCount++; }
    }
}

namespace Verse
{
    public class ThingDef { }
    public class Graphic { }
    public class ShaderTypeDef { }
    public class ThingWithComps { }
    public class Pawn_StoryTracker { public RimWorld.BackstoryDef Childhood; }
    public class PawnRenderer
    {
        public int DirtyCount;
        public void SetAllGraphicsDirty() { DirtyCount++; }
    }
    public class Pawn_DrawTracker { public PawnRenderer renderer = new PawnRenderer(); }
    public class Pawn : ThingWithComps
    {
        public Pawn_StoryTracker story = new Pawn_StoryTracker();
        public Pawn_DrawTracker Drawer = new Pawn_DrawTracker();
        public object Comp;
        public T TryGetComp<T>() where T : class { return Comp as T; }
    }
    public class CompProperties
    {
        public Type compClass;
    }
    public class ThingComp
    {
        public ThingWithComps parent;
        public CompProperties props;
        public virtual void CompTick() { }
        public virtual void CompTickRare() { }
        public virtual void PostSpawnSetup(bool respawningAfterLoad) { }
        public virtual void PostExposeData() { }
    }
    public struct IntRange
    {
        public int min;
        public int max;
        public IntRange(int min, int max) { this.min = min; this.max = max; }
    }
    public enum LoadSaveMode { Inactive, PostLoadInit }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class Scribe_Values
    {
        public static void Look(ref int value, string label, int defaultValue) { }
    }
    public static class Rand
    {
        public static readonly Queue<float> Rolls = new Queue<float>();
        public static readonly Queue<int> Integers = new Queue<int>();
        public static bool Chance(float chance) { return Rolls.Dequeue() < chance; }
        public static float Value { get { return Rolls.Dequeue(); } }
        public static int RangeInclusive(int min, int max) { return Integers.Count > 0 ? Integers.Dequeue() : min; }
    }
    public static class DefDatabase<T> where T : class
    {
        public static T GetNamedSilentFail(string name) { return null; }
    }
}

namespace AlienRace
{
    public class AlienPartGenerator
    {
        public class AlienComp { }
        public class BodyAddon
        {
            private string name;
            public bool linkVariantIndexWithPrevious;
            public int VariantCountMax { get; set; } = 6;
            public string ColorChannel { get; set; }
            public Verse.ShaderTypeDef ShaderType { get; set; }
            public Verse.ShaderTypeDef ShaderTypeStatue { get; set; }
            public virtual string GetPath(Verse.Pawn pawn, ref int sharedIndex, int? savedIndex = 0, string pathAppendix = null)
            {
                int value = savedIndex ?? 0;
                sharedIndex = value;
                return value.ToString();
            }
            public virtual Verse.Graphic GetGraphic(Verse.Pawn pawn, AlienComp alienComp, ref int sharedIndex, int? savedIndex = null, bool precheckCompare = false, Verse.Graphic preGraphic = null)
            {
                return preGraphic;
            }
        }
    }
}

namespace Mugirl
{
    public static class Mugirl_DefOf
    {
        public static RimWorld.BackstoryDef Mugirl_ExperimentalChild = new RimWorld.BackstoryDef();
    }
    internal static class MugirlTickUtility
    {
        internal static int CurrentTick;
        internal static bool Available = true;
        internal static bool TryGetCurrentGameTick(out int tick)
        {
            tick = CurrentTick;
            return Available;
        }
    }
}
'@

$tests = @'
public static class BodyAccessoryLifecycleChecks
{
    private static int passed;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static Mugirl.Features.Appearance.MugirlBodyAccessoryAddon NewAddon()
    {
        return new Mugirl.Features.Appearance.MugirlBodyAccessoryAddon
        {
            VariantCountMax = 6,
            experimentalMarkChance = 0.35f,
            chemicalScarShare = 0.5f
        };
    }

    private static Verse.Pawn NewPawn(bool experimental)
    {
        var pawn = new Verse.Pawn();
        pawn.story.Childhood = experimental ? Mugirl.Mugirl_DefOf.Mugirl_ExperimentalChild : new RimWorld.BackstoryDef();
        return pawn;
    }

    private static string InitialPath(Mugirl.Features.Appearance.MugirlBodyAccessoryAddon addon, Verse.Pawn pawn)
    {
        int shared = -1;
        return addon.GetPath(pawn, ref shared, null, null);
    }

    private static Mugirl.Features.Appearance.CompMugirlBodyAccessory AttachComp(Verse.Pawn pawn, float biteChance, float handprintChance, int duration)
    {
        var comp = new Mugirl.Features.Appearance.CompMugirlBodyAccessory();
        comp.parent = pawn;
        comp.props = new Mugirl.Features.Appearance.CompProperties_MugirlBodyAccessory
        {
            lovinBiteChance = biteChance,
            lovinHandprintChance = handprintChance,
            lovinMarkDurationTicks = new Verse.IntRange(duration, duration)
        };
        pawn.Comp = comp;
        return comp;
    }

    public static void Main()
    {
        var addon = NewAddon();
        Check(InitialPath(addon, NewPawn(false)) == "0", "普通背景不会随机获得特殊附件");

        Verse.Rand.Rolls.Enqueue(0.9f);
        Check(InitialPath(addon, NewPawn(true)) == "0", "实验对象未命中总概率时保持无附件");

        Verse.Rand.Rolls.Enqueue(0.1f);
        Verse.Rand.Rolls.Enqueue(0.1f);
        Check(InitialPath(addon, NewPawn(true)) == "5", "实验对象命中化学疤痕分支");

        Verse.Rand.Rolls.Enqueue(0.1f);
        Verse.Rand.Rolls.Enqueue(0.9f);
        Check(InitialPath(addon, NewPawn(true)) == "3", "实验对象命中条形码分支");

        var manualPawn = NewPawn(true);
        int shared = -1;
        Check(addon.GetPath(manualPawn, ref shared, 4, null) == "4", "已有梳妆台选择不重新随机");

        Mugirl.MugirlTickUtility.CurrentTick = 1000;
        var bitePawn = NewPawn(false);
        var biteComp = AttachComp(bitePawn, 0.25f, 0.25f, 120);
        Verse.Rand.Rolls.Enqueue(0.1f);
        Verse.Rand.Integers.Enqueue(120);
        Check(biteComp.TryApplyLovinMark(), "Lovin 牙印概率命中");
        shared = -1;
        Check(addon.GetPath(bitePawn, ref shared, 3, null) == "1", "牙印临时覆盖永久条形码");
        Check(bitePawn.Drawer.renderer.DirtyCount == 1 && RimWorld.PortraitsCache.DirtyCount == 1, "添加印记刷新人物和头像");

        Mugirl.MugirlTickUtility.CurrentTick = 1119;
        biteComp.CompTick();
        shared = -1;
        Check(addon.GetPath(bitePawn, ref shared, 3, null) == "1", "持续时间结束前保留牙印");

        Mugirl.MugirlTickUtility.CurrentTick = 1120;
        biteComp.CompTick();
        shared = -1;
        Check(addon.GetPath(bitePawn, ref shared, 3, null) == "3", "牙印到期后恢复永久条形码");
        Check(bitePawn.Drawer.renderer.DirtyCount == 2 && RimWorld.PortraitsCache.DirtyCount == 2, "印记到期刷新人物和头像");

        Mugirl.MugirlTickUtility.CurrentTick = 2000;
        var handPawn = NewPawn(false);
        var handComp = AttachComp(handPawn, 0.25f, 0.25f, 60);
        Verse.Rand.Rolls.Enqueue(0.3f);
        Verse.Rand.Integers.Enqueue(60);
        Check(handComp.TryApplyLovinMark(), "Lovin 巴掌印概率命中");
        shared = -1;
        Check(addon.GetPath(handPawn, ref shared, 5, null) == "2", "巴掌印临时覆盖永久化学疤痕");

        var nonePawn = NewPawn(false);
        var noneComp = AttachComp(nonePawn, 0.25f, 0.25f, 60);
        Verse.Rand.Rolls.Enqueue(0.75f);
        Check(!noneComp.TryApplyLovinMark(), "Lovin 未命中时不改变外观");
        shared = -1;
        Check(addon.GetPath(nonePawn, ref shared, 4, null) == "4", "未命中时保留梳妆台选择");

        Console.WriteLine("Body accessory lifecycle checks: " + passed + " PASS");
    }
}
'@

$sources = @(
    (Get-Content -Raw (Join-Path $modRoot '1.6\Source\Features\Appearance\MugirlBodyAccessoryAddon.cs')),
    (Get-Content -Raw (Join-Path $modRoot '1.6\Source\Features\Appearance\CompMugirlBodyAccessory.cs')),
    (Get-Content -Raw (Join-Path $modRoot '1.6\Source\Features\Appearance\Harmony_LovinBodyAccessory.cs'))
) | ForEach-Object { $_ -replace '(?m)^using [^\r\n]+;\r?\n', '' }
$source = $harness + [Environment]::NewLine + ($sources -join [Environment]::NewLine) + [Environment]::NewLine + $tests
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$tempRoot = Join-Path $tempBase ('MugirlBodyAccessoryChecks-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$sourcePath = Join-Path $tempRoot 'BodyAccessoryLifecycleChecks.cs'
$assemblyPath = Join-Path $tempRoot 'BodyAccessoryLifecycleChecks.exe'
[IO.File]::WriteAllText($sourcePath, $source, [Text.UTF8Encoding]::new($false))
try {
    & $CscPath /nologo /langversion:7.2 /target:exe /out:$assemblyPath $sourcePath
    if ($LASTEXITCODE -ne 0) { throw "Body accessory lifecycle test compilation failed: $LASTEXITCODE" }
    & $assemblyPath
    if ($LASTEXITCODE -ne 0) { throw "Body accessory lifecycle tests failed: $LASTEXITCODE" }
}
finally {
    $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
    $expectedPrefix = $tempBase + [IO.Path]::DirectorySeparatorChar + 'MugirlBodyAccessoryChecks-'
    $canDeleteTemp = $resolvedTempRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTempRoot)
    if ($canDeleteTemp) {
        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
    }
}
