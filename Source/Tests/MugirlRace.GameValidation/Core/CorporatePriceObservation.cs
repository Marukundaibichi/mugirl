using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>只观察原始旧报价的一次自然重算；不调用市值、定价或续接，不从重存档构造期望。</summary>
internal static class CorporatePriceObservation
{
    private const string NetworkName = "Mugirl.Features.Corporate.CorporateNetwork";
    private const string OfferName = "Mugirl.Features.Corporate.CorporatePersonOffer";
    private const string CompletionName = "Mugirl.SaveUpgrade.CorporateOfferPriceCompletion";
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Guid EngineMvid = ReviewedEngine.Mvid;
    private static readonly string[] Fields = ["id", "pawn", "price", "paid", "paidAmount", "delivered", "buyAsColonist", "transferApplied", "continuation"];
    private static readonly (string Type, string Method, string Hash)[] Contracts =
    [
        (NetworkName, "ExposeData", "F677D5CCD6C7DFE9741359C15C0F518FAF9EA4B3C44EF0E198012FE82AE3A343"),
        (NetworkName, "PeopleExposeData", "B7FAFB92314FF4ADECFC9059E8EA327939F91B4EB68859415A70AB809A322ED7"),
        (NetworkName, "LoadedGame", "16F42705544B4AA8965B150B0B64EFEAB7D9E8D94E650D6A95036D8F210A08B8"),
        (NetworkName, "RefreshPeopleOffers", "34B02071D6A4EA1268874BD093B75BD3556A95DF706E3F2B19C2384BB5DE29F3"),
        (NetworkName, "PeopleBasePrice", "421CB2ED119EF4BCE47F7DE1D2A9C266053132E9F50EF543DFDD637F8799C909"),
        (NetworkName, "Price", "5FF8477C0EB03DD3A63D3783E21CCB3365DDC10119753CEA6AAE727A36C674E8"),
        (OfferName, "ExposeData", "0E441532AB8607C70A812DF9B10C14ADEE2FA4ECA5702B869A98FE7EA5169A38"),
        (OfferName, "Complete", "01FD6E598082E13067EFDE9C83FDC20AAF6CF1D48C9CBF8362B062666F521F59"),
        (CompletionName, "Complete", "FB85CE47270A2F3EDDA929A55120E77798C73B5ED3C52BEB8F33C512D58FC38A"),
        ("Mugirl.Features.Corporate.CorporateStateScribe", "Look", "A588920045DEC595C6CF7B4F1BAA4EE73BDC6D4AE1252ED2EF23E99BB3FA189D"),
        ("Mugirl.Features.Corporate.CorporatePeopleDesk", "ExposeData", "45A0EA7B39DDC171E896BD0474AA399E5FF67481636EAF3685F068FFDACCEDB9"),
        ("Mugirl.Features.Corporate.CorporateMoney", "Price", "CFE9F14DBABAE65BEEC7B93F9B11EA64E334B557B4D83888686907860BED7E4F"),
        ("Mugirl.Features.Corporate.CorporateMoney", "Total", "D5D2AFB9A631174E382AD62D421B9A7AEAAE34896265F4FC7B11A3483E36DD66"),
        ("Mugirl.Mathematics.UnityMathf", "Max", "138342AAC7E23ABEDCC5B0C84C674A758216109EBEA31457DBC7AFF99F2AFBDB"),
    ];

    // StaticCacheLifecycle: Begin/Stop 只服务隔离验证；Snapshot 后释放所有游戏对象和 XML。
    private static readonly Dictionary<object, XmlElement> networks = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<object, Entry> entries = new(ReferenceEqualityComparer.Instance);
    private static readonly List<string> errors = [];
    private static readonly JsonArray readers = [];
    private static readonly List<(MethodInfo Target, MethodInfo Hook, string Role, int Priority)> installed = [];
    private static Assembly? production;
    private static Harmony? harmony;
    private static XmlDocument? original;
    private static XmlDocument? readingDocument;
    private static string? inputPath;
    private static bool active;
    private static int readThread;
    private static int loadedCalls;
    private static Entry? inFlightCompletion;
    private static int generation;
    [ThreadStatic] private static object? loadedNetwork;
    [ThreadStatic] private static Entry? current;

