namespace Mugirl.Features.WeaponWheel;

// 轮盘当前可用的连携模式，由 CompWeaponWheel.EvaluateStatus 一次求出，供状态文字与界面共用。
// 剑舞资格优先；两者都不满足时显示普通标签。翻译键写成字面量，便于静态检查核对语言文件。
internal readonly record struct WeaponWheelStatus(bool SwordDance, bool FullFirepower)
{
    internal bool AnyLinkedMode => SwordDance || FullFirepower;

    internal string Label() => SwordDance
        ? "Mugirl.WeaponWheel.StatusSwordDance".Translate()
        : FullFirepower ? "Mugirl.WeaponWheel.StatusFull".Translate() : "Mugirl.WeaponWheel.Tab".Translate();

    internal string Tooltip() => SwordDance ? "Mugirl.WeaponWheel.StatusSwordDanceDesc".Translate() : Label();
}
