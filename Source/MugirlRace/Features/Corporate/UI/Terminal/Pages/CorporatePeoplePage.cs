using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

/// <summary>人员页：购买与出售两个页签、人员名册（或紧凑模式的下拉选择）、人员档案与买卖操作。</summary>
internal sealed partial class CorporatePeoplePage(Dialog_CorporateTerminal terminal) : CorporateTerminalPage(terminal)
{
    private const float RosterRowHeight = 80f;
    private const float RosterRowStride = RosterRowHeight + 6f;

    // 页面级派生数据缓存：forcePause 模态下数据只在操作（缓存版本变化）或买卖模式切换后重建，
    // 替代每个 GUI 事件的全图枚举与多层 LINQ。
    private readonly List<CorporatePersonOffer> offersView = [];
    private readonly List<Pawn> peopleView = [];
    private readonly Dictionary<Pawn, CorporatePersonOffer> offerByPawn = [];
    private UVector2 dossierScroll;
    private UVector2 rosterScroll;
    private UVector2 headerScroll;
    private float dossierHeight = 900f;
    private bool selling;
    private bool buyAsColonist;
    private int selectedId = -1;
    private bool tipsEnabled;
    private int viewVersion = -1;
    private bool viewSelling;

    internal override void Draw(Rect rect, CorporateNetwork network)
    {
        if (!ModsConfig.IdeologyActive)
        {
            buyAsColonist = true;
        }

        float width = rect.width;
        bool canTrade = network.CanTrade(Context, out string? accessReason);
        float y = DrawHeader(rect, network, canTrade, accessReason);
        EnsureView(network);
        List<Pawn> people = peopleView;
        if (people.Count == 0)
        {
            CorporateUI.Notice(new Rect(0f, y, width, 80f),
                (selling ? "Mugirl.CorporatePeople.NoCandidates" : "Mugirl.CorporatePeople.NoOffers").Translate());
            return;
        }

        Pawn? selected = null;
        for (int i = 0; i < people.Count; i++)
        {
            if (people[i].thingIDNumber == selectedId)
            {
                selected = people[i];
                break;
            }
        }

        if (selected is null)
        {
            selected = people[0];
            selectedId = selected.thingIDNumber;
            dossierScroll = default;
        }

        Rect body = new(0f, y, width, UnityMathf.Max(1f, rect.height - y));
        Rect roster;
        Rect dossier;
        bool compact = width < 760f || body.height < 260f;
        if (!compact)
        {
            float rosterWidth = UnityMathf.Clamp(width * 0.24f, 180f, 226f);
            roster = new Rect(body.x, body.y, rosterWidth, body.height);
            dossier = new Rect(roster.xMax + 20f, body.y, body.width - rosterWidth - 20f, body.height);
            CorporateUI.Rule(new Rect(roster.xMax + 9f, body.y, 1f, body.height));
            DrawRoster(roster, people, network);
        }
        else
        {
            const float rosterHeight = 34f;
            roster = new Rect(body.x, body.y, body.width, rosterHeight);
            dossier = new Rect(body.x, roster.yMax + 12f, body.width, UnityMathf.Max(1f, body.height - rosterHeight - 12f));
            DrawSelector(roster, people, selected);
        }

        CorporatePersonOffer? offer = !selling && offerByPawn.TryGetValue(selected, out CorporatePersonOffer? selectedOffer) ? selectedOffer : null;
        bool asColonist = offer?.paid == true ? offer.buyAsColonist : buyAsColonist;
        CorporatePersonQuote quote = selling ? network.PeopleSaleQuote(selected) : network.PeoplePurchaseQuote(offer, asColonist);
        int price = selling ? quote.total : network.PeoplePurchasePrice(offer, asColonist);
        bool needsSilver = offer is not null && !offer.paid && Terminal.SilverCount < price;
        float footerHeight = needsSilver ? 73f : 48f;
        Rect viewport = new(dossier.x, dossier.y, dossier.width, UnityMathf.Max(1f, dossier.height - footerHeight));
        float contentWidth = UnityMathf.Max(1f, viewport.width - 18f);
        tipsEnabled = Mouse.IsOver(viewport);
        CorporateUI.BeginScrollView(viewport, ref dossierScroll,
            new Rect(0f, 0f, contentWidth, UnityMathf.Max(viewport.height, dossierHeight)), "people/dossier/" + selected.thingIDNumber);
        float bottom = 0f;
        try
        {
            DrawDossier(ref bottom, contentWidth, selected, quote, price, offer is not null && price == 0);
        }
        finally
        {
            CorporateUI.EndScrollView();
        }

        if (GuiPhase.IsLayout)
        {
            dossierHeight = bottom + 10f;
        }

        DrawAction(new Rect(dossier.x, dossier.yMax - footerHeight, dossier.width, footerHeight),
            network, selected, offer, quote, price, asColonist, canTrade, needsSilver);
    }

