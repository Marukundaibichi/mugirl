using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>几个已经审阅的目标序列化形式。只投影独立期望文档，不看 after，也不授权任意 added/default。</summary>
internal sealed class CurrentSerializationDefaults(Assembly production, IReadOnlyDictionary<string, SerializationPawnDefaults> pawnDefs)
{
    public const string MilkType = "Mugirl.Features.Milk.CompMilkProduction";
    public const string MountType = "Mugirl.Features.Mounting.CompMount";
    public const string WheelType = "Mugirl.Features.WeaponWheel.CompWeaponWheel";
    private const string SlotType = "Mugirl.Features.WeaponWheel.WeaponWheelSlot";
    private const string OrderType = "Mugirl.Features.Corporate.CorporateOrder";
    private const string NetworkType = "Mugirl.Features.Corporate.CorporateNetwork";
    private const string MigrationType = "Mugirl.Features.Incidents.MapComponent_Migration";
    private const string PunisherType = "Mugirl.Features.Incidents.MapComponent_Punisher";
    private const string StorylineType = "Mugirl.Features.Incidents.GameComponent_Storyline";
    private const string OpeningType = "Mugirl.Features.Incidents.OpeningCrashState";
    private const string InvestmentType = "Mugirl.Features.Incidents.FusionInvestmentState";
    private const string EventScribeType = "Mugirl.Features.Incidents.EventStateScribe";
    private const string ClientType = "Mugirl.Features.Corporate.CorporateClientAccount";
    private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private readonly HashSet<string> verified = new(StringComparer.Ordinal);
    private readonly CurrentScribeRoots calls = new(production);
    private static readonly string[] WheelLabels = ["weaponWheelReserveWeapons", "weaponWheelSlots", "weaponWheelUnlockedSlots", "weaponWheelSwordDanceFailureStreak",
        "mugirlWheelReserveWeapons", "mugirlWheelSlotRecords", "mugirlWheelSwordDanceFailureStreak"];
    public JsonArray Actions { get; } = [];
    public JsonArray Readers { get; } = [];

    // 审阅的是具体字段的当前 reader 与容器类型绑定；指纹包含语义成员身份、方法/type flags、locals 和 EH。
    private static readonly (string Type, string Method, string Hash)[] Contracts =
    [
        (SlotType, "ExposeData", "6F71B8F6F59A3C72808B608382BE8B126522CE9C3550BBA15CD766DD03C2F39F"),
        (WheelType, "PostExposeData", "44D83AD8AA2F219AF048BB1214E6780F4C36CD1327885705C8BF659089DBBBCD"),
        (OrderType, "ExposeData", "B6940E35CDD35CD01BF6E0CA07789D08421C1D8A688FC3DCB0B9DC62FC37A3CE"),
        ("Mugirl.Features.Corporate.CorporateTradeDesk", "ExposeData", "46AE7B6E9C024745481CE181C464E198977A33EE1E9A892ABCB6CB62C6073AB0"),
        (NetworkType, "TradeExposeData", "7AE31FE6FC4CA24939D4218AF1B71A76E37A089F9598D0ADDE3719073E143FB4"),
        ("Mugirl.Features.Corporate.CorporateStateScribe", "Look", "A588920045DEC595C6CF7B4F1BAA4EE73BDC6D4AE1252ED2EF23E99BB3FA189D"),
        (MilkType, "ExposeProductionData", "36A8787515E6E53057D1C53786E83EB6B284DEA610BE63093AE23FE765B5A193"),
        (MilkType, "PostExposeData", "7A058FCCC31F084D600604DD7BE41FF129A398B19C8F283B2D6E9087157E054C"),
        (MigrationType, "ExposeData", "C74C7381B5F1537A000AE05F47857FF5AEAD25301D40D1FDE44E49BE60F8295A"),
        (PunisherType, "ExposeData", "388953A4CF3F31BA60D173F7701B7582B393C09DC654010837B0618329BB9B7D"),
        (MountType, "PostExposeData", "61948BF880F90CE31EB78E67A6FE0C9371D7ED5232FC1D00AEB03CADE83FCAD7"),
        (MountType, "Initialize", "DBAE646A6B2C405A4B2299C3F69A09CC51E53962540CEECF5C840C23960136BF"),
        (MountType, "get_Props", "7ECA4BFEA7DA38458531A6817F4AC9AE2C15A7F86EAE01A956B4645F3C28CA61"),
        (StorylineType, "ExposeData", "B7729ECA54898CBE260AA8E42E36B4BDD027BE0F4D67AD1EE2493C31A59D634D"),
        (OpeningType, "ExposeData", "F65A189C3DF9FBC360D72ED0000CCF7F04F913C5D5F2433A39E498CCEF4DF954"),
        (InvestmentType, "ExposeData", "C704F8910F59F203C12B167E3EFC8CC1140F15BCA4765F4330FBE8C2F99EE6D3"),
        (EventScribeType, "Look", "218126EFFF8F296ABEC02D209C26F28919A286C00C2A0C0EA0B4D2B10D5BF583"),
        (ClientType, "ExposeData", "D1AA290CB54B42F61ABFF30DA8C2617BFC0B7125299C9F3188C8F40D71038756"),
        (NetworkType, "ServicesExposeData", "08FA2BA2FF0730024E7704E32DC1804B9143E3088969A78F69B5F2F8C607D4F0"),
        (NetworkType, "ExposeData", "F677D5CCD6C7DFE9741359C15C0F518FAF9EA4B3C44EF0E198012FE82AE3A343"),
        (WheelType, "get_Props", "3F31127D7B2410316B69D8FE30AD3A5552A11D3E20497923A7F8BCA24060229A"),
        (WheelType, "get_MaxSlots", "E01A8DCF74F639BD281EF3687C098F37FDEE3C18100AA2B24D8B4C1C2CDDF7C4"),
        ("Mugirl.Features.WeaponWheel.WeaponWheelStorage", "EnsureCollections", "D0BEC512BC4B35C0271CD414E8491CEA19FD4AAE016CDAC17871F166AA48D59D"),
        ("Mugirl.Features.WeaponWheel.WeaponWheelStorage", "ValidateSlotReferences", "396581EEE92226D50305116A7BCAD7A55300EB1B577C3C4F80582B0CCD265B57"),
        ("Mugirl.Core.GameUtility", "IsResearchFinished", "51FCF6BB26B464345BE70286E8F22C3F77E8D9EAFA75738D02C5D71A5D1AE9BA"),
    ];

