namespace Mugirl.Tests.SaveUpgrade;

/// <summary>升级表格测试中充当“新类型”的空类型；存档写法为 <c>Mugirl.Tests.SaveUpgrade.Fakes+名称</c>。</summary>
internal static class Fakes
{
    internal sealed class NewComponent
    {
    }

    internal sealed class CompMilk
    {
    }

    internal sealed class HediffCompLactation
    {
    }

    internal sealed class NewQuestPart
    {
    }

    internal sealed class NewLordJob
    {
    }

    internal sealed class NewJobDriver
    {
    }

    internal static string Name<T>() => typeof(T).FullName!;
}
