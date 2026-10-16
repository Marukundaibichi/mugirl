using Mugirl.Features.Mounting;
using Verse;

namespace Mugirl.Tests.Mounting;

/// <summary>骑乘炮塔的两个临时作用域：施放者替换（<see cref="MountedVerbScope"/>）与暖机覆盖（<see cref="MountedWarmupOverride"/>）。</summary>
public sealed class MountedCombatScopeTests
{
    [Fact]
    public void VerbScope_NestedScopesRestoreInStackOrder()
    {
        Verb verb = new Verb_Shoot();
        Thing rider = new();
        Thing carrier = new();
        verb.caster = rider;

        using (new MountedVerbScope(verb, carrier))
        {
            Assert.Same(carrier, verb.caster);
            // 1.6 的内层作用域会把施放者恢复成骑手，外层剩余部分（原版目标评分）随之失效。
            using (new MountedVerbScope(verb, carrier))
            {
                Assert.Same(carrier, verb.caster);
            }

            Assert.Same(carrier, verb.caster);
        }

        Assert.Same(rider, verb.caster);
    }

    [Fact]
    public void VerbScope_WithoutTemporaryCasterOrVerb_ChangesNothing()
    {
        Verb verb = new Verb_Shoot();
        Thing rider = new();
        verb.caster = rider;

        using (new MountedVerbScope(verb, null))
        {
            Assert.Same(rider, verb.caster);
        }

        Assert.Same(rider, verb.caster);
        using (new MountedVerbScope(null, rider))
        {
        }
    }

    [Fact]
    public void WarmupOverride_IsVisibleOnlyInsideScopeAndRestoresOuterSlot()
    {
        MountedWarmupOverride.Clear();
        Verb outer = new Verb_Shoot();
        Verb inner = new Verb_Shoot();

        Assert.False(MountedWarmupOverride.TryGet(outer, out _));
        using (new MountedWarmupOverride(outer, 0f))
        {
            Assert.True(MountedWarmupOverride.TryGet(outer, out float outerValue));
            Bits.Equal(0f, outerValue);
            Assert.False(MountedWarmupOverride.TryGet(inner, out _));

            using (new MountedWarmupOverride(inner, 2.5f))
            {
                Assert.True(MountedWarmupOverride.TryGet(inner, out float innerValue));
                Bits.Equal(2.5f, innerValue);
                Assert.False(MountedWarmupOverride.TryGet(outer, out _));
            }

            Assert.True(MountedWarmupOverride.TryGet(outer, out _));
        }

        Assert.False(MountedWarmupOverride.TryGet(outer, out _));
        Assert.False(MountedWarmupOverride.TryGet(null, out _));
    }

    [Fact]
    public void WarmupOverride_ForgetAndClearRemoveTheActiveSlot()
    {
        MountedWarmupOverride.Clear();
        Verb verb = new Verb_Shoot();
        Verb other = new Verb_Shoot();
        using (new MountedWarmupOverride(verb, 0f))
        {
            MountedWarmupOverride.Forget(other);
            Assert.True(MountedWarmupOverride.TryGet(verb, out _));
            MountedWarmupOverride.Forget(verb);
            Assert.False(MountedWarmupOverride.TryGet(verb, out _));
        }

        using (new MountedWarmupOverride(verb, 0f))
        {
            MountedWarmupOverride.Clear();
            Assert.False(MountedWarmupOverride.TryGet(verb, out _));
        }

        Assert.False(MountedWarmupOverride.TryGet(verb, out _));
    }
}