    public void Apply(XDocument expected, XDocument? original = null)
    {
        XElement? game = SaveXml.Game(expected);
        if (game is null) { return; }
        XElement? originalGame = original is null ? null : SaveXml.Game(original);
        ILookup<string, XElement>? originalPawns = originalGame?.Descendants().Where(SaveXml.IsPawn).ToLookup(p => p.Element("id")?.Value ?? string.Empty, StringComparer.Ordinal);
        foreach (XElement pawn in game.Descendants().Where(SaveXml.IsPawn).ToList())
        {
            if (pawn.Element("def")?.Value is not { } def || !pawnDefs.TryGetValue(def, out SerializationPawnDefaults? facts)
                || !MatchesPawnType(pawn, game, facts))
            {
                continue;
            }
            if (facts.Comps.GetValueOrDefault(WheelType) == 1 && !pawn.Elements().Any(e => WheelLabels.Contains(e.Name.LocalName, StringComparer.Ordinal))
                && originalPawns is not null && pawn.Element("id")?.Value is { Length: > 0 } id)
            {
                XElement[] sources = [.. originalPawns[id]];
                if (sources.Length > 1) { throw new InvalidOperationException("Repeated source Pawn identity cannot authorize Wheel initialization: " + id); }
                if (sources.Length == 1 && sources[0].Element("def")?.Value == def && MatchesPawnType(sources[0], originalGame!, facts)
                    && !sources[0].Elements().Any(e => WheelLabels.Contains(e.Name.LocalName, StringComparer.Ordinal)))
                {
                    AddMissingWheel(pawn, game, facts.Wheel ?? throw new InvalidOperationException("No unique typed Wheel/Research configuration for " + def));
                }
            }
            if (facts.Comps.GetValueOrDefault(WheelType) == 1 && Single(pawn, "mugirlWheelSlotRecords") is { } records)
            {
                foreach (XElement slot in records.Elements("li").Where(e => ExactTypeOrImplicit(e, SlotType)))
                {
                    Omit(slot, "unlocked", "False", "WheelSlot.Unlocked.False", [(SlotType, "ExposeData"), (WheelType, "PostExposeData")]);
                }
            }
            if (facts.Comps.GetValueOrDefault(MilkType) == 1 && Single(pawn, "mugirlMilkAccrualContinuation") is null)
            {
                Verify(MilkType, "ExposeProductionData");
                Verify(MilkType, "PostExposeData");
                AddNull(pawn, "mugirlMilkAccrualContinuation", "Milk.MissingDeepContinuation");
            }
            if (facts.Comps.GetValueOrDefault(MountType) == 1 && Single(pawn, "mugirlMountFireAtWill") is null)
            {
                if (facts.MountDefault is not { } value || facts.MountPropsType is null)
                {
                    throw new InvalidOperationException("Missing verified current Mount CompProperties for " + def + "; no hard-coded True fallback is allowed.");
                }
                Verify(MountType, "PostExposeData");
                Verify(MountType, "Initialize");
                Verify(MountType, "get_Props");
                var field = new XElement("mugirlMountFireAtWill", value ? "True" : "False");
                pawn.Add(field);
                Record("Mount.MissingDynamicDefault", field, null, field.ToString(SaveOptions.DisableFormatting),
                    def + ": unique " + facts.MountPropsType + ".turretFireAtWillDefault; current forceSave=True; published reader same property default (P1004 source hash f136386ce3b822fcfbf11915dc927fc4019d6c65ae7ea8dcb8ffd11b2d25f2ea)");
            }
        }

        foreach (XElement network in game?.Element("components")?.Elements("li").Where(e => SaveXml.ClassOf(e) == NetworkType) ?? [])
        {
            if (Single(network, "services") is { } services && ExactTypeOrImplicit(services, ClientType))
            {
                AddMissingFlat(services, "creditedPawnIds", null, "CorporateClient.MissingCreditedPawnIds",
                    [(ClientType, "ExposeData"), (NetworkType, "ServicesExposeData"), (NetworkType, "ExposeData"), ("Mugirl.Features.Corporate.CorporateStateScribe", "Look")]);
            }
            XElement? orders = Single(network, "trade") is { } trade ? Single(trade, "orders") : null;
            foreach (XElement order in orders?.Elements("li").Where(e => ExactTypeOrImplicit(e, OrderType)) ?? [])
            {
                Omit(order, "creditedTurnover", "0", "CorporateOrder.CreditedTurnover.Zero",
                    [(OrderType, "ExposeData"), ("Mugirl.Features.Corporate.CorporateTradeDesk", "ExposeData"), (NetworkType, "TradeExposeData"), ("Mugirl.Features.Corporate.CorporateStateScribe", "Look")]);
            }
        }
        foreach (XElement story in game?.Element("components")?.Elements("li").Where(e => SaveXml.ClassOf(e) == StorylineType) ?? [])
        {
            if (Single(story, "opening") is { } opening && ExactTypeOrImplicit(opening, OpeningType))
            {
                AddMissingFlat(opening, "pawns", null, "Storyline.MissingOpeningPawns",
                    [(OpeningType, "ExposeData"), (StorylineType, "ExposeData"), (EventScribeType, "Look")]);
            }
            if (Single(story, "investment") is { } investment && ExactTypeOrImplicit(investment, InvestmentType))
            {
                AddMissingFlat(investment, "investor", "null", "Storyline.MissingInvestorReference",
                    [(InvestmentType, "ExposeData"), (StorylineType, "ExposeData"), (EventScribeType, "Look")]);
            }
        }
        foreach (XElement component in game?.Element("maps")?.Elements("li").SelectMany(map => map.Element("components")?.Elements("li") ?? []) ?? [])
        {
            if (SaveXml.ClassOf(component) is { } type && (type == MigrationType || type == PunisherType) && Single(component, "initialization") is null)
            {
                Verify(type, "ExposeData");
                AddNull(component, "initialization", type + ".MissingDeepInitialization");
            }
        }
    }

