using System.Linq;

namespace Mugirl.Features.Corporate;

/// <summary>人员档案：肖像与指标、报价明细、技能、特性、健康、服装与关系。</summary>
internal sealed partial class CorporatePeoplePage
{
    // 选中档案的逐项缓存：数据在暂停期间静态，按（pawn、缓存版本）重建。
    private readonly List<SkillRecord> skillsView = [];
    private readonly List<Hediff> healthVisible = [];
    private readonly List<HealthRow> healthRows = [];
    private readonly List<DirectPawnRelation> relationsView = [];
    private Pawn? skillsPawn;
    private int skillsVersion = -1;
    private Pawn? healthPawn;
    private int healthVersion = -1;
    private Pawn? relationsPawn;
    private int relationsVersion = -1;
    private PawnCapacityDef[]? capacities;

    private sealed class HealthRow(Hediff condition, string label, string part, string value)
    {
        internal readonly Hediff Condition = condition;
        internal readonly string Label = label;
        internal readonly string Part = part;
        internal readonly string Value = value;
    }

    private void DrawDossier(ref float y, float width, Pawn pawn, CorporatePersonQuote quote, int price, bool freeApplied)
    {
        CorporateUI.Texture(new Rect(0f, y, 78f, 100f), PortraitsCache.Get(pawn, new UVector2(78f, 100f), Rot4.South));
        SingleLine(new Rect(90f, y, width - 132f, 34f), pawn.LabelShortCap, GameFont.Medium);
        Rect info = new(width - 34f, y, 34f, 30f);
        if (CorporateUI.Button(info, "i", id: "people/info", index: pawn.thingIDNumber))
        {
            GameUtility.TryAddWindow(new Dialog_InfoCard(pawn));
        }

        if (tipsEnabled)
        {
            TooltipHandler.TipRegionByKey(info, "Mugirl.CorporatePeopleCards.FullRecord");
        }

        float metricWidth = (width - 90f) / 3f;
        Metric(new Rect(90f, y + 40f, metricWidth, 55f), "Mugirl.CorporatePeopleCards.Age".Translate(), pawn.ageTracker.AgeBiologicalYears.ToString());
        Metric(new Rect(90f + metricWidth, y + 40f, metricWidth, 55f), "Mugirl.CorporatePeopleCards.Health".Translate(),
            pawn.health.summaryHealth.SummaryHealthPercent.ToStringPercent());
        Metric(new Rect(90f + metricWidth * 2f, y + 40f, metricWidth, 55f), "Mugirl.CorporatePeopleCards.Quote".Translate(), price.ToString("N0"));
        y += 108f;

        Section(ref y, width, "Mugirl.CorporatePeopleCards.Quote".Translate());
        QuoteLine(ref y, width, "Mugirl.CorporatePeople.BasePrice".Translate(CorporateUI.Money(quote.basePrice)));
        if (quote.colonistPremium > 0)
        {
            QuoteLine(ref y, width, "Mugirl.CorporatePeople.ColonistPremium".Translate(CorporateUI.Money(quote.colonistPremium)));
        }

        string feeSign = selling ? "-" : string.Empty;
        QuoteLine(ref y, width, "Mugirl.CorporatePeople.HandlingFee".Translate(feeSign + CorporateUI.Money(quote.handlingFee)));
        QuoteLine(ref y, width, "Mugirl.CorporatePeople.ShippingFee".Translate(feeSign + CorporateUI.Money(quote.shippingFee)));
        QuoteLine(ref y, width, (selling ? "Mugirl.CorporatePeople.SaleNet" : "Mugirl.CorporatePeople.PurchaseTotal").Translate(CorporateUI.Money(price)));
        if (freeApplied)
        {
            QuoteLine(ref y, width, "Mugirl.CorporatePeople.FreeApplied".Translate());
        }

        y += 8f;
        DrawSkills(ref y, width, pawn);
        DrawTraits(ref y, width, pawn);
        DrawHealth(ref y, width, pawn);
        DrawApparel(ref y, width, pawn);
        DrawRelations(ref y, width, pawn);
    }

