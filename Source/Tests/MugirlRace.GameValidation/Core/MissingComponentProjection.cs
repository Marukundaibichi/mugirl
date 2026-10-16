using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>只预测四种完全缺失组件的已证明构造/LoadedGame结果；不调用业务代码，不读取重存档。</summary>
internal sealed class MissingComponentProjection
{
    internal const string Network = "Mugirl.Features.Corporate.CorporateNetwork";
    internal const string Introduction = "Mugirl.Features.Corporate.CorporateIntroduction";
    internal const string Migration = "Mugirl.Features.Incidents.MapComponent_Migration";
    internal const string Punisher = "Mugirl.Features.Incidents.MapComponent_Punisher";
    internal const string Hub = "Mugirl.Core.GameComponent_Mugirl";
    private const string Story = "Mugirl.Features.Incidents.GameComponent_Storyline";
    private const string CourierPart = "Mugirl.Features.Incidents.QuestPart_SpawnCourier";
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Guid EngineMvid = ReviewedEngine.Mvid;
    private readonly Assembly production;
    private readonly Type[] gameOrder;
    private readonly Type[] mapOrder;
    private readonly QuestScriptDef courierRoot;
    private readonly CurrentScribeRoots calls;
    public JsonArray Actions { get; } = [];
    public JsonArray Readers { get; } = [];
    public JsonArray OrderEvidence { get; } = [];

