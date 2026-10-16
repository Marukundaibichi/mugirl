using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>
/// 用升级清单（K1）解释旧档（A，已经 <see cref="SaveNameMap.Apply"/> 翻译成新名）与升级后重存档（B）之间的差异：
/// 显式删除的类型与 Def、明确映射过的值和精确的删除策略。Consumed/Claimed 本身不是保存结果的证据。
/// </summary>
/// <remarks>
/// 旧清单诊断辅助，不用于证明实际升级后的保存完整性；当前验证使用真实 DOM 产物与读取轨迹。
/// 消费步骤必须实际执行并给出规范结果，不能根据消费声明移除比较证据。
/// </remarks>
internal sealed class SaveDiffExplainer
{
    private readonly SaveNameMap map;
    private readonly ISaveOwnerContext context;
    private readonly bool schemaZero;
    private readonly Func<string, bool> queuedJobEnds;

    /// <param name="map">升级清单。</param>
    /// <param name="context">owner 选择器的求值上下文（默认值行）。</param>
    /// <param name="schemaZero">A 是没有版本戳的旧档；仍须有明确 endInFlight 行，不能推断中断。</param>
    /// <param name="queuedJobEnds">排队工作的 JobDef（新名）是否由本 Mod 的 JobDriver 驱动且被策略移除。</param>
    public SaveDiffExplainer(SaveNameMap map, ISaveOwnerContext context, bool schemaZero, Func<string, bool> queuedJobEnds)
    {
        this.map = map;
        this.context = context;
        this.schemaZero = schemaZero;
        this.queuedJobEnds = queuedJobEnds;
    }

    /// <summary>差异的解释；不能由清单解释时为 null。</summary>
    public string? Explain(SaveDifference difference, IReadOnlyList<SaveDifference> all)
    {
        XElement? element = difference.BeforeElement ?? difference.AfterElement;
        if (difference.Kind == "missing" && element is not null && PolicyFor(element) is { } policy)
        {
            return policy;
        }

        if (difference.Kind == "missing" && difference.BeforeElement is { } before && DroppedOwner(before) is { } dropped)
        {
            return dropped;
        }

        switch (difference.Kind)
        {
            case "changed":
                if (difference.Before is { } from && difference.After is { } to && element is not null
                    && map.ExplainsValueChange(from, to, element, context))
                {
                    return "value mapped by the upgrade manifest";
                }

                return null;
            case "missing" when difference.BeforeElement is { } missing:
                // K1 defaults 只声明旧默认值的物化，不声明当前默认值；不能据此允许任何已保存值消失。
                if (missing.Name.LocalName == "li" && !missing.HasElements && missing.Value.Trim() is { Length: > 0 } text)
                {
                    if (map.DefRootsOf(text).Any() && map.DefRootsOf(text).All(root => map.IsDroppedDef(root, text)))
                    {
                        return "dropped def removed from a list";
                    }

                    string parent = Parent(difference.Path);
                    if (all.Any(x => x.Kind == "added" && x.Item == difference.Item && Parent(x.Path) == parent && x.AfterElement is { } added
                        && map.ExplainsValueChange(text, added.Value.Trim(), missing, context)))
                    {
                        return "list entry mapped by the upgrade manifest";
                    }
                }

                return null;
            case "nullness":
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// 差异所在的元素是否属于读档后按清单策略处理的对象：结束的进行中工作（Pawn 的 <c>jobs</c>），或重置、移除、由步骤认领的 Lord。
    /// </summary>
    private string? PolicyFor(XElement element)
    {
        foreach (XElement ancestor in element.AncestorsAndSelf())
        {
            if (ancestor.Name.LocalName == "jobs" && ancestor.Parent is { } pawn && SaveXml.IsPawn(pawn))
            {
                XElement? branch = element.AncestorsAndSelf().FirstOrDefault(e => e.Parent == ancestor);
                string? driver = ancestor.Element("curDriver") is { } current ? SaveXml.ClassOf(current) : null;
                if (element == branch && branch?.Name.LocalName is "curJob" or "curDriver" && driver is not null && map.EndsJob(driver, schemaZero))
                {
                    return "in-flight job of " + driver + " ended by the job policy";
                }

                if (branch?.Name.LocalName == "jobQueue" && element.Name.LocalName == "li" && element.Parent == branch.Element("jobs")
                    && element.Element("job")?.Element("def")?.Value is { } queued && queuedJobEnds(queued))
                {
                    return "queued job " + queued + " removed by the job policy";
                }

                return null;
            }

            if (ancestor.Name.LocalName == "li" && ancestor.Parent?.Name.LocalName == "lords"
                && ancestor.Element("lordJob") is { } lordJob && SaveXml.ClassOf(lordJob) is { } lordClass && map.LordPolicy(lordClass) is { } lordPolicy)
            {
                if (lordPolicy == "removeLord" && element == ancestor)
                {
                    return $"lord of {lordClass} handled by the lord policy {lordPolicy}";
                }

                return null;
            }
        }

        return null;
    }

    /// <summary>元素或其外层对象的类型或 Def 被删除或被步骤消费（随之移除的物品、Hediff、列表项及其内容）。</summary>
    private string? DroppedOwner(XElement element)
    {
        foreach (XElement node in element.AncestorsAndSelf())
        {
            if (node != element)
            {
                continue;
            }
            if (node.Name.LocalName == "li" && node.Parent?.Name.LocalName == "lords" && node.Element("lordJob") is { } lordJob
                && SaveXml.ClassOf(lordJob) is { } lordType && map.IsDroppedType(lordType))
            {
                return $"lord job type {lordType} explicitly dropped";
            }

            if (SaveXml.ClassOf(node) is { } className && map.IsDroppedType(className))
            {
                return $"type {className} explicitly dropped";
            }

            if (node.Element("def") is { } defElement && defElement.Value.Trim() is { Length: > 0 } defName
                && SaveDefRoots.RootOf(defElement) is { } root && map.IsDroppedDef(root, defName))
            {
                return $"{root} {defName} explicitly dropped";
            }
        }

        return null;
    }

    private static string Parent(string path) => path.Contains('/', StringComparison.Ordinal) ? path[..path.LastIndexOf('/')] : string.Empty;
}
