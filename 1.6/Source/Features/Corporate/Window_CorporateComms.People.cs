using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public partial class Window_CorporateComms
    {
        private Vector2 peopleScroll;
        private float peopleHeight = 1200f;
        private bool peopleSelling;

        private void DrawPeople(Rect rect)
        {
            float width = rect.width - 20f;
            Widgets.BeginScrollView(rect, ref peopleScroll, new Rect(0f, 0f, width, peopleHeight));
            float y = 0f;
            try
            {
                CorporateUI.Heading(ref y, width, "Mugirl.CorporatePeople.Title".Translate(), "Mugirl.CorporatePeople.Intro".Translate());
                if (CorporateUI.Button(new Rect(0f, y, (width - 8f) / 2f, 36f), "Mugirl.CorporatePeople.BuyTab".Translate(), true, !peopleSelling)) peopleSelling = false;
                if (CorporateUI.Button(new Rect((width + 8f) / 2f, y, (width - 8f) / 2f, 36f), "Mugirl.CorporatePeople.SellTab".Translate(), true, peopleSelling)) peopleSelling = true;
                y += 46f;
                string accessReason;
                bool canTrade = network.CanTrade(context, out accessReason);
                if (!canTrade) FinanceText(ref y, width, accessReason, true);
                if (network.PeoplePendingSilver > 0)
                {
                    if (CorporateUI.Button(new Rect(0f, y, width, 36f),
                        "Mugirl.CorporatePeople.ClaimSilver".Translate(CorporateUI.Money(network.PeoplePendingSilver)), true, true))
                    {
                        string reason;
                        PeopleResult(network.TryClaimPeopleSilver(context, out reason), reason);
                    }
                    y += 44f;
                }
                if (!peopleSelling)
                {
                    if (!ModsConfig.IdeologyActive) FinanceText(ref y, width, "Mugirl.CorporatePeople.RequiresIdeology".Translate(), true);
                    var offers = network.PeopleOffers.Where(o => !o.delivered).ToList();
                    if (offers.Count == 0) FinanceText(ref y, width, "Mugirl.CorporatePeople.NoOffers".Translate());
                    foreach (CorporatePersonOffer offer in offers)
                    {
                        if (offer.pawn == null) continue;
                        DrawCorporatePawnCard(ref y, width, offer.pawn);
                        string button = offer.paid ? "Mugirl.CorporatePeople.ClaimPerson".Translate().ToString()
                            : "Mugirl.CorporatePeople.BuyPrice".Translate(CorporateUI.Money(offer.price)).ToString();
                        bool enabled = ModsConfig.IdeologyActive && (offer.paid || canTrade && context.SilverCount >= offer.price);
                        if (CorporateUI.Button(new Rect(0f, y, width, 36f), button, enabled, true))
                        {
                            CorporatePersonOffer selected = offer;
                            if (offer.paid) PurchaseCorporatePerson(selected);
                            else MugirlGameUtility.Windows.Add(Dialog_MessageBox.CreateConfirmation(
                                "Mugirl.CorporatePeople.BuyConfirm".Translate(offer.pawn.LabelShortCap, CorporateUI.Money(offer.price), context.Label),
                                () => PurchaseCorporatePerson(selected)));
                        }
                        y += 44f;
                        if (!offer.paid && context.SilverCount < offer.price)
                            FinanceText(ref y, width, "Mugirl.CorporatePeople.NoSilver".Translate(), true);
                        y += 12f;
                    }
                }
                else
                {
                    FinanceText(ref y, width, "Mugirl.CorporatePeople.SaleWarning".Translate(), true);
                    var candidates = network.PeopleSaleCandidates(context).ToList();
                    if (candidates.Count == 0) FinanceText(ref y, width, "Mugirl.CorporatePeople.NoCandidates".Translate());
                    foreach (Pawn pawn in candidates)
                    {
                        DrawCorporatePawnCard(ref y, width, pawn);
                        int price = network.PeopleSalePrice(pawn);
                        if (CorporateUI.Button(new Rect(0f, y, width, 36f),
                            "Mugirl.CorporatePeople.SellPrice".Translate(CorporateUI.Money(price)), canTrade))
                        {
                            Pawn selected = pawn;
                            string gear = (context.Map != null ? "Mugirl.CorporatePeople.MapGear" : "Mugirl.CorporatePeople.CaravanGear").Translate();
                            MugirlGameUtility.Windows.Add(Dialog_MessageBox.CreateConfirmation(
                                "Mugirl.CorporatePeople.SellConfirm".Translate(pawn.LabelShortCap, CorporateUI.Money(price), gear), () =>
                                {
                                    string reason;
                                    PeopleResult(network.TrySellPerson(context, selected, price, out reason), reason);
                                }, true));
                        }
                        y += 58f;
                    }
                }
            }
            finally { Widgets.EndScrollView(); }
            if (Event.current.type == EventType.Layout) peopleHeight = y + 12f;
        }

        private void DrawCorporatePawnCard(ref float y, float width, Pawn pawn)
        {
            Rect portrait = new Rect(0f, y, 64f, 80f);
            GUI.DrawTexture(portrait, PortraitsCache.Get(pawn, new Vector2(64f, 80f), Rot4.South));
            CorporateUI.Label(new Rect(76f, y, width - 120f, 34f), pawn.LabelShortCap, GameFont.Medium);
            if (CorporateUI.Button(new Rect(width - 38f, y, 38f, 30f), "i")) MugirlGameUtility.Windows.Add(new Dialog_InfoCard(pawn));
            CorporateUI.Label(new Rect(76f, y + 37f, width - 76f, 42f), "Mugirl.CorporatePeople.AgeHealth".Translate(
                pawn.ageTracker.AgeBiologicalYears, pawn.health.summaryHealth.SummaryHealthPercent.ToStringPercent()));
            y += 86f;
            string skills = pawn.skills == null ? "-" : string.Join(" · ", pawn.skills.skills
                .Where(s => !s.TotallyDisabled).OrderByDescending(s => s.Level).Take(6)
                .Select(s => s.def.LabelCap + " " + s.Level + (s.passion == Passion.Major ? " **" : s.passion == Passion.Minor ? " *" : "")).ToArray());
            FinanceText(ref y, width, "Mugirl.CorporatePeople.Skills".Translate(skills));
            string traits = pawn.story?.traits == null ? "-" : string.Join(" · ", pawn.story.traits.allTraits.Select(t => t.LabelCap).ToArray());
            FinanceText(ref y, width, "Mugirl.CorporatePeople.Traits".Translate(traits));
            string health = string.Join(" · ", pawn.health.hediffSet.hediffs.Where(h => h.Visible).Select(h => h.LabelCap.ToString()).Take(8).ToArray());
            if (!string.IsNullOrEmpty(health)) FinanceText(ref y, width, "Mugirl.CorporatePeople.Health".Translate(health));
            string apparel = pawn.apparel == null ? "-" : string.Join(" · ", pawn.apparel.WornApparel.Select(a => a.LabelCap.ToString()).ToArray());
            FinanceText(ref y, width, "Mugirl.CorporatePeople.Apparel".Translate(apparel));
            string relations = string.Join(" · ", pawn.relations.DirectRelations.Where(r => r.otherPawn != null && MugirlWildSlaveUtility.IsPlayerFaction(r.otherPawn.Faction))
                .Select(r => r.def.LabelCap + ": " + r.otherPawn.LabelShortCap).ToArray());
            if (!string.IsNullOrEmpty(relations)) FinanceText(ref y, width, "Mugirl.CorporatePeople.Relations".Translate(relations));
        }

        private void PurchaseCorporatePerson(CorporatePersonOffer offer)
        {
            string reason;
            PeopleResult(network.TryPurchasePerson(context, offer, out reason), reason);
        }

        private static void PeopleResult(bool success, string reason)
        {
            Messages.Message(success ? "Mugirl.CorporatePeople.Success".Translate().ToString() : reason,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }
    }
}