    public static void Begin(Assembly? assembly, Harmony patcher, string originalInputPath)
    {
        if (!GenCommandLine.CommandLineArgPassed("mugirlvalidate")
            || !File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "mugirl-gamevalidation.json")))
        {
            throw new InvalidOperationException("Corporate pricing observations require isolated game validation.");
        }
        if (assembly is null) { throw new InvalidOperationException("No production assembly for pricing observations."); }
        var input = new XmlDocument { XmlResolver = null };
        input.Load(originalInputPath);
        Start(assembly, patcher, input, originalInputPath);
    }

    internal static void Start(Assembly assembly, Harmony patcher, XmlDocument input, string? originalInputPath = null)
    {
        Stop();
        errors.Clear(); readers.Clear();
        production = assembly; harmony = patcher; original = input; inputPath = originalInputPath;
        readThread = 0;
        active = true;
        Require(input.DocumentElement?.Name == "savegame" && input.DocumentElement["game"] is not null, "Not a main save input.");
        CheckContracts("before load");
        // 先安装被调用方，避免首次编译调用方时仍看见未观察的短 getter/续接方法。
        Patch(MarketGetter, nameof(BeforeMarket), nameof(AfterMarket), nameof(CallFailed));
        Patch(Method(NetworkName, "PeopleBasePrice"), nameof(BeforePrice), nameof(AfterPrice), nameof(PriceFailed));
        Patch(Method(CompletionName, "Complete"), nameof(BeforeContinuation), nameof(AfterContinuation), nameof(CallFailed));
        Patch(Method(OfferName, "Complete"), nameof(BeforeComplete), nameof(AfterComplete), nameof(CompleteFailed));
        Patch(Method(OfferName, "ExposeData"), nameof(BeforeExpose), nameof(AfterExpose), nameof(ExposeFailed));
        Patch(Method(NetworkName, "ExposeData"), nameof(BeforeExpose), nameof(AfterExpose), nameof(ExposeFailed));
        Patch(Method(NetworkName, "LoadedGame"), nameof(BeforeLoaded), nameof(AfterLoaded), nameof(LoadedFailed));
        CheckContracts("installed");
    }

    private static MethodInfo MarketGetter => typeof(Thing).GetProperty(nameof(Thing.MarketValue))!.GetMethod!;
    private static MethodInfo Method(string type, string name)
    {
        MethodInfo[] methods = [.. production!.GetType(type, true)!.GetMethods(Members).Where(m => m.Name == name)];
        return methods.Length == 1 ? methods[0] : methods.Single(m => m.ReturnType == typeof(float)
            && m.GetParameters() is [{ ParameterType: var a }, { ParameterType: var b }] && a == typeof(float) && b == typeof(float));
    }

    private static void Patch(MethodInfo target, string prefix, string postfix, string finalizer)
    {
        MethodInfo Hook(string name) => typeof(CorporatePriceObservation).GetMethod(name, Members)!;
        harmony!.Patch(target, new HarmonyMethod(Hook(prefix)) { priority = Priority.First },
            new HarmonyMethod(Hook(postfix)) { priority = Priority.Last }, finalizer: new HarmonyMethod(Hook(finalizer)) { priority = Priority.Last });
        installed.Add((target, Hook(prefix), "prefix", Priority.First));
        installed.Add((target, Hook(postfix), "postfix", Priority.Last));
        installed.Add((target, Hook(finalizer), "finalizer", Priority.Last));
    }

    public static void Stop()
    {
        active = false; generation++;
        foreach ((MethodInfo target, MethodInfo hook, _, _) in installed) { harmony?.Unpatch(target, hook); }
        installed.Clear(); entries.Clear(); networks.Clear();
        original = null; readingDocument = null; inputPath = null; production = null; harmony = null;
        current = null; loadedNetwork = null;
        readThread = 0; loadedCalls = 0; System.Threading.Volatile.Write(ref inFlightCompletion, null);
    }

    private static bool OnReadThread()
    {
        if (!active) { return false; }
        if (readThread != 0 && Environment.CurrentManagedThreadId == readThread) { return true; }
        Error("A bound pricing observation crossed the actual read thread.");
        return false;
    }

    private static void BeforeExpose(object __instance, out ExposeCall? __state)
    {
        __state = null;
        if (!active) { return; }
        try
        {
            if (__instance.GetType().FullName == NetworkName)
            {
                if (Scribe.mode != LoadSaveMode.LoadingVars) { return; }
                Require(ReferenceEquals(Scribe.loader.curParent, __instance) && Scribe.loader.curXmlParent is XmlElement, "Network reader has no exact owner.");
                var node = (XmlElement)Scribe.loader.curXmlParent;
                Require(MainSaveDocument.Matches(production!, node.OwnerDocument, inputPath), "Network source is not the production main save document/path.");
                System.Threading.Interlocked.CompareExchange(ref readThread, Environment.CurrentManagedThreadId, 0);
                Require(OnReadThread(), "Network loading moved between read threads.");
                Require(IsNetwork(node) && node.GetAttribute("Class") == NetworkName, "Network is outside the main component list.");
                Require(readingDocument is null || ReferenceEquals(readingDocument, node.OwnerDocument), "A secondary document reached the pricing observer.");
                readingDocument = node.OwnerDocument;
                Require(networks.TryAdd(__instance, node), "Network was read twice.");
                __state = new ExposeCall(null, Scribe.mode, Environment.CurrentManagedThreadId);
                return;
            }
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Require(ReferenceEquals(Scribe.loader.curParent, __instance) && Scribe.loader.curXmlParent is XmlElement, "Offer reader has no exact source.");
                var node = (XmlElement)Scribe.loader.curXmlParent;
                XmlElement? continuation = Single(node, "continuation");
                if (continuation?.GetAttribute("Class") != CompletionName) { return; }
                Require(MainSaveDocument.Matches(production!, node.OwnerDocument, inputPath), "Offer source is not the production main save document/path.");
                Require(OnReadThread(), "Offer loading left its bound read thread.");
                Require(continuation.Attributes.Count == 1 && !continuation.ChildNodes.OfType<XmlElement>().Any() && string.IsNullOrWhiteSpace(continuation.InnerText), "Malformed pricing continuation.");
                Require(node.Name == "li" && ExactClass(node, OfferName) && node.ParentNode is XmlElement { Name: "offers" }
                    && node.ParentNode.ParentNode is XmlElement { Name: "people" }, "Offer is outside its typed people list.");
                var owner = node.ParentNode!.ParentNode!.ParentNode as XmlElement;
                Require(owner is not null && IsNetwork(owner) && ReferenceEquals(readingDocument, node.OwnerDocument), "Offer belongs to a different main document.");
                object network = networks.Single(p => ReferenceEquals(p.Value, owner)).Key;
                int id = Integer(node, "id");
                string pawnId = Text(node, "pawn") ?? throw new InvalidOperationException("Missing offer Pawn reference.");
                Require(pawnId.StartsWith("Thing_", StringComparison.Ordinal), "Offer is not a Thing reference.");
                XmlElement old = OriginalOffer(id);
                Require(Text(old, "pawn") == pawnId && Integer(old, "priceVersion") == 0 && Single(old, "continuation") is null,
                    "The original offer does not prove uncompleted legacy pricing.");
                Require(Integer(old, "price") == Integer(node, "price") && Boolean(old, "paid") == Boolean(node, "paid"), "Offer input changed before its natural pricing call.");
                int originalReceipt = Text(old, "paidAmount") is { } amount ? int.Parse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture) : -1;
                int canonicalReceipt = Boolean(old, "paid") && originalReceipt < 0 ? Integer(old, "price")
                    : Single(old, "paidAmount") is null ? 0 : originalReceipt;
                Require(Integer(node, "paidAmount") == canonicalReceipt, "Receipt does not preserve its exact original input/default.");
                Require(!entries.ContainsKey(__instance) && !entries.Values.Any(e => e.Id == id && ReferenceEquals(e.Network, network)), "Duplicate offer identity.");
                XmlElement pawnSource = owner!["vault"]?["innerList"]?.ChildNodes.OfType<XmlElement>()
                    .Single(e => Text(e, "id") == pawnId[6..]) ?? throw new InvalidOperationException("The offered Pawn has no unique vault source.");
                Require(ExactClass(pawnSource, "Pawn", "Verse.Pawn") && Text(pawnSource, "kindDef") != "Mugirl_CorporateShowcase", "Showcase outfit changes require a separate observation proof.");
                XmlElement oldPawn = (old.ParentNode!.ParentNode as XmlElement)?["corporateVault"]?["innerList"]?.ChildNodes.OfType<XmlElement>()
                    .Single(e => Text(e, "id") == pawnId[6..]) ?? throw new InvalidOperationException("The original offered Pawn has no unique corporate vault source.");
                Require(ExactClass(oldPawn, "Pawn", "Verse.Pawn") && Text(oldPawn, "def") == Text(pawnSource, "def")
                    && Text(oldPawn, "kindDef") == Text(pawnSource, "kindDef"), "The original and canonical Pawn definitions do not prove the same identity.");
                var entry = new Entry(__instance, network, id, pawnId, VanillaReadObservation.PathOf(node), node, pawnSource);
                entries.Add(__instance, entry);
                __state = new ExposeCall(entry, Scribe.mode, Environment.CurrentManagedThreadId);
            }
            else if (entries.TryGetValue(__instance, out Entry? entry))
            {
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    Require(System.Threading.Volatile.Read(ref inFlightCompletion) is null && loadedCalls == 0, "Saving overlapped bound loading/pricing.");
                    Require(entry.Completed && Read<int>(entry.Offer, "price") == entry.ExpectedPrice
                        && Read<int>(entry.Offer, "paidAmount") == entry.ExpectedReceipt && Field(entry.Offer, "continuation") is null,
                        "Completed pricing was not preserved until the actual save.");
                    CheckIdentity(entry);
                }
                else { Require(OnReadThread(), "An offer read phase left its bound worker."); }
                __state = new ExposeCall(entry, Scribe.mode, Environment.CurrentManagedThreadId);
            }
        }
        catch (Exception exception) { Error(exception.Message); }
    }

    private static void AfterExpose(ExposeCall? __state, bool __runOriginal)
    {
        if (!active || __state is null) { return; }
        try
        {
            Require(__runOriginal && Scribe.mode == __state.Mode && Environment.CurrentManagedThreadId == __state.Thread,
                "An original corporate reader did not complete on its calling thread.");
            if (__state.Entry is not { } entry) { return; }
            if (__state.Mode == LoadSaveMode.LoadingVars)
            {
                Require(Read<int>(entry.Offer, "id") == entry.Id && Read<int>(entry.Offer, "price") == Integer(entry.Source, "price")
                    && Read<bool>(entry.Offer, "paid") == Boolean(entry.Source, "paid")
                    && Read<int>(entry.Offer, "paidAmount") == Integer(entry.Source, "paidAmount"), "Loaded offer scalars differ from their source.");
                entry.Continuation = Field(entry.Offer, "continuation");
                Require(entry.Continuation?.GetType() == production!.GetType(CompletionName), "The source continuation did not load as its exact type.");
                entry.Loaded = true;
            }
            if (__state.Mode == LoadSaveMode.ResolvingCrossRefs)
            {
                entry.Pawn = Field(entry.Offer, "pawn") as Pawn;
                CheckIdentity(entry);
                entry.Resolved = true;
            }
            if (__state.Mode == LoadSaveMode.Saving) { entry.Saved = true; }
        }
        catch (Exception exception) { Error(exception.Message); }
    }

    private static void BeforeLoaded(object __instance, out LoadedScope __state)
    {
        __state = new LoadedScope(loadedNetwork, generation, false);
        if (!active) { return; }
        loadedNetwork = null;
        if (!networks.ContainsKey(__instance)) { return; }
        try
        {
            Require(OnReadThread(), "Bound LoadedGame left the actual read worker.");
            Require(Current.Game?.components is { } components && components.Count(c => ReferenceEquals(c, __instance)) == 1,
                "LoadedGame does not belong to the actual current game component.");
            loadedNetwork = __instance;
            System.Threading.Interlocked.Increment(ref loadedCalls);
            __state = __state with { Bound = true };
        }
        catch (Exception exception) { Error(exception.Message); }
    }
    private static void AfterLoaded(bool __runOriginal, LoadedScope __state)
    { if (active && __state.Bound && (!OnReadThread() || !__runOriginal)) { Error("Bound LoadedGame was skipped or crossed threads."); } }
    private static void LoadedFailed(Exception? __exception, LoadedScope __state)
    {
        if (__state.Bound) { Failure(__exception); }
        if (__state.Generation == generation && __state.Bound) { System.Threading.Interlocked.Decrement(ref loadedCalls); }
        if (__state.Generation == generation) { loadedNetwork = __state.Previous; }
    }

    private static void BeforeComplete(object __instance, object network, out CompleteScope __state)
    {
        __state = new CompleteScope(current, generation, null, null);
        if (!active) { return; }
        current = null;
        try
        {
            if (!entries.TryGetValue(__instance, out Entry? entry))
            {
                Require(Field(__instance, "continuation")?.GetType().FullName != CompletionName, "An unbound pricing continuation executed.");
                return;
            }
            Require(OnReadThread(), "Bound completion left the actual read worker.");
            Require(entry.Loaded && entry.Resolved && !entry.Completed && ReferenceEquals(network, entry.Network)
                && ReferenceEquals(network, loadedNetwork) && ReferenceEquals(Field(entry.Offer, "continuation"), entry.Continuation), "Completion has no matching original LoadedGame scope.");
            CheckIdentity(entry);
            Require(Read<int>(entry.Offer, "price") == Integer(entry.Source, "price") && Read<int>(entry.Offer, "paidAmount") == Integer(entry.Source, "paidAmount")
                && Read<bool>(entry.Offer, "paid") == Boolean(entry.Source, "paid"), "Offer changed before pricing.");
            current = entry;
            __state = __state with { Bound = entry, PreviousFlight = System.Threading.Interlocked.Exchange(ref inFlightCompletion, entry) };
        }
        catch (Exception exception) { Error(exception.Message); }
    }

    private static void AfterComplete(bool __runOriginal)
    {
        if (!active || current is not { } entry || !OnReadThread()) { return; }
        try
        {
            Require(__runOriginal && entry.ContinuationCalls == 1 && entry.PriceCalls == 1 && entry.MarketCalls == 1 && entry.PriceReturned
                && entry.ContinuationReturned && Field(entry.Offer, "continuation") is null, "The exact once-only completion did not finish.");
            CheckIdentity(entry);
            entry.Completed = true;
        }
        catch (Exception exception) { Error(exception.Message); }
    }
    private static void CompleteFailed(Exception? __exception, CompleteScope __state)
    {
        if (__state.Bound is not null) { Failure(__exception); }
        if (__state.Generation == generation && __state.Bound is not null) { System.Threading.Volatile.Write(ref inFlightCompletion, __state.PreviousFlight); }
        if (__state.Generation == generation) { current = __state.Previous; }
    }

    private static void BeforeContinuation(object __instance, object network, object offer, out Entry? __state)
    {
        __state = null;
        if (!active) { return; }
        if (current is not { } entry || !ReferenceEquals(entry.Continuation, __instance) || !ReferenceEquals(entry.Network, network) || !ReferenceEquals(entry.Offer, offer))
        { Error("A pricing continuation has no exact bound call."); return; }
        if (!OnReadThread()) { return; }
        entry.ContinuationCalls++;
        __state = entry;
        if (entry.ContinuationCalls != 1) { Error("Repeated pricing continuation."); }
    }
    private static void AfterContinuation(Entry? __state, bool __runOriginal)
    {
        if (!active || __state is not { } entry || !OnReadThread()) { return; }
        try
        {
            int receipt = Integer(entry.Source, "paidAmount");
            entry.ExpectedReceipt = Boolean(entry.Source, "paid") && receipt < 0 ? Integer(entry.Source, "price") : receipt;
            Require(__runOriginal && entry.PriceReturned && Read<int>(entry.Offer, "price") == entry.ExpectedPrice
                && Read<int>(entry.Offer, "paidAmount") == entry.ExpectedReceipt, "The continuation did not write the independently verified values.");
            entry.ContinuationReturned = true;
        }
        catch (Exception exception) { Error(exception.Message); }
    }

    private static void BeforePrice(object __instance, Pawn? pawn, out Entry? __state)
    {
        __state = null;
        if (!active) { return; }
        if (current is not { } entry)
        {
            Entry? flight = System.Threading.Volatile.Read(ref inFlightCompletion);
            if (flight is not null && ReferenceEquals(flight.Network, __instance) && ReferenceEquals(flight.Pawn, pawn)
                && Environment.CurrentManagedThreadId != readThread) { Error("A bound pricing call crossed the actual read thread."); }
            return;
        }
        if (!OnReadThread()) { return; }
        try
        {
            Require(ReferenceEquals(entry.Network, __instance) && ReferenceEquals(entry.Pawn, pawn) && entry.ContinuationCalls == 1, "Unrelated price call inside completion.");
            entry.PriceCalls++; entry.PriceDepth++;
            __state = entry;
            Require(entry.PriceCalls == 1 && entry.PriceDepth == 1, "Repeated or nested valuation.");
        }
        catch (Exception exception) { Error(exception.Message); }
    }
    private static void AfterPrice(Entry? __state, int __result, bool __runOriginal)
    {
        if (!active || __state is not { } entry || !OnReadThread()) { return; }
        try
        {
            Require(__runOriginal && entry.MarketCalls == 1 && entry.MarketReturned, "The natural original MarketValue call was not observed exactly once.");
            entry.ExpectedPrice = Calculate(entry.MarketBits, entry.MultiplierBits);
            Require(__result == entry.ExpectedPrice, "PeopleBasePrice differs from independent pricing arithmetic.");
            entry.PriceReturned = true;
        }
        catch (Exception exception) { Error(exception.Message); }
    }
    private static void PriceFailed(Exception? __exception, Entry? __state)
    {
        if (__state is not null) { Failure(__exception); }
        if (__state is not null) { __state.PriceDepth--; }
    }

    private static void BeforeMarket(Thing __instance, out Entry? __state)
    {
        __state = null;
        if (!active) { return; }
        if (current is not { PriceDepth: > 0 } entry || !ReferenceEquals(entry.Pawn, __instance))
        {
            Entry? flight = System.Threading.Volatile.Read(ref inFlightCompletion);
            if (flight is { PriceDepth: > 0 } && ReferenceEquals(flight.Pawn, __instance) && Environment.CurrentManagedThreadId != readThread)
            { Error("The bound MarketValue call crossed the actual read thread."); }
            return;
        }
        if (!OnReadThread()) { return; }
        entry.MarketCalls++;
        __state = entry;
    }
    private static void AfterMarket(Entry? __state, float __result, bool __runOriginal)
    {
        if (!active || __state is not { } entry || !OnReadThread()) { return; }
        if (!__runOriginal) { Error("MarketValue was replaced instead of naturally evaluated."); return; }
        try
        {
            // 已核实 IL 先求 MarketValue，再读取此 Def 字段；不能在 getter 之前提前取倍率。
            object config = production!.GetType("Mugirl.Features.Corporate.CorporateDefOf", true)!.GetField("Mugirl_CorporatePeopleConfig", Members)!.GetValue(null)
                ?? throw new InvalidOperationException("The actual pricing configuration is missing.");
            Require(config.GetType() == production.GetType("Mugirl.Features.Corporate.CorporatePeopleConfigDef"), "Unexpected pricing configuration type.");
            entry.MultiplierBits = BitConverter.SingleToInt32Bits(Read<float>(config, "basePriceMultiplier"));
            entry.MarketBits = BitConverter.SingleToInt32Bits(__result);
            entry.MarketReturned = true;
        }
        catch (Exception exception) { Error(exception.Message); }
    }

    internal static int Calculate(int marketBits, int multiplierBits)
    {
        float multiplier = BitConverter.Int32BitsToSingle(multiplierBits);
        float unit = BitConverter.Int32BitsToSingle(marketBits) * (0f > multiplier ? 0f : multiplier);
        if (!float.IsFinite(unit) || unit <= 0f || unit > 100000000f) { return 1; }
        return Math.Max(1, (int)decimal.Ceiling((decimal)unit));
    }

    private static void CheckIdentity(Entry entry)
    {
        Require(entry.Pawn is { } pawn && pawn.GetType() == typeof(Pawn) && ReferenceEquals(Field(entry.Offer, "pawn"), pawn)
            && "Thing_" + pawn.ThingID == entry.PawnId && pawn.def?.defName == Text(entry.PawnSource, "def")
            && pawn.kindDef?.defName == Text(entry.PawnSource, "kindDef")
            && ReferenceEquals(pawn.holdingOwner, Field(entry.Network, "vault")), "Offered Pawn identity or vault ownership changed.");
        foreach (string field in new[] { "delivered", "buyAsColonist", "transferApplied", "paid" })
        { Require(Read<bool>(entry.Offer, field) == Boolean(entry.Source, field), "Unrelated offer state changed: " + field); }
        Require(Read<int>(entry.Offer, "id") == entry.Id, "Offer ID changed.");
    }

    private static XmlElement OriginalOffer(int id)
    {
        XmlElement owner = original!.SelectNodes("/savegame/game/components/li")!.OfType<XmlElement>()
            .Single(e => e.GetAttribute("Class") is "Mugirl.CorporateNetwork" or NetworkName);
        XmlElement list = Single(owner, "corporatePeopleOffers") ?? throw new InvalidOperationException("No original flat legacy offer list.");
        XmlElement offer = list.ChildNodes.OfType<XmlElement>().Single(e => e.Name == "li" && Integer(e, "id") == id);
        Require(ExactClass(offer, "Mugirl.CorporatePersonOffer", OfferName), "Unknown original offer class.");
        return offer;
    }
    private static bool IsNetwork(XmlElement node) => node.Name == "li" && node.ParentNode is XmlElement { Name: "components" } parent
        && parent.ParentNode is XmlElement { Name: "game" } game && ReferenceEquals(game.ParentNode, node.OwnerDocument.DocumentElement)
        && node.OwnerDocument.DocumentElement!.Name == "savegame";
    private static bool ExactClass(XmlElement node, params string[] names)
        => node.Attributes.Count == 0 || node.Attributes.Count == 1 && names.Contains(node.GetAttribute("Class"), StringComparer.Ordinal);
    private static XmlElement? Single(XmlElement parent, string name)
    {
        XmlElement[] found = [.. parent.ChildNodes.OfType<XmlElement>().Where(e => e.Name == name)];
        Require(found.Length <= 1, "Duplicate source field " + name);
        return found.SingleOrDefault();
    }
    private static string? Text(XmlElement parent, string name)
    {
        XmlElement? node = Single(parent, name);
        if (node is null) { return null; }
        Require(node.Attributes.Count == 0 && !node.ChildNodes.OfType<XmlElement>().Any(), "Non-scalar source field " + name);
        return node.InnerText;
    }
    private static int Integer(XmlElement parent, string name) => Text(parent, name) is { } value ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) : 0;
    private static bool Boolean(XmlElement parent, string name) => Text(parent, name) is { } value && bool.Parse(value);
    private static object? Field(object owner, string name) => owner.GetType().GetField(name, Members)!.GetValue(owner);
    private static T Read<T>(object owner, string name) => (T)Field(owner, name)!;
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    private static void Error(string message) { lock (errors) { errors.Add(message); } }
    private static void Failure(Exception? exception) { if (active && exception is not null) { Error("Natural pricing/read call threw: " + exception); } }
    private static void ExposeFailed(Exception? __exception, ExposeCall? __state) { if (__state is not null) { Failure(__exception); } }
    private static void CallFailed(Exception? __exception, Entry? __state) { if (__state is not null) { Failure(__exception); } }

    private static void CheckContracts(string check)
    {
        var calls = new CurrentScribeRoots(production!);
        var scribe = new HashSet<MethodInfo>();
        foreach ((string type, string method, string hash) in Contracts)
        {
            MethodInfo target = Method(type, method);
            Require(ObservationPatchEvidence.SerializationFingerprint(target) == hash, "Unreviewed pricing/reader method: " + type + "." + method);
            CheckPatches(target, check);
            foreach (MethodInfo call in calls.DirectCalls(target).Where(m => m.DeclaringType?.Namespace == "Verse" && m.DeclaringType.Name.StartsWith("Scribe", StringComparison.Ordinal)))
            {
                scribe.Add(call);
                if (call.IsGenericMethod) { scribe.Add(call.GetGenericMethodDefinition()); }
            }
        }
        Require(MarketGetter.Module.ModuleVersionId == EngineMvid, "Unverified engine MarketValue reader.");
        CheckPatches(MarketGetter, check);
        CheckPatches(typeof(Current).GetProperty(nameof(Current.Game))!.GetMethod!, check);
        foreach (MethodInfo call in scribe) { Require(call.Module.ModuleVersionId == EngineMvid, "Unverified Scribe reader assembly."); CheckPatches(call, check); }
    }
    private static void CheckPatches(MethodInfo method, string check)
    {
        if (Harmony.GetPatchInfo(method) is not { } patches) { return; }
        var seen = new HashSet<(string Role, MethodInfo Hook)>();
        foreach ((string role, IEnumerable<Patch> group) in new[]
            {
                ("prefix", (IEnumerable<Patch>)patches.Prefixes), ("postfix", patches.Postfixes),
                ("transpiler", patches.Transpilers), ("finalizer", patches.Finalizers),
                ("inner-prefix", patches.InnerPrefixes), ("inner-postfix", patches.InnerPostfixes), ("inner-finalizer", patches.InnerFinalizers),
            })
        {
            foreach (Patch patch in group)
            {
                bool own = patch.owner == harmony!.Id && installed.Contains((method, patch.PatchMethod, role, patch.priority))
                    && (patch.before?.Length ?? 0) == 0 && (patch.after?.Length ?? 0) == 0
                    && seen.Add((role, patch.PatchMethod)) && !HasPatches(patch.PatchMethod);
                readers.Add(ObservationPatchEvidence.Describe(method, patch, role, check, own ? "exact corporate observer hook" : null));
                if (!own) { Error("Unverified patch affects pricing/reader: " + method.DeclaringType?.FullName + "." + method.Name + " / " + patch.PatchMethod); }
            }
        }
    }
    private static bool HasPatches(MethodInfo method) => Harmony.GetPatchInfo(method) is { } p
        && p.Prefixes.Count + p.Postfixes.Count + p.Transpilers.Count + p.Finalizers.Count
            + p.InnerPrefixes.Count + p.InnerPostfixes.Count + p.InnerFinalizers.Count != 0;

    public static string SnapshotJson()
    {
        Require(active, "No active corporate pricing observation.");
        CheckContracts("after save");
        if (current is not null || loadedNetwork is not null || System.Threading.Volatile.Read(ref inFlightCompletion) is not null || loadedCalls != 0)
        { Error("A pricing call scope did not unwind."); }
        var events = new JsonArray();
        foreach (Entry entry in entries.Values)
        {
            if (!entry.Completed || !entry.Saved) { Error("A legacy offer did not complete and save: " + entry.Path); }
            events.Add(new JsonObject { ["kind"] = "CorporateOfferPriceCompletion", ["path"] = entry.Path, ["offerId"] = entry.Id,
                ["pawnId"] = entry.PawnId, ["before"] = entry.Before.DeepClone(), ["marketBits"] = entry.MarketBits,
                ["multiplierBits"] = entry.MultiplierBits, ["expectedPrice"] = entry.ExpectedPrice,
                ["expectedReceipt"] = entry.ExpectedReceipt, ["completed"] = entry.Completed, ["saved"] = entry.Saved });
        }
        string json = new JsonObject { ["format"] = 1, ["gameMvid"] = EngineMvid.ToString(), ["events"] = events,
            ["readers"] = readers.DeepClone(), ["errors"] = new JsonArray([.. errors.Select(e => (JsonNode?)JsonValue.Create(e))]) }.ToJsonString();
        Stop();
        return json;
    }

    public static int Apply(XDocument expected, string json)
    {
        JsonObject evidence = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("Missing pricing evidence.");
        Require((int?)evidence["format"] == 1 && (string?)evidence["gameMvid"] == EngineMvid.ToString()
            && evidence["errors"] is JsonArray { Count: 0 } && evidence["events"] is JsonArray, "Pricing evidence contains errors.");
        var edits = new List<(XElement Owner, int Price, int Receipt, bool ReceiptChanged)>();
        var seen = new HashSet<XElement>();
        foreach (JsonNode? raw in evidence["events"]!.AsArray())
        {
            JsonObject item = raw?.AsObject() ?? throw new InvalidOperationException("Malformed pricing event.");
            string path = (string?)item["path"] ?? string.Empty;
            XElement owner = SaveReadTrace.Select(expected, path) ?? throw new InvalidOperationException("Pricing owner is absent.");
            Require((string?)item["kind"] == "CorporateOfferPriceCompletion" && (bool?)item["completed"] == true && (bool?)item["saved"] == true
                && seen.Add(owner) && owner.Name.LocalName == "li" && owner.Parent?.Name.LocalName == "offers"
                && owner.Parent.Parent?.Name.LocalName == "people" && owner.Parent.Parent.Parent?.Attribute("Class")?.Value == NetworkName
                && owner.Parent.Parent.Parent.Parent?.Name.LocalName == "components"
                && owner.Parent.Parent.Parent.Parent.Parent == SaveXml.Game(expected), "Pricing event has no exact unique owner.");
            JsonObject before = item["before"]!.AsObject();
            foreach (string field in Fields)
            {
                XElement[] actual = [.. owner.Elements(field)];
                string? xml = (string?)before[field];
                Require(xml is null ? actual.Length == 0 : actual.Length == 1 && XNode.DeepEquals(actual[0], XElement.Parse(xml, LoadOptions.PreserveWhitespace)), "Pricing source changed: " + field);
            }
            Require(owner.Element("id")?.Value == ((int)item["offerId"]!).ToString(CultureInfo.InvariantCulture)
                && owner.Element("pawn")?.Value == (string?)item["pawnId"]
                && owner.Element("continuation")?.Attribute("Class")?.Value == CompletionName, "Pricing identities or continuation differ.");
            int price = Calculate((int)item["marketBits"]!, (int)item["multiplierBits"]!);
            Require(price == (int?)item["expectedPrice"], "Pricing arithmetic evidence is inconsistent.");
            int priorReceipt = owner.Element("paidAmount") is { } receipt ? int.Parse(receipt.Value, CultureInfo.InvariantCulture) : 0;
            bool changedReceipt = owner.Element("paid") is { } paid && bool.Parse(paid.Value) && priorReceipt < 0;
            int expectedReceipt = changedReceipt ? int.Parse(owner.Element("price")!.Value, CultureInfo.InvariantCulture) : priorReceipt;
            Require(expectedReceipt == (int?)item["expectedReceipt"], "Receipt evidence is inconsistent.");
            edits.Add((owner, price, expectedReceipt, changedReceipt));
        }
        foreach (var edit in edits)
        {
            edit.Owner.SetElementValue("price", edit.Price.ToString(CultureInfo.InvariantCulture));
            if (edit.ReceiptChanged) { edit.Owner.SetElementValue("paidAmount", edit.Receipt.ToString(CultureInfo.InvariantCulture)); }
            edit.Owner.Element("continuation")!.ReplaceWith(new XElement("continuation", new XAttribute("IsNull", "True")));
        }
        return edits.Count;
    }

    private sealed class Entry(object offer, object network, int id, string pawnId, string path, XmlElement source, XmlElement pawnSource)
    {
        internal readonly object Offer = offer;
        internal readonly object Network = network;
        internal readonly int Id = id;
        internal readonly string PawnId = pawnId;
        internal readonly string Path = path;
        internal readonly XmlElement Source = (XmlElement)source.CloneNode(true);
        internal readonly XmlElement PawnSource = (XmlElement)pawnSource.CloneNode(true);
        internal readonly JsonObject Before = new([.. Fields.Select(f => new KeyValuePair<string, JsonNode?>(f, source[f]?.OuterXml is { } text ? JsonValue.Create(text) : null))]);
        internal object? Continuation;
        internal Pawn? Pawn;
        internal bool Loaded, Resolved, MarketReturned, PriceReturned, ContinuationReturned, Completed, Saved;
        internal int ContinuationCalls, PriceCalls, MarketCalls, PriceDepth, MarketBits, MultiplierBits, ExpectedPrice, ExpectedReceipt;
    }
    private sealed record ExposeCall(Entry? Entry, LoadSaveMode Mode, int Thread);
    private readonly record struct LoadedScope(object? Previous, int Generation, bool Bound);
    private readonly record struct CompleteScope(Entry? Previous, int Generation, Entry? Bound, Entry? PreviousFlight);
}
