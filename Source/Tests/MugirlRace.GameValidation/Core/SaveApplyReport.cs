namespace Mugirl.GameValidation.Core;

/// <summary><see cref="SaveNameMap.Apply"/> 对存档副本做的翻译。</summary>
internal sealed class SaveApplyReport
{
    /// <summary>改写的 <c>Class</c> 属性数。</summary>
    public int Types { get; set; }

    /// <summary>改写的 Def 引用数（含按位置判断根类型的与跨根类型一致的）。</summary>
    public int Defs { get; set; }

    /// <summary>改写的 ThingID 与 <c>Thing_</c> 引用数。</summary>
    public int Ids { get; set; }

    /// <summary>改名的标签数。</summary>
    public int LabelsRenamed { get; set; }

    /// <summary>因显式删除而从副本中移除的标签（路径与原因）。</summary>
    public List<string> LabelsRemoved { get; } = [];

    /// <summary>名字表无法执行的结构消费；原始证据保留，必须运行生产 DOM 步骤。</summary>
    public List<string> PendingConsumptions { get; } = [];

    /// <summary>同一 defName 在不同根类型中映射不同、而位置认不出根类型的引用：保持原名。</summary>
    public List<string> AmbiguousDefs { get; } = [];

    /// <summary>多个标签行冲突的标签（清单缺陷）。</summary>
    public List<string> AmbiguousLabels { get; } = [];

    public override string ToString()
        => $"{Types} Class value(s), {Defs} def reference(s), {Ids} ID(s), {LabelsRenamed} label(s) renamed, {LabelsRemoved.Count} dropped label(s), {PendingConsumptions.Count} unapplied structural consumption(s), "
            + $"{AmbiguousDefs.Count} ambiguous def reference(s), {AmbiguousLabels.Count} ambiguous label(s)";
}
