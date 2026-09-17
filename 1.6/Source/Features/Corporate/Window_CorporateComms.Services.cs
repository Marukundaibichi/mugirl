using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public partial class Window_CorporateComms
    {
        private Vector2 servicesScroll;
        private float servicesHeight = 1000f;

        private void DrawServices(Rect rect)
        {
            float width = rect.width - 18f;
            CorporateUI.BeginScrollView(rect, ref servicesScroll,
                new Rect(0f, 0f, width, Mathf.Max(rect.height, servicesHeight)), "services");
            float y = 0f;
            try
            {
                ServiceText(ref y, width, "Mugirl.CorporateServices.Status".Translate(network.ServiceLevel,
                    network.TradeTurnover.ToString("N0")), GameFont.Medium);
                ServiceText(ref y, width, "Mugirl.CorporateServices.Counting".Translate());
                ServiceText(ref y, width, "Mugirl.CorporateServices.Current".Translate(
                    ((1f - network.OrderDiscountFactor) * 100f).ToString("0"), network.WeeklyStockCount,
                    (Mathf.Max(0, network.NextRefreshTick - CorporateNetwork.Now) / 60000f).ToString("0.0")));
                for (int level = 1; level <= CorporateNetwork.MaxServiceLevel; level++)
                {
                    CorporateUI.Rule(new Rect(0f, y, width, 1f));
                    y += 10f;
                    string state = (network.ServiceLevel >= level ? "Mugirl.CorporateServices.Active" : "Mugirl.CorporateServices.Locked").Translate();
                    ServiceText(ref y, width, "Mugirl.CorporateServices.Tier".Translate(level,
                        CorporateNetwork.ServiceThreshold(level).ToString("N0"), state));
                    ServiceText(ref y, width, ("Mugirl.CorporateServices.Tier" + level).Translate());
                }
                bool canTrade = network.CanTrade(context, out string reason);
                if (!canTrade) ServiceText(ref y, width, reason);
                if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateServices.ClaimGift".Translate(),
                    canTrade && network.WeeklyGiftAvailable, id: "services/gift"))
                {
                    bool success = network.TryClaimWeeklyGift(context, out reason);
                    ShowFeedback(success, success ? "Mugirl.CorporateServices.GiftSuccess".Translate().ToString() : reason);
                }
                y += 44f;
                ServiceText(ref y, width, (network.WeeklyPersonAvailable ? "Mugirl.CorporateServices.FreeAvailable"
                    : "Mugirl.CorporateServices.FreeUnavailable").Translate());
                if (!ModsConfig.IdeologyActive) ServiceText(ref y, width, "Mugirl.CorporatePeople.RequiresIdeology".Translate());
                if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateServices.OpenPeople".Translate(), id: "services/people")) RequestPage(3);
                y += 44f;
                ServiceText(ref y, width, "Mugirl.CorporateServices.SupportAllowance".Translate(
                    network.SupportSquadSize, network.WeeklySupportCallsRemaining, network.WeeklySupportLimit));
                if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateServices.CallSupport".Translate(),
                    canTrade && network.WeeklySupportAvailable, id: "services/support"))
                {
                    var options = new List<FloatMenuOption>();
                    foreach (Map map in MugirlGameUtility.LoadedMaps)
                    {
                        Map destination = map;
                        options.Add(new FloatMenuOption(map.Parent.LabelCap, () => BeginSupportTargeting(destination)));
                    }
                    ShowMenu(options);
                }
                y += 44f;
                ServiceText(ref y, width, "Mugirl.CorporateServices.WeeklyHint".Translate());
            }
            finally { CorporateUI.EndScrollView(); }
            if (Event.current.type == EventType.Layout) servicesHeight = y + 12f;
        }

        private static void ServiceText(ref float y, float width, string text, GameFont font = GameFont.Small)
        {
            GameFont previous = Text.Font;
            Text.Font = font;
            float height = Mathf.Max(25f, Text.CalcHeight(text, width));
            Text.Font = previous;
            CorporateUI.Label(new Rect(0f, y, width, height), text, font);
            y += height + 12f;
        }

        private void BeginSupportTargeting(Map map)
        {
            if (MugirlGameUtility.LoadedMaps?.Contains(map) != true) return;
            CloseThen(() => StartSupportTargeting(map));
        }

        private void StartSupportTargeting(Map map)
        {
            if (!MugirlGameUtility.TryShowMap(map)) return;
            MugirlTickUtility.Pause();
            Messages.Message("Mugirl.CorporateServices.TargetHint".Translate(), MessageTypeDefOf.NeutralEvent, false);
            MugirlGameUtility.TryBeginTargeting(new TargetingParameters
            {
                canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false,
                validator = target => MugirlGameUtility.IsCurrentMap(map) && CorporateNetwork.ValidSupportCell(map, target.Cell)
            }, target =>
            {
                if (!MugirlGameUtility.IsCurrentMap(map)) return;
                bool success = network.TryCallSupport(context, map, target.Cell, out string reason);
                Messages.Message(success ? "Mugirl.CorporateServices.SupportSuccess".Translate().ToString() : reason,
                    success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
            }, null);
        }
    }
}
