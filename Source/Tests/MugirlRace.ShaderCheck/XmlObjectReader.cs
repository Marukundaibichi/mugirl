using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 按 RimWorld Def 加载器处理普通数据类的方式把 XML 元素读成对象：每个子元素设置同名的公共实例字段；列表为 <c>&lt;li&gt;</c>，
/// 字典为 <c>&lt;li&gt;&lt;key/&gt;&lt;value/&gt;&lt;/li&gt;</c>。未知字段与无法解析的值照游戏加载时那样报告。
/// </summary>
internal static class XmlObjectReader
{
    /// <summary>创建 <typeparamref name="T"/> 并用 <paramref name="element"/> 的子元素填充。</summary>
    public static T Read<T>(XElement element, string file, List<string> errors)
        where T : class, new()
        => (T)ReadObject(element, typeof(T), file, errors);

    private static object ReadObject(XElement element, Type type, string file, List<string> errors)
    {
        object instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Cannot create {type.FullName}.");
        foreach (XElement child in element.Elements())
        {
            string name = child.Name.LocalName;
            FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field is null)
            {
                errors.Add($"{XmlDef.Describe(child, file)}: {type.Name} has no field '{name}'.");
                continue;
            }

            try
            {
                field.SetValue(instance, ReadValue(child, field.FieldType, file, errors));
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
            {
                errors.Add($"{XmlDef.Describe(child, file)}: cannot read <{name}>: {exception.Message}");
            }
        }

        return instance;
    }

    private static object? ReadValue(XElement element, Type type, string file, List<string> errors)
    {
        string text = element.Value.Trim();
        if (type == typeof(string))
        {
            return text;
        }

        if (type == typeof(bool))
        {
            return bool.Parse(text);
        }

        if (type == typeof(int))
        {
            return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        if (type == typeof(float))
        {
            return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        if (type.IsEnum)
        {
            return Enum.Parse(type, text, ignoreCase: false);
        }

        if (type == typeof(RenderQueueValue))
        {
            return new RenderQueueValue(RenderQueueParser.Parse(text));
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (IList)(Activator.CreateInstance(type) ?? throw new InvalidOperationException(type.FullName));
            Type itemType = type.GetGenericArguments()[0];
            foreach (XElement item in element.Elements("li"))
            {
                list.Add(ReadValue(item, itemType, file, errors));
            }

            return list;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var dictionary = (IDictionary)(Activator.CreateInstance(type) ?? throw new InvalidOperationException(type.FullName));
            Type[] arguments = type.GetGenericArguments();
            foreach (XElement item in element.Elements("li"))
            {
                XElement key = item.Element("key") ?? throw new FormatException("dictionary <li> without <key>");
                XElement value = item.Element("value") ?? throw new FormatException("dictionary <li> without <value>");
                object keyValue = ReadValue(key, arguments[0], file, errors) ?? throw new FormatException("null dictionary key");
                dictionary.Add(keyValue, ReadValue(value, arguments[1], file, errors));
            }

            return dictionary;
        }

        if (type.IsClass)
        {
            return ReadObject(element, type, file, errors);
        }

        throw new ArgumentException($"field type {type.Name} is not supported by ShaderCheck");
    }
}
