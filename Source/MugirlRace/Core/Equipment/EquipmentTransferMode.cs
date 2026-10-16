namespace Mugirl.Core;

internal enum EquipmentTransferMode
{
    None,
    // 轮盘内部的主手/储备互换：原版装备通知整体跳过。
    InternalTransfer,
    // 轮盘登记或注销储备武器：原版通知照常执行，但轮盘自己的外部装备同步不响应。
    Registration
}
