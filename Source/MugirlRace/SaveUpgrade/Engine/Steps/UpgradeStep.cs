using System.Linq;

namespace Mugirl.SaveUpgrade;

/// <summary>一个登记的升级步骤：稳定 id、运行阶段、所属模块、消耗的旧名称与理由。</summary>
/// <remarks>
/// 步骤必须幂等并按 XML 形状或游戏状态判断是否适用：同一存档升级两次与升级一次结果相同，失败重试时已应用的步骤再运行也无害。
/// <see cref="Consumes"/> 列出本步骤读取并改写的旧名称，格式为 <c>type:&lt;类型&gt;</c>、<c>def:&lt;根&gt;/&lt;名&gt;</c>、
/// <c>label:&lt;所属选择器&gt;/&lt;标签&gt;</c>、<c>enum:&lt;枚举&gt;.&lt;成员&gt;</c>、<c>value:&lt;上下文&gt;/&lt;值&gt;</c>。
/// 验证器用 <c>Golden/&lt;id&gt;/before.xml</c> 核对这些名称确实出现在升级前而不出现在升级后。
/// </remarks>
internal abstract class UpgradeStep
{
    private static readonly string[] ConsumePrefixes = ["type:", "def:", "label:", "enum:", "value:"];

    private protected UpgradeStep(string id, UpgradePhase phase, string reason, IReadOnlyList<string> consumes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(consumes);
        foreach (string consumed in consumes)
        {
            if (string.IsNullOrWhiteSpace(consumed) || !ConsumePrefixes.Any(prefix => consumed.StartsWith(prefix, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Step " + id + " declares a consumed name without a known kind prefix: " + consumed, nameof(consumes));
            }
        }

        Id = id;
        Phase = phase;
        Reason = reason;
        Consumes = [.. consumes];
    }

    /// <summary>全局唯一的步骤 id，形如 <c>&lt;模块&gt;.&lt;名称&gt;</c>；写入报告与升级清单，发布后不改。</summary>
    internal string Id { get; }

    internal UpgradePhase Phase { get; }

    internal string Reason { get; }

    internal IReadOnlyList<string> Consumes { get; }

    /// <summary>登记本步骤的模块；由登记表格时填写。</summary>
    internal string Module { get; private set; } = string.Empty;

    /// <summary>引入本步骤的存档结构版本：来源版本小于它时运行。</summary>
    internal virtual int Schema => 1;

    /// <summary><see cref="Consumes"/> 中的标签名（<c>label:</c> 项最后一个 <c>/</c> 之后的部分）。</summary>
    internal IEnumerable<string> ConsumedLabels =>
        Consumes.Where(c => c.StartsWith("label:", StringComparison.Ordinal)).Select(c => c[(c.LastIndexOf('/') + 1)..]);

    internal void AssignModule(string module)
    {
        if (Module.Length != 0 && Module != module)
        {
            throw new InvalidOperationException("Step " + Id + " is already registered by module " + Module + ".");
        }

        Module = module;
    }
}
