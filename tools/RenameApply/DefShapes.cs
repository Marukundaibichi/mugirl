using System.Xml;

namespace RenameApply;

// Which engine-derived names exist for a ThingDef (design 5.4, decisions 2 "Load IDs"): only those its shape implies.
//   Make_       recipeMaker present                      (RecipeDefGenerator.ImpliedRecipeDefs)
//   Administer_ ingestible/drugCategory other than None    (RecipeDefGenerator.DrugAdministerDefs: ThingDef.IsDrug)
//   Corpse_     race present                              (ThingDefGenerator_Corpses)
//   Meat_       race present, no useMeatFrom, not a mechanoid (ThingDefGenerator_Meat)
//   Blueprint_, Frame_   designationCategory present       (ThingDefGenerator_Buildings: BuildableByPlayer)
//   Blueprint_Install_   minifiedDef present              (ThingDefGenerator_Buildings: Minifiable)
// The fields are resolved through ParentName inheritance over the mod's own Defs and the game's Data/*/Defs (abstract parents
// such as ApparelMakeableBase carry recipeMaker). IsNull="True" and Inherit="False" without content count as absent.
public sealed class DefShapes
{
    private readonly Dictionary<string, XmlElement> _byName = new(StringComparer.Ordinal);       // Name= (abstract or not)
    private readonly Dictionary<string, XmlElement> _thingByDefName = new(StringComparer.Ordinal);    // ThingDef-rooted elements only
    public bool Available { get; private set; }
    public string? Problem { get; private set; }

    public static DefShapes Load(RepoPaths repo, string? gameDir)
    {
        var s = new DefShapes();
        try
        {
            var files = new List<string>();
            if (gameDir != null && Directory.Exists(Path.Combine(gameDir, "Data")))
            {
                foreach (string pack in Directory.EnumerateDirectories(Path.Combine(gameDir, "Data")).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string defs = Path.Combine(pack, "Defs");
                    if (Directory.Exists(defs))
                    {
                        files.AddRange(Directory.EnumerateFiles(defs, "*.xml", SearchOption.AllDirectories));
                    }
                }
            }
            else
            {
                s.Problem = $"game Data folder not found ({gameDir ?? "RimWorldDir not given"}); vanilla parents unresolved";
            }
            int vanillaCount = files.Count;
            files.AddRange(Directory.EnumerateFiles(repo.Full("1.7"), "*.xml", SearchOption.AllDirectories)
                .Where(f => repo.Rel(f).Contains("/Defs/", StringComparison.OrdinalIgnoreCase)));
            for (int i = 0; i < files.Count; i++)
            {
                var doc = new XmlDocument();
                try
                {
                    doc.Load(files[i]);
                }
                catch (XmlException)
                {
                    continue;
                }
                if (doc.DocumentElement == null)
                {
                    continue;
                }
                bool own = i >= vanillaCount;
                foreach (XmlElement e in doc.DocumentElement.ChildNodes.OfType<XmlElement>())
                {
                    string? name = e.GetAttribute("Name") is { Length: > 0 } n ? n : null;
                    if (name != null && (own || !s._byName.ContainsKey(name)))
                    {
                        s._byName[name] = e;   // the mod's own Name wins over a vanilla one (same as load order)
                    }
                    if (own && IsThingDefElement(e) && e["defName"]?.InnerText.Trim() is { Length: > 0 } dn)
                    {
                        s._thingByDefName.TryAdd(dn, e);
                    }
                }
            }
            s.Available = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            s.Problem = ex.Message;
        }
        return s;
    }

    private static bool IsThingDefElement(XmlElement e) =>
        e.Name == "ThingDef" || e.Name.EndsWith(".ThingDef_AlienRace", StringComparison.Ordinal) || e.Name == "Mugirl.SlaveApparelDef"
        || e.GetAttribute("Class") is { Length: > 0 } c && (c.EndsWith("ThingDef_AlienRace", StringComparison.Ordinal) || c == "Mugirl.SlaveApparelDef");

    // Nearest declaration of a child path along the ParentName chain: true = present, false = absent/nulled, null = unknown.
    private bool? Field(XmlElement e, string path, Func<XmlElement, bool>? valueOk = null, int depth = 0)
    {
        if (depth > 32)
        {
            return null;
        }
        XmlElement? cur = e;
        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length && cur != null; i++)
        {
            XmlElement? next = cur[parts[i]];
            if (next == null)
            {
                cur = null;
                break;
            }
            if (next.GetAttribute("IsNull") == "True")
            {
                return false;
            }
            cur = next;
        }
        if (cur != null)
        {
            if (cur.GetAttribute("Inherit") == "False" && !cur.HasChildNodes)
            {
                return false;
            }
            return valueOk == null || valueOk(cur);
        }
        string parent = e.GetAttribute("ParentName");
        if (parent.Length == 0)
        {
            return false;
        }
        return _byName.TryGetValue(parent, out XmlElement? p) ? Field(p, path, valueOk, depth + 1) : null;
    }

    // Prefixes the engine derives for this ThingDef; null when the shape is unknown (def not found, parent not resolvable).
    public IReadOnlyList<string>? DerivedPrefixes(string defName)
    {
        if (!Available || !_thingByDefName.TryGetValue(defName, out XmlElement? e))
        {
            return null;
        }
        var result = new List<string>();
        bool? recipe = Field(e, "recipeMaker");
        bool? drug = Field(e, "ingestible/drugCategory", x => x.InnerText.Trim() != "None");
        bool? race = Field(e, "race");
        bool? meatFrom = Field(e, "race/useMeatFrom");
        bool? mech = Field(e, "race/fleshType", x => x.InnerText.Trim() == "Mechanoid");
        bool? buildable = Field(e, "designationCategory");
        bool? minifiable = Field(e, "minifiedDef");
        if (new[] { recipe, drug, race, buildable, minifiable }.Any(v => v == null))
        {
            return null;
        }
        if (buildable == true)
        {
            result.Add("Blueprint_");
            result.Add("Frame_");
        }
        if (minifiable == true)
        {
            result.Add("Blueprint_Install_");
        }
        if (race == true)
        {
            result.Add("Corpse_");
            if (meatFrom != true && mech != true)
            {
                result.Add("Meat_");
            }
        }
        if (recipe == true)
        {
            result.Add("Make_");
        }
        if (drug == true)
        {
            result.Add("Administer_");
        }
        return result;
    }
}