    // 构造器的字段初值也属于契约；这些指纹由独立代码/IL审查及真实Scribe写出交叉核验。
    private static readonly (string Type, string Method, string Hash)[] Contracts =
    [
        (Network, ".ctor", "0EA1FFFFBCE1538FFAF17BFB386398F14E1B63050D41D2371070230966E50640"), (Network, "ExposeData", "F677D5CCD6C7DFE9741359C15C0F518FAF9EA4B3C44EF0E198012FE82AE3A343"), (Network, "LoadedGame", "16F42705544B4AA8965B150B0B64EFEAB7D9E8D94E650D6A95036D8F210A08B8"),
        (Network, "PrepareOrderCatalog", "7EA50EEC059D7C6945D9A953F8CDFEBB17A62BC7C98DEC4D31A6C920068CBF58"), (Network, "RefreshStockSamples", "50A42C07586D086D0AAB4C371996ECAEB796864DDC61A2303BA7709908004A9C"), (Network, "RefreshPeopleOffers", "34B02071D6A4EA1268874BD093B75BD3556A95DF706E3F2B19C2384BB5DE29F3"),
        (Network, "TradeExposeData", "7AE31FE6FC4CA24939D4218AF1B71A76E37A089F9598D0ADDE3719073E143FB4"), (Network, "FinanceExposeData", "D9A3952892F671B87B5E49C7B0A38EFBB2D59767A41616CC09764B6F6B52377F"), (Network, "PeopleExposeData", "B7FAFB92314FF4ADECFC9059E8EA327939F91B4EB68859415A70AB809A322ED7"),
        (Network, "QuestsExposeData", "370980D4A46B319441E8E6380136D52D26F118CAB9EFBDC77EECCE1DCAEF0FC8"), (Network, "ServicesExposeData", "08FA2BA2FF0730024E7704E32DC1804B9143E3088969A78F69B5F2F8C607D4F0"),
        ("Mugirl.Features.Corporate.CorporateHistory", ".ctor", "BCD5D1E8117512BD0F5193689E30114508C70E8BB40CB30C8C71EF5A89968130"), ("Mugirl.Features.Corporate.CorporateHistory", "ExposeData", "9804D4AFF43C6AA773EF8ACE8E135548B3FA08DC94E478AD894134294648F10A"),
        ("Mugirl.Features.Corporate.CorporateTradeDesk", ".ctor", "21A6BE24F20E72834D64705B7C80E8BEBE8799D58BE292E90A1EFBDF4C3E3E8A"), ("Mugirl.Features.Corporate.CorporateTradeDesk", "ExposeData", "46AE7B6E9C024745481CE181C464E198977A33EE1E9A892ABCB6CB62C6073AB0"),
        ("Mugirl.Features.Corporate.CorporateLoanAccount", ".ctor", "7570F8E73AFAD4D838674936681068B801A21B1DB4803C9E02A9934ABE105B43"), ("Mugirl.Features.Corporate.CorporateLoanAccount", "ExposeData", "9952EF0A535886F8760195ED11E226FCBA96DCB7DEC767F29885110648BD5C3D"),
        ("Mugirl.Features.Corporate.CorporatePeopleDesk", ".ctor", "4A42EE02652297BB89AC4F2D0C6E0A1FC1AFF1849044389E9085C6BFC340E1E5"), ("Mugirl.Features.Corporate.CorporatePeopleDesk", "ExposeData", "45A0EA7B39DDC171E896BD0474AA399E5FF67481636EAF3685F068FFDACCEDB9"),
        ("Mugirl.Features.Corporate.CorporateMissionBoard", ".ctor", "6FE4C59470DE3A4682C695641E6800CB6B6ABBE4AED1B2D072A2A18B84CFF400"), ("Mugirl.Features.Corporate.CorporateMissionBoard", "ExposeData", "89BB224127BF7DA6A40353CCBB386D13A0823A41211C44D0A905F9E9E1E355DE"),
        ("Mugirl.Features.Corporate.CorporateClientAccount", ".ctor", "15FE4AF699D30F0481356F42211FC40144F302975E35F641C536933113906642"), ("Mugirl.Features.Corporate.CorporateClientAccount", "ExposeData", "D1AA290CB54B42F61ABFF30DA8C2617BFC0B7125299C9F3188C8F40D71038756"),
        (Introduction, ".ctor", "33673C8CC1A1A950EFDAFCB5108D0779EAD83F38B5860EE36560479A2A6E37D3"), (Introduction, "ExposeData", "0485CD53918A3C90C11CD162651E435F6CBCE208BE02F3A615D6F0B85F529B36"), (Introduction, "LoadedGame", "6558E2B80383F4F64BD41EB26723CB108D8EFD43261125D6948352D142FCB0E5"),
        (Introduction, "RecoverCourierHistory", "38E6728CEC466A3CADC4BC7970B35FE80006DB0112764BE4D2C0BE26765A8611"), (Introduction, "RememberCourier", "34D2E46DA08795B4863494AE92590338AE49848FF663EF739A29358D95D081FF"),
        (Introduction, "CanArrangeIndependentContact", "9CBBD05E9D465213FFE7D14B68200B8BEC5FF358D72F6357CB9840451DDAF3EB"), (Introduction, "UpdateIndependentContact", "D59E83770A5B70C54FACC718B97A7A93C5D5171ECEC2A65C03B8018E6C19BBC9"),
        ("Mugirl.Features.Corporate.CorporateContactState", ".ctor", "B8DC700BEA763C782A8073079E898FEA8F53677F4F400FDE778A071B55B4F65C"), ("Mugirl.Features.Corporate.CorporateContactState", "ExposeData", "E4134CB8F26AB9EB6FD0C4E67F800F120381E58580BD22051AB1BD74DF6AD942"),
        ("Mugirl.Features.Corporate.CorporateStateScribe", "Look", "A588920045DEC595C6CF7B4F1BAA4EE73BDC6D4AE1252ED2EF23E99BB3FA189D"),
        (Story, "ExposeData", "B7729ECA54898CBE260AA8E42E36B4BDD027BE0F4D67AD1EE2493C31A59D634D"), (Story, "LoadedGame", "FAD0282B0A6CF767F32AD9FB15B150A0368C8804AC4A35C82D65F30CFDD3F968"), (Story, "FinalizeInit", "A3DFE26DEB5D3D9C208ECCD5A127DAFBA85DF40548115E145A8A8D1EB9CC042C"),
        (Story, ".ctor", "E41B662A892A1C66082E9DE5C0A3DBBE0EEA6D4A5BC9D1C6C28410F1E70C8B35"),
        (Story, "get_OpeningCrashStarted", "92CF90A1F4F0E7A7788DFCCE0320DC60CD1312EB05E477F3F7DE73266832F8C5"), (Story, "get_CourierTriggered", "CBCF3651AF8A96A70404F21E2B2673E86E01A2E0C209E00F193E780F96567132"), (Story, "get_CourierQuestStarted", "22723D84E784D664E67F5EE93FB01383E249C3B65405AE7A0AFAAEA9353D49BC"),
        ("Mugirl.Features.Incidents.OpeningCrashState", ".ctor", "DD2070FC5BF82054865FC8C273CC53EE52921EE850759E7B25D5EE876521DE22"), ("Mugirl.Features.Incidents.OpeningCrashState", "ExposeData", "F65A189C3DF9FBC360D72ED0000CCF7F04F913C5D5F2433A39E498CCEF4DF954"),
        ("Mugirl.Features.Incidents.CourierContactState", ".ctor", "F8B258DCE579B767ECBD35C62A88B9B0DD9F4C3D53B823B82C212B6D0E2BF270"), ("Mugirl.Features.Incidents.CourierContactState", "ExposeData", "74243AC48DB3D28244E1FD2A2AFEB86B585A140F61E0D774297631F5A2C1A49F"),
        (CourierPart, "ExposeData", "2A939002A65E70CF781F9779583BB122F56C7C732D9295C09D8A81C7A61BD91E"), ("Mugirl.Core.GameUtility", "GameComponent", "693B010FAC099C6900A1EDF592FE2783D2A91DD2BCB853C8DF993361767FF813"),
        ("Mugirl.Core.GameUtility", "TryGetQuestsListForReading", "EF2425AE81E736E29D37AFEE7387E18CFE368536F3B297D828347E3957F3A92E"),
        (Migration, ".ctor", "6508008CCDB8CA2DF88D5F8930AD423D58F4CAA144DFCA8168713E69B6540DB0"), (Migration, "ExposeData", "C74C7381B5F1537A000AE05F47857FF5AEAD25301D40D1FDE44E49BE60F8295A"),
        ("Mugirl.Features.Incidents.MigrationState", ".ctor", "370AEDEEBA2960EE98DC57F4BEBC73323AEEDDFD8542558296C5431036ED487E"), ("Mugirl.Features.Incidents.MigrationState", "ExposeData", "5B3733558B5AD617ADEA795146EFF8D28249E77A9B3683F67743FFD3D3E84B59"),
        ("Mugirl.Features.Incidents.EventStateScribe", "Look", "218126EFFF8F296ABEC02D209C26F28919A286C00C2A0C0EA0B4D2B10D5BF583"),
        (Punisher, ".ctor", "A4FF7AE1157FD97687A1439E5133FE5D8960F006CA674ED45616264BFDE15F9E"), (Punisher, "ExposeData", "388953A4CF3F31BA60D173F7701B7582B393C09DC654010837B0618329BB9B7D"),
    ];

