using System.Globalization;
using System.Linq;
using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>合成存档与测试表格的辅助方法。所有 XML 都是手写的匿名片段，不来自玩家存档。</summary>
internal static class SaveXml
{
    internal const string ModuleName = "Test";

    /// <summary>
    /// 合成一个存档：<c>meta</c>（Mod 列表含本 Mod）+ <c>game</c>。<paramref name="stamp"/> 不为 null 时在 <c>game/components</c>
    /// 开头加入写版本戳的中心组件。<paramref name="gameBody"/> 可以自带 <c>components</c>，版本戳组件会插到它的开头。
    /// </summary>
    internal static XmlDocument Save(string gameBody, int? stamp = null, bool mugirl = true)
    {
        string modIds = "<li>ludeon.rimworld</li>" + (mugirl ? "<li>har.mugirlrace</li>" : "<li>some.othermod</li>");
        var document = new XmlDocument();
        document.LoadXml("<savegame><meta><gameVersion>1.6.4871 rev591</gameVersion><modIds>" + modIds + "</modIds></meta><game>" + gameBody + "</game></savegame>");
        if (stamp is { } value)
        {
            XmlElement game = document.DocumentElement!["game"]!;
            XmlElement components = game["components"] ?? (XmlElement)game.AppendChild(document.CreateElement("components"))!;
            XmlElement hub = document.CreateElement("li");
            hub.SetAttribute("Class", "Mugirl.Core.GameComponent_Mugirl");
            XmlElement label = document.CreateElement("mugirlSaveSchema");
            label.InnerText = value.ToString(CultureInfo.InvariantCulture);
            hub.AppendChild(label);
            components.PrependChild(hub);
        }

        return document;
    }

    /// <summary><c>game</c> 元素的内部 XML（不含版本戳组件），用于比较升级结果。</summary>
    internal static string Game(XmlDocument document) => document.DocumentElement!["game"]!.InnerXml;

    /// <summary>用单个测试模块的登记编译表格。</summary>
    /// <param name="currentSchema">允许的最高存档结构版本（测试以后版本的行时大于 <see cref="SaveSchema.Current"/>）。</param>
    internal static UpgradeTables Tables(Action<UpgradeModuleBuilder> register, int currentSchema = SaveSchema.Current)
    {
        var builder = new UpgradeTableBuilder(currentSchema);
        register(builder.Module(ModuleName, 100));
        return builder.Build();
    }

    internal static UpgradeTables Empty() => new UpgradeTableBuilder().Build();

    /// <summary>识别并运行 DOM 流水线，返回会话。</summary>
    internal static SaveUpgradeSession Upgrade(XmlDocument document, UpgradeTables tables, IDefCatalog? catalog = null)
    {
        SaveUpgradeSession session = SaveUpgradeSession.Start(1, document, filePath: null);
        session.RunDom(tables, catalog ?? new TestCatalog());
        return session;
    }

    /// <summary>升级 <c>game</c> 内容并断言成功，返回升级后的 <c>game</c> 内部 XML。</summary>
    internal static string Upgraded(string gameBody, UpgradeTables tables, IDefCatalog? catalog = null)
    {
        XmlDocument document = Save(gameBody);
        SaveUpgradeSession session = Upgrade(document, tables, catalog);
        Assert.False(session.Report.Failed, session.Report.FailedStep + ": " + session.Report.Error);
        AssertNoMixedContent(document.DocumentElement!);
        return Game(document);
    }

    /// <summary>升级后的 DOM 中不能出现与元素并列的文本节点或任何注释（Scribe 会把它们当成列表项）。</summary>
    internal static void AssertNoMixedContent(XmlElement element)
    {
        bool hasElement = false;
        bool hasText = false;
        for (XmlNode? child = element.FirstChild; child is not null; child = child.NextSibling)
        {
            Assert.NotEqual(XmlNodeType.Comment, child.NodeType);
            hasElement |= child.NodeType == XmlNodeType.Element;
            hasText |= child.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace;
            if (child is XmlElement childElement)
            {
                AssertNoMixedContent(childElement);
            }
        }

        Assert.False(hasElement && hasText, "Mixed content under <" + element.Name + ">.");
    }

    /// <summary>步骤报告中某步骤的动作数；步骤没有运行时为 -1。</summary>
    internal static int Actions(SaveUpgradeSession session, string stepId) =>
        session.Report.Steps.FirstOrDefault(step => step.Id == stepId)?.Actions ?? -1;
}