    private void QuoteLine(ref float y, float width, string text)
    {
        SingleLine(new Rect(0f, y, width, 24f), text, GameFont.Tiny);
        y += 25f;
    }

    private void DrawSkills(ref float y, float width, Pawn pawn)
    {
        Section(ref y, width, "Mugirl.CorporatePeopleCards.Skills".Translate());
        if (pawn.skills is null)
        {
            Empty(ref y, width);
            return;
        }

        if (skillsPawn != pawn || skillsVersion != Terminal.CacheVersion)
        {
            skillsPawn = pawn;
            skillsVersion = Terminal.CacheVersion;
            skillsView.Clear();
            skillsView.AddRange(pawn.skills.skills.OrderBy(s => s.TotallyDisabled).ThenByDescending(s => s.Level));
        }

        int columns = width >= 430f ? 2 : 1;
        float columnWidth = (width - (columns - 1) * 20f) / columns;
        for (int i = 0; i < skillsView.Count; i++)
        {
            SkillRecord skill = skillsView[i];
            Rect row = new((i % columns) * (columnWidth + 20f), y + (i / columns) * 37f, columnWidth, 33f);
            bool disabled = skill.TotallyDisabled;
            Color ink = disabled ? CorporateUI.Muted : CorporateUI.Ink;
            SingleLine(new Rect(row.x, row.y, row.width - 64f, 23f), skill.def.LabelCap, color: ink);
            string level = disabled ? "-" : skill.GetLevelForUI().ToString();
            CorporateUI.Label(new Rect(row.xMax - 34f, row.y, 34f, 23f), level, color: ink, anchor: TextAnchor.MiddleRight);
            if (!disabled && skill.passion != Passion.None)
            {
                Rect passion = new(row.xMax - 59f, row.y + 5f, 20f, 16f);
                int bars = skill.passion == Passion.Major ? 2 : 1;
                for (int p = 0; p < bars; p++)
                {
                    CorporateUI.Fill(new Rect(passion.x + p * 7f, passion.y + (1 - p) * 4f, 4f, 9f + p * 4f), CorporateUI.Ink);
                }

                if (tipsEnabled && CorporateUI.WantsTooltip(passion))
                {
                    TooltipHandler.TipRegion(passion, SkillUI.GetLabel(skill.passion));
                }
            }

            Rect bar = new(row.x, row.yMax - 4f, row.width, 3f);
            CorporateUI.Fill(bar, CorporateUI.Border);
            if (!disabled)
            {
                CorporateUI.Fill(new Rect(bar.x, bar.y, bar.width * UnityMathf.Clamp01(skill.GetLevelForUI() / 20f), bar.height), CorporateUI.Gray(0.78f));
            }

            if (tipsEnabled && CorporateUI.WantsTooltip(row))
            {
                TooltipHandler.TipRegion(row, skill.def.LabelCap + "\n" + (disabled
                    ? "Mugirl.CorporatePeopleCards.Incapable".Translate().ToString()
                    : skill.LevelDescriptor + "\n" + SkillUI.GetLabel(skill.passion)) + "\n\n" + skill.def.description);
            }
        }

        y += UnityMathf.Ceil(skillsView.Count / (float)columns) * 37f + 12f;
    }

