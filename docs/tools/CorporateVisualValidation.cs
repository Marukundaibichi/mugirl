// 仅由 EnableCorporateValidation=true 条件编译。此探针不属于正常发布版本。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Text;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal sealed class CorporateVisualValidation
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private bool enabled;
        private bool started;
        private bool finished;
        private bool compactRequested;
        private bool compactPass;
        private bool narrowPass;
        private float startedAt;
        private float nextAt;
        private float nextArrivalDiagnosticAt;
        private int phase;
        private int page;
        private string outputRoot;
        private string pendingShot;
        private DateTime pendingShotSince;
        private Map map;
        private Pawn negotiator;
        private Window_CorporateComms terminal;
        private CorporateAnimatedWindow modal;
        private int confirmCalls;
        private int cancelCalls;
        private int choiceCalls;
        private int selectionCalls;
        private CorporateStock expectedStock;
        private int expectedPerson;
        private bool awaitingReload;
        private int roundtripMapId;
        private int roundtripNegotiatorId;
        private int roundtripOrderId;
        private string roundtripSnapshot;
        private readonly VisualReport report = new VisualReport();

        public CorporateVisualValidation(Game game)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (!HasFlag(arguments, "mugirlCorporateVisual")) return;
            string requestedFolder = ArgumentValue(arguments, "savedatafolder");
            if (!AllowedSaveFolder(requestedFolder))
            {
                Log.Warning("[CorporateVisualValidation] Disabled: savedatafolder must be an explicit isolated temporary directory.");
                return;
            }
            outputRoot = Path.Combine(Path.GetFullPath(requestedFolder), "ValidationShots");
            compactRequested = HasFlag(arguments, "mugirlCorporateVisualCompact");
            enabled = true;
        }

        internal bool AwaitingReload => enabled && !finished && awaitingReload;

        internal void ResumeAfterReload()
        {
            // The report and scalar expectations belong to the probe, while every
            // reference to the prior Game must be replaced after the actual load.
            map = null;
            negotiator = null;
            terminal = null;
            modal = null;
            expectedStock = null;
            awaitingReload = false;
            phase = 52;
            Wait(0.5f);
        }

        internal void Update()
        {
            if (!enabled || finished || Current.ProgramState != ProgramState.Playing) return;
            try
            {
                if (!started)
                {
                    // 再核对引擎实际采用的路径；不相信仅传入但未生效的命令行目录。
                    string requested = Path.GetDirectoryName(outputRoot);
                    if (!string.Equals(Normalize(GenFilePaths.SaveDataFolderPath), Normalize(requested), StringComparison.OrdinalIgnoreCase))
                    {
                        enabled = false;
                        Log.Warning("[CorporateVisualValidation] Disabled: active save path does not match the isolated requested path.");
                        return;
                    }
                    Directory.CreateDirectory(outputRoot);
                    started = true;
                    startedAt = Time.realtimeSinceStartup;
                    nextAt = startedAt + 2f;
                    Application.runInBackground = true;
                    Application.logMessageReceived += OnLog;
                    Prefs.UIScale = 1f;
                    Screen.SetResolution(1920, 1080, false);
                    report.saveRoot = requested;
                    report.mode = "Actual RimWorld Unity window; isolated validation only";
                    report.steps.Add("Safety gates passed; actual save folder verified.");
                    WriteProgress("waiting-for-colony");
                    return;
                }
                if (Time.realtimeSinceStartup - startedAt > 420f) throw new TimeoutException("Visual validation exceeded 420 seconds after entering play.");
                if (awaitingReload) return;
                if (Time.realtimeSinceStartup < nextAt) return;
                if (pendingShot != null)
                {
                    if (!File.Exists(pendingShot)) return;
                    FileInfo screenshot = new FileInfo(pendingShot);
                    if (screenshot.Length == 0 || screenshot.LastWriteTimeUtc < pendingShotSince) return;
                    report.screenshots.Add(pendingShot);
                    pendingShot = null;
                }
                Advance();
            }
            catch (Exception exception)
            {
                report.errors.Add(exception.ToString());
                Finish(false);
            }
        }

        private void Advance()
        {
            CorporateIntroduction introduction = CorporateIntroduction.Current;
            switch (phase)
            {
                case 0:
                    map = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
                    if (map == null || introduction == null) return;
                    // 开局信息窗会暂停原版空投舱落地；先恢复隔离场景，再等待殖民者真正出现。
                    CloseMessageBoxes();
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    negotiator = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Dead && !p.Downed
                        && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking));
                    if (negotiator == null) return;
                    if (introduction.Completed) throw new InvalidOperationException("Use a fresh validation save with an unfinished corporate introduction.");
                    // 只在一次性隔离环境准备前置；之后走真实来访服务和真实对白选项。
                    Set(introduction, "pending", true);
                    Set(introduction, "initialized", true);
                    Set(introduction, "courierChoice", 1);
                    Set(introduction, "contactTick", CorporateNetwork.Now);
                    Set(introduction, "preferredMap", map);
                    Set(introduction, "nextServiceTick", 0);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    report.steps.Add("Registered introduction and allowed its real visitor service to run.");
                    phase = 1;
                    Wait(1f);
                    return;
                case 1:
                    RecordArrivalConditions(introduction);
                    if (introduction.Representative?.Spawned != true) return;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(introduction.Representative);
                    CameraJumper.TryJumpAndSelect(introduction.Representative);
                    report.representative = introduction.Representative.LabelShortCap;
                    Capture("01-representative-arrived");
                    phase = 2;
                    return;
                case 2:
                    introduction.ShowDialog(negotiator, introduction.Representative);
                    if (!Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Any()) throw new InvalidOperationException("Introduction did not open its dialogue.");
                    report.steps.Add("Opened real introduction Dialog_NodeTree.");
                    phase = 3;
                    Wait(2f);
                    return;
                case 3:
                    Capture("02-introduction-dialogue");
                    phase = 4;
                    return;
                case 4:
                    Dialog_NodeTree dialogue = Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Last();
                    DiaNode node = (DiaNode)typeof(Dialog_NodeTree).GetField("curNode", Fields).GetValue(dialogue);
                    FieldInfo optionText = typeof(DiaOption).GetField("text", Fields);
                    string acceptText = "Mugirl.CorporateIntro.Accept".Translate();
                    DiaOption accept = node.options.Single(o => (string)optionText.GetValue(o) == acceptText);
                    if (accept.disabled) throw new InvalidOperationException("The actual introduction accept option is disabled.");
                    // Activate 关闭原始对话并调用业务 action，不直接改 completed 或调用私有 Complete。
                    typeof(DiaOption).GetMethod("Activate", Fields).Invoke(accept, null);
                    if (!introduction.Completed || CorporateNetwork.Current?.Unlocked != true)
                        throw new InvalidOperationException("Accepting the actual DiaOption did not unlock the network.");
                    report.introductionCompleted = true;
                    report.steps.Add("Activated actual DiaOption; verified introduction completion and network unlock.");
                    PrepareTradeFixture();
                    phase = 5;
                    Wait(1.5f);
                    return;
                case 5:
                    CloseMessageBoxes();
                    OpenTerminal();
                    page = 0;
                    phase = 6;
                    Wait(2f);
                    return;
                case 6:
                    if (terminal == null || !Find.WindowStack.Windows.Contains(terminal)) throw new InvalidOperationException("Terminal disappeared before page capture.");
                    int previousPage = (int)typeof(Window_CorporateComms).GetField("selectedPage", Fields).GetValue(terminal);
                    terminal.RequestPage(page);
                    if (previousPage != page && (int)typeof(Window_CorporateComms).GetField("selectedPage", Fields).GetValue(terminal) != previousPage)
                        throw new InvalidOperationException("Page navigation replaced content before its exit animation.");
                    phase = !compactPass && page == 1 ? 20 : !compactPass && page == 2 ? 21 : 7;
                    Wait(phase == 20 ? 0.055f : phase == 21 ? 0.19f : 0.7f);
                    return;
                case 7:
                    if ((int)typeof(Window_CorporateComms).GetField("selectedPage", Fields).GetValue(terminal) != page)
                        throw new InvalidOperationException("Animated navigation did not commit the requested page.");
                    string[] names = { "overview", "supplies", "orders", "people", "finance", "missions", "story", "records" };
                    Capture(CapturePrefix + names[page]);
                    int capturedPage = page;
                    page++;
                    phase = capturedPage == 2 ? 32 : capturedPage == 3 ? 30 : page < names.Length ? 6 : 8;
                    return;
                case 8:
                    if (!compactPass)
                    {
                        phase = 40;
                        Wait(0.05f);
                    }
                    else BeginTerminalClose();
                    return;
                case 9:
                    Finish(report.errors.Count == 0 && report.introductionCompleted && report.screenshots.Count >= 13
                        && confirmCalls == 1 && cancelCalls == 0 && choiceCalls == 1 && report.animatedCloseVerified
                        && report.saveMigrationRoundtripVerified);
                    return;
                case 10:
                    CorporatePersonOffer personOffer = CorporateNetwork.Current.PeopleOffers.First(o => !o.delivered && o.pawn != null);
                    modal = new CorporateConfirmWindow("Mugirl.CorporatePeople.BuyConfirm".Translate(
                        personOffer.pawn.LabelShortCap, CorporateUI.Money(personOffer.price), map.Parent.LabelCap), () => confirmCalls++);
                    Find.WindowStack.Add(modal);
                    phase = 11;
                    Wait(0.55f);
                    return;
                case 11:
                    Capture("normal-confirm-dialog");
                    phase = 12;
                    return;
                case 12:
                    Action confirmation = (Action)typeof(CorporateConfirmWindow).GetField("confirmed", Fields).GetValue(modal);
                    QueueClose(modal, confirmation);
                    QueueClose(modal, confirmation);
                    RequireClosing(modal, "Confirmation");
                    if (confirmCalls != 0) throw new InvalidOperationException("Confirmation ran before its visual exit completed.");
                    phase = 13;
                    Wait(0.4f);
                    return;
                case 13:
                    RequireRemoved(modal, "Confirmation");
                    if (confirmCalls != 1) throw new InvalidOperationException("Repeated confirmation did not execute exactly once.");
                    report.steps.Add("Confirmation remained modal during exit; callback executed exactly once after removal despite a repeated request.");
                    modal = new CorporateConfirmWindow("Mugirl.CorporateUI.Hostile".Translate(), () => cancelCalls++, true);
                    Find.WindowStack.Add(modal);
                    phase = 14;
                    Wait(0.4f);
                    return;
                case 14:
                    // WindowStack's direct removal path also covers outside-click
                    // requests. It must consult OnCloseRequest and defer removal.
                    if (Find.WindowStack.TryRemove(modal, false))
                        throw new InvalidOperationException("WindowStack bypassed animated OnCloseRequest.");
                    RequireClosing(modal, "Cancel");
                    phase = 15;
                    Wait(0.4f);
                    return;
                case 15:
                    RequireRemoved(modal, "Cancel");
                    if (cancelCalls != 0) throw new InvalidOperationException("Cancelling a confirmation executed its transaction.");
                    report.steps.Add("Direct WindowStack close deferred correctly; cancelling executed no transaction.");
                    var choices = new List<FloatMenuOption>();
                    string[] choiceKeys = { "Overview", "Trade", "Orders", "People", "Finance", "Missions", "Introduction", "History" };
                    foreach (string key in choiceKeys)
                        choices.Add(new FloatMenuOption(("Mugirl.CorporateUI.Page." + key).Translate(), () => choiceCalls++));
                    choices.Insert(2, new FloatMenuOption("Mugirl.CorporateUI.ConnectionLost".Translate(), null));
                    modal = new CorporateChoiceWindow(choices);
                    Find.WindowStack.Add(modal);
                    var copiedChoices = (List<FloatMenuOption>)typeof(CorporateChoiceWindow).GetField("options", Fields).GetValue(modal);
                    if (!copiedChoices[2].Disabled || copiedChoices[2].action != null)
                        throw new InvalidOperationException("Choice dialog did not preserve a disabled option.");
                    phase = 16;
                    Wait(0.55f);
                    return;
                case 16:
                    Capture("normal-choice-dialog");
                    phase = 17;
                    return;
                case 17:
                    var actualChoices = (List<FloatMenuOption>)typeof(CorporateChoiceWindow).GetField("options", Fields).GetValue(modal);
                    QueueClose(modal, actualChoices[0].action);
                    QueueClose(modal, actualChoices[0].action);
                    RequireClosing(modal, "Choice");
                    if (choiceCalls != 0) throw new InvalidOperationException("Choice action ran before its exit completed.");
                    phase = 18;
                    Wait(0.4f);
                    return;
                case 18:
                    RequireRemoved(modal, "Choice");
                    if (choiceCalls != 1) throw new InvalidOperationException("Repeated choice did not execute exactly once.");
                    modal = null;
                    report.steps.Add("Choice dialog preserved disabled entries and invoked its selected action exactly once after exit.");
                    BeginTerminalClose();
                    return;
                case 19:
                    RequireRemoved(terminal, "Terminal");
                    report.animatedCloseVerified = true;
                    report.steps.Add((compactPass ? "Compact" : "Normal") + " terminal close retained its stack entry during exit and removed it after the duration.");
                    terminal = null;
                    if (compactRequested && !compactPass)
                    {
                        compactPass = true;
                        Prefs.UIScale = 1f;
                        Screen.SetResolution(1280, 720, false);
                        report.steps.Add("Requested compact pass: 1280x720 with 100% UI scaling (within the game's supported logical resolution).");
                        phase = 5;
                        Wait(3f);
                    }
                    else if (compactRequested && !narrowPass)
                    {
                        narrowPass = true;
                        report.steps.Add("Requested narrow terminal pass: 940px window width at 1280x720.");
                        phase = 5;
                        Wait(1f);
                    }
                    else
                    {
                        phase = 50;
                        Wait(2f);
                    }
                    return;
                case 20:
                    float pageOpacity = (float)typeof(Window_CorporateComms).GetProperty("ContentVisibility", Fields).GetValue(terminal, null);
                    report.steps.Add("Sampled live overview-to-supplies transition at content opacity " + pageOpacity.ToString("0.000", CultureInfo.InvariantCulture) + ".");
                    Capture("normal-page-transition-out");
                    phase = 7;
                    return;
                case 21:
                    float incomingOpacity = (float)typeof(Window_CorporateComms).GetProperty("ContentVisibility", Fields).GetValue(terminal, null);
                    report.steps.Add("Sampled live supplies-to-orders transition at content opacity " + incomingOpacity.ToString("0.000", CultureInfo.InvariantCulture) + ".");
                    Capture("normal-page-transition-in");
                    phase = 7;
                    return;
                case 30:
                    Set(terminal, "peopleScroll", new Vector2(0f, 10000f));
                    phase = 31;
                    Wait(0.6f);
                    return;
                case 31:
                    Capture(CapturePrefix + "people-health-apparel");
                    phase = 6;
                    return;
                case 32:
                    Set(terminal, "orderListView", true);
                    Set(terminal, "orderCategory", 6);
                    Set(terminal, "orderSearch", DefDatabase<ThingDef>.GetNamed("MechSerumHealer").label);
                    phase = 33;
                    Wait(0.6f);
                    return;
                case 33:
                    Capture(CapturePrefix + "orders-filtered-list");
                    Set(terminal, "orderPageScroll", new Vector2(0f, 10000f));
                    phase = 34;
                    return;
                case 34:
                    Capture(CapturePrefix + "orders-checkout");
                    phase = 6;
                    return;
                case 40:
                    terminal.RequestPage(1);
                    phase = 41;
                    Wait(0.6f);
                    return;
                case 41:
                    CorporateStock original = (CorporateStock)typeof(Window_CorporateComms).GetField("selectedStock", Fields).GetValue(terminal);
                    var candidates = CorporateNetwork.Current.Stock.Where(s => s.count > 0 && s != original).Take(2).ToList();
                    expectedStock = candidates.Last();
                    object transition = typeof(Window_CorporateComms).GetField("tradeSelectionMotion", Fields).GetValue(terminal);
                    MethodInfo request = transition.GetType().GetMethod("Request", Fields);
                    foreach (CorporateStock candidate in candidates)
                    {
                        CorporateStock next = candidate;
                        request.Invoke(transition, new object[] { (Action)(() => { selectionCalls++; Set(terminal, "selectedStock", next); }) });
                    }
                    if (selectionCalls != 0 || (CorporateStock)typeof(Window_CorporateComms).GetField("selectedStock", Fields).GetValue(terminal) != original)
                        throw new InvalidOperationException("Product selection changed before its exit animation.");
                    phase = 42;
                    Wait(0.6f);
                    return;
                case 42:
                    if (selectionCalls != 1 || (CorporateStock)typeof(Window_CorporateComms).GetField("selectedStock", Fields).GetValue(terminal) != expectedStock)
                        throw new InvalidOperationException("Rapid product selection did not settle once on the last request.");
                    report.steps.Add("Rapid catalogue selection retained outgoing data, committed the final selection once and completed its local fade.");
                    Capture("normal-product-selection");
                    phase = 43;
                    return;
                case 43:
                    terminal.RequestPage(3);
                    phase = 44;
                    Wait(0.6f);
                    return;
                case 44:
                    int oldPerson = (int)typeof(Window_CorporateComms).GetField("peopleSelectedId", Fields).GetValue(terminal);
                    expectedPerson = CorporateNetwork.Current.PeopleOffers.First(o => !o.delivered && o.pawn != null && o.pawn.thingIDNumber != oldPerson).pawn.thingIDNumber;
                    RequestFixtureContent(() => { Set(terminal, "peopleSelectedId", expectedPerson); Set(terminal, "peopleScroll", Vector2.zero); });
                    if ((int)typeof(Window_CorporateComms).GetField("peopleSelectedId", Fields).GetValue(terminal) != oldPerson)
                        throw new InvalidOperationException("Personnel selection bypassed the content exit animation.");
                    phase = 45;
                    Wait(0.6f);
                    return;
                case 45:
                    if ((int)typeof(Window_CorporateComms).GetField("peopleSelectedId", Fields).GetValue(terminal) != expectedPerson)
                        throw new InvalidOperationException("Personnel selection did not commit.");
                    report.steps.Add("Personnel selection committed after its exit and reset the selected dossier's scroll.");
                    Capture("normal-person-selection");
                    phase = 46;
                    return;
                case 46:
                    RequestFixtureContent(() => typeof(Window_CorporateComms).GetMethod("SelectPeopleMode", Fields).Invoke(terminal, new object[] { true }));
                    phase = 47;
                    Wait(0.6f);
                    return;
                case 47:
                    Capture("normal-people-procurement-empty");
                    phase = 48;
                    return;
                case 48:
                    RequestFixtureContent(() => typeof(Window_CorporateComms).GetMethod("SelectPeopleMode", Fields).Invoke(terminal, new object[] { false }));
                    phase = 10;
                    Wait(0.6f);
                    return;
                case 50:
                    BeginSaveMigrationRoundtrip();
                    return;
                case 52:
                    VerifySaveMigrationRoundtrip();
                    phase = 9;
                    Wait(1f);
                    return;
            }
        }

        private void OpenTerminal()
        {
            // 视觉夹具直接提供有效地图上下文；此步骤不冒充通讯台寻路或据点到场的验收。
            terminal = new Window_CorporateComms(new CorporateTradeContext(map) { Negotiator = negotiator });
            Find.WindowStack.Add(terminal);
            if (narrowPass)
                terminal.windowRect = new Rect((UI.screenWidth - 940f) * 0.5f, terminal.windowRect.y, 940f, terminal.windowRect.height);
            Set(terminal, "selectedStock", CorporateNetwork.Current.Stock.FirstOrDefault(s => s.count > 0));
            Set(terminal, "orderDef", DefDatabase<ThingDef>.GetNamed("MechSerumHealer"));
            typeof(Window_CorporateComms).GetMethod("RefreshOrderPreview", Fields).Invoke(terminal, null);
            report.steps.Add("Opened actual animated monochrome terminal at logical viewport " + UI.screenWidth + "x" + UI.screenHeight + ".");
        }

        private string CapturePrefix => narrowPass ? "narrow-" : compactPass ? "compact-" : "normal-";

        private void RequestFixtureContent(Action change)
        {
            typeof(Window_CorporateComms).GetMethod("RequestContentTransition", Fields).Invoke(terminal, new object[] { change, "validation/selection" });
        }

        private void PrepareTradeFixture()
        {
            CorporateNetwork network = CorporateNetwork.Current;
            IntVec3 center = GenRadial.RadialCellsAround(negotiator.Position, 40f, true)
                .Where(c => c.InBounds(map) && c.Standable(map)).InRandomOrder().Take(40)
                .OrderByDescending(c => Building_OrbitalTradeBeacon.TradeableCellsAround(c, map).Count).First();
            Building_OrbitalTradeBeacon beacon = (Building_OrbitalTradeBeacon)ThingMaker.MakeThing(ThingDefOf.OrbitalTradeBeacon);
            beacon.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(beacon, center, map);
            beacon.TryGetComp<CompPowerTrader>().PowerOn = true;
            foreach (IntVec3 cell in beacon.TradeableCells.Where(c => c.InBounds(map) && c.Standable(map) && c != center).Take(200))
            {
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = ThingDefOf.Silver.stackLimit;
                GenSpawn.Spawn(silver, cell, map);
            }
            CorporateTradeContext trade = new CorporateTradeContext(map) { Negotiator = negotiator };
            CheckPricing("Powered trade beacon exposes fixture silver", trade.SilverCount >= 40000);
            network.EnsureWeeklyOffers();
            CorporatePricingChecks.RunQuotes(network, CheckPricing);
            CorporatePricingChecks.RunContract(network, trade, CheckPricing);
            CorporateSaveMigrationChecks.Run(CheckPricing);
            CorporateCatalogCleanupChecks.Run(network, trade, CheckPricing);
            report.steps.Add("Prepared real goods, weekly personnel and funded trade context in the disposable colony.");
        }

        private void BeginSaveMigrationRoundtrip()
        {
            const string saveName = "CorporateVisualMigrationRoundtrip";
            // Re-check both actual directory and resolved save file immediately
            // before touching XML; only this probe's disposable save is writable.
            string savePath = IsolatedSavePath(saveName);
            CheckPricing("Roundtrip runs in the explicitly selected isolated save directory", savePath != null);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Prefs.PauseOnLoad = true;
            CorporateNetwork network = CorporateNetwork.Current;
            var trade = new CorporateTradeContext(map) { Negotiator = negotiator };
            bool placed = network.PlaceOrder(trade, ThingDefOf.Steel, null, QualityCategory.Normal, 2, out string reason);
            CheckPricing("Migration roundtrip retains a real paid pending order: " + reason, placed);
            roundtripOrderId = network.Orders.Last().id;
            roundtripMapId = map.uniqueID;
            roundtripNegotiatorId = negotiator.thingIDNumber;
            roundtripSnapshot = CorporateSnapshot(network);
            CheckPricing("Active game contains no validation GameComponents",
                Current.Game.components.All(c => c != null && !IsLegacyDriver(c.GetType().FullName)));

            GameDataSaveLoader.SaveGame(saveName);
            CheckPricing("Disposable roundtrip save was written", File.Exists(savePath));
            XmlDocument saved = ReadSave(savePath);
            XmlNode components = saved.SelectSingleNode("/savegame/game/components");
            CheckPricing("Unmodified roundtrip save has no validation driver records",
                components != null && !HasLegacyDrivers(components));
            foreach (string className in new[] { "Mugirl.CorporateRuntimeValidation", "Mugirl.CorporateVisualValidation" })
            {
                XmlElement node = saved.CreateElement("li");
                node.SetAttribute("Class", className);
                components.AppendChild(node);
            }
            // Confirm the in-memory mutation affects precisely the two obsolete
            // test nodes before saving this explicitly named disposable fixture.
            XmlDocument unchanged = ReadSave(savePath);
            XmlDocument cleaned = (XmlDocument)saved.CloneNode(true);
            int removed = CorporateValidationSaveMigration.RemoveLegacyComponents(cleaned.SelectSingleNode("/savegame/game"));
            CheckPricing("Injected fixture differs only by the two exact obsolete test nodes",
                removed == 2 && cleaned.OuterXml == unchanged.OuterXml);
            saved.Save(savePath);
            CheckPricing("Disposable fixture contains both legacy driver nodes before actual load",
                ReadSave(savePath).SelectNodes("/savegame/game/components/li[@Class='Mugirl.CorporateRuntimeValidation' or @Class='Mugirl.CorporateVisualValidation']").Count == 2);
            phase = 51;
            awaitingReload = true;
            report.steps.Add("Loading the disposable fixture through the real GameDataSaveLoader with both obsolete driver nodes present.");
            WriteProgress("loading-save-migration-fixture");
            GameDataSaveLoader.LoadGame(saveName);
        }

        private void VerifySaveMigrationRoundtrip()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            map = Find.Maps.SingleOrDefault(m => m.uniqueID == roundtripMapId);
            negotiator = map?.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.thingIDNumber == roundtripNegotiatorId);
            CheckPricing("Actual migration load restores the colony and original negotiator", map != null && negotiator != null);
            CorporateNetwork network = CorporateNetwork.Current;
            CheckPricing("Actual migration load preserves account, custody, offers and contract state",
                network != null && CorporateSnapshot(network) == roundtripSnapshot);
            CorporateOrder order = network.Orders.FirstOrDefault(o => o.id == roundtripOrderId);
            CheckPricing("Pending paid order keeps its unique physical goods and vault ownership after migration",
                order != null && order.state == CorporateOrderState.Preparing && order.goods.Count > 0
                && order.goods.All(t => t != null && t.holdingOwner == network.Vault)
                && network.Vault.Distinct().Count() == network.Vault.Count);
            CheckPricing("Actual load contains only valid game components and no validation drivers",
                Current.Game.components.All(c => c != null && !IsLegacyDriver(c.GetType().FullName)));

            const string resaveName = "CorporateVisualMigrationResaved";
            string resavePath = IsolatedSavePath(resaveName);
            GameDataSaveLoader.SaveGame(resaveName);
            XmlDocument resaved = ReadSave(resavePath);
            XmlNode components = resaved.SelectSingleNode("/savegame/game/components");
            CheckPricing("Saving the migrated game does not write obsolete or active validation drivers",
                components != null && !HasLegacyDrivers(components));
            CheckPricing("Real save-load-resave produced no error log entries", report.errors.Count == 0);
            report.saveMigrationRoundtripVerified = true;
            report.steps.Add("Completed real save/load/resave migration; original player saves were never accessed by the probe.");
            WriteProgress("save-migration-roundtrip-verified");
        }

        private string IsolatedSavePath(string saveName)
        {
            string requested = Normalize(Path.GetDirectoryName(outputRoot));
            if (!AllowedSaveFolder(requested)
                || !string.Equals(Normalize(GenFilePaths.SaveDataFolderPath), requested, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save migration probe lost its isolated directory gate.");
            string file = Path.GetFullPath(GenFilePaths.FilePathForSavedGame(saveName));
            if (!IsChildOf(file, requested) || !AllowedSaveFolder(Path.GetDirectoryName(file)))
                throw new InvalidOperationException("Save migration fixture is outside the isolated save directory.");
            return file;
        }

        private static XmlDocument ReadSave(string path)
        {
            var document = new XmlDocument { PreserveWhitespace = true };
            document.Load(path);
            return document;
        }

        private static bool IsLegacyDriver(string className) => className == "Mugirl.CorporateRuntimeValidation"
            || className == "Mugirl.CorporateVisualValidation";

        private static bool HasLegacyDrivers(XmlNode components) => components.ChildNodes.Cast<XmlNode>()
            .Any(node => node.Name == "li" && IsLegacyDriver(node.Attributes?["Class"]?.Value));

        private static string CorporateSnapshot(CorporateNetwork network)
        {
            return network.Unlocked + "|" + network.NextRefreshTick + "|" + network.LoanBalance + "|"
                + string.Join(";", network.Vault.Select(t => t.ThingID + ":" + t.stackCount).OrderBy(id => id)) + "|"
                + string.Join(";", network.Stock.Select(s => s.id + ":" + s.sample?.ThingID + ":" + s.count + ":" + s.unitPrice)) + "|"
                + string.Join(";", network.Orders.Select(o => o.id + ":" + o.def?.defName + ":" + o.quantity + ":"
                    + o.deliveredQuantity + ":" + o.paid + ":" + o.createdTick + ":" + o.readyTick + ":" + o.state
                    + ":" + string.Join(",", o.goods.Select(t => t?.ThingID)))) + "|"
                + string.Join(";", network.PeopleOffers.Select(o => o.id + ":" + o.pawn?.ThingID + ":" + o.price + ":" + o.paid + ":" + o.delivered)) + "|"
                + string.Join(";", network.Missions.Select(m => m.id + ":" + m.state));
        }

        private void CheckPricing(string label, bool passed)
        {
            report.steps.Add((passed ? "PASS " : "FAIL ") + label);
            if (!passed) throw new InvalidOperationException("Corporate regression check failed: " + label);
        }

        private void BeginTerminalClose()
        {
            terminal.Close(false);
            terminal.Close(false);
            RequireClosing(terminal, "Terminal");
            phase = 19;
            Wait(0.4f);
        }

        private static void QueueClose(CorporateAnimatedWindow window, Action action)
        {
            typeof(CorporateAnimatedWindow).GetMethod("CloseThen", Fields).Invoke(window, new object[] { action });
        }

        private static void RequireClosing(CorporateAnimatedWindow window, string label)
        {
            if (!window.IsClosing || !Find.WindowStack.Windows.Contains(window))
                throw new InvalidOperationException(label + " was removed immediately instead of animating its close.");
        }

        private static void RequireRemoved(CorporateAnimatedWindow window, string label)
        {
            if (Find.WindowStack.Windows.Contains(window))
                throw new InvalidOperationException(label + " remained in the window stack after its exit duration.");
        }

        private void RecordArrivalConditions(CorporateIntroduction introduction)
        {
            if (Time.realtimeSinceStartup < nextArrivalDiagnosticAt) return;
            nextArrivalDiagnosticAt = Time.realtimeSinceStartup + 5f;
            var text = new StringBuilder();
            text.AppendLine("tick=" + CorporateNetwork.Now + "; speed=" + Find.TickManager.CurTimeSpeed
                + "; pending=" + typeof(CorporateIntroduction).GetField("pending", Fields).GetValue(introduction)
                + "; nextServiceTick=" + typeof(CorporateIntroduction).GetField("nextServiceTick", Fields).GetValue(introduction)
                + "; corporation=" + CorporateNetwork.Current?.CorporateFaction?.Name
                + "; defeated=" + CorporateNetwork.Current?.CorporateFaction?.defeated
                + "; activeThreat=" + GenHostility.AnyHostileActiveThreatToPlayer(map)
                + "; representative=" + introduction.Representative?.ThingID);
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.Downed && p.HostileTo(Faction.OfPlayer)))
                text.AppendLine("hostile=" + pawn.ThingID + "; kind=" + pawn.kindDef.defName + "; fogged=" + pawn.Fogged()
                    + "; dormancyAwake=" + (pawn.TryGetComp<CompCanBeDormant>()?.Awake.ToString() ?? "no-dormancy-comp")
                    + "; active=" + GenHostility.IsActiveThreatToPlayer(pawn) + "; job=" + pawn.CurJobDef?.defName);
            File.AppendAllText(Path.Combine(outputRoot, "arrival-diagnostics.txt"), text.ToString() + Environment.NewLine);
            WriteProgress("waiting-for-representative");
        }

        private void Capture(string name)
        {
            pendingShot = Path.Combine(outputRoot, name + ".png");
            pendingShotSince = DateTime.UtcNow;
            ScreenCapture.CaptureScreenshot(pendingShot);
            report.frames.Add(new VisualFrame
            {
                file = name + ".png", screenWidth = Screen.width, screenHeight = Screen.height,
                uiWidth = UI.screenWidth, uiHeight = UI.screenHeight, uiScale = Prefs.UIScale
            });
            WriteProgress("capturing-" + name);
            Wait(2f);
        }

        private void Wait(float seconds) { nextAt = Time.realtimeSinceStartup + seconds; }
        private static void Set(object instance, string field, object value)
        {
            FieldInfo member = instance.GetType().GetField(field, Fields);
            if (member == null) throw new MissingFieldException(instance.GetType().FullName, field);
            member.SetValue(instance, value);
        }
        private static void CloseMessageBoxes()
        {
            foreach (Dialog_MessageBox box in Find.WindowStack.Windows.OfType<Dialog_MessageBox>().ToList()) box.Close(false);
        }
        private void OnLog(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && report.errors.Count < 100)
                report.errors.Add(message + "\n" + stack);
        }
        private void WriteProgress(string step)
        {
            File.WriteAllText(Path.Combine(outputRoot, "visual-progress.txt"), DateTime.UtcNow.ToString("O") + " " + step);
        }
        private void Finish(bool success)
        {
            if (finished || !started) return;
            finished = true;
            report.success = success;
            report.seconds = Time.realtimeSinceStartup - startedAt;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(Path.Combine(outputRoot, "visual-result.json"), SerializeReport());
            File.WriteAllText(Path.Combine(outputRoot, "visual-result.txt"),
                (success ? "PASS" : "FAIL") + "\n" + string.Join("\n", report.steps) + "\n\n" + string.Join("\n", report.errors));
            // 只退出已通过路径与参数双重验证的本次隔离进程，不操作其他 RimWorld 进程。
            Application.Quit();
        }

        private string SerializeReport()
        {
            return "{\n  \"success\": " + (report.success ? "true" : "false")
                + ",\n  \"introductionCompleted\": " + (report.introductionCompleted ? "true" : "false")
                + ",\n  \"animatedCloseVerified\": " + (report.animatedCloseVerified ? "true" : "false")
                + ",\n  \"saveMigrationRoundtripVerified\": " + (report.saveMigrationRoundtripVerified ? "true" : "false")
                + ",\n  \"confirmCalls\": " + confirmCalls + ",\n  \"cancelCalls\": " + cancelCalls + ",\n  \"choiceCalls\": " + choiceCalls
                + ",\n  \"saveRoot\": " + Quote(report.saveRoot) + ",\n  \"mode\": " + Quote(report.mode)
                + ",\n  \"representative\": " + Quote(report.representative)
                + ",\n  \"seconds\": " + report.seconds.ToString("R", CultureInfo.InvariantCulture)
                + ",\n  \"screenshots\": [" + string.Join(",", report.screenshots.Select(Quote)) + "]"
                + ",\n  \"steps\": [" + string.Join(",", report.steps.Select(Quote)) + "]"
                + ",\n  \"errors\": [" + string.Join(",", report.errors.Select(Quote)) + "]"
                + ",\n  \"frames\": [" + string.Join(",", report.frames.Select(f =>
                    "{\"file\":" + Quote(f.file) + ",\"screenWidth\":" + f.screenWidth + ",\"screenHeight\":" + f.screenHeight
                    + ",\"uiWidth\":" + f.uiWidth + ",\"uiHeight\":" + f.uiHeight + ",\"uiScale\":" + f.uiScale.ToString("R", CultureInfo.InvariantCulture) + "}"))
                + "]\n}\n";
        }

        private static string Quote(string value)
        {
            if (value == null) return "null";
            StringBuilder text = new StringBuilder("\"");
            foreach (char character in value)
            {
                if (character == '\\' || character == '"') { text.Append('\\'); text.Append(character); }
                else if (character < 32) text.Append("\\u" + ((int)character).ToString("x4"));
                else text.Append(character);
            }
            return text.Append('"').ToString();
        }

        private static bool HasFlag(IEnumerable<string> arguments, string name)
        {
            return arguments.Any(a => a.TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        private static string ArgumentValue(IEnumerable<string> arguments, string name)
        {
            string prefix = name + "=";
            string match = arguments.Select(a => a.TrimStart('-')).FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return match?.Substring(prefix.Length).Trim('"');
        }
        private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static bool IsChildOf(string path, string root) => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static bool AllowedSaveFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
            try
            {
                string resolved = Normalize(path);
                for (DirectoryInfo directory = new DirectoryInfo(resolved); directory != null; directory = directory.Parent)
                    if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                string temp = Normalize(Path.GetTempPath());
                if (IsChildOf(resolved, temp)) return true;
                string modRoot = Normalize(Path.Combine(Path.GetDirectoryName(typeof(CorporateNetwork).Assembly.Location), "..", ".."));
                string repositoryTemp = Path.Combine(modRoot, "TMP");
                if (!IsChildOf(resolved, repositoryTemp)) return false;
                string relative = resolved.Substring(repositoryTemp.Length + 1);
                string firstFolder = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                return firstFolder.StartsWith("CorporateValidation", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        [Serializable]
        private sealed class VisualReport
        {
            public bool success;
            public bool introductionCompleted;
            public bool animatedCloseVerified;
            public bool saveMigrationRoundtripVerified;
            public string saveRoot;
            public string mode;
            public string representative;
            public float seconds;
            public List<string> screenshots = new List<string>();
            public List<string> steps = new List<string>();
            public List<string> errors = new List<string>();
            public List<VisualFrame> frames = new List<VisualFrame>();
        }
        [Serializable]
        private sealed class VisualFrame
        {
            public string file;
            public int screenWidth;
            public int screenHeight;
            public int uiWidth;
            public int uiHeight;
            public float uiScale;
        }
    }
}
