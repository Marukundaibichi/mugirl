namespace Mugirl.ShaderCheck;

/// <summary>MaterialDef 的一个具名属性。</summary>
/// <param name="Kind">来自哪个列表。</param>
/// <param name="Name">着色器属性名。</param>
/// <param name="Value">原始值（纹理为纹理路径）。</param>
/// <param name="Location">文件与行号。</param>
internal sealed record MaterialProperty(MaterialPropertyKind Kind, string Name, string Value, string Location);