    private void DrawTraits(ref float y, float width, Pawn pawn)
    {
        Section(ref y, width, "Mugirl.CorporatePeopleCards.Traits".Translate());
        List<Trait>? traits = pawn.story?.traits?.allTraits;
        if (traits is null || traits.Count == 0)
        {
            Empty(ref y, width);
            return;
        }

        float x = 0f;
        float rowHeight = 0f;
        foreach (Trait trait in traits)
        {
            string label = trait.LabelCap;
            float cellWidth = UnityMathf.Min(width, Terminal.TextCache.Width(label, GameFont.Small) + 18f);
            float cellHeight = UnityMathf.Max(28f, TextHeight(label, cellWidth - 16f) + 8f);
            if (x > 0f && x + cellWidth > width)
            {
                y += rowHeight + 6f;
                x = 0f;
                rowHeight = 0f;
            }

            Rect cell = new(x, y, cellWidth, cellHeight);
            CorporateUI.Fill(cell, CorporateUI.Raised);
            CorporateUI.Label(cell.ContractedBy(8f, 4f), label, color: trait.Suppressed ? CorporateUI.Muted : CorporateUI.Ink);
            if (tipsEnabled && CorporateUI.WantsTooltip(cell))
            {
                TooltipHandler.TipRegion(cell, trait.TipString(pawn));
            }

            x += cellWidth + 6f;
            rowHeight = UnityMathf.Max(rowHeight, cellHeight);
        }

        y += rowHeight + 16f;
    }

    private void DrawHealth(ref float y, float width, Pawn pawn)
    {
        EnsureHealthRows(pawn);
        int conditionsCount = healthRows.Count;
        Section(ref y, width, "Mugirl.CorporatePeopleCards.HealthRecord".Translate(), conditionsCount.ToString());
        // DefOf 在定义加载后才绑定，所以首次绘制时才建表；之后整个窗口复用。
        capacities ??=
        [
            PawnCapacityDefOf.Consciousness, PawnCapacityDefOf.Moving, PawnCapacityDefOf.Manipulation,
            PawnCapacityDefOf.Sight, PawnCapacityDefOf.Hearing,
        ];
        int columns = width >= 430f ? 3 : 2;
        float metricWidth = (width - (columns - 1) * 14f) / columns;
        for (int i = 0; i < capacities.Length; i++)
        {
            PawnCapacityDef capacity = capacities[i];
            Rect metric = new((i % columns) * (metricWidth + 14f), y + (i / columns) * 47f, metricWidth, 41f);
            SingleLine(new Rect(metric.x, metric.y, metric.width, 19f), capacity.LabelCap, GameFont.Tiny, CorporateUI.Muted);
            CorporateUI.Label(new Rect(metric.x, metric.y + 18f, metric.width, 23f), pawn.health.capacities.GetLevel(capacity).ToStringPercent());
        }

        Rect pain = new((capacities.Length % columns) * (metricWidth + 14f), y + (capacities.Length / columns) * 47f, metricWidth, 41f);
        SingleLine(new Rect(pain.x, pain.y, pain.width, 19f), "Mugirl.CorporatePeopleCards.Pain".Translate(), GameFont.Tiny, CorporateUI.Muted);
        CorporateUI.Label(new Rect(pain.x, pain.y + 18f, pain.width, 23f), pawn.health.hediffSet.PainTotal.ToStringPercent());
        y += UnityMathf.Ceil((capacities.Length + 1) / (float)columns) * 47f + 5f;
        if (conditionsCount == 0)
        {
            CorporateUI.Label(new Rect(0f, y, width, 26f), "Mugirl.CorporatePeopleCards.NoConditions".Translate(), color: CorporateUI.Muted);
            y += 40f;
            return;
        }

        float nameWidth = width * 0.49f;
        float partWidth = width * 0.28f;
        float valueWidth = width - nameWidth - partWidth - 16f;
        for (int i = 0; i < healthRows.Count; i++)
        {
            HealthRow healthRow = healthRows[i];
            float height = UnityMathf.Max(27f, UnityMathf.Max(TextHeight(healthRow.Label, nameWidth - 8f),
                UnityMathf.Max(TextHeight(healthRow.Part, partWidth - 8f), TextHeight(healthRow.Value, valueWidth))) + 7f);
            Rect row = new(0f, y, width, height);
            CorporateUI.Label(new Rect(0f, y + 2f, nameWidth - 8f, height - 4f), healthRow.Label);
            CorporateUI.Label(new Rect(nameWidth, y + 2f, partWidth - 8f, height - 4f), healthRow.Part, color: CorporateUI.Muted);
            CorporateUI.Label(new Rect(nameWidth + partWidth, y + 2f, valueWidth, height - 4f), healthRow.Value, color: CorporateUI.Muted, anchor: TextAnchor.UpperRight);
            CorporateUI.Rule(new Rect(0f, row.yMax - 1f, width, 1f));
            if (tipsEnabled && CorporateUI.WantsTooltip(row))
            {
                TooltipHandler.TipRegion(row, healthRow.Condition.GetTooltip(pawn, false));
            }

            y += height;
        }

        y += 16f;
    }

