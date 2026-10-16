namespace Mugirl.Features.WeaponWheel;

// 一把武器在某一帧的绘制姿态：地图坐标、朝向角（度，0 = 北）、缩放与透明度。
internal readonly record struct WeaponWheelWeaponPose(Vector3 Position, float Angle, float Scale, float Alpha);
