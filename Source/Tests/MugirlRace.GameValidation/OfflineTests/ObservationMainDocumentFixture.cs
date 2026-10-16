using System.Reflection;
using System.Xml;
using Mugirl.Features.Milk;

namespace Mugirl.GameValidation.OfflineTests;

// 只设置真实生产会话的测试环境；不调用DOM升级，不替换Scribe或任何被观察方法。
internal sealed class ObservationMainDocumentFixture : IDisposable
{
    private const BindingFlags Members = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Production = typeof(CompMilkProduction).Assembly;
    private static readonly FieldInfo Active = Production.GetType("Mugirl.SaveUpgrade.SaveUpgradeRuntime")!.GetField("activeSession", Members)!;
    private readonly object? before = Active.GetValue(null);
    public static void Bind(XmlDocument document, string? path = null)
    {
        object session = Production.GetType("Mugirl.SaveUpgrade.SaveUpgradeSession")!.GetMethod("Start", Members)!.Invoke(null, [901, document, path])!;
        Active.SetValue(null, session);
    }
    public void Dispose() => Active.SetValue(null, before);
}
