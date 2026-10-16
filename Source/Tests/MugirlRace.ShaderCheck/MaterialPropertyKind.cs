namespace Mugirl.ShaderCheck;

/// <summary>MaterialDef 属性列表的种类。</summary>
internal enum MaterialPropertyKind
{
    /// <summary><c>&lt;textures&gt;</c>：值为纹理路径。</summary>
    Texture,

    /// <summary><c>&lt;floats&gt;</c>。</summary>
    Float,

    /// <summary><c>&lt;colors&gt;</c>：用 ParseHelper.ParseColor 解析（分量大于 1 时整体按 0-255 读）。</summary>
    Color,

    /// <summary><c>&lt;vectors&gt;</c>：四个浮点数。</summary>
    Vector,
}
