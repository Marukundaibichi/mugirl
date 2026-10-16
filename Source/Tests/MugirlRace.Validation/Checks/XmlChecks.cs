using System.Reflection;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Reporting;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. 按 RimWorld 1.7 的加载方式检查 Mod 的 Def 与补丁 XML：每个 <c>Class=</c> 与 <c>Type</c> 值都能解析（到游戏、本程序集或依赖的
/// 1.6 DLL，如 HAR）并可赋给声明类型；每个元素对应一个字段；每个 <c>ParentName</c> 存在；每个补丁操作都能应用。
/// </summary>
internal static class XmlChecks
{
    // 值必须派生自已知基类的 Type 字段（字段本身只是 System.Type）。
    private static readonly (string Field, string Base)[] TypeFieldBases =
    [
        ("Verse.ThingDef.thingClass", "Verse.Thing"),
        ("Verse.CompProperties.compClass", "Verse.ThingComp"),
        ("Verse.HediffCompProperties.compClass", "Verse.HediffComp"),
        ("Verse.AbilityCompProperties.compClass", "RimWorld.AbilityComp"),
        ("Verse.HediffDef.hediffClass", "Verse.Hediff"),
        ("Verse.JobDef.driverClass", "Verse.AI.JobDriver"),
        ("Verse.VerbProperties.verbClass", "Verse.Verb"),
        ("Verse.GraphicData.graphicClass", "Verse.Graphic"),
        ("Verse.DamageDef.workerClass", "Verse.DamageWorker"),
        ("RimWorld.StatDef.workerClass", "RimWorld.StatWorker"),
        ("RimWorld.IncidentDef.workerClass", "RimWorld.IncidentWorker"),
        ("RimWorld.ThoughtDef.thoughtClass", "RimWorld.Thought"),
        ("RimWorld.ThoughtDef.workerClass", "RimWorld.ThoughtWorker"),
        ("RimWorld.AbilityDef.abilityClass", "RimWorld.Ability"),
        ("Verse.PawnRenderNodeProperties.nodeClass", "Verse.PawnRenderNode"),
        ("Verse.PawnRenderNodeProperties.workerClass", "Verse.PawnRenderNodeWorker"),
        ("Verse.RecipeDef.workerClass", "Verse.RecipeWorker"),
        ("RimWorld.WorkGiverDef.giverClass", "RimWorld.WorkGiver"),
        ("RimWorld.PawnColumnDef.workerClass", "RimWorld.PawnColumnWorker"),
        ("Verse.BuildableDef.placeWorkers", "Verse.PlaceWorker"),
        ("Verse.ThingDef.inspectorTabs", "Verse.InspectTabBase"),
        ("Verse.MentalStateDef.stateClass", "Verse.AI.MentalState"),
        ("Verse.GameConditionDef.conditionClass", "RimWorld.GameCondition"),
        ("RimWorld.WorldObjectDef.worldObjectClass", "RimWorld.Planet.WorldObject"),
    ];

    public static void Run(Report report, ContentModel content)
    {
        report.Run("C.xml-types", "Def XML: Class and Type values resolve and fit, elements map to fields", recorder => CheckDefs(recorder, content));
        report.Run("C.patches", "Patch XML: every operation applies and inserts well-typed XML", recorder => CheckPatches(recorder, content));
    }

    private static void CheckDefs(CheckRecorder recorder, ContentModel content)
    {
        string root = content.Catalog.UnderTest.RootDir;
        foreach ((ContentPack pack, string file, string problem) in content.Corpus.Problems.Where(p => ContentModel.IsOurs(p.Pack)))
        {
            recorder.Fail(XmlPaths.File(root, file), problem);
        }

        Dictionary<string, Type> bases = ResolveTypeFieldBases(recorder, content);
        XmlTypeWalker walker = CreateWalker(recorder, content, bases);
        int defs = 0;
        foreach (XmlElement node in content.Corpus.Defs)
        {
            NodeSource? source = content.Corpus.SourceOf(node);
            if (!ContentModel.IsOurs(source?.Pack) || source!.InsertedByPatch || !DefIndex.MayRequireSatisfied(node, content.Catalog.Full))
            {
                continue;
            }

            defs++;
            string parentName = node.GetAttribute("ParentName");
            if (parentName.Length > 0 && !content.Inheritance.IsRegistered(parentName))
            {
                recorder.Fail(XmlPaths.File(root, source.File) + " " + XmlPaths.Of(node), $"ParentName '{parentName}' is not defined by any loaded mod (the game logs an XML error and the def loses its inherited content).");
            }

            List<Type> inheritors = node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase)
                ? content.Inheritance.Descendants(node).Select(walker.DefTypeOf).OfType<Type>().Distinct().ToList()
                : [];
            walker.WalkDef(node, inheritors);
        }