    public void VerifyAllContracts()
    {
        foreach ((string type, string method, _) in Contracts) { Verify(type, method); }
    }

    private void Omit(XElement owner, string label, string value, string rule, (string Type, string Method)[] readers)
    {
        XElement? field = Single(owner, label);
        if (field is null || field.HasAttributes || field.HasElements || field.Value != value) { return; }
        foreach ((string type, string method) in readers) { Verify(type, method); }
        Record(rule, field, field.ToString(SaveOptions.DisableFormatting), null, "exact typed Scribe_Values default; forceSave=False; target writer omits only this value");
        field.Remove();
    }

    private void AddNull(XElement owner, string label, string rule)
    {
        var field = new XElement(label, new XAttribute("IsNull", "True"));
        owner.Add(field);
        Record(rule, field, null, field.ToString(SaveOptions.DisableFormatting), "this specific Scribe_Deep reader maps a missing node to null and writes null as IsNull=True");
    }

    private void AddMissingWheel(XElement pawn, XElement game, SerializationWheelDefaults facts)
    {
        foreach ((string type, string method) in new[] { (WheelType, "PostExposeData"), (WheelType, "get_Props"), (WheelType, "get_MaxSlots"), (SlotType, "ExposeData"),
            ("Mugirl.Features.WeaponWheel.WeaponWheelStorage", "EnsureCollections"), ("Mugirl.Features.WeaponWheel.WeaponWheelStorage", "ValidateSlotReferences"),
            ("Mugirl.Core.GameUtility", "IsResearchFinished") }) { Verify(type, method); }
        foreach (MethodInfo method in new[] { typeof(ResearchProjectDef).GetProperty("IsFinished")!.GetMethod!, typeof(ResearchProjectDef).GetProperty("ProgressReal")!.GetMethod!,
            typeof(ResearchProjectDef).GetProperty("Cost")!.GetMethod!, typeof(ResearchManager).GetMethod("GetProgress")!, typeof(ResearchManager).GetMethod("ExposeData")!,
            typeof(BackCompatibility).GetMethod("ResearchManagerPostLoadInit", Methods)!, typeof(Current).GetProperty("Game")!.GetMethod! })
        {
            RequireUnpatched(method);
        }
        foreach (XElement field in facts.MissingState(game))
        {
            pawn.Add(field);
            Record("Wheel.MissingCompleteState", field, null, field.ToString(SaveOptions.DisableFormatting),
                "all four legacy and three current roots absent in original and canonical Pawn; unique actual props plus source Def/Single progress determine empty slots and unlock flags; no IsFinished/GetProgress call");
        }
    }

