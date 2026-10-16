using System.Xml.Linq;
using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>读成引擎自己的 <see cref="ShaderProgramDefinition"/> 的 ShaderDef。</summary>
/// <param name="Source">XML 元素。</param>
/// <param name="Program">着色器程序；Def 没有时为 null。</param>
/// <param name="Keywords">Def 的 shaderKeywords。</param>
/// <param name="ReadErrors">游戏加载时会报告的问题。</param>
internal sealed record ShaderDefEntry(XmlDef Source, ShaderProgramDefinition? Program, IReadOnlyList<string> Keywords, IReadOnlyList<string> ReadErrors)
{
    private static readonly HashSet<string> DefFields =
        ["defName", "label", "description", "shaderProgram", "shaderKeywords", "isComplex", "complexVersion", "iconVersion"];

    /// <summary>defName。</summary>
    public string DefName => Source.DefName;

    /// <summary>本模组的 ShaderDef 为 true。</summary>
    public bool IsOwn => Source.IsOwn;

    /// <summary>读取一个 <c>&lt;ShaderDef&gt;</c> 元素。</summary>
    public static ShaderDefEntry Read(XmlDef def)
    {
        List<string> errors = [];
        foreach (XElement child in def.Element.Elements())
        {
            if (!DefFields.Contains(child.Name.LocalName))
            {
                errors.Add($"{XmlDef.Describe(child, def.File)}: ShaderDef has no field '{child.Name.LocalName}'.");
            }
        }

        if (def.Element.Attribute("ParentName") is not null)
        {
            errors.Add($"{def.Location}: ShaderDef inheritance (ParentName) is not supported by ShaderCheck.");
        }

        XElement? programElement = def.Element.Element("shaderProgram");
        ShaderProgramDefinition? program = programElement is null
            ? null
            : XmlObjectReader.Read<ShaderProgramDefinition>(programElement, def.File, errors);
        List<string> keywords = [.. def.Element.Element("shaderKeywords")?.Elements("li").Select(li => li.Value.Trim()) ?? []];
        return new ShaderDefEntry(def, program, keywords, errors);
    }
}