    private void EnsureHealthRows(Pawn pawn)
    {
        if (healthPawn == pawn && healthVersion == Terminal.CacheVersion)
        {
            return;
        }

        healthPawn = pawn;
        healthVersion = Terminal.CacheVersion;
        healthVisible.Clear();
        healthVisible.AddRange(pawn.health.hediffSet.hediffs.Where(h => h.Visible));
        healthRows.Clear();
        string wholeBody = "Mugirl.CorporatePeopleCards.WholeBody".Translate().ToString();
        foreach (var group in healthVisible.GroupBy(h => new { Label = h.LabelCap, Part = h.Part?.LabelCap, h.SeverityLabel }))
        {
            Hediff condition = group.First();
            int count = group.Count();
            healthRows.Add(new HealthRow(
                condition,
                condition.LabelCap + (count > 1 ? " x" + count : string.Empty),
                condition.Part?.LabelCap ?? wholeBody,
                condition.SeverityLabel ?? string.Empty));
        }
    }

    private void DrawApparel(ref float y, float width, Pawn pawn)
    {
        List<Apparel>? apparel = pawn.apparel?.WornApparel;
        Section(ref y, width, "Mugirl.CorporatePeopleCards.Apparel".Translate(), (apparel?.Count ?? 0).ToString());
        if (apparel is null || apparel.Count == 0)
        {
            Empty(ref y, width);
            return;
        }

        int columns = width >= 600f ? 2 : 1;
        float columnWidth = (width - (columns - 1) * 16f) / columns;
        for (int offset = 0; offset < apparel.Count; offset += columns)
        {
            float rowHeight = 65f;
            for (int c = 0; c < columns && offset + c < apparel.Count; c++)
            {
                rowHeight = UnityMathf.Max(rowHeight, TextHeight(apparel[offset + c].LabelNoParenthesisCap, columnWidth - 58f) + 37f);
            }

            for (int c = 0; c < columns && offset + c < apparel.Count; c++)
            {
                DrawApparelRow(new Rect(c * (columnWidth + 16f), y, columnWidth, rowHeight), apparel[offset + c]);
            }

            y += rowHeight + 6f;
        }

        y += 10f;
    }

    private void DrawApparelRow(Rect row, Apparel item)
    {
        if (CorporateUI.Row(row, false, "people/apparel", item.thingIDNumber))
        {
            GameUtility.TryAddWindow(new Dialog_InfoCard(item));
        }

        CorporateUI.ThingIcon(new Rect(row.x + 4f, row.y + 8f, 38f, 38f), item);
        CorporateUI.Label(new Rect(row.x + 50f, row.y + 4f, row.width - 58f, row.height - 34f), item.LabelNoParenthesisCap);
        string qualityLabel = item.TryGetQuality(out QualityCategory quality) ? quality.GetLabel().CapitalizeFirst() : string.Empty;
        float detailsWidth = row.width - 58f;
        SingleLine(new Rect(row.x + 50f, row.yMax - 26f, detailsWidth * 0.58f, 22f), qualityLabel, GameFont.Tiny, CorporateUI.Muted);
        if (item.def.useHitPoints && item.MaxHitPoints > 0)
        {
            float condition = UnityMathf.Clamp01(item.HitPoints / (float)item.MaxHitPoints);
            CorporateUI.Label(new Rect(row.x + 50f + detailsWidth * 0.6f, row.yMax - 26f, detailsWidth * 0.4f, 20f),
                condition.ToStringPercent(), GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleRight);
            Rect bar = new(row.x + 50f, row.yMax - 5f, detailsWidth, 2f);
            CorporateUI.Fill(bar, CorporateUI.Border);
            CorporateUI.Fill(new Rect(bar.x, bar.y, bar.width * condition, bar.height), CorporateUI.Gray(0.74f));
        }

        if (tipsEnabled && CorporateUI.WantsTooltip(row))
        {
            TooltipHandler.TipRegion(row, item.GetTooltip());
        }
    }

