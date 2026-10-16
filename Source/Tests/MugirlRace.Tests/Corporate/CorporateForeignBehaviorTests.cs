using Mugirl.Features.Corporate;
using Verse;

namespace Mugirl.Tests.Corporate;

/// <summary>
/// 巨企供货的“不透明行为”判断：测试程序集中的组件类相当于第三方组件。只用反射检查类型，不创建 Def 或游戏对象。
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
    public void TickOverrideIsOpaque()
        => Assert.Equal(
            typeof(TickComp).FullName + " -> " + typeof(TickComp).FullName + "." + nameof(ThingComp.CompTick),
            CorporateForeignBehavior.OpaqueCompReason(typeof(TickComp)));

    [Fact]
    public void DoEffectMethodIsOpaqueEvenWithoutOverride()
        => Assert.Equal(
            typeof(DoEffectComp).FullName + " -> " + typeof(DoEffectComp).FullName + ".DoEffect",
            CorporateForeignBehavior.OpaqueCompReason(typeof(DoEffectComp)));

    [Fact]
    public void InheritedForeignBehaviorIsReportedOnTheDeclaringBase()
        => Assert.Equal(
            typeof(DerivedFromTickComp).FullName + " -> " + typeof(TickComp).FullName + "." + nameof(ThingComp.CompTick),
            CorporateForeignBehavior.OpaqueCompReason(typeof(DerivedFromTickComp)));

    [Fact]
    public void ResultIsCachedPerType()
    {
        string? first = CorporateForeignBehavior.OpaqueCompReason(typeof(TickComp));
        string? second = CorporateForeignBehavior.OpaqueCompReason(typeof(TickComp));
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
}