        recorder.Summary = $"{defs} defs of the mod checked";
    }

    private static void CheckPatches(CheckRecorder recorder, ContentModel content)
    {
        string root = content.Catalog.UnderTest.RootDir;
        int operations = 0;
        foreach (PatchResult result in content.Patches.Results)
        {
            if (ContentModel.IsOurs(result.Pack))
            {
                operations++;
                if (!result.Succeeded || result.Problem is not null)
                {
                    recorder.Fail(XmlPaths.File(root, result.File) + " " + result.Operation,
                        (result.Succeeded ? "" : "The game would log \"Patch operation failed\". ") + (result.Problem ?? "No node matched or a step returned false."));
                }
            }
            else if (!result.Succeeded)
            {
                recorder.Info(result.Pack.PackageId + " " + result.Operation, "A dependency's patch operation does not apply in this setup" + (result.Problem is null ? "." : ": " + result.Problem));
            }
        }

        // Mod 的补丁插入的 XML，在它最终所在的位置上检查。解析不到的 Class/Type 字符串由下面按补丁文件的检查报告一次，
        // 那一遍也覆盖从未插入的值。
        Dictionary<string, Type> bases = ResolveTypeFieldBases(null, content);
        XmlTypeWalker walker = CreateWalker(recorder, content, bases);
        walker.UnresolvedType = (_, _) => { };
        int inserted = 0;
        foreach (XmlElement node in content.Corpus.InsertedBy(content.Catalog.UnderTest))
        {
            inserted++;
            if (node.ParentNode == content.Corpus.Root)
            {
                walker.WalkDef(node, []);
                continue;
            }

            (Type? declared, FieldInfo? field) = walker.DeclaredTypeOf(node, content.Corpus.Root);
            if (declared is null)
            {
                recorder.Info(XmlPaths.Of(node), "Inserted where the declared type cannot be determined; only its Class strings are checked.");
                continue;
            }

            walker.Walk(node, declared, field);
        }

        // 补丁文件里的每个 Class 字符串都要能解析，不论其操作是否插入了内容。
        foreach ((ContentPack pack, LoadedFile file) in content.Corpus.PatchFiles.Where(p => ContentModel.IsOurs(p.Pack)))
        {
            if (content.Corpus.TryParse(pack, file.FullPath)?.DocumentElement is not { } patch)
            {
                continue;
            }

            string where = XmlPaths.File(root, file.FullPath);
            foreach (XmlElement operation in patch.SelectNodes("//*[@Class]")!.OfType<XmlElement>().Where(IsOperationNode))
            {
                Type? type = content.Types.GetTypeInAnyAssembly(operation.GetAttribute("Class"), "Verse");
                if (type is null || !typeof(PatchOperation).IsAssignableFrom(type))
                {
                    recorder.Fail(where + " Class=" + operation.GetAttribute("Class"), "Patch operation Class does not resolve to a PatchOperation.");
                }
            }

            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement value in patch.SelectNodes("//value")!.OfType<XmlElement>())
            {
                CheckClassStrings(recorder, content, value, where, reported);
            }
        }

        recorder.Summary = $"{operations} operations and {inserted} inserted nodes checked";
    }

    private static XmlTypeWalker CreateWalker(CheckRecorder recorder, ContentModel content, Dictionary<string, Type> bases)
    {
        string root = content.Catalog.UnderTest.RootDir;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        return new XmlTypeWalker(content.Types, content.Catalog.Full)
        {
            Problem = (node, message) =>
            {
                string subject = Where(content, root, node);
                if (reported.Add(subject + message))
                {
                    recorder.Fail(subject, message);
                }
            },
            TypeValue = (node, field, type) =>
            {
                if (field is not null && bases.TryGetValue(field.DeclaringType!.FullName + "." + field.Name, out Type? expected) && !expected.IsAssignableFrom(type))
                {
                    recorder.Fail(Where(content, root, node), $"{type.FullName} is not a {expected.FullName}; the game cannot use it as {field.Name}.");
                }
            },
        };
    }

    private static string Where(ContentModel content, string root, XmlElement node)
    {
        NodeSource? source = content.Corpus.SourceOf(node);
        return (source is null ? "" : XmlPaths.File(root, source.File) + " ") + XmlPaths.Of(node);
    }

    // 补丁 value 中的 Class 特性与 *Class 值必须能解析（插入 XML 的类型检查需要落点，这一部分不需要）。
    private static void CheckClassStrings(CheckRecorder recorder, ContentModel content, XmlElement scope, string where, HashSet<string> reported)
    {
        foreach (XmlElement element in scope.SelectNodes("descendant-or-self::*")!.OfType<XmlElement>())
        {
            string className = element.GetAttribute("Class");
            if (className.Length > 0 && content.Types.GetTypeInAnyAssembly(className) is null && reported.Add("Class=" + className))
            {
                recorder.Fail(where + " Class=" + className, "Class does not resolve to any type; RimWorld 1.7's loader would drop the patched Def.");
            }

            if (element.Name.EndsWith("Class", StringComparison.Ordinal) && element.ChildNodes.Count == 1 && element.FirstChild is XmlText text
                && text.Value!.Trim() is { Length: > 0 } value && value is not ("null" or "Null")
                && content.Types.GetTypeInAnyAssembly(value) is null && reported.Add(element.Name + "=" + value))
            {
                recorder.Fail(where + " " + element.Name + "=" + value, "Type value does not resolve to any type.");
            }
        }
    }

    private static bool IsOperationNode(XmlElement element)
        => element.Name == "Operation"
            || element.Name is "match" or "nomatch"
            || (element.Name == "li" && element.ParentNode is XmlElement { Name: "operations" });

    private static Dictionary<string, Type> ResolveTypeFieldBases(CheckRecorder? recorder, ContentModel content)
    {
        var result = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach ((string field, string baseName) in TypeFieldBases)
        {
            int dot = field.LastIndexOf('.');
            Type? owner = content.Types.GetTypeInAnyAssembly(field[..dot]);
            Type? baseType = content.Types.GetTypeInAnyAssembly(baseName);
            FieldInfo? info = owner?.GetField(field[(dot + 1)..], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info is null || baseType is null || info.DeclaringType!.FullName + "." + info.Name != field)
            {
                recorder?.Warn("table: " + field, $"The validator's expected-base entry ({field} → {baseName}) no longer matches the game; update XmlChecks.TypeFieldBases.");
                continue;
            }

            result[field] = baseType;
        }

        return result;
    }
}
