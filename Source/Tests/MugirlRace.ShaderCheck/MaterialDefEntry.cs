using System.Globalization;
using System.Xml.Linq;

namespace Mugirl.ShaderCheck;

/// <summary>从 XML 读到的 MaterialDef（Verse.MaterialDef 的字段）。</summary>
/// <param name="Source">XML 元素。</param>
/// <param name="ShaderDefName">引用的 ShaderDef，缺失时为 null。</param>
/// <param name="RenderQueue">渲染队列覆盖（-1 = 用着色器的）。</param>
/// <param name="Properties">纹理、浮点、颜色与向量，按文件顺序。</param>
/// <param name="ReadErrors">游戏加载时会报告的问题。</param>
internal sealed record MaterialDefEntry(
    XmlDef Source,
    string? ShaderDefName,
    int RenderQueue,
    IReadOnlyList<MaterialProperty> Properties,
    IReadOnlyList<string> ReadErrors)
{
    private static readonly HashSet<string> DefFields =
        ["defName", "label", "description", "resourcePath", "shader", "renderQueue", "textures", "floats", "colors", "vectors", "keywords"];

    private static readonly HashSet<string> TextureFields = ["name", "path", "scale", "offset", "alphaFromGrayscale"];

    private static readonly HashSet<string> ValueFields = ["name", "value"];

    /// <summary>defName。</summary>
    public string DefName => Source.DefName;

    /// <summary>读取一个 <c>&lt;MaterialDef&gt;</c> 元素。</summary>
    public static MaterialDefEntry Read(XmlDef def)
    {
        List<string> errors = [];
        foreach (XElement child in def.Element.Elements())
        {
            if (!DefFields.Contains(child.Name.LocalName))
            {
                errors.Add($"{XmlDef.Describe(child, def.File)}: MaterialDef has no field '{child.Name.LocalName}'.");
            }
        }

        if (def.Element.Attribute("ParentName") is not null)
        {
            errors.Add($"{def.Location}: MaterialDef inheritance (ParentName) is not supported by ShaderCheck.");
        }

        int renderQueue = -1;
        string? queueText = def.Element.Element("renderQueue")?.Value;
        if (queueText is not null && !int.TryParse(queueText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out renderQueue))
        {
            errors.Add($"{def.Location}: renderQueue '{queueText}' is not an integer.");
        }

        List<MaterialProperty> properties = [];
        ReadList(def, "textures", MaterialPropertyKind.Texture, "path", TextureFields, properties, errors);
        ReadList(def, "floats", MaterialPropertyKind.Float, "value", ValueFields, properties, errors);
        ReadList(def, "colors", MaterialPropertyKind.Color, "value", ValueFields, properties, errors);
        ReadList(def, "vectors", MaterialPropertyKind.Vector, "value", ValueFields, properties, errors);
        string? shader = def.Element.Element("shader")?.Value.Trim();
        return new MaterialDefEntry(def, string.IsNullOrEmpty(shader) ? null : shader, renderQueue, properties, errors);
    }

    private static void ReadList(
        XmlDef def,
        string listName,
        MaterialPropertyKind kind,
        string valueField,
        HashSet<string> allowedFields,
        List<MaterialProperty> properties,
        List<string> errors)
    {
        XElement? list = def.Element.Element(listName);
        if (list is null)
        {
            return;
        }

        foreach (XElement item in list.Elements())
        {
            string location = XmlDef.Describe(item, def.File);
            if (item.Name.LocalName != "li")
            {
                errors.Add($"{location}: <{listName}> items must be <li>.");
                continue;
            }

            foreach (XElement field in item.Elements().Where(field => !allowedFields.Contains(field.Name.LocalName)))
            {
                errors.Add($"{XmlDef.Describe(field, def.File)}: {listName} item has no field '{field.Name.LocalName}'.");
            }

            string? name = item.Element("name")?.Value.Trim();
            string? value = item.Element(valueField)?.Value.Trim();
            if (string.IsNullOrEmpty(name) || value is null)
            {
                errors.Add($"{location}: {listName} item needs <name> and <{valueField}>.");
                continue;
            }

            properties.Add(new MaterialProperty(kind, name, value, location));
        }
    }
}