    private void EnsureView(CorporateNetwork network)
    {
        if (viewVersion == Terminal.CacheVersion && viewSelling == selling)
        {
            return;
        }

        viewVersion = Terminal.CacheVersion;
        viewSelling = selling;
        offersView.Clear();
        IReadOnlyList<CorporatePersonOffer> offers = network.PeopleOffers;
        for (int i = 0; i < offers.Count; i++)
        {
            CorporatePersonOffer offer = offers[i];
            if (!offer.delivered && offer.pawn is not null)
            {
                offersView.Add(offer);
            }
        }

        peopleView.Clear();
        offerByPawn.Clear();
        if (selling)
        {
            peopleView.AddRange(network.PeopleSaleCandidates(Context));
        }

        for (int i = 0; i < offersView.Count; i++)
        {
            Pawn? pawn = offersView[i].pawn;
            if (pawn is null)
            {
                continue;
            }

            if (!selling)
            {
                peopleView.Add(pawn);
            }

            offerByPawn[pawn] = offersView[i];
        }
    }

    private void SelectMode(bool sell)
    {
        selling = sell;
        selectedId = -1;
        dossierScroll = default;
        rosterScroll = default;
        headerScroll = default;
        if (!sell && !ModsConfig.IdeologyActive)
        {
            buyAsColonist = true;
        }
    }

    private float DrawHeader(Rect rect, CorporateNetwork network, bool canTrade, string? accessReason)
    {
        bool showIntro = rect.height >= 410f;
        float contentHeight = 80f + (showIntro ? 25f : 0f) + (!canTrade ? 28f : 0f)
            + (network.PeoplePendingSilver > 0 ? 40f : 0f) + (selling ? 28f : 0f);
        // 页眉提示可以滚动，但不能挤掉所选人员的操作按钮。
        float height = UnityMathf.Min(contentHeight, UnityMathf.Max(40f, rect.height - 160f));
        float width = rect.width - (contentHeight > height ? 18f : 0f);
        tipsEnabled = Mouse.IsOver(new Rect(0f, 0f, rect.width, height));
        CorporateUI.BeginScrollView(new Rect(0f, 0f, rect.width, height), ref headerScroll,
            new Rect(0f, 0f, width, contentHeight), "people/header");
        try
        {
            SingleLine(new Rect(0f, 0f, width, 34f), "Mugirl.CorporatePeople.Title".Translate(), GameFont.Medium);
            if (tipsEnabled)
            {
                TooltipHandler.TipRegionByKey(new Rect(0f, 0f, width, 34f), "Mugirl.CorporatePeople.Intro");
            }

            float y = 36f;
            if (showIntro)
            {
                SingleLine(new Rect(0f, y, width, 23f), "Mugirl.CorporatePeople.Intro".Translate(), GameFont.Tiny, CorporateUI.Muted);
                y += 25f;
            }

            float tabWidth = (width - 8f) / 2f;
            if (CorporateUI.Button(new Rect(0f, y, tabWidth, 34f), "Mugirl.CorporatePeople.BuyTab".Translate(), true, !selling, "people/buy") && selling)
            {
                Terminal.RequestContentTransition(() => SelectMode(false));
            }

            if (CorporateUI.Button(new Rect(tabWidth + 8f, y, tabWidth, 34f), "Mugirl.CorporatePeople.SellTab".Translate(), true, selling, "people/sell") && !selling)
            {
                Terminal.RequestContentTransition(() => SelectMode(true));
            }

            y += 44f;
            if (!canTrade && accessReason is not null)
            {
                HeaderNotice(ref y, width, accessReason);
            }

            if (network.PeoplePendingSilver > 0)
            {
                if (CorporateUI.Button(new Rect(0f, y, width, 32f),
                    "Mugirl.CorporatePeople.ClaimSilver".Translate(CorporateUI.Money(network.PeoplePendingSilver)), true, true, "people/claim-silver"))
                {
                    PeopleResult(network.TryClaimPeopleSilver(Context, out string? reason), reason);
                }

                y += 40f;
            }

            if (selling)
            {
                HeaderNotice(ref y, width, "Mugirl.CorporatePeople.SaleWarning".Translate());
            }
        }
        finally
        {
            CorporateUI.EndScrollView();
        }

        return height + 8f;
    }

    private void HeaderNotice(ref float y, float width, string text)
    {
        SingleLine(new Rect(0f, y, width, 24f), text, GameFont.Tiny, CorporateUI.Muted);
        y += 28f;
    }

