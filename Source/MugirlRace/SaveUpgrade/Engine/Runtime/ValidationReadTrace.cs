using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>隔离验证专用的真实读取轨迹；正常游戏不启用，也不保存游戏对象或 XML 文档。</summary>
internal sealed class ValidationReadTrace
{
    // StaticCacheLifecycle: 仅验证进程显式启用；每次主存档读取替换，内容只有路径、标量和诊断文字。
    private static bool enabled;
    private static ValidationReadTrace? latest;
    private readonly List<ValidationReadEvent> events = [];
    private readonly List<string> errors = [];

    internal static void EnableForValidation()
    {
        if (!GenCommandLine.CommandLineArgPassed("mugirlvalidate")
            || !File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "mugirl-gamevalidation.json")))
        {
            throw new InvalidOperationException("Read tracing requires the isolated game-validation launch and its savedata marker.");
        }

        enabled = true;
    }

    internal static void Begin(SaveUpgradeSession session)
    {
        ValidationReadTrace? trace = enabled ? new ValidationReadTrace() : null;
        session.ReadTrace = trace;
        Volatile.Write(ref latest, trace);
    }

    internal static string? SnapshotJson() => Volatile.Read(ref latest)?.ToJson();

    internal string ToJson() => JsonSerializer.Serialize(new { format = 1, events, errors });

    internal void Reference(XmlNode? current, string? currentPath, string path, IExposable? currentParent,
        IExposable? parent, Type targetType, string? oldValue, string? newValue)
    {
        if (oldValue == newValue) { return; }
        if (!ReferenceEquals(currentParent, parent) || current is not XmlElement element)
        {
            Error("Reference", path, "The bank registration does not belong to the current Scribe owner.");
            return;
        }

        string prefix = (currentPath ?? string.Empty) + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            Error("Reference", path, "The registration path is outside the current Scribe node.");
            return;
        }

        string relative = path[prefix.Length..];
        XmlElement? source = relative.IndexOf('/', StringComparison.Ordinal) < 0 ? element[relative] : null;
        // ScribeExtractor 的三个 TargetInfo 读取器先进入标量，再用虚拟 /thing 登记它。
        // 该虚拟路径并不是一个 XML 子元素；这里只接受引擎已核实的类型与真实标量。
        if (source is null && relative == "thing" && targetType == typeof(Thing)
            && ReviewedSaveEngine.ReadersAvailable
            && !DomEditor.HasElementChild(element))
        {
            source = element;
        }

        Scalar("Reference", source, oldValue, newValue);
    }

    internal void References(XmlNode? source, List<string>? oldValues, List<string>? newValues)
    {
        if (ReferenceEquals(oldValues, newValues)) { return; }
        if (source is not XmlElement list || oldValues is null || newValues is null
            || oldValues.Count != newValues.Count)
        {
            Error("ReferenceList", string.Empty, "The registered collection has no matching XML list.");
            return;
        }

        XmlElement[] items = list.ChildNodes.OfType<XmlElement>().ToArray();
        if (items.Length != oldValues.Count || items.Any(item => item.Name != "li"))
        {
            Error("ReferenceList", PathOf(list), "The registered collection does not match its XML positions.");
            return;
        }

        for (int i = 0; i < oldValues.Count; i++)
        {
            Scalar("ReferenceList", items[i], oldValues[i], newValues[i]);
        }
    }

    internal void Scalar(string kind, XmlNode? source, string? oldValue, string? newValue)
    {
        if (oldValue == newValue) { return; }
        if (source is not XmlElement element || DomEditor.HasElementChild(element)
            || element.InnerText != oldValue || element.OwnerDocument?.DocumentElement is not { } root
            || !IsAttached(element, root))
        {
            Error(kind, source is XmlElement candidate ? PathOf(candidate) : string.Empty,
                "The actual reader's source is missing, detached, non-scalar, or differs from the registered value.");
            return;
        }

        events.Add(new ValidationReadEvent(kind, PathOf(element), oldValue, newValue));
    }

    internal void DictionaryPairs(XmlElement dictionary, XmlElement keys, XmlElement values,
        IReadOnlyList<int> removed, IReadOnlyList<string> rewritten)
    {
        events.Add(new ValidationReadEvent("DictionaryPairs", PathOf(dictionary), null, null,
            [.. removed], removed.Select(i => rewritten[i]).ToArray(), removed.Select(i => values.ChildNodes[i]!.InnerText).ToArray()));
    }

    private void Error(string kind, string path, string message) => errors.Add(kind + " " + path + ": " + message);

    private static bool IsAttached(XmlElement element, XmlElement root)
    {
        for (XmlNode? node = element; node is not null; node = node.ParentNode)
        {
            if (ReferenceEquals(node, root)) { return true; }
        }
        return false;
    }

    internal static string PathOf(XmlElement element)
    {
        List<string> parts = [];
        for (XmlNode? node = element; node is XmlElement current; node = node.ParentNode)
        {
            int position = 1;
            // XmlNode.PreviousSibling 自身要从父节点的首项查找；反向逐项取会在宽列表上变成平方复杂度。
            for (XmlNode? sibling = current.ParentNode?.FirstChild; sibling is not null && !ReferenceEquals(sibling, current); sibling = sibling.NextSibling)
            {
                if (sibling is XmlElement other && other.Name == current.Name) { position++; }
            }
            parts.Add(current.Name + "[" + position.ToString(CultureInfo.InvariantCulture) + "]");
        }
        parts.Reverse();
        return "/" + string.Join("/", parts);
    }
}
