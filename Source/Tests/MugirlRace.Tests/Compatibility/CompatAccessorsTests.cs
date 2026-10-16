using System.Reflection;
using Mugirl.Compatibility;

namespace Mugirl.Tests.Compatibility;

/// <summary>
/// 兼容层的强类型访问器：实例以基类传入（运行时才知道第三方的声明类型），签名不符时返回 null 而不是抛异常。
/// </summary>
public sealed class CompatAccessorsTests
{
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    [Fact]
    public void PropertyGetterReadsThroughTheBaseType()
    {
        Func<AccessorBase, bool>? getter = CompatAccessors.InstanceGetter<AccessorBase, bool>(
            typeof(AccessorTarget).GetProperty("Enabled", AnyInstance));

        Assert.NotNull(getter);
        Assert.True(getter(new AccessorTarget(enabled: true)));
        Assert.False(getter(new AccessorTarget(enabled: false)));
        Assert.Throws<InvalidCastException>(() => getter(new AccessorBase()));
    }

    [Fact]
    public void FieldGetterReadsThroughTheBaseType()
    {
        Func<AccessorBase, bool>? getter = CompatAccessors.InstanceGetter<AccessorBase, bool>(
            typeof(AccessorTarget).GetField("enabled", AnyInstance));

        var target = new AccessorTarget(enabled: true);
        Assert.NotNull(getter);
        Assert.True(getter(target));
        target.Toggle();
        Assert.False(getter(target), "the getter reads the live field, not a snapshot");
    }

    [Fact]
    public void MismatchedMembersGiveNull()
    {
        Assert.Null(CompatAccessors.InstanceGetter<AccessorBase, bool>(typeof(AccessorTarget).GetProperty("Count", AnyInstance)));
        Assert.Null(CompatAccessors.InstanceGetter<AccessorBase, bool>(typeof(AccessorTarget).GetField("count", AnyInstance)));
        Assert.Null(CompatAccessors.InstanceGetter<AccessorBase, bool>((PropertyInfo?)null));
        Assert.Null(CompatAccessors.InstanceGetter<string, bool>(typeof(AccessorTarget).GetProperty("Enabled", AnyInstance)));
        Assert.Null(CompatAccessors.InstanceAction<AccessorBase>(typeof(AccessorTarget).GetMethod("Add", AnyInstance)));
        Assert.Null(CompatAccessors.InstanceAction<AccessorBase>(typeof(AccessorTarget).GetMethod("IsOdd", AnyStatic)));
        Assert.Null(CompatAccessors.StaticMethod<Func<int, int>>(typeof(AccessorTarget).GetMethod("IsOdd", AnyStatic)));
        Assert.Null(CompatAccessors.StaticMethod<Func<int, bool>>(typeof(AccessorTarget).GetMethod("Ping", AnyInstance)));
        Assert.Null(CompatAccessors.StaticField<int>(typeof(AccessorTarget).GetField("Flag", AnyStatic)));
        Assert.Null(CompatAccessors.StaticField<bool>(typeof(AccessorTarget).GetField("enabled", AnyInstance)));
    }

    [Fact]
    public void InstanceActionCallsTheDeclaredMethod()
    {
        Action<AccessorBase>? ping = CompatAccessors.InstanceAction<AccessorBase>(typeof(AccessorTarget).GetMethod("Ping", AnyInstance));
        var target = new AccessorTarget(enabled: true);

        Assert.NotNull(ping);
        ping(target);
        ping(target);
        Assert.Equal(2, target.Count);
    }

    [Fact]
    public void StaticMembersBind()
    {
        Func<int, bool>? isOdd = CompatAccessors.StaticMethod<Func<int, bool>>(typeof(AccessorTarget).GetMethod("IsOdd", AnyStatic));
        Assert.NotNull(isOdd);
        Assert.True(isOdd(3));
        Assert.False(isOdd(4));

        HarmonyLib.AccessTools.FieldRef<bool>? flag = CompatAccessors.StaticField<bool>(typeof(AccessorTarget).GetField("Flag", AnyStatic));
        Assert.NotNull(flag);
        Assert.True(flag());
    }

    public class AccessorBase
    {
    }

    public sealed class AccessorTarget : AccessorBase
    {
        internal static bool Flag = true;

        private bool enabled;
        private int count;

        internal AccessorTarget(bool enabled)
        {
            this.enabled = enabled;
        }

        internal int Count => count;

        private bool Enabled => enabled;

        internal static bool IsOdd(int value) => value % 2 != 0;

        internal void Ping() => count++;

        internal void Toggle() => enabled = !enabled;

        internal void Add(int amount) => count += amount;
    }
}
