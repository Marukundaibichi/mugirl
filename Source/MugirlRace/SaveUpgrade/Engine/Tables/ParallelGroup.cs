namespace Mugirl.SaveUpgrade;

/// <summary>同一所属元素下按下标一一对应的若干列表标签；删除其中一项时同一下标的各项一起删除。</summary>
internal sealed record ParallelGroup(OwnerSelector Owner, IReadOnlyList<string> Labels, string Module);