    private void DrawSelector(Rect rect, List<Pawn> people, Pawn selected)
    {
        string suffix = "   ·   " + (people.IndexOf(selected) + 1) + " / " + people.Count + "   ▾";
        string name = selected.LabelShortCap;
        string label = FittedText(name, rect.width - Terminal.TextCache.Width(suffix, GameFont.Small) - 20f, GameFont.Small) + suffix;
        if (CorporateUI.WantsTooltip(rect))
        {
            TooltipHandler.TipRegion(rect, name);
        }

        if (!CorporateUI.Button(rect, label, id: "people/select-menu"))
        {
            return;
        }

        List<FloatMenuOption> options = new(people.Count);
        foreach (Pawn person in people)
        {
            int id = person.thingIDNumber;
            options.Add(new FloatMenuOption(person.LabelShortCap, () => Select(id)));
        }

        Terminal.ShowMenu(options);
    }

    private void Select(int id)
    {
        Terminal.RequestContentTransition(() =>
        {
            selectedId = id;
            dossierScroll = default;
        });
    }

    private void DrawRoster(Rect rect, List<Pawn> people, CorporateNetwork network)
    {
        float width = rect.width - 18f;
        float height = people.Count * RosterRowStride;
        tipsEnabled = Mouse.IsOver(rect);
        CorporateUI.BeginScrollView(rect, ref rosterScroll,
            new Rect(0f, 0f, width, UnityMathf.Max(rect.height, height)), selling ? "people/roster/True" : "people/roster/False");
        try
        {
            float viewTop = rosterScroll.y;
            float viewBottom = viewTop + rect.height;
            for (int i = 0; i < people.Count; i++)
            {
                Rect row = new(0f, i * RosterRowStride, width, RosterRowHeight);
                // 只绘制视口内的行：屏幕外的行不再每个事件生成肖像与报价。
                if (row.yMax < viewTop || row.y > viewBottom)
                {
                    continue;
                }

                DrawRosterRow(row, people[i], network);
            }
        }
        finally
        {
            CorporateUI.EndScrollView();
        }
    }

    private void DrawRosterRow(Rect row, Pawn pawn, CorporateNetwork network)
    {
        CorporatePersonOffer? offer = selling ? null : offerByPawn.TryGetValue(pawn, out CorporatePersonOffer? rowOffer) ? rowOffer : null;
        int price = selling ? network.PeopleSalePrice(pawn)
            : network.PeoplePurchasePrice(offer, offer?.paid == true ? offer.buyAsColonist : buyAsColonist);
        int id = pawn.thingIDNumber;
        if (CorporateUI.Row(row, id == selectedId, "people/select", id) && selectedId != id)
        {
            Select(id);
        }

        CorporateUI.Texture(new Rect(row.x + 5f, row.y + 7f, 42f, 58f), PortraitsCache.Get(pawn, new UVector2(42f, 58f), Rot4.South));
        float textX = row.x + 53f;
        float textWidth = row.width - 60f;
        SingleLine(new Rect(textX, row.y + 7f, textWidth, 23f), pawn.LabelShortCap);
        SingleLine(new Rect(textX, row.y + 31f, textWidth, 21f),
            "Mugirl.CorporatePeopleCards.Years".Translate(pawn.ageTracker.AgeBiologicalYears), GameFont.Tiny, CorporateUI.Muted);
        SingleLine(new Rect(textX, row.y + 53f, textWidth, 22f),
            offer?.paid == true ? "Mugirl.CorporatePeopleCards.Paid".Translate().ToString() : CorporateUI.Money(price), GameFont.Tiny);
    }