    private MissingComponentProjection(Assembly production, Type[] gameOrder, Type[] mapOrder)
    {
        this.production = production;
        this.gameOrder = gameOrder;
        this.mapOrder = mapOrder;
        courierRoot = ReadCourierRoot(production);
        calls = new CurrentScribeRoots(production);
    }

    public static MissingComponentProjection Capture(Assembly production)
    {
        var projection = new MissingComponentProjection(production, CachedOrder(typeof(GameComponent)), CachedOrder(typeof(MapComponent)));
        projection.VerifyEngine();
        projection.Readers.Add(new JsonObject { ["type"] = "Mugirl.Features.Incidents.IncidentsDefOf", ["field"] = "Mugirl_CourierContact",
            ["defType"] = typeof(QuestScriptDef).FullName, ["defName"] = projection.courierRoot.defName,
            ["sameUniqueDatabaseObject"] = true, ["source"] = "actual initialized static field and current DefDatabase; no after document" });
        return projection;
    }

    public void Apply(XDocument expected, XDocument original)
    {
        VerifyEngine();
        if (!ReferenceEquals(courierRoot, ReadCourierRoot(production)))
        { throw new InvalidOperationException("The actual CourierContact DefOf binding changed after capture."); }
        if (!gameOrder.SequenceEqual(CachedOrder(typeof(GameComponent))) || !mapOrder.SequenceEqual(CachedOrder(typeof(MapComponent))))
        { throw new InvalidOperationException("The actual FillComponents type cache changed after capture."); }

        // 原子地构造独立期望；任何未知来源都不留下已经补了一半的模板。
        var work = new XDocument(expected);
        var planned = new JsonArray();
        var order = new JsonArray();
        XElement game = Game(work), source = Game(original);
        XElement components = Required(game, "components"), sourceComponents = Required(source, "components");
        Fill(components, sourceComponents, gameOrder, [Network, Introduction], game, source, planned, order);
        XElement? maps = Single(game, "maps"), sourceMaps = Single(source, "maps");
        if (maps is not null)
        {
            ValidateList(maps); if (sourceMaps is null) { throw new InvalidOperationException("Missing source maps cannot prove component owners."); }
            ValidateList(sourceMaps);
            var byId = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (XElement map in sourceMaps.Elements())
            {
                string id = MapId(map);
                if (!byId.TryAdd(id, map)) { throw new InvalidOperationException("Duplicate source map id."); }
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement map in maps.Elements())
            {
                string id = MapId(map);
                if (!ids.Add(id) || !byId.TryGetValue(id, out XElement? sourceMap)) { throw new InvalidOperationException("The projected map has no unique source owner."); }
                Fill(Required(map, "components"), Required(sourceMap, "components"), mapOrder, [Migration, Punisher], game, source, planned, order);
            }
        }
        expected.Root!.ReplaceWith(work.Root!);
        foreach (JsonNode? action in planned.ToArray()) { Actions.Add(action!.DeepClone()); }
        foreach (JsonNode? evidence in order.ToArray()) { OrderEvidence.Add(evidence!.DeepClone()); }
    }