    private void AddMissingFlat(XElement owner, string label, string? value, string rule, (string Type, string Method)[] readers)
    {
        if (Single(owner, label) is not null) { return; }
        foreach ((string type, string method) in readers) { Verify(type, method); }
        var field = new XElement(label, value);
        owner.Add(field);
        Record(rule, field, null, field.ToString(SaveOptions.DisableFormatting), value is null
            ? "this exact present group reads a missing collection as null and its verified reference resolver/PostLoadInit produces an empty list"
            : "this exact present group reads a missing Pawn reference as null; Scribe_References writes the literal null marker");
    }

    private void Verify(string type, string methodName)
    {
        string key = type + "::" + methodName;
        if (typeof(Scribe_Values).Module.ModuleVersionId != ReviewedEngine.Mvid)
        {
            throw new InvalidOperationException("Current Scribe default rules have not been verified for this engine assembly.");
        }
        MethodInfo method = production.GetType(type, throwOnError: true)!.GetMethod(methodName, Methods)!;
        RequireUnpatched(method);
        foreach (MethodInfo call in calls.DirectCalls(method).Where(m => m.DeclaringType?.Namespace == "Verse"
            && (m.DeclaringType.Name.StartsWith("Scribe_", StringComparison.Ordinal) || m.DeclaringType == typeof(Scribe) || m.DeclaringType == typeof(ThingComp))))
        {
            RequireUnpatched(call);
            if (call.IsGenericMethod) { RequireUnpatched(call.GetGenericMethodDefinition()); }
        }
        // IL 是已加载模块的不可变元数据；补丁链会变，必须在每次使用证据时重新检查。
        if (verified.Contains(key)) { return; }
        (string _, string _, string hash) = Contracts.Single(c => c.Type == type && c.Method == methodName);
        if (ObservationPatchEvidence.SerializationFingerprint(method) != hash) { throw new InvalidOperationException("The reviewed serializer contract changed: " + key); }
        Readers.Add(new JsonObject { ["type"] = type, ["method"] = methodName, ["mvid"] = method.Module.ModuleVersionId.ToString(),
            ["token"] = method.MetadataToken.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), ["semanticIlWithMetadataSha256"] = hash, ["extraPatches"] = 0 });
        verified.Add(key);
    }

    private static void RequireUnpatched(MethodBase method)
    {
        if (Harmony.GetPatchInfo(method) is { } patches && patches.Prefixes.Count + patches.Postfixes.Count + patches.Transpilers.Count + patches.Finalizers.Count
            + patches.InnerPrefixes.Count + patches.InnerPostfixes.Count + patches.InnerFinalizers.Count > 0)
        {
            throw new InvalidOperationException("An extra patch invalidates the reviewed serialization default contract: " + method.DeclaringType?.FullName + "." + method.Name);
        }
    }

    private bool MatchesPawnType(XElement pawn, XElement game, SerializationPawnDefaults facts)
    {
        if (pawn.Attribute("Class")?.Value is { } type) { return facts.Classes.Contains(type, StringComparer.Ordinal); }
        // WorldPawns 的三个真实 HashSet<Pawn> Deep reader 以 Pawn 为隐式类型；绝不将其它无 Class 的形状都认作 Pawn。
        if (pawn.HasAttributes || pawn.Name.LocalName != "li" || pawn.Parent?.Name.LocalName is not ("pawnsAlive" or "pawnsDead" or "pawnsMothballed")
            || pawn.Parent.Parent?.Name.LocalName != "worldPawns" || pawn.Parent.Parent.Parent?.Name.LocalName != "world"
            || pawn.Parent.Parent.Parent.Parent != game || !facts.Classes.Contains(typeof(Pawn).FullName!, StringComparer.Ordinal)
            || !ExactTypeOrImplicit(pawn.Parent, typeof(HashSet<Pawn>).FullName!)
            || !ExactTypeOrImplicit(pawn.Parent.Parent, typeof(RimWorld.Planet.WorldPawns).FullName!)
            || !ExactTypeOrImplicit(pawn.Parent.Parent.Parent, typeof(RimWorld.Planet.World).FullName!))
        {
            return false;
        }
        Type world = typeof(RimWorld.Planet.WorldPawns);
        MethodInfo reader = world.GetMethod("ExposeData")!;
        FieldInfo field = world.GetField(pawn.Parent.Name.LocalName, Methods)!;
        if (reader.Module.ModuleVersionId != ReviewedEngine.Mvid || reader.MetadataToken != ReviewedEngine.Token(0x0600E52D)
            || field.FieldType != typeof(HashSet<Pawn>))
        {
            throw new InvalidOperationException("The implicit WorldPawns Pawn reader is not the verified engine contract.");
        }
        RequireUnpatched(reader);
        foreach (MethodInfo call in calls.DirectCalls(reader).Where(m => m.DeclaringType == typeof(Scribe_Collections)))
        {
            RequireUnpatched(call);
            if (call.IsGenericMethod) { RequireUnpatched(call.GetGenericMethodDefinition()); }
        }
        if (verified.Add("WorldPawns." + pawn.Parent.Name.LocalName + " implicit Pawn"))
        {
            Readers.Add(new JsonObject { ["type"] = world.FullName, ["method"] = "ExposeData", ["mvid"] = reader.Module.ModuleVersionId.ToString(),
                ["token"] = reader.MetadataToken.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), ["semanticIlWithMetadataSha256"] = ObservationPatchEvidence.SerializationFingerprint(reader),
                ["binding"] = "/savegame/game/world/worldPawns/" + pawn.Parent.Name.LocalName + "/li; HashSet<Pawn>, LookMode.Deep", ["extraPatches"] = 0 });
        }
        return true;
    }

    private static XElement? Single(XElement parent, string name)
    {
        XElement[] values = [.. parent.Elements(name)];
        return values.Length < 2 ? values.SingleOrDefault() : throw new InvalidOperationException("Duplicate field cannot be projected as a default: " + name);
    }

    private static bool ExactTypeOrImplicit(XElement element, string type) => !element.HasAttributes || element.Attributes().Count() == 1 && element.Attribute("Class")?.Value == type;

    private void Record(string rule, XElement element, string? before, string? after, string evidence)
        => Actions.Add(new JsonObject { ["rule"] = rule, ["path"] = "/" + string.Join('/', element.AncestorsAndSelf().Reverse().Select(e => e.Name.LocalName + "[" + (e.ElementsBeforeSelf(e.Name).Count() + 1) + "]")),
            ["before"] = before, ["after"] = after, ["evidence"] = evidence });
}
