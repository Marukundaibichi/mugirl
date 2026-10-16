using System.Xml;
using System.Text.RegularExpressions;
using MugirlRace.Validation.Legacy;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;

namespace MugirlRace.Validation.Parity;

/// <summary>用被比较一侧的程序集元数据确定 XML 字段含义；普通字符串不能因为恰好同名就变成 Def 引用。</summary>
internal sealed class XmlParitySchema(TypeUniverse universe, ModuleDefinition module)
{
    internal sealed record Slot(string? DefRoot = null, string? ElementDefRoot = null, bool IsType = false);

    internal string? RootOf(XmlElement node)
        => Resolve(node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name) is { } type ? TypeUniverse.DatabaseRoot(type) : null;

    internal Dictionary<XmlElement, Slot> Analyze(XmlElement node)
    {
        var slots = new Dictionary<XmlElement, Slot>();
        if (Resolve(node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name) is { } type)
        {
            Visit(node, type, slots, null, null);
        }

        return slots;
    }

    internal Dictionary<XmlElement, Slot> AnalyzePatch(XmlElement operation)
    {
        var slots = new Dictionary<XmlElement, Slot>();
        foreach (XmlElement candidate in new[] { operation }.Concat(operation.SelectNodes(".//*")!.OfType<XmlElement>()))
        {
            if (candidate["xpath"] is not { } xpath || candidate["value"] is not { } value) { continue; }
            (TypeReference? type, string? owner, string? field) = AtPath(xpath.InnerText);
            if (type is null) { continue; }
            if (candidate.GetAttribute("Class").EndsWith("PatchOperationReplace", StringComparison.Ordinal))
            {
                foreach (XmlElement child in value.ChildNodes.OfType<XmlElement>()) { Visit(child, type, slots, owner, field); }
            }
            else { Visit(value, type, slots, owner, field); }
        }

        return slots;
    }

    internal string MapXPath(string xpath, RenameMap map)
    {
        string rewritten = Regex.Replace(xpath, """(?<field>@?[A-Za-z_][A-Za-z0-9_]*|text\(\))\s*=\s*(?<q>["'])(?<value>[^"']*)\k<q>""", match =>
        {
            string field = match.Groups["field"].Value;
            string value = match.Groups["value"].Value;
            string mapped = value;
            if (field == "@Class") { mapped = map.Type(value); }
            else if (field is "@Name" or "@ParentName") { mapped = map.AbstractName(value); }
            else
            {
                (TypeReference? type, _, _) = AtPath(xpath[..match.Index]);
                TypeDefinition? definition = type is null ? null : CecilNames.TryResolve(type);
                if (field == "defName" && definition is not null) { mapped = map.Def(value, TypeUniverse.DatabaseRoot(definition)); }
                else if (field == "text()" && definition is not null && TypeUniverse.DatabaseRoot(definition) is { } root) { mapped = map.Def(value, root); }
                else if (definition is not null && Field(definition, field)?.FieldType.FullName == "System.Type") { mapped = map.Type(value); }
            }

            return match.Value[..(match.Groups["value"].Index - match.Index)] + mapped + match.Groups["q"].Value;
        }, RegexOptions.CultureInvariant);
        return string.Join("/", rewritten.Split('/').Select(step =>
        {
            int bracket = step.IndexOf('[', StringComparison.Ordinal);
            string name = bracket < 0 ? step : step[..bracket];
            return map.Type(name) + (bracket < 0 ? "" : step[bracket..]);
        }));
    }

    private (TypeReference? Type, string? Owner, string? Field) AtPath(string xpath)
    {
        TypeReference? type = null;
        string? owner = null;
        string? field = null;
        foreach (string step in xpath.Trim('/').Split('/'))
        {
            string name = step.Split('[')[0].Trim();
            if (name.Length == 0 || name == "Defs") { continue; }
            if (type is null) { type = Resolve(name); if (type is null) { return (null, null, null); } }
            else if (Element(type) is { } item && name == "li") { type = item; }
            else if (CecilNames.TryResolve(type) is { } definition && Field(definition, name) is { } member)
            { type = member.FieldType; owner = member.DeclaringType.FullName; field = member.Name; }
            else { return (null, null, null); }
            Match cls = Regex.Match(step, """@Class\s*=\s*["'](?<class>[^"']+)["']""", RegexOptions.CultureInvariant);
            if (cls.Success) { type = Resolve(cls.Groups["class"].Value) ?? type; }
        }

        return (type, owner, field);
    }

    private static TypeReference? Element(TypeReference type)
        => type is ArrayType array ? array.ElementType
            : type is GenericInstanceType collection && collection.ElementType.FullName is "System.Collections.Generic.List`1" or "System.Collections.Generic.HashSet`1" or "System.Collections.Generic.IEnumerable`1" or "System.Collections.Generic.IList`1"
                ? collection.GenericArguments[0] : null;

    private TypeDefinition? Resolve(string name) => universe.Find(name, module);