    private void Fill(XElement target, XElement original, Type[] enumeration, string[] supported, XElement game, XElement source,
        JsonArray planned, JsonArray order)
    {
        ValidateList(target); ValidateList(original);
        XElement[] entries = [.. target.Elements()];
        foreach (XElement entry in original.Elements()) { ComponentClass(entry); }
        var found = new Dictionary<Type, XElement>();
        foreach (XElement entry in entries)
        {
            string name = ComponentClass(entry);
            Type[] candidates = [.. enumeration.Where(t => t.FullName == name || t.Name == name)];
            if (candidates.Length != 1 || !found.TryAdd(candidates[0], entry))
            { throw new InvalidOperationException("An unknown or duplicate component Class cannot be projected: " + name); }
        }
        bool originalHub = original.Elements().Any(e => ComponentClass(e) is Hub or "GameComponent_Mugirl");
        XElement? syntheticHub = target.Parent == game && !originalHub ? entries.SingleOrDefault(e => ComponentClass(e) == Hub) : null;
        if (syntheticHub is not null) { found.Remove(production.GetType(Hub, throwOnError: true)!); }
        var appended = new List<XElement>();
        foreach (Type type in enumeration)
        {
            // 原版 GetComponent(Type) 使用 IsInstanceOfType；已有派生实例也满足其基类组件。
            if (typeof(CustomMapComponent).IsAssignableFrom(type) || found.Keys.Any(type.IsAssignableFrom)) { continue; }
            string name = type.FullName!;
            if (name == Hub && syntheticHub is not null) { appended.Add(syntheticHub); found.Add(type, syntheticHub); continue; }
            if (!supported.Contains(name, StringComparer.Ordinal))
            { throw new InvalidOperationException("FillComponents would add an unreviewed type: " + name); }
            if (HasSourceType(original, type, enumeration))
            { throw new InvalidOperationException("An existing source component cannot be replaced by an empty template: " + name); }
            VerifyContracts(name);
            if (name == Introduction) { ProveFreshIntroduction(game, source); }
            XElement created = Template(name);
            appended.Add(created);
            found.Add(type, created);
            planned.Add(new JsonObject { ["rule"] = "MissingComponent." + name, ["owner"] = Path(target), ["class"] = name,
                ["after"] = created.ToString(SaveOptions.DisableFormatting), ["evidence"] = "exact absent owner; verified constructor, source-dependent LoadedGame path and current writer; no after input" });
        }
        // 只从预览人工新建的Hub移走；原source已存在Hub保持原序和内容。
        if (syntheticHub is not null && !ReferenceEquals(entries.LastOrDefault(), syntheticHub))
        { throw new InvalidOperationException("The synthetic preview hub was not the final appended diagnostic component."); }
        if (appended.Count == 0 || appended.Count == 1 && ReferenceEquals(appended[0], syntheticHub) && ReferenceEquals(entries.LastOrDefault(), syntheticHub)) { return; }
        syntheticHub?.Remove();
        target.Add(appended);
        order.Add(new JsonObject { ["owner"] = Path(target), ["cachedTypes"] = new JsonArray(enumeration.Select(t => (JsonNode?)JsonValue.Create(t.FullName)).ToArray()),
            ["existing"] = new JsonArray(entries.Where(e => !ReferenceEquals(e, syntheticHub)).Select(e => (JsonNode?)JsonValue.Create(ComponentClass(e))).ToArray()),
            ["appended"] = new JsonArray(appended.Select(e => (JsonNode?)JsonValue.Create(ComponentClass(e))).ToArray()), ["syntheticHub"] = syntheticHub is not null });
    }

