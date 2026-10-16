namespace Mugirl.Features.Restraints;

// 高级束具按钮图标：XML 配置的贴图路径解析一次后缓存在 CompProperties 字段上（Def 级，随 Def 生命周期）。
// 路径为空或找不到贴图时用原版 DesirePower 图标，与 1.6 相同；找不到时警告一次（1.7 模组贴图路径区分大小写）。
internal static class RestraintCommandIcon
{
    internal static Texture2D Resolve(ref Texture2D? cache, string? iconPath)
    {
        if (cache is not null)
        {
            return cache;
        }

        Texture2D? found = null;
        if (!string.IsNullOrEmpty(iconPath))
        {
            found = ContentFinder<Texture2D>.Get(iconPath, false);
            if (found is null)
            {
                MugirlLog.WarningOnce("RestraintCommandIcon.Missing." + iconPath, "Restraint command icon not found: " + iconPath);
            }
        }

        cache = found ?? TexCommand.DesirePower;
        return cache;
    }
}