    private void Visit(XmlElement node, TypeReference declared, Dictionary<XmlElement, Slot> slots, string? owner, string? field)
    {
        TypeReference type = declared;
        if (node.GetAttribute("Class") is { Length: > 0 } cls && Resolve(cls) is { } actual) { type = actual; }
        if (type is GenericInstanceType nullable && nullable.ElementType.FullName == "System.Nullable`1") { type = nullable.GenericArguments[0]; }
        TypeDefinition? definition = CecilNames.TryResolve(type);
        string? root = definition is null ? null : TypeUniverse.DatabaseRoot(definition);
        if (root is not null && node.ParentNode is not XmlElement { Name: "Defs" } && node.Name != "value")
        {
            slots[node] = new Slot(DefRoot: root);
            return;
        }

        if (type.FullName == "System.Type") { slots[node] = new Slot(IsType: true); return; }
        if (type.FullName == "System.String")
        {
            // 现有组件在 ResolveReferences 中明确按 ThingDef 读取这些字符串；其余字符串不作名字映射。
            if (owner is "Mugirl.Features.Milk.CompProperties_MilkingDevice" or "Mugirl.Features.Milk.CompProperties_MilkingDevice"
                && field is "releaseThingDefName" or "filthDefName")
            { slots[node] = new Slot(DefRoot: "ThingDef"); return; }
            if (owner == "Verse.ThingFilter")
            {
                string? stringRoot = field switch
                {
                    "categories" or "disallowedCategories" => "ThingCategoryDef",
                    "specialFiltersToAllow" or "specialFiltersToDisallow" => "SpecialThingFilterDef",
                    _ => null,
                };
                if (stringRoot is not null) { slots[node] = new Slot(DefRoot: stringRoot); }
            }

            return;
        }

        TypeReference? item = type is ArrayType array ? array.ElementType
            : type is GenericInstanceType collection && collection.ElementType.FullName is "System.Collections.Generic.List`1" or "System.Collections.Generic.HashSet`1" or "System.Collections.Generic.IEnumerable`1" or "System.Collections.Generic.IList`1"
                ? collection.GenericArguments[0] : null;
        if (item is not null)
        {
            foreach (XmlElement child in node.ChildNodes.OfType<XmlElement>())
            {
                Visit(child, item, slots, owner, field);
                string? customRoot = item.FullName switch
                {
                    "RimWorld.PawnGenOption" => "PawnKindDef",
                    "RimWorld.PrefabThingData" => "ThingDef",
                    "Verse.ThingDefCountClass" or "Verse.ThingDefCountRangeClass" => "ThingDef",
                    "RimWorld.BiomePlantRecord" => "ThingDef",
                    "RimWorld.BiomeAnimalRecord" => "PawnKindDef",
                    _ => null,
                };
                if (child.Name != "li" && customRoot is not null) { slots[child] = new Slot(ElementDefRoot: customRoot); }
            }

            return;
        }

        if (type is GenericInstanceType dictionary && dictionary.ElementType.FullName is "System.Collections.Generic.Dictionary`2" or "System.Collections.Generic.IDictionary`2")
        {
            TypeReference keyType = dictionary.GenericArguments[0];
            TypeReference valueType = dictionary.GenericArguments[1];
            string? keyRoot = CecilNames.TryResolve(keyType) is { } keyDefinition ? TypeUniverse.DatabaseRoot(keyDefinition) : null;
            foreach (XmlElement child in node.ChildNodes.OfType<XmlElement>())
            {
                if (child.Name == "li")
                {
                    if (child["key"] is { } key) { Visit(key, keyType, slots, null, null); }
                    if (child["value"] is { } value) { Visit(value, valueType, slots, null, null); }
                }
                else
                {
                    Visit(child, valueType, slots, null, null);
                    if (keyRoot is not null) { slots[child] = (slots.GetValueOrDefault(child) ?? new Slot()) with { ElementDefRoot = keyRoot }; }
                }
            }

            return;
        }

        if (definition is null || type.IsPrimitive || definition.IsEnum) { return; }
        foreach (XmlElement child in node.ChildNodes.OfType<XmlElement>())
        {
            if (Field(definition, child.Name) is not { } member) { continue; }
            Visit(child, Substitute(member.FieldType, type), slots, member.DeclaringType.FullName, member.Name);
        }
    }

    private static FieldDefinition? Field(TypeDefinition type, string name)
    {
        for (TypeDefinition? current = type; current is not null; current = CecilNames.TryResolve(current.BaseType))
        {
            FieldDefinition? field = current.Fields.FirstOrDefault(f => !f.IsStatic && (f.Name == name
                || f.CustomAttributes.Any(a => a.AttributeType.FullName == "Verse.LoadAliasAttribute" && a.ConstructorArguments.Any(v => v.Value is string alias && alias == name))));
            if (field is not null) { return field; }
        }

        return null;
    }

    private static TypeReference Substitute(TypeReference type, TypeReference owner)
    {
        if (type is GenericParameter parameter && owner is GenericInstanceType generic && parameter.Position < generic.GenericArguments.Count)
        { return generic.GenericArguments[parameter.Position]; }
        if (type is GenericInstanceType instance)
        {
            var result = new GenericInstanceType(instance.ElementType);
            foreach (TypeReference argument in instance.GenericArguments) { result.GenericArguments.Add(Substitute(argument, owner)); }
            return result;
        }

        return type;
    }
}