    private static bool HasSourceType(XElement source, Type target, Type[] enumeration)
    {
        foreach (XElement entry in source.Elements())
        {
            string name = ComponentClass(entry);
            Type[] candidates = [.. enumeration.Where(t => t.FullName == name || t.Name == name)];
            if (candidates.Length > 1) { throw new InvalidOperationException("An ambiguous source component short name cannot prove absence: " + name); }
            if (candidates.Length == 1 && target.IsAssignableFrom(candidates[0]) || IsKnownName(name, target.FullName!)) { return true; }
        }
        return false;
    }

    private static void ProveFreshIntroduction(XElement canonical, XElement original)
    {
        foreach (XElement game in new[] { original, canonical })
        {
            XElement[] stories = [.. Required(game, "components").Elements("li").Where(e => IsKnownName(ComponentClass(e), Story))];
            if (stories.Length != 1) { throw new InvalidOperationException("Missing introduction requires one proven Storyline owner."); }
            XElement story = stories[0];
            bool legacy = ComponentClass(story) == "Mugirl.MugirlStoryState";
            XElement? opening = legacy ? story : Single(story, "opening");
            XElement? courier = legacy ? story : Single(story, "courier");
            if (!Bool(opening, legacy ? "openingCrashStarted" : "started", false)
                || Bool(courier, legacy ? "courierRaidTriggered" : "triggered", false))
            { throw new InvalidOperationException("Storyline state can schedule nondefault corporate contact."); }
            if (!legacy)
            {
                RequireImplicit(opening!, "Mugirl.Features.Incidents.OpeningCrashState");
                if (courier is not null) { RequireImplicit(courier, "Mugirl.Features.Incidents.CourierContactState"); }
            }
            XElement manager = Required(game, "questManager");
            RequireImplicit(manager, typeof(QuestManager).FullName!);
            XElement quests = Required(manager, "quests"); ValidateList(quests);
            XElement? last = null;
            foreach (XElement quest in quests.Elements())
            {
                RequireImplicit(quest, typeof(Quest).FullName!);
                string? root = Scalar(quest, "root");
                if (root is "Mugirl_CourierRaid" or "Mugirl_CourierContact") { last = quest; }
            }
            if (last is null) { continue; }
            string? acceptance = Scalar(last, "acceptanceTick");
            if (Bool(last, "ended", false) || acceptance is null
                || !int.TryParse(acceptance, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick) || tick < 0)
            { throw new InvalidOperationException("A courier quest has no proven Ongoing state."); }
            XElement parts = Required(last, "parts"); ValidateList(parts);
            XElement? courierPart = null;
            foreach (XElement part in parts.Elements())
            {
                string? type = part.Attribute("Class")?.Value;
                if (type is null || part.Attributes().Count() != 1) { throw new InvalidOperationException("An unknown quest part cannot prove courier history."); }
                if (IsKnownName(type, CourierPart))
                {
                    if (courierPart is not null) { throw new InvalidOperationException("Multiple courier parts are outside the reviewed default proof."); }
                    courierPart = part;
                }
                else if (type is not ("QuestPart_Delay" or "RimWorld.QuestPart_Delay"))
                { throw new InvalidOperationException("An unreviewed quest part cannot prove unchanged courier history."); }
            }
            if (courierPart is null || Scalar(courierPart, "courier") != "null")
            { throw new InvalidOperationException("A courier quest does not prove the first SpawnCourier has a null Pawn."); }
        }
    }