    private void DrawRelations(ref float y, float width, Pawn pawn)
    {
        if (relationsPawn != pawn || relationsVersion != Terminal.CacheVersion)
        {
            relationsPawn = pawn;
            relationsVersion = Terminal.CacheVersion;
            relationsView.Clear();
            foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
            {
                Faction? faction = relation.otherPawn?.Faction;
                if (faction is not null && PlayerFactionUtility.IsPlayerFaction(faction))
                {
                    relationsView.Add(relation);
                }
            }
        }

        if (relationsView.Count == 0)
        {
            return;
        }

        Section(ref y, width, "Mugirl.CorporatePeopleCards.Relations".Translate(), relationsView.Count.ToString());
        float labelWidth = width * 0.36f;
        foreach (DirectPawnRelation relation in relationsView)
        {
            string label = relation.def.LabelCap;
            string name = relation.otherPawn.LabelShortCap;
            float height = UnityMathf.Max(28f, UnityMathf.Max(TextHeight(label, labelWidth - 12f), TextHeight(name, width - labelWidth)) + 4f);
            CorporateUI.Label(new Rect(0f, y, labelWidth - 12f, height), label, color: CorporateUI.Muted);
            CorporateUI.Label(new Rect(labelWidth, y, width - labelWidth, height), name);
            y += height;
        }

        y += 10f;
    }

    private static void Section(ref float y, float width, string label, string? count = null)
    {
        CorporateUI.Label(new Rect(0f, y, width - 40f, 23f), label, GameFont.Tiny, CorporateUI.Muted);
        if (count is not null)
        {
            CorporateUI.Label(new Rect(width - 36f, y, 36f, 23f), count, GameFont.Tiny, CorporateUI.Muted, TextAnchor.UpperRight);
        }

        y += 27f;
    }

    private void Metric(Rect rect, string label, string value)
    {
        SingleLine(new Rect(rect.x, rect.y, rect.width - 8f, 21f), label, GameFont.Tiny, CorporateUI.Muted);
        SingleLine(new Rect(rect.x, rect.y + 20f, rect.width - 8f, 30f), value, GameFont.Medium);
    }

    private static void Empty(ref float y, float width)
    {
        CorporateUI.Label(new Rect(0f, y, width, 25f), "Mugirl.CorporatePeopleCards.None".Translate(), color: CorporateUI.Muted);
        y += 36f;
    }

    /// <summary>单行标签：不换行，放不下时截断（Verse 的“...”），截断后悬停显示全文。</summary>
    private void SingleLine(Rect rect, string text, GameFont font = GameFont.Small, Color? color = null)
    {
        using (new TextBlock(font, null, false))
        {
            string fitted = FittedText(text, rect.width, font);
            CorporateUI.Label(rect, fitted, font, color);
            if (tipsEnabled && fitted != text && CorporateUI.WantsTooltip(rect))
            {
                TooltipHandler.TipRegion(rect, text);
            }
        }
    }

    private string FittedText(string? text, float width, GameFont font)
        => Terminal.TextCache.FitWidth(text ?? string.Empty, UnityMathf.Max(1f, width), font);

    private float TextHeight(string text, float width)
        => Terminal.TextCache.Height(text, UnityMathf.Max(1f, width), GameFont.Small);
}
