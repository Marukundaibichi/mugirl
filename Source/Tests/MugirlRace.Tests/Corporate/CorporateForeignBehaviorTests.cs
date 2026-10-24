using System.Collections.Generic;
using System.Reflection;
using Mugirl.Features.Corporate;
using RimWorld;
using Verse;

namespace Mugirl.Tests.Corporate;

/// <summary>
/// 巨企供货的产出判断：测试程序集中的组件类相当于第三方组件。使用最小 Def 和反射，不执行组件回调。
/// </summary>
public sealed class CorporateForeignBehaviorTests
{
    [Fact]
    public void PresentationOnlyOverridesAreAllowed()
        => Assert.Null(CorporateForeignBehavior.OpaqueCompReason(typeof(PresentationOnlyComp)));

    [Fact]
    public void PrivateHelpersNamedLikeCallbacksAreAllowed()
        => Assert.Null(CorporateForeignBehavior.OpaqueCompReason(typeof(HelperNamesComp)));

    [Fact]
    public void TickOverrideAloneIsNotProductionEvidence()
        => Assert.Null(CorporateForeignBehavior.OpaqueCompReason(typeof(TickComp)));

    [Fact]
    public void MethodNameAloneIsNotProductionEvidence()
        => Assert.Null(CorporateForeignBehavior.OpaqueCompReason(typeof(DoEffectComp)));

    [Fact]
    public void InheritedBookkeepingIsAllowed()
        => Assert.Null(CorporateForeignBehavior.OpaqueCompReason(typeof(DerivedFromTickComp)));

    [Fact]
    public void InheritedProductionIsReportedOnTheDeclaringBase()
        => Assert.Equal(
            typeof(DerivedFromLeavingsComp).FullName + " -> " + typeof(LeavingsComp).FullName + "." + nameof(ThingComp.GetAdditionalLeavings),
            CorporateForeignBehavior.OpaqueCompReason(typeof(DerivedFromLeavingsComp)));

    [Fact]
    public void HiddenMethodCannotMaskInheritedProduction()
        => Assert.Equal(
            typeof(HiddenLeavingsComp).FullName + " -> " + typeof(LeavingsComp).FullName + "." + nameof(ThingComp.GetAdditionalLeavings),
            CorporateForeignBehavior.OpaqueCompReason(typeof(HiddenLeavingsComp)));

    [Theory]
    [InlineData(typeof(SaveOnlyComp))]
    [InlineData(typeof(BookkeepingComp))]
    [InlineData(typeof(UseEffectComp))]
    public void GenericCallbacksAreAllowedWithoutExecutingThem(Type type)
        => Assert.Null(CorporateForeignBehavior.OpaqueCompReason(type));

    [Fact]
    public void UninspectableMethodsAreNotProductionEvidence()
        => Assert.Null(CorporateForeignBehavior.OpaqueCompReason(new UninspectableType()));

    [Fact]
    public void ProductionNamedHelperIsNotAnOverride()
        => Assert.Null(CorporateForeignBehavior.OpaqueCompReason(typeof(ProductionNameHelpersComp)));

    [Theory]
    [InlineData(typeof(CompSpawner))]
    [InlineData(typeof(CompSpawnerItems))]
    [InlineData(typeof(CompHatcher))]
    [InlineData(typeof(CompUseEffect_GiveQuest))]
    [InlineData(typeof(ForeignSpawner))]
    [InlineData(typeof(ForeignSpawnerItems))]
    [InlineData(typeof(ForeignHatcher))]
    [InlineData(typeof(ForeignQuest))]
    public void KnownProducersAndSubclassesStayRestricted(Type type)
        => Assert.Equal(type.FullName, CorporateForeignBehavior.OpaqueCompReason(type));

    [Theory]
    [InlineData(typeof(LeavingsComp), nameof(ThingComp.GetAdditionalLeavings))]
    [InlineData(typeof(HarvestComp), nameof(ThingComp.GetAdditionalHarvestYield))]
    public void ExplicitProductionCallbacksStayRestrictedWithoutExecutingThem(Type type, string callback)
        => Assert.Equal(type.FullName + " -> " + type.FullName + "." + callback,
            CorporateForeignBehavior.OpaqueCompReason(type));

    [Fact]
    public void ProductionRestrictionSurvivesComponentOrder()
    {
        var save = new CompProperties { compClass = typeof(SaveOnlyComp) };
        var producer = new CompProperties { compClass = typeof(LeavingsComp) };
        var def = new ThingDef { thingClass = typeof(ThingWithComps), comps = [save, producer] };
        string? first = CorporateForeignBehavior.OpaqueBehaviorReason(def);
        def.comps.Reverse();
        Assert.NotNull(first);
        Assert.Equal(first, CorporateForeignBehavior.OpaqueBehaviorReason(def));
    }

