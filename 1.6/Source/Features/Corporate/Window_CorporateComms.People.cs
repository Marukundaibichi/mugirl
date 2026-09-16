using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public partial class Window_CorporateComms
    {
        private Vector2 peopleScroll;
        private Vector2 peopleRosterScroll;
        private Vector2 peopleHeaderScroll;
        private float peopleHeight = 900f;
        private bool peopleSelling;
        private int peopleSelectedId = -1;
        private bool peopleTipsEnabled;

        private void DrawPeople(Rect rect)
        {
            float width = rect.width;
            string accessReason;
            bool canTrade = network.CanTrade(context, out accessReason);
            float y = DrawPeopleHeader(rect, canTrade, accessReason);

            List<CorporatePersonOffer> offers = network.PeopleOffers.Where(o => !o.delivered && o.pawn != null).ToList();
            List<Pawn> people = peopleSelling ? network.PeopleSaleCandidates(context).ToList() : offers.Select(o => o.pawn).ToList();
            if (people.Count == 0)
            {
                CorporateUI.Notice(new Rect(0f, y, width, 80f),
                    (peopleSelling ? "Mugirl.CorporatePeople.NoCandidates" : "Mugirl.CorporatePeople.NoOffers").Translate());
                return;
            }
            Pawn selected = people.FirstOrDefault(p => p.thingIDNumber == peopleSelectedId);
            if (selected == null)
            {
                selected = people[0];
                peopleSelectedId = selected.thingIDNumber;
                peopleScroll = Vector2.zero;
            }

            Rect body = new Rect(0f, y, width, Mathf.Max(1f, rect.height - y));
            Rect roster, dossier;
            bool compact = width < 760f || body.height < 260f;
            if (!compact)
            {
                float rosterWidth = Mathf.Clamp(width * 0.24f, 180f, 226f);
                roster = new Rect(body.x, body.y, rosterWidth, body.height);
                dossier = new Rect(roster.xMax + 20f, body.y, body.width - rosterWidth - 20f, body.height);
                CorporateUI.Rule(new Rect(roster.xMax + 9f, body.y, 1f, body.height));
            }
            else
            {
                const float rosterHeight = 34f;
                roster = new Rect(body.x, body.y, body.width, rosterHeight);
                dossier = new Rect(body.x, roster.yMax + 12f, body.width, Mathf.Max(1f, body.height - rosterHeight - 12f));
            }
            if (compact) DrawPeopleSelector(roster, people, selected);
            else DrawPeopleRoster(roster, people, offers);

            CorporatePersonOffer offer = peopleSelling ? null : offers.FirstOrDefault(o => o.pawn == selected);
            int price = peopleSelling ? network.PeopleSalePrice(selected) : offer?.price ?? 0;
            bool needsSilver = offer != null && !offer.paid && context.SilverCount < price;
            float footerHeight = needsSilver ? 73f : 48f;
            Rect viewport = new Rect(dossier.x, dossier.y, dossier.width, Mathf.Max(1f, dossier.height - footerHeight));
            float contentWidth = Mathf.Max(1f, viewport.width - 18f);
            peopleTipsEnabled = Mouse.IsOver(viewport);
            CorporateUI.BeginScrollView(viewport, ref peopleScroll,
                new Rect(0f, 0f, contentWidth, Mathf.Max(viewport.height, peopleHeight)), "people/dossier/" + selected.thingIDNumber);
            float bottom = 0f;
            try { DrawCorporatePawnCard(ref bottom, contentWidth, selected, price); }
            finally { CorporateUI.EndScrollView(); }
            if (Event.current.type == EventType.Layout) peopleHeight = bottom + 10f;

            DrawPeopleAction(new Rect(dossier.x, dossier.yMax - footerHeight, dossier.width, footerHeight), selected, offer, price, canTrade, needsSilver);
        }

        private void SelectPeopleMode(bool selling)
        {
            peopleSelling = selling;
            peopleSelectedId = -1;
            peopleScroll = Vector2.zero;
            peopleRosterScroll = Vector2.zero;
            peopleHeaderScroll = Vector2.zero;
        }

        private float DrawPeopleHeader(Rect rect, bool canTrade, string accessReason)
        {
            bool showIntro = rect.height >= 410f;
            float contentHeight = 80f + (showIntro ? 25f : 0f) + (!canTrade ? 28f : 0f)
                + (network.PeoplePendingSilver > 0 ? 40f : 0f) + (!peopleSelling && !ModsConfig.IdeologyActive ? 28f : 0f)
                + (peopleSelling ? 28f : 0f);
            // Header notices may scroll, but never displace the selected person's action.
            float height = Mathf.Min(contentHeight, Mathf.Max(40f, rect.height - 160f));
            float width = rect.width - (contentHeight > height ? 18f : 0f);
            peopleTipsEnabled = Mouse.IsOver(new Rect(0f, 0f, rect.width, height));
            CorporateUI.BeginScrollView(new Rect(0f, 0f, rect.width, height), ref peopleHeaderScroll,
                new Rect(0f, 0f, width, contentHeight), "people/header");
            try
            {
                PeopleSingleLine(new Rect(0f, 0f, width, 34f), "Mugirl.CorporatePeople.Title".Translate(), GameFont.Medium);
                if (peopleTipsEnabled) TooltipHandler.TipRegion(new Rect(0f, 0f, width, 34f), "Mugirl.CorporatePeople.Intro".Translate());
                float y = 36f;
                if (showIntro)
                {
                    PeopleSingleLine(new Rect(0f, y, width, 23f), "Mugirl.CorporatePeople.Intro".Translate(), GameFont.Tiny, CorporateUI.Muted);
                    y += 25f;
                }
                float tabWidth = (width - 8f) / 2f;
                if (CorporateUI.Button(new Rect(0f, y, tabWidth, 34f), "Mugirl.CorporatePeople.BuyTab".Translate(), true, !peopleSelling, "people/buy") && peopleSelling)
                    RequestContentTransition(() => SelectPeopleMode(false), "people/buy");
                if (CorporateUI.Button(new Rect(tabWidth + 8f, y, tabWidth, 34f), "Mugirl.CorporatePeople.SellTab".Translate(), true, peopleSelling, "people/sell") && !peopleSelling)
                    RequestContentTransition(() => SelectPeopleMode(true), "people/sell");
                y += 44f;
                if (!canTrade) PeopleHeaderNotice(ref y, width, accessReason);
                if (network.PeoplePendingSilver > 0)
                {
                    if (CorporateUI.Button(new Rect(0f, y, width, 32f),
                        "Mugirl.CorporatePeople.ClaimSilver".Translate(CorporateUI.Money(network.PeoplePendingSilver)), true, true, "people/claim-silver"))
                    {
                        string reason;
                        PeopleResult(network.TryClaimPeopleSilver(context, out reason), reason);
                    }
                    y += 40f;
                }
                if (!peopleSelling && !ModsConfig.IdeologyActive)
                    PeopleHeaderNotice(ref y, width, "Mugirl.CorporatePeople.RequiresIdeology".Translate());
                if (peopleSelling) PeopleHeaderNotice(ref y, width, "Mugirl.CorporatePeople.SaleWarning".Translate());
            }
            finally { CorporateUI.EndScrollView(); }
            return height + 8f;
        }

        private void PeopleHeaderNotice(ref float y, float width, string text)
        {
            PeopleSingleLine(new Rect(0f, y, width, 24f), text, GameFont.Tiny, CorporateUI.Muted);
            y += 28f;
        }

        private void DrawPeopleSelector(Rect rect, List<Pawn> people, Pawn selected)
        {
            string suffix = "   ·   " + (people.IndexOf(selected) + 1) + " / " + people.Count + "   ▾";
            string label = PeopleFittedText(selected.LabelShortCap, rect.width - PeopleTextSize(suffix).x - 20f, GameFont.Small) + suffix;
            TooltipHandler.TipRegion(rect, selected.LabelShortCap);
            if (!CorporateUI.Button(rect, label, id: "people/select-menu")) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn person in people)
            {
                int id = person.thingIDNumber;
                options.Add(new FloatMenuOption(person.LabelShortCap, () =>
                    RequestContentTransition(() => { peopleSelectedId = id; peopleScroll = Vector2.zero; }, "people/select/" + id)));
            }
            ShowMenu(options);
        }

        private void DrawPeopleRoster(Rect rect, List<Pawn> people, List<CorporatePersonOffer> offers)
        {
            float width = rect.width - 18f;
            const float rowHeight = 80f;
            float height = people.Count * (rowHeight + 6f);
            peopleTipsEnabled = Mouse.IsOver(rect);
            CorporateUI.BeginScrollView(rect, ref peopleRosterScroll,
                new Rect(0f, 0f, width, Mathf.Max(rect.height, height)), "people/roster/" + peopleSelling);
            try
            {
                for (int i = 0; i < people.Count; i++)
                {
                    Pawn pawn = people[i];
                    CorporatePersonOffer offer = peopleSelling ? null : offers.FirstOrDefault(o => o.pawn == pawn);
                    int price = peopleSelling ? network.PeopleSalePrice(pawn) : offer?.price ?? 0;
                    Rect row = new Rect(0f, i * (rowHeight + 6f), width, rowHeight);
                    if (CorporateUI.Row(row, pawn.thingIDNumber == peopleSelectedId, "people/select/" + pawn.thingIDNumber) && peopleSelectedId != pawn.thingIDNumber)
                    {
                        int id = pawn.thingIDNumber;
                        RequestContentTransition(() => { peopleSelectedId = id; peopleScroll = Vector2.zero; }, "people/select/" + id);
                    }
                    CorporateUI.Texture(new Rect(row.x + 5f, row.y + 7f, 42f, 58f), PortraitsCache.Get(pawn, new Vector2(42f, 58f), Rot4.South));
                    float textX = row.x + 53f, textWidth = row.width - 60f;
                    PeopleSingleLine(new Rect(textX, row.y + 7f, textWidth, 23f), pawn.LabelShortCap);
                    PeopleSingleLine(new Rect(textX, row.y + 31f, textWidth, 21f),
                        "Mugirl.CorporatePeopleCards.Years".Translate(pawn.ageTracker.AgeBiologicalYears), GameFont.Tiny, CorporateUI.Muted);
                    PeopleSingleLine(new Rect(textX, row.y + 53f, textWidth, 22f),
                        offer?.paid == true ? "Mugirl.CorporatePeopleCards.Paid".Translate().ToString() : CorporateUI.Money(price), GameFont.Tiny);
                }
            }
            finally { CorporateUI.EndScrollView(); }
        }

        private void DrawPeopleAction(Rect rect, Pawn pawn, CorporatePersonOffer offer, int price, bool canTrade, bool needsSilver)
        {
            CorporateUI.Rule(new Rect(rect.x, rect.y, rect.width, 1f));
            Rect button = new Rect(rect.x, rect.y + 9f, rect.width, 36f);
            if (peopleSelling)
            {
                if (CorporateUI.Button(button, "Mugirl.CorporatePeople.SellPrice".Translate(CorporateUI.Money(price)), canTrade, id: "people/sell/" + pawn.thingIDNumber))
                {
                    string gear = (context.Map != null ? "Mugirl.CorporatePeople.MapGear" : "Mugirl.CorporatePeople.CaravanGear").Translate();
                    Confirm("Mugirl.CorporatePeople.SellConfirm".Translate(pawn.LabelShortCap, CorporateUI.Money(price), gear), () =>
                    {
                        string reason;
                        PeopleResult(network.TrySellPerson(context, pawn, price, out reason), reason);
                    }, true);
                }
                return;
            }
            if (offer == null) return;
            string label = offer.paid ? "Mugirl.CorporatePeople.ClaimPerson".Translate().ToString()
                : "Mugirl.CorporatePeople.BuyPrice".Translate(CorporateUI.Money(price)).ToString();
            bool enabled = ModsConfig.IdeologyActive && (offer.paid || canTrade && !needsSilver);
            if (CorporateUI.Button(button, label, enabled, true, "people/purchase/" + pawn.thingIDNumber))
            {
                if (offer.paid) PurchaseCorporatePerson(offer);
                else Confirm("Mugirl.CorporatePeople.BuyConfirm".Translate(pawn.LabelShortCap, CorporateUI.Money(price), context.Label),
                    () => PurchaseCorporatePerson(offer));
            }
            if (needsSilver)
                CorporateUI.Label(new Rect(rect.x, button.yMax + 4f, rect.width, 22f),
                    "Mugirl.CorporatePeople.NoSilver".Translate(), GameFont.Tiny, CorporateUI.Muted);
        }

        private void DrawCorporatePawnCard(ref float y, float width, Pawn pawn, int price)
        {
            CorporateUI.Texture(new Rect(0f, y, 78f, 100f), PortraitsCache.Get(pawn, new Vector2(78f, 100f), Rot4.South));
            PeopleSingleLine(new Rect(90f, y, width - 132f, 34f), pawn.LabelShortCap, GameFont.Medium);
            Rect info = new Rect(width - 34f, y, 34f, 30f);
            if (CorporateUI.Button(info, "i", id: "people/info/" + pawn.thingIDNumber))
                MugirlGameUtility.Windows.Add(new Dialog_InfoCard(pawn));
            if (peopleTipsEnabled) TooltipHandler.TipRegion(info, "Mugirl.CorporatePeopleCards.FullRecord".Translate());
            float metricWidth = (width - 90f) / 3f;
            PeopleMetric(new Rect(90f, y + 40f, metricWidth, 55f), "Mugirl.CorporatePeopleCards.Age".Translate(), pawn.ageTracker.AgeBiologicalYears.ToString());
            PeopleMetric(new Rect(90f + metricWidth, y + 40f, metricWidth, 55f), "Mugirl.CorporatePeopleCards.Health".Translate(), pawn.health.summaryHealth.SummaryHealthPercent.ToStringPercent());
            PeopleMetric(new Rect(90f + metricWidth * 2f, y + 40f, metricWidth, 55f), "Mugirl.CorporatePeopleCards.Quote".Translate(), price.ToString("N0"));
            y += 108f;

            DrawPeopleSkills(ref y, width, pawn);
            DrawPeopleTraits(ref y, width, pawn);
            DrawPeopleHealth(ref y, width, pawn);
            DrawPeopleApparel(ref y, width, pawn);
            DrawPeopleRelations(ref y, width, pawn);
        }

        private void DrawPeopleSkills(ref float y, float width, Pawn pawn)
        {
            PeopleSection(ref y, width, "Mugirl.CorporatePeopleCards.Skills".Translate());
            if (pawn.skills == null) { PeopleEmpty(ref y, width); return; }
            int columns = width >= 430f ? 2 : 1;
            float columnWidth = (width - (columns - 1) * 20f) / columns;
            List<SkillRecord> skills = pawn.skills.skills.OrderBy(s => s.TotallyDisabled).ThenByDescending(s => s.Level).ToList();
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord skill = skills[i];
                Rect row = new Rect((i % columns) * (columnWidth + 20f), y + (i / columns) * 37f, columnWidth, 33f);
                bool disabled = skill.TotallyDisabled;
                Color ink = disabled ? CorporateUI.Muted : CorporateUI.Ink;
                PeopleSingleLine(new Rect(row.x, row.y, row.width - 64f, 23f), skill.def.LabelCap, color: ink);
                string level = disabled ? "-" : skill.GetLevelForUI().ToString();
                CorporateUI.Label(new Rect(row.xMax - 34f, row.y, 34f, 23f), level, color: ink, anchor: TextAnchor.MiddleRight);
                if (!disabled && skill.passion != Passion.None)
                {
                    Rect passion = new Rect(row.xMax - 59f, row.y + 5f, 20f, 16f);
                    int bars = skill.passion == Passion.Major ? 2 : 1;
                    for (int p = 0; p < bars; p++)
                        CorporateUI.Fill(new Rect(passion.x + p * 7f, passion.y + (1 - p) * 4f, 4f, 9f + p * 4f), CorporateUI.Ink);
                    if (peopleTipsEnabled) TooltipHandler.TipRegion(passion, SkillUI.GetLabel(skill.passion));
                }
                Rect bar = new Rect(row.x, row.yMax - 4f, row.width, 3f);
                CorporateUI.Fill(bar, CorporateUI.Border);
                if (!disabled) CorporateUI.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(skill.GetLevelForUI() / 20f), bar.height), CorporateUI.Gray(0.78f));
                if (peopleTipsEnabled && Mouse.IsOver(row))
                    TooltipHandler.TipRegion(row, skill.def.LabelCap + "\n" + (disabled
                        ? "Mugirl.CorporatePeopleCards.Incapable".Translate().ToString()
                        : skill.LevelDescriptor + "\n" + SkillUI.GetLabel(skill.passion)) + "\n\n" + skill.def.description);
            }
            y += Mathf.Ceil(skills.Count / (float)columns) * 37f + 12f;
        }

        private void DrawPeopleTraits(ref float y, float width, Pawn pawn)
        {
            PeopleSection(ref y, width, "Mugirl.CorporatePeopleCards.Traits".Translate());
            if (pawn.story?.traits == null || pawn.story.traits.allTraits.Count == 0) { PeopleEmpty(ref y, width); return; }
            float x = 0f, rowHeight = 0f;
            foreach (Trait trait in pawn.story.traits.allTraits)
            {
                string label = trait.LabelCap;
                float cellWidth = Mathf.Min(width, PeopleTextSize(label).x + 18f);
                float cellHeight = Mathf.Max(28f, PeopleTextHeight(label, cellWidth - 16f) + 8f);
                if (x > 0f && x + cellWidth > width)
                {
                    y += rowHeight + 6f;
                    x = 0f;
                    rowHeight = 0f;
                }
                Rect cell = new Rect(x, y, cellWidth, cellHeight);
                CorporateUI.Fill(cell, CorporateUI.Raised);
                CorporateUI.Label(cell.ContractedBy(8f, 4f), label, color: trait.Suppressed ? CorporateUI.Muted : CorporateUI.Ink);
                if (peopleTipsEnabled && Mouse.IsOver(cell)) TooltipHandler.TipRegion(cell, trait.TipString(pawn));
                x += cellWidth + 6f;
                rowHeight = Mathf.Max(rowHeight, cellHeight);
            }
            y += rowHeight + 16f;
        }

        private void DrawPeopleHealth(ref float y, float width, Pawn pawn)
        {
            List<Hediff> conditions = pawn.health.hediffSet.hediffs.Where(h => h.Visible).ToList();
            PeopleSection(ref y, width, "Mugirl.CorporatePeopleCards.HealthRecord".Translate(), conditions.Count.ToString());
            PawnCapacityDef[] capacities = { PawnCapacityDefOf.Consciousness, PawnCapacityDefOf.Moving,
                PawnCapacityDefOf.Manipulation, PawnCapacityDefOf.Sight, PawnCapacityDefOf.Hearing };
            int columns = width >= 430f ? 3 : 2;
            float metricWidth = (width - (columns - 1) * 14f) / columns;
            for (int i = 0; i < capacities.Length; i++)
            {
                PawnCapacityDef capacity = capacities[i];
                Rect metric = new Rect((i % columns) * (metricWidth + 14f), y + (i / columns) * 47f, metricWidth, 41f);
                PeopleSingleLine(new Rect(metric.x, metric.y, metric.width, 19f), capacity.LabelCap, GameFont.Tiny, CorporateUI.Muted);
                CorporateUI.Label(new Rect(metric.x, metric.y + 18f, metric.width, 23f), pawn.health.capacities.GetLevel(capacity).ToStringPercent());
            }
            Rect pain = new Rect((capacities.Length % columns) * (metricWidth + 14f), y + (capacities.Length / columns) * 47f, metricWidth, 41f);
            PeopleSingleLine(new Rect(pain.x, pain.y, pain.width, 19f), "Mugirl.CorporatePeopleCards.Pain".Translate(), GameFont.Tiny, CorporateUI.Muted);
            CorporateUI.Label(new Rect(pain.x, pain.y + 18f, pain.width, 23f), pawn.health.hediffSet.PainTotal.ToStringPercent());
            y += Mathf.Ceil((capacities.Length + 1) / (float)columns) * 47f + 5f;
            if (conditions.Count == 0)
            {
                CorporateUI.Label(new Rect(0f, y, width, 26f), "Mugirl.CorporatePeopleCards.NoConditions".Translate(), color: CorporateUI.Muted);
                y += 40f;
                return;
            }
            float nameWidth = width * 0.49f, partWidth = width * 0.28f, valueWidth = width - nameWidth - partWidth - 16f;
            foreach (var group in conditions.GroupBy(h => new { Label = h.LabelCap, Part = h.Part?.LabelCap, h.SeverityLabel }))
            {
                Hediff condition = group.First();
                int count = group.Count();
                string label = condition.LabelCap + (count > 1 ? " x" + count : string.Empty);
                string part = condition.Part?.LabelCap ?? "Mugirl.CorporatePeopleCards.WholeBody".Translate().ToString();
                string value = condition.SeverityLabel ?? string.Empty;
                float height = Mathf.Max(27f, Mathf.Max(PeopleTextHeight(label, nameWidth - 8f),
                    Mathf.Max(PeopleTextHeight(part, partWidth - 8f), PeopleTextHeight(value, valueWidth))) + 7f);
                Rect row = new Rect(0f, y, width, height);
                CorporateUI.Label(new Rect(0f, y + 2f, nameWidth - 8f, height - 4f), label);
                CorporateUI.Label(new Rect(nameWidth, y + 2f, partWidth - 8f, height - 4f), part, color: CorporateUI.Muted);
                CorporateUI.Label(new Rect(nameWidth + partWidth, y + 2f, valueWidth, height - 4f), value, color: CorporateUI.Muted, anchor: TextAnchor.UpperRight);
                CorporateUI.Rule(new Rect(0f, row.yMax - 1f, width, 1f));
                if (peopleTipsEnabled && Mouse.IsOver(row)) TooltipHandler.TipRegion(row, condition.GetTooltip(pawn, false));
                y += height;
            }
            y += 16f;
        }

        private void DrawPeopleApparel(ref float y, float width, Pawn pawn)
        {
            List<Apparel> apparel = pawn.apparel?.WornApparel;
            PeopleSection(ref y, width, "Mugirl.CorporatePeopleCards.Apparel".Translate(), (apparel?.Count ?? 0).ToString());
            if (apparel == null || apparel.Count == 0) { PeopleEmpty(ref y, width); return; }
            int columns = width >= 600f ? 2 : 1;
            float columnWidth = (width - (columns - 1) * 16f) / columns;
            for (int offset = 0; offset < apparel.Count; offset += columns)
            {
                float rowHeight = 65f;
                for (int c = 0; c < columns && offset + c < apparel.Count; c++)
                    rowHeight = Mathf.Max(rowHeight, PeopleTextHeight(apparel[offset + c].LabelNoParenthesisCap, columnWidth - 58f) + 37f);
                for (int c = 0; c < columns && offset + c < apparel.Count; c++)
                {
                    Apparel item = apparel[offset + c];
                    Rect row = new Rect(c * (columnWidth + 16f), y, columnWidth, rowHeight);
                    if (CorporateUI.Row(row, false, "people/apparel/" + item.thingIDNumber))
                        MugirlGameUtility.Windows.Add(new Dialog_InfoCard(item));
                    CorporateUI.ThingIcon(new Rect(row.x + 4f, row.y + 8f, 38f, 38f), item);
                    CorporateUI.Label(new Rect(row.x + 50f, row.y + 4f, row.width - 58f, row.height - 34f), item.LabelNoParenthesisCap);
                    QualityCategory quality;
                    string qualityLabel = item.TryGetQuality(out quality) ? quality.GetLabel().CapitalizeFirst() : string.Empty;
                    float detailsWidth = row.width - 58f;
                    PeopleSingleLine(new Rect(row.x + 50f, row.yMax - 26f, detailsWidth * 0.58f, 22f), qualityLabel, GameFont.Tiny, CorporateUI.Muted);
                    if (item.def.useHitPoints && item.MaxHitPoints > 0)
                    {
                        float condition = Mathf.Clamp01(item.HitPoints / (float)item.MaxHitPoints);
                        CorporateUI.Label(new Rect(row.x + 50f + detailsWidth * 0.6f, row.yMax - 26f, detailsWidth * 0.4f, 20f),
                            condition.ToStringPercent(), GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleRight);
                        Rect bar = new Rect(row.x + 50f, row.yMax - 5f, detailsWidth, 2f);
                        CorporateUI.Fill(bar, CorporateUI.Border);
                        CorporateUI.Fill(new Rect(bar.x, bar.y, bar.width * condition, bar.height), CorporateUI.Gray(0.74f));
                    }
                    if (peopleTipsEnabled && Mouse.IsOver(row)) TooltipHandler.TipRegion(row, item.GetTooltip());
                }
                y += rowHeight + 6f;
            }
            y += 10f;
        }

        private void DrawPeopleRelations(ref float y, float width, Pawn pawn)
        {
            var relations = pawn.relations.DirectRelations.Where(r => r.otherPawn != null && MugirlWildSlaveUtility.IsPlayerFaction(r.otherPawn.Faction)).ToList();
            if (relations.Count == 0) return;
            PeopleSection(ref y, width, "Mugirl.CorporatePeopleCards.Relations".Translate(), relations.Count.ToString());
            foreach (var relation in relations)
            {
                float labelWidth = width * 0.36f;
                string label = relation.def.LabelCap;
                string name = relation.otherPawn.LabelShortCap;
                float height = Mathf.Max(28f, Mathf.Max(PeopleTextHeight(label, labelWidth - 12f), PeopleTextHeight(name, width - labelWidth)) + 4f);
                CorporateUI.Label(new Rect(0f, y, labelWidth - 12f, height), label, color: CorporateUI.Muted);
                CorporateUI.Label(new Rect(labelWidth, y, width - labelWidth, height), name);
                y += height;
            }
            y += 10f;
        }

        private static void PeopleSection(ref float y, float width, string label, string count = null)
        {
            CorporateUI.Label(new Rect(0f, y, width - 40f, 23f), label, GameFont.Tiny, CorporateUI.Muted);
            if (count != null) CorporateUI.Label(new Rect(width - 36f, y, 36f, 23f), count, GameFont.Tiny, CorporateUI.Muted, TextAnchor.UpperRight);
            y += 27f;
        }

        private void PeopleMetric(Rect rect, string label, string value)
        {
            PeopleSingleLine(new Rect(rect.x, rect.y, rect.width - 8f, 21f), label, GameFont.Tiny, CorporateUI.Muted);
            PeopleSingleLine(new Rect(rect.x, rect.y + 20f, rect.width - 8f, 30f), value, GameFont.Medium);
        }

        private static void PeopleEmpty(ref float y, float width)
        {
            CorporateUI.Label(new Rect(0f, y, width, 25f), "Mugirl.CorporatePeopleCards.None".Translate(), color: CorporateUI.Muted);
            y += 36f;
        }

        private void PeopleSingleLine(Rect rect, string text, GameFont font = GameFont.Small, Color? color = null)
        {
            GameFont previousFont = Text.Font;
            bool previousWrap = Text.WordWrap;
            try
            {
                Text.Font = font;
                Text.WordWrap = false;
                string fitted = PeopleFittedText(text, rect.width, font);
                CorporateUI.Label(rect, fitted, font, color);
                if (peopleTipsEnabled && fitted != text) TooltipHandler.TipRegion(rect, text);
            }
            finally { Text.Font = previousFont; Text.WordWrap = previousWrap; }
        }

        private static string PeopleFittedText(string text, float width, GameFont font)
        {
            GameFont previous = Text.Font;
            try { Text.Font = font; return (text ?? string.Empty).Truncate(Mathf.Max(1f, width)); }
            finally { Text.Font = previous; }
        }

        private static Vector2 PeopleTextSize(string text)
        {
            GameFont previous = Text.Font;
            try { Text.Font = GameFont.Small; return Text.CalcSize(text); }
            finally { Text.Font = previous; }
        }

        private static float PeopleTextHeight(string text, float width)
        {
            GameFont previous = Text.Font;
            try { Text.Font = GameFont.Small; return Text.CalcHeight(text, Mathf.Max(1f, width)); }
            finally { Text.Font = previous; }
        }

        private void PurchaseCorporatePerson(CorporatePersonOffer offer)
        {
            string reason;
            PeopleResult(network.TryPurchasePerson(context, offer, out reason), reason);
        }

        private void PeopleResult(bool success, string reason)
        {
            ShowFeedback(success, success ? "Mugirl.CorporatePeople.Success".Translate().ToString() : reason);
        }
    }
}
