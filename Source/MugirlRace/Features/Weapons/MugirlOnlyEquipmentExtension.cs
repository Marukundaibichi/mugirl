namespace Mugirl.Features.Weapons;

// XML 扩展：标记只允许雪牛娘装备的武器（如专属长枪）。enabled 仍允许定义显式关闭限制。
public sealed class MugirlOnlyEquipmentExtension : DefModExtension
{
    public bool enabled = true;
}
