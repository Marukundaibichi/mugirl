namespace Mugirl.Control.Api;

/// <summary>C.public-api 正对照：扩展点 <see cref="ControlPublicAlias.PatchableCalled"/> 仍是本 Mod 代码真正调用的决策点。</summary>
public static class PublicApiCaller
{
    public static bool Decide() => ControlPublicAlias.PatchableCalled();
}
