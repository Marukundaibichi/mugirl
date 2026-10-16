using System.Reflection;
using System.Globalization;
using System.Xml;
using MugirlRace.Validation.Checks;
using MugirlRace.Validation.Content;

namespace MugirlRace.Validation;

/// <summary>
/// 升级器 <c>IDefCatalog</c> 接口（定义在被测 Mod 中，验证程序编译时不知道它）的运行时实现，数据来自内容模型：
/// <c>Contains(root, defName)</c> 查完整配置中的 Def，<c>CompClasses(root, defName)</c> 读继承与补丁之后的 comps 列表。
/// </summary>
/// <remarks><see cref="DispatchProxy"/> 要求代理类型 public、非 sealed 且有无参构造函数。</remarks>
public class DefCatalogProxy : DispatchProxy
{
    private ContentModel? content;
    private HandleDefaults? defaults;

    internal static object Create(Type catalogInterface, ContentModel content, HandleDefaults defaults)
    {
        object proxy = DispatchProxy.Create(catalogInterface, typeof(DefCatalogProxy));
        var self = (DefCatalogProxy)proxy;
        self.content = content;
        self.defaults = defaults;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        string root = args?[0] as string ?? "";
        string defName = args?[1] as string ?? "";
        return targetMethod.Name switch
        {
            "Contains" => Entries(root, defName).Any(),
            "CompClasses" => CompClasses(root, defName),
            "IntegerField" => IntegerField(root, defName, args?[2] as string ?? ""),
            _ => throw new NotSupportedException("IDefCatalog member not implemented by the validator: " + targetMethod.Name),
        };
    }

    private IEnumerable<DefEntry> Entries(string root, string defName)
        => content!.Defs.FindAny(defName).Where(entry => DefRoots.Database(entry.Type).Name == root);

    private int? IntegerField(string root, string defName, string field)
    {
        XmlElement? node = content!.Corpus.Defs.FirstOrDefault(n => n["defName"]?.InnerText.Trim() == defName
            && content.Types.GetTypeInAnyAssembly(n.GetAttribute("Class") is { Length: > 0 } cls ? cls : n.Name) is { } type && DefRoots.Database(type).Name == root);
        if (node is null)
        {
            return null;
        }

        Type? defType = content.Types.GetTypeInAnyAssembly(node.GetAttribute("Class") is { Length: > 0 } className ? className : node.Name);
        if (defType?.GetField(field)?.FieldType != typeof(int))
        {
            return null;
        }

        string? raw = content.Inheritance.Resolve(node)[field]?.InnerText;
        if (raw is not null)
        {
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int explicitValue) ? explicitValue : null;
        }

        string[] values = [.. defaults!.Of(defType, field).Distinct(StringComparer.Ordinal)];
        return values.Length == 1 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    private List<string>? CompClasses(string root, string defName)
    {
        if (!Entries(root, defName).Any())
        {
            return null;
        }

        XmlElement? node = content!.Corpus.Defs.FirstOrDefault(n => n["defName"]?.InnerText.Trim() == defName
            && content.Types.GetTypeInAnyAssembly(n.GetAttribute("Class") is { Length: > 0 } cls ? cls : n.Name) is { } type && DefRoots.Database(type).Name == root);
        if (node is null)
        {
            return [];
        }

        var classes = new List<string>();
        foreach (XmlElement item in content.Inheritance.Resolve(node)["comps"]?.ChildNodes.OfType<XmlElement>() ?? [])
        {
            if (item["compClass"]?.InnerText.Trim() is { Length: > 0 } explicitClass)
            {
                classes.Add(content.Types.GetTypeInAnyAssembly(explicitClass)?.FullName ?? explicitClass);
            }
            else if (item.GetAttribute("Class") is { Length: > 0 } props && content.Types.GetTypeInAnyAssembly(props) is { } propsType
                && defaults!.Of(propsType, "compClass").Skip(1).FirstOrDefault() is { } fullName)
            {
                classes.Add(fullName);
            }
        }

        return classes;
    }
}
