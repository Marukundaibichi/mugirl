namespace MugirlRace.Validation.Content;

/// <summary>传给游戏 API 的字符串所命名的对象。</summary>
internal enum LookupKind
{
    /// <summary><c>DefDatabase&lt;T&gt;.GetNamed</c>、<c>GenDefDatabase.GetDef</c>、<c>XDef.Named</c>：缺失时记录错误。</summary>
    DefName,

    /// <summary><c>GetNamedSilentFail</c>、<c>GetNamed(name, false)</c>、<c>GetDefSilentFail</c>：缺失时返回 null。</summary>
    DefNameSilent,

    /// <summary><c>Translate</c>/<c>TranslateSimple</c>：缺失时显示原始键。</summary>
    TranslationKey,

    /// <summary><c>CanTranslate</c>/<c>TryTranslate</c>/<c>TranslateWithBackup</c>：缺失由调用方处理。</summary>
    TranslationProbe,

    /// <summary>贴图路径（<c>ContentFinder&lt;Texture2D&gt;.Get</c>、<c>GraphicDatabase.Get</c>、<c>MaterialPool.MatFrom</c>、<c>GraphicData.texPath</c>）。</summary>
    TexturePath,

    /// <summary>贴图目录（<c>ContentFinder&lt;Texture2D&gt;.GetAllInFolder</c>）。</summary>
    TextureFolder,

    /// <summary>Shader 路径（<c>ShaderDatabase.LoadShader</c>）：1.7 中其最后一段是 ShaderDef 名。</summary>
    ShaderPath,
}
