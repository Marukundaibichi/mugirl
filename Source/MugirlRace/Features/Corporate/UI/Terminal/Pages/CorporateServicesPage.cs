using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

/// <summary>客户服务页：服务等级与各级权益、每周礼品、免费人员与支援小队呼叫。</summary>
internal sealed class CorporateServicesPage(Dialog_CorporateTerminal terminal) : CorporateTerminalPage(terminal)
{
    private UVector2 scroll;
    private float contentHeight = 1000f;

    internal override void Draw(Rect rect, CorporateNetwork network)
    {
        float width = rect.width - 18f;
        CorporateUI.BeginScrollView(rect, ref scroll, new Rect(0f, 0f, width, UnityMathf.Max(rect.height, contentHeight)), "services");
        float y = 0f;
        try
        {
            ServiceText(ref y, width, "Mugirl.CorporateServices.Status".Translate(network.ServiceLevel,
                network.TradeTurnover.ToString("N0")), GameFont.Medium);
            ServiceText(ref y, width, "Mugirl.CorporateServices.Counting".Translate());
            ServiceText(ref y, width, "Mugirl.CorporateServices.Current".Translate(
                ((1f - network.OrderDiscountFactor) * 100f).ToString("0"), network.WeeklyStockCount,
                (Math.Max(0, network.NextRefreshTick - CorporateNetwork.Now) / 60000f).ToString("0.0")));
            for (int level = 1; level <= CorporateNetwork.MaxServiceLevel; level++)
            {
                CorporateUI.Rule(new Rect(0f, y, width, 1f));
                y += 10f;
                string state = (network.ServiceLevel >= level ? "Mugirl.CorporateServices.Active" : "Mugirl.CorporateServices.Locked").Translate();
                ServiceText(ref y, width, "Mugirl.CorporateServices.Tier".Translate(level,
                    CorporateNetwork.ServiceThreshold(level).ToString("N0"), state));
                ServiceText(ref y, width, CorporateNetwork.ServiceBenefitKey(level).Translate());
            }

            bool canTrade = network.CanTrade(Context, out string? reason);
            if (!canTrade && reason is not null)
            {
                ServiceText(ref y, width, reason);
            }

            if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateServices.ClaimGift".Translate(),
                canTrade && network.WeeklyGiftAvailable, id: "services/gift"))
            {
                bool success = network.TryClaimWeeklyGift(Context, out string? giftReason);
                Terminal.ShowFeedback(success, success ? "Mugirl.CorporateServices.GiftSuccess".Translate().ToString() : giftReason);
            }

            y += 44f;
            ServiceText(ref y, width, (network.WeeklyPersonAvailable ? "Mugirl.CorporateServices.FreeAvailable"
                : "Mugirl.CorporateServices.FreeUnavailable").Translate());
            if (!ModsConfig.IdeologyActive)
            {
                ServiceText(ref y, width, "Mugirl.CorporatePeople.RequiresIdeology".Translate());
            }

            if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateServices.OpenPeople".Translate(), id: "services/people"))
            {
                Terminal.RequestPage(Dialog_CorporateTerminal.PeoplePage);
            }

            y += 44f;
            ServiceText(ref y, width, "Mugirl.CorporateServices.SupportAllowance".Translate(
                network.SupportSquadSize, network.WeeklySupportCallsRemaining, network.WeeklySupportLimit));
            if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateServices.CallSupport".Translate(),
                canTrade && network.WeeklySupportAvailable, id: "services/support"))
            {
                ShowSupportMaps();
            }

            y += 44f;
            ServiceText(ref y, width, "Mugirl.CorporateServices.WeeklyHint".Translate());
        }
        finally
        {
            CorporateUI.EndScrollView();
        }

        if (GuiPhase.IsLayout)
        {
            contentHeight = y + 12f;
        }
    }

    private static void ServiceText(ref float y, float width, string text, GameFont font = GameFont.Small)
    {
        float height;
        using (new TextBlock(font))
        {
            height = UnityMathf.Max(25f, Text.CalcHeight(text, width));
        }

        CorporateUI.Label(new Rect(0f, y, width, height), text, font);
        y += height + 12f;
    }

    private void ShowSupportMaps()
    {
        List<FloatMenuOption> options = [];
        List<Map>? maps = GameUtility.LoadedMaps;
        if (maps is not null)
        {
            foreach (Map map in maps)
            {
                Map destination = map;
                options.Add(new FloatMenuOption(map.Parent.LabelCap, () => BeginSupportTargeting(destination)));
            }
        }

        Terminal.ShowMenu(options);
    }

    private void BeginSupportTargeting(Map map)
    {
        if (GameUtility.LoadedMaps?.Contains(map) != true)
        {
            return;
        }

        Terminal.CloseThen(() => StartSupportTargeting(map));
    }

    private void StartSupportTargeting(Map map)
    {
        if (!GameUtility.TryShowMap(map))
        {
            return;
        }

        TickUtility.Pause();
        Messages.Message("Mugirl.CorporateServices.TargetHint".Translate(), MessageTypeDefOf.NeutralEvent, false);
        GameUtility.TryBeginTargeting(new TargetingParameters
        {
            canTargetLocations = true,
            canTargetPawns = false,
            canTargetBuildings = false,
            validator = target => GameUtility.IsCurrentMap(map) && CorporateNetwork.ValidSupportCell(map, target.Cell),
        }, target =>
        {
            CorporateNetwork? network = CorporateNetwork.Current;
            if (network is null || !GameUtility.IsCurrentMap(map))
            {
                return;
            }

            bool success = network.TryCallSupport(Context, map, target.Cell, out string? reason);
            Messages.Message(success ? "Mugirl.CorporateServices.SupportSuccess".Translate().ToString() : reason,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }, null);
    }
}
