namespace Mugirl.Features.Milk;

// 一名 Pawn 的可见尺寸（地图格单位）与头部锚点（相对 Root，未旋转）。
internal readonly record struct PawnVisualMetrics(Vector2 BodySize, Vector2 HeadSize, Vector3 HeadAnchor, bool HasExplicitHead);
