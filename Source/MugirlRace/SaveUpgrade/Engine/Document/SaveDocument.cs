using System.Globalization;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 读档中的存档 DOM：<c>savegame</c> 根、<c>meta</c> 中的 Mod 列表与游戏版本、<c>game</c> 元素和 Mugirl 版本戳。
/// </summary>
/// <remarks>
/// 元数据从 DOM 读取，而不是 <c>ScribeMetaHeaderUtility</c> 的静态字段：那些字段会被之后任何一次文件头读取（思想体系、
/// 异种、剧本文件等）覆盖。游戏版本只用于诊断，没有任何规则按它分支（旧版构建也会在 1.7 游戏上运行）。
/// </remarks>
internal sealed class SaveDocument
{
    private SaveDocument(XmlDocument xml, XmlElement root, XmlElement game, IReadOnlyList<string> modIds, string? gameVersion)
    {
        Xml = xml;
        Root = root;
        Game = game;
        ModIds = modIds;
        GameVersion = gameVersion;
    }

    internal XmlDocument Xml { get; }

    /// <summary><c>savegame</c> 元素。</summary>
    internal XmlElement Root { get; }

    /// <summary><c>savegame/game</c> 元素；所有改写都在它的子树内。</summary>
    internal XmlElement Game { get; }

    /// <summary><c>meta/modIds</c> 中的 packageId（存档里是小写，带 <c>_steam</c> 后缀的是创意工坊副本）。</summary>
    internal IReadOnlyList<string> ModIds { get; }

    /// <summary><c>meta/gameVersion</c>，只用于诊断。</summary>
    internal string? GameVersion { get; }

    /// <summary>Mod 列表中是否有本 Mod（MooGirl 时代的 packageId 小写后相同）。</summary>
    internal bool HasMugirl
    {
        get
        {
            foreach (string id in ModIds)
            {
                foreach (string ours in SaveSchema.PackageIds)
                {
                    if (string.Equals(id.Trim(), ours, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>从 <paramref name="xml"/> 建立存档视图；根元素不是 <c>savegame</c> 或没有 <c>game</c> 时返回 null。</summary>
    internal static SaveDocument? TryCreate(XmlDocument xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (xml.DocumentElement is not { Name: "savegame" } root || root["game"] is not { } game)
        {
            return null;
        }

        List<string> modIds = [];
        string? gameVersion = null;
        if (root["meta"] is { } meta)
        {
            gameVersion = meta["gameVersion"]?.InnerText;
            if (meta["modIds"] is { } ids)
            {
                for (XmlNode? node = ids.FirstChild; node is not null; node = node.NextSibling)
                {
                    if (node is XmlElement item)
                    {
                        modIds.Add(item.InnerText);
                    }
                }
            }
        }

        return new SaveDocument(xml, root, game, modIds, gameVersion);
    }

    /// <summary>
    /// 读取版本戳：<c>game/components/li</c> 中带 <see cref="SaveSchema.StampLabel"/> 子元素的组件（按标签识别，不按 Class）。
    /// 没有版本戳时返回 0；有但无法解析时返回 null。
    /// </summary>
    internal int? ReadStamp()
    {
        if (Game["components"] is not { } components)
        {
            return 0;
        }

        for (XmlNode? node = components.FirstChild; node is not null; node = node.NextSibling)
        {
            if (node is XmlElement component && component[SaveSchema.StampLabel] is { } stamp)
            {
                return int.TryParse(stamp.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0
                    ? value
                    : null;
            }
        }

        return 0;
    }
}
