namespace Mugirl.ShaderCheck;

/// <summary>从 XML 读到的 ShaderTypeDef。</summary>
/// <param name="Source">XML 元素。</param>
/// <param name="ShaderPath">着色器路径；1.7 中它的最后一段就是解析到的 ShaderDef 名。</param>
/// <param name="UiShaderPath">可选的 UI 着色器路径。</param>
internal sealed record ShaderTypeDefEntry(XmlDef Source, string? ShaderPath, string? UiShaderPath)
{
    /// <summary>defName。</summary>
    public string DefName => Source.DefName;

    /// <summary>读取一个 <c>&lt;ShaderTypeDef&gt;</c> 元素。</summary>
    public static ShaderTypeDefEntry Read(XmlDef def) => new(
        def,
        def.Element.Element("shaderPath")?.Value.Trim(),
        def.Element.Element("uiShaderPath")?.Value.Trim());

    /// <summary>Verse.ShaderDatabase.ShaderDefNameFor：最后一个 '/' 之后的部分。</summary>
    public static string ShaderDefNameFor(string shaderPath)
    {
        int slash = shaderPath.LastIndexOf('/');
        return slash < 0 ? shaderPath : shaderPath[(slash + 1)..];
    }
}