    private void DrawAction(Rect rect, CorporateNetwork network, Pawn pawn, CorporatePersonOffer? offer, CorporatePersonQuote quote,
        int price, bool asColonist, bool canTrade, bool needsSilver)
    {
        CorporateUI.Rule(new Rect(rect.x, rect.y, rect.width, 1f));
        Rect button = new(rect.x, rect.y + 9f, rect.width, 36f);
        CorporateTradeContext context = Context;
        if (selling)
        {
            if (CorporateUI.Button(button, "Mugirl.CorporatePeople.SellPrice".Translate(CorporateUI.Money(price)), canTrade, id: "people/sell", index: pawn.thingIDNumber))
            {
                string gear = (context.Map != null ? "Mugirl.CorporatePeople.MapGear" : "Mugirl.CorporatePeople.CaravanGear").Translate();
                Terminal.Confirm("Mugirl.CorporatePeople.SellConfirmDetailed".Translate(pawn.LabelShortCap,
                    CorporateUI.Money(quote.basePrice), CorporateUI.Money(quote.handlingFee),
                    CorporateUI.Money(quote.shippingFee), CorporateUI.Money(price), gear), () =>
                    {
                        PeopleResult(network.TrySellPerson(context, pawn, price, out string? reason), reason);
                    }, true);
            }

            return;
        }

        if (offer is null)
        {
            return;
        }

        string label = offer.paid ? "Mugirl.CorporatePeople.ClaimPerson".Translate().ToString()
            : price == 0 ? "Mugirl.CorporateServices.FreePerson".Translate().ToString()
            : "Mugirl.CorporatePeople.BuyPrice".Translate(CorporateUI.Money(price)).ToString();
        bool enabled = (asColonist || ModsConfig.IdeologyActive) && (offer.paid || (canTrade && !needsSilver));
        const float actionGap = 8f;
        string modeLabel = (asColonist ? "Mugirl.CorporatePeople.BuyAsColonist" : "Mugirl.CorporatePeople.BuyAsSlave").Translate();
        string modeButtonLabel = modeLabel + "  ▾";
        float maxModeWidth = UnityMathf.Max(160f, UnityMathf.Min(280f, rect.width - 180f));
        float modeWidth = UnityMathf.Clamp(Terminal.TextCache.Width(modeButtonLabel, GameFont.Small) + 20f, 160f, maxModeWidth);
        button = new Rect(button.x, button.y, rect.width - modeWidth - actionGap, button.height);
        Rect modeButton = new(button.xMax + actionGap, button.y, modeWidth, button.height);
        if (CorporateUI.Button(modeButton, FittedText(modeButtonLabel, modeButton.width - 12f, GameFont.Small),
            !offer.paid, id: "people/purchase-mode", index: pawn.thingIDNumber))
        {
            OpenPurchaseMenu();
        }

        if (CorporateUI.WantsTooltip(modeButton))
        {
            TooltipHandler.TipRegion(modeButton, modeLabel);
        }

        string fittedLabel = FittedText(label, button.width - 12f, GameFont.Small);
        if (fittedLabel != label && CorporateUI.WantsTooltip(button))
        {
            TooltipHandler.TipRegion(button, label);
        }

        if (CorporateUI.Button(button, fittedLabel, enabled, true, "people/purchase", pawn.thingIDNumber))
        {
            if (offer.paid)
            {
                Purchase(network, offer, asColonist);
            }
            else
            {
                string confirm = asColonist
                    ? "Mugirl.CorporatePeople.BuyColonistConfirmDetailed".Translate(pawn.LabelShortCap,
                        CorporateUI.Money(quote.basePrice), CorporateUI.Money(quote.colonistPremium),
                        CorporateUI.Money(quote.handlingFee), CorporateUI.Money(quote.shippingFee),
                        CorporateUI.Money(price), context.Label)
                    : "Mugirl.CorporatePeople.BuySlaveConfirmDetailed".Translate(pawn.LabelShortCap,
                        CorporateUI.Money(quote.basePrice), CorporateUI.Money(quote.handlingFee),
                        CorporateUI.Money(quote.shippingFee), CorporateUI.Money(price), context.Label);
                if (price == 0)
                {
                    confirm += "\n\n" + "Mugirl.CorporatePeople.FreeApplied".Translate();
                }

                Terminal.Confirm(confirm, () => Purchase(network, offer, asColonist));
            }
        }

        if (needsSilver)
        {
            CorporateUI.Label(new Rect(rect.x, button.yMax + 4f, rect.width, 22f),
                "Mugirl.CorporatePeople.NoSilver".Translate(), GameFont.Tiny, CorporateUI.Muted);
        }
    }

    private void OpenPurchaseMenu()
    {
        List<FloatMenuOption> modes = [];
        if (ModsConfig.IdeologyActive)
        {
            modes.Add(new FloatMenuOption("Mugirl.CorporatePeople.BuyAsSlave".Translate(), () => SelectPurchaseMode(false)));
        }

        modes.Add(new FloatMenuOption("Mugirl.CorporatePeople.BuyAsColonist".Translate(), () => SelectPurchaseMode(true)));
        GameUtility.TryAddWindow(new FloatMenu(modes));
    }

    private void SelectPurchaseMode(bool asColonist)
    {
        if ((!asColonist && !ModsConfig.IdeologyActive) || buyAsColonist == asColonist)
        {
            return;
        }

        Terminal.RequestContentTransition(() =>
        {
            buyAsColonist = asColonist;
            dossierScroll = default;
        });
    }

    private void Purchase(CorporateNetwork network, CorporatePersonOffer offer, bool asColonist)
        => PeopleResult(network.TryPurchasePerson(Context, offer, asColonist, out string? reason), reason);

    private void PeopleResult(bool success, string? reason)
        => Terminal.ShowFeedback(success, success ? "Mugirl.CorporatePeople.Success".Translate().ToString() : reason);
}