    private static XElement Template(string type) => type switch
    {
        Network => XElement.Parse("<li Class='" + Network + "'><vault><innerList/></vault><history><entries/></history><trade><stock/><orders/></trade><loan><creditor>null</creditor><collateral/></loan><people><offers/><awaitingWorld/></people><missionBoard><missions/><retainedInvestor>null</retainedInvestor></missionBoard><services><creditedPawnIds/></services></li>"),
        Introduction => XElement.Parse("<li Class='" + Introduction + "'><contact><attached>True</attached><retrofitted>True</retrofitted><courierPawn>null</courierPawn><representativePawn>null</representativePawn><visitMap>null</visitMap><quest>null</quest></contact><gifts><innerList/></gifts></li>"),
        Migration => XElement.Parse("<li Class='" + Migration + "'><migration><members/><playerKillIds/></migration><initialization IsNull='True'/></li>"),
        Punisher => XElement.Parse("<li Class='" + Punisher + "'><activePunishers/><initialization IsNull='True'/><heldLoot><innerList/></heldLoot><lootBursts/></li>"),
        _ => throw new InvalidOperationException("No reviewed component template exists."),
    };

    private void VerifyContracts(string added)
    {
        foreach ((string type, string name, string hash) in Contracts.Where(c => added == Network ? c.Type.Contains("Corporate", StringComparison.Ordinal) && c.Type != Introduction && c.Type != "Mugirl.Features.Corporate.CorporateContactState"
            : added == Introduction ? c.Type == Introduction || c.Type.Contains("ContactState", StringComparison.Ordinal) || c.Type == Story || c.Type.Contains("OpeningCrashState", StringComparison.Ordinal)
                || c.Type.Contains("StateScribe", StringComparison.Ordinal) || c.Type == CourierPart || c.Type == "Mugirl.Core.GameUtility"
            : c.Type == added || c.Type == "Mugirl.Features.Incidents.EventStateScribe" || added == Migration && c.Type == "Mugirl.Features.Incidents.MigrationState"))
        {
            Type declaring = production.GetType(type, throwOnError: true)!;
            MethodBase method = name == ".ctor"
                ? declaring.GetConstructor(Members, binder: null, [typeof(Game)], modifiers: null) ?? declaring.GetConstructors(Members & ~BindingFlags.Static).Single()
                : declaring.GetMethod(name, Members)!;
            RequireReviewedPatchChain(method);
            foreach (MethodBase callee in calls.DirectInvocations(method))
            {
                RequireReviewedPatchChain(callee);
                if (callee.IsGenericMethod) { RequireReviewedPatchChain(((MethodInfo)callee).GetGenericMethodDefinition()); }
            }
            if (ObservationPatchEvidence.SerializationFingerprint(method) != hash)
            { throw new InvalidOperationException("The reviewed missing-component contract changed: " + type + "." + name); }
            if (!Readers.OfType<JsonObject>().Any(r => (string?)r["type"] == type && (string?)r["method"] == name))
            { Readers.Add(new JsonObject { ["type"] = type, ["method"] = name, ["mvid"] = method.Module.ModuleVersionId.ToString(), ["token"] = method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture), ["semanticIlWithMetadataSha256"] = hash, ["extraPatches"] = 0 }); }
        }
    }

    private void VerifyEngine()
    {
        if (typeof(Game).Module.ModuleVersionId != EngineMvid) { throw new InvalidOperationException("Missing-component initialization was not reviewed for this engine."); }
        (Type Type, string Name, int Token)[] methods = [(typeof(Game), "FillComponents", 0x06000DD3), (typeof(Map), "FillComponents", 0x06001458),
            (typeof(Game), "ExposeSmallComponents", 0x06000DD2), (typeof(Map), "ExposeComponents", 0x0600145B),
            (typeof(GameComponentUtility), "LoadedGame", 0x06000DEF), (typeof(GenTypes), "AllSubclassesNonAbstract", 0x06000595),
            (typeof(Quest), "ExposeData", 0x060068A7), (typeof(QuestManager), "ExposeData", 0x060068C2),
            (typeof(QuestPart_Delay), "ExposeData", 0x060068D8)];
        foreach ((Type type, string name, int token) in methods)
        {
            MethodInfo method = type.GetMethod(name, Members)!;
            if (method.MetadataToken != ReviewedEngine.Token(token)) { throw new InvalidOperationException("Engine component binding changed."); }
            RequireReviewedPatchChain(method);
            foreach (MethodBase callee in calls.DirectInvocations(method))
            {
                RequireReviewedPatchChain(callee);
                if (callee.IsGenericMethod) { RequireReviewedPatchChain(((MethodInfo)callee).GetGenericMethodDefinition()); }
            }
        }
        RequireReviewedPatchChain(typeof(Quest).GetProperty("State")!.GetMethod!);
        RequireReviewedPatchChain(typeof(Current).GetProperty("Game")!.GetMethod!);
        RequireReviewedPatchChain(typeof(GenTypes).GetProperty("AllTypes")!.GetMethod!);
        RequireReviewedPatchChain(typeof(QuestManager).GetProperty("QuestsListForReading")!.GetMethod!);
        foreach ((Type type, int token) in new[] { (typeof(Game), 0x06000DD9), (typeof(Map), 0x06001460) })
        {
            MethodInfo get = type.GetMethod("GetComponent", Members, binder: null, [typeof(Type)], modifiers: null)!;
            if (get.MetadataToken != ReviewedEngine.Token(token)) { throw new InvalidOperationException("The component assignability contract changed."); }
            RequireReviewedPatchChain(get);
        }
    }

    private static Type[] CachedOrder(Type baseType)
    {
        FieldInfo field = typeof(GenTypes).GetField("cachedSubclassesNonAbstract", Members)!;
        if (field.GetValue(null) is not Dictionary<Type, List<Type>> cache || !cache.TryGetValue(baseType, out List<Type>? order))
        { throw new InvalidOperationException("The actual FillComponents subclass cache does not exist; no new enumeration was generated."); }
        Type[] result = [.. order];
        if (result.Length != result.Distinct().Count() || result.Any(t => t is null || t.IsAbstract || !t.IsSubclassOf(baseType)))
        { throw new InvalidOperationException("The actual component enumeration cache is invalid."); }
        return result;
    }

    private QuestScriptDef ReadCourierRoot(Assembly production)
    {
        FieldInfo field = production.GetType("Mugirl.Features.Incidents.IncidentsDefOf", throwOnError: true)!
            .GetField("Mugirl_CourierContact", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
        // Capture发生在真实游戏加载之后；DefOf已由游戏自然初始化。只读字段，不调用业务方法/构造器。
        RequireReviewedPatchChain(typeof(DefDatabase<QuestScriptDef>).GetProperty("AllDefsListForReading")!.GetMethod!);
        if (field.FieldType != typeof(QuestScriptDef) || field.GetValue(null) is not QuestScriptDef value || value.GetType() != typeof(QuestScriptDef)
            || value.defName != "Mugirl_CourierContact")
        { throw new InvalidOperationException("No exact actual CourierContact DefOf binding proves the source quest root."); }
        QuestScriptDef[] matches = [.. DefDatabase<QuestScriptDef>.AllDefsListForReading.Where(d => d.defName == value.defName)];
        if (matches.Length != 1 || !ReferenceEquals(matches[0], value))
        { throw new InvalidOperationException("The actual CourierContact DefOf is not the unique current DefDatabase object."); }
        return value;
    }

    private void RequireReviewedPatchChain(MethodBase method)
    {
        if (Harmony.GetPatchInfo(method) is not { } patches) { return; }
        MethodInfo verifier = typeof(UnconsumedQualityObservation).GetMethod(nameof(UnconsumedQualityObservation.ProveRegisteredHook), Members)!;
        // 调用证明器之前先核其自身；被前缀跳过的证明器不能靠内部自检保护调用方。
        if (HasAnyPatches(verifier)) { throw new InvalidOperationException("The quality-hook proof method itself was patched."); }
        var seen = new HashSet<(string Role, MethodInfo Method)>();
        foreach ((string role, IEnumerable<Patch> group) in new[] { ("prefix", (IEnumerable<Patch>)patches.Prefixes), ("postfix", patches.Postfixes),
            ("transpiler", patches.Transpilers), ("finalizer", patches.Finalizers), ("innerPrefix", patches.InnerPrefixes),
            ("innerPostfix", patches.InnerPostfixes), ("innerFinalizer", patches.InnerFinalizers) })
        {
            foreach (Patch patch in group)
            {
                // 每次 Capture/Apply 都重查实际登记、整组helper指纹和七类嵌套补丁；不按owner放行，不缓存proof。
                string? proof = UnconsumedQualityObservation.ProveRegisteredHook(method, patch, role);
                if (!seen.Add((role, patch.PatchMethod)) || proof is null)
                { throw new InvalidOperationException("An extra patch invalidates missing-component initialization: " + method.DeclaringType?.FullName + "." + method.Name); }
                string token = method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture);
                string hookToken = patch.PatchMethod.MetadataToken.ToString("X8", CultureInfo.InvariantCulture);
                if (!Readers.OfType<JsonObject>().Any(r => (string?)r["targetToken"] == token && (string?)r["token"] == hookToken && (string?)r["role"] == role))
                { Readers.Add(ObservationPatchEvidence.Describe(method, patch, role, "missing-component initialization", proof)); }
            }
        }
    }

    private static bool HasAnyPatches(MethodBase method) => Harmony.GetPatchInfo(method) is { } p
        && p.Prefixes.Count + p.Postfixes.Count + p.Transpilers.Count + p.Finalizers.Count + p.InnerPrefixes.Count + p.InnerPostfixes.Count + p.InnerFinalizers.Count > 0;

    private static bool IsKnownName(string name, string current) => name == current || (current, name) switch
    {
        (Network, "Mugirl.CorporateNetwork") or (Introduction, "Mugirl.CorporateIntroduction")
            or (Migration, "Mugirl.MapComponent_MugirlMigration") or (Punisher, "Mugirl.MapComponent_MugirlPunisher")
            or (Story, "Mugirl.MugirlStoryState") or (CourierPart, "Mugirl.QuestPart_SpawnCourier") => true,
        _ => false,
    };
    private static XElement Game(XDocument document) => document.Root?.Name == "savegame" && document.Root.Elements("game").Count() == 1
        ? document.Root.Element("game")! : throw new InvalidOperationException("No unique real game root.");
    private static XElement? Single(XElement parent, string name) => parent.Elements(name).Count() > 1
        ? throw new InvalidOperationException("Duplicate source field: " + name) : parent.Element(name);
    private static XElement Required(XElement parent, string name) => Single(parent, name) ?? throw new InvalidOperationException("Missing proven source field: " + name);
    private static string? Scalar(XElement owner, string label)
    {
        XElement? field = Single(owner, label);
        if (field is null) { return null; }
        if (field.HasAttributes || field.HasElements || field.Nodes().Any(n => n is not XText || n is XCData))
        { throw new InvalidOperationException("Unknown scalar source shape: " + label); }
        return field.Value;
    }
    private static bool Bool(XElement? owner, string label, bool fallback)
    {
        string? value = owner is null ? null : Scalar(owner, label);
        return value is null ? fallback : bool.TryParse(value, out bool result) ? result
            : throw new InvalidOperationException("Malformed Boolean source: " + label);
    }
    private static string ComponentClass(XElement entry)
    {
        if (entry.Name != "li" || entry.Attributes().Count() != 1 || entry.Attribute("Class") is not { Value.Length: > 0 } attribute)
        { throw new InvalidOperationException("Unknown component entry shape."); }
        return attribute.Value;
    }
    private static void RequireImplicit(XElement element, string type)
    {
        if (element.HasAttributes && (element.Attributes().Count() != 1 || element.Attribute("Class")?.Value != type))
        { throw new InvalidOperationException("Unknown typed source shape: " + type); }
    }
    private static void ValidateList(XElement list)
    {
        if (list.HasAttributes || list.Nodes().Any(n => n is not XElement && (n is not XText t || n is XCData || t.Value.Any(c => c is not (' ' or '\t' or '\r' or '\n'))))
            || list.Elements().Any(e => e.Name != "li"))
        { throw new InvalidOperationException("Unknown list source shape."); }
    }
    private static string MapId(XElement map)
    {
        RequireImplicit(map, typeof(Map).FullName!);
        string? value = Scalar(map, "uniqueID");
        if (value is null || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        { throw new InvalidOperationException("Map owner has no canonical numeric uniqueID."); }
        return value;
    }
    private static string Path(XElement element) => "/" + string.Join('/', element.AncestorsAndSelf().Reverse()
        .Select(e => e.Name.LocalName + "[" + (e.ElementsBeforeSelf(e.Name).Count() + 1) + "]"));
}
