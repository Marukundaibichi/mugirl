namespace Mugirl.Features.Milk;

// 喂奶时一名 Pawn 的 Root 世界位移与角度。
internal readonly record struct InteractionPose(Vector3 RootWorldOffset, float RootWorldAngle, bool HasExplicitHead, float BodyHeight);