    [Fact]
    public void ForeignApparelWithSaveComponentIsAllowed()
        => Assert.Null(CorporateForeignBehavior.OpaqueBehaviorReason(new ThingDef
        {
            thingClass = typeof(ForeignApparel),
            comps = [new CompProperties { compClass = typeof(SaveOnlyComp) }],
        }));

    [Theory]
    [InlineData(typeof(ButcherThing), nameof(Thing.ButcherProducts))]
    [InlineData(typeof(SmeltThing), nameof(Thing.SmeltProducts))]
    public void ScriptedThingProductsStayRestricted(Type type, string callback)
        => Assert.Equal(type.FullName + " -> " + type.FullName + "." + callback,
            CorporateForeignBehavior.OpaqueBehaviorReason(new ThingDef { thingClass = type }));

    [Fact]
    public void InvalidComponentStillHasAReason()
        => Assert.NotNull(CorporateForeignBehavior.OpaqueBehaviorReason(new ThingDef
        {
            thingClass = typeof(ThingWithComps), comps = [new CompProperties { compClass = null }],
        }));

    [Fact]
    public void ResultIsCachedPerType()
    {
        string? first = CorporateForeignBehavior.OpaqueCompReason(typeof(LeavingsComp));
        string? second = CorporateForeignBehavior.OpaqueCompReason(typeof(LeavingsComp));
        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    public class PresentationOnlyComp : ThingComp
    {
        public override void PostDraw()
        {
        }

        public override string CompInspectStringExtra() => string.Empty;
    }

    public sealed class HelperNamesComp : ThingComp
    {
        internal int Calls { get; private set; }

        internal void PostSomething() => Calls++;

        internal void PreSomething() => Calls--;
    }

    public class TickComp : ThingComp
    {
        public override void CompTick()
        {
        }
    }

    public sealed class DerivedFromTickComp : TickComp
    {
    }

    public sealed class DoEffectComp : ThingComp
    {
        internal int Uses { get; private set; }

        public void DoEffect() => Uses++;
    }

    public sealed class SaveOnlyComp : ThingComp
    {
        // 截图中的失败触发点：存档回调被误认为未知产出。断言分析不会调用它。
        public override void PostExposeData() => throw new InvalidOperationException("Do not execute a save callback to price an item.");
    }

    public sealed class BookkeepingComp : TickComp
    {
        public override void Initialize(CompProperties props) => throw new InvalidOperationException();
        public override void PostPostMake() => throw new InvalidOperationException();
        public override void PostSpawnSetup(bool respawningAfterLoad) => throw new InvalidOperationException();
        public override void PostExposeData() => throw new InvalidOperationException();
        public override void PostSplitOff(Thing piece) => throw new InvalidOperationException();
        public override bool AllowStackWith(Thing other) => throw new InvalidOperationException();
        public override float GetStatFactor(StatDef stat) => throw new InvalidOperationException();
        public override void Notify_Equipped(Pawn pawn) => throw new InvalidOperationException();
    }

    public sealed class UseEffectComp : CompUseEffect
    {
        public override void DoEffect(Pawn user) => throw new InvalidOperationException();
    }

    public class LeavingsComp : ThingComp
    {
        public override IEnumerable<ThingDefCountClass> GetAdditionalLeavings(Map map, DestroyMode mode)
            => throw new InvalidOperationException("Do not generate products during analysis.");
    }

    public sealed class DerivedFromLeavingsComp : LeavingsComp;

    public sealed class HiddenLeavingsComp : LeavingsComp
    {
        public new IEnumerable<ThingDefCountClass> GetAdditionalLeavings(Map map, DestroyMode mode) => [];
    }

    public sealed class HarvestComp : ThingComp
    {
        public override IEnumerable<ThingDefCountClass> GetAdditionalHarvestYield() => throw new InvalidOperationException();
    }

    public sealed class ProductionNameHelpersComp : ThingComp
    {
        public void GetAdditionalLeavings(int amount) => throw new InvalidOperationException();
    }

    private sealed class UninspectableType() : TypeDelegator(typeof(PresentationOnlyComp))
    {
        public override MethodInfo[] GetMethods(BindingFlags bindingAttr) => throw new TypeLoadException("unavailable optional signature");
    }

    public sealed class ForeignSpawner : CompSpawner;
    public sealed class ForeignSpawnerItems : CompSpawnerItems;
    public sealed class ForeignHatcher : CompHatcher;
    public sealed class ForeignQuest : CompUseEffect_GiveQuest;
    public sealed class ForeignApparel : Apparel;

    public sealed class ButcherThing : ThingWithComps
    {
        public override IEnumerable<Thing> ButcherProducts(Pawn butcher, float efficiency) => throw new InvalidOperationException();
    }

    public sealed class SmeltThing : ThingWithComps
    {
        public override IEnumerable<Thing> SmeltProducts(float efficiency) => throw new InvalidOperationException();
    }
}
