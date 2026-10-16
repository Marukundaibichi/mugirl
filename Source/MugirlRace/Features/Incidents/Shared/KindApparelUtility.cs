using Mugirl.Features.Restraints;

namespace Mugirl.Features.Incidents;

// 理念取消服装要求（preventApparelRequirements）时，原版生成不再按 PawnKind 的 apparelTags 穿衣；
// 事件与任务生成的雪牛娘在这里补穿每个标签下随机选出的一件服装（奴隶束具会锁定）。
internal static class KindApparelUtility
{
    // StaticCacheLifecycle: 进程级的“服装标签 → 服装 ThingDef”索引，首次使用时按 DefDatabase 顺序构建。
    // ThingDef 在启动加载完成后不再变化，进程内无需失效；只引用 Def，不持有任何游戏对象。
    private static Dictionary<string, List<ThingDef>>? apparelByTag;

    internal static bool TryWearIdeoSuppressedKindApparel(Pawn? pawn)
    {
        List<string>? apparelTags = pawn?.kindDef?.apparelTags;
        if (pawn is null || !IdeoSuppressesApparelRequirements(pawn) || apparelTags is null || apparelTags.Count == 0 || pawn.apparel is null)
        {
            return false;
        }

        bool wornAny = false;
        for (int i = 0; i < apparelTags.Count; i++)
        {
            ThingDef? chosenDef = TryChooseAllowedApparel(apparelTags[i]);
            if (chosenDef is null)
            {
                continue;
            }

            Apparel apparel = MakeApparel(chosenDef);
            pawn.apparel.Wear(apparel, dropReplacedApparel: true, locked: apparel is RestraintApparel);
            wornAny = true;
        }

        return wornAny;
    }

    private static bool IdeoSuppressesApparelRequirements(Pawn pawn)
    {
        Ideo? ideo = pawn.Ideo;
        if (ideo is null)
        {
            return false;
        }

        for (int i = 0; i < ideo.memes.Count; i++)
        {
            if (ideo.memes[i].preventApparelRequirements)
            {
                return true;
            }
        }

        return false;
    }

    // 在带该标签的服装中等概率选一件。逐个候选做蓄水池抽样，随机数的消耗与 1.6 逐个扫描全部 ThingDef 时完全相同。
    private static ThingDef? TryChooseAllowedApparel(string? tag)
    {
        if (string.IsNullOrEmpty(tag) || !ApparelByTag().TryGetValue(tag, out List<ThingDef>? candidates))
        {
            return null;
        }

        ThingDef? chosenDef = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (Rand.Range(0, i + 1) == 0)
            {
                chosenDef = candidates[i];
            }
        }

        return chosenDef;
    }

    private static Dictionary<string, List<ThingDef>> ApparelByTag()
    {
        if (apparelByTag is not null)
        {
            return apparelByTag;
        }

        Dictionary<string, List<ThingDef>> index = new(StringComparer.Ordinal);
        List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
        for (int i = 0; i < allDefs.Count; i++)
        {
            ThingDef thingDef = allDefs[i];
            List<string>? tags = thingDef.IsApparel ? thingDef.apparel?.tags : null;
            if (tags is null)
            {
                continue;
            }

            for (int j = 0; j < tags.Count; j++)
            {
                string? tag = tags[j];
                if (tag is null)
                {
                    continue;
                }

                if (!index.TryGetValue(tag, out List<ThingDef>? bucket))
                {
                    bucket = [];
                    index.Add(tag, bucket);
                }

                // 同一服装的标签列表中重复出现同一标签时只记一次（1.6 用 List.Contains 判断，同样只计一次）。
                if (bucket.Count == 0 || bucket[^1] != thingDef)
                {
                    bucket.Add(thingDef);
                }
            }
        }

        apparelByTag = index;
        return index;
    }

    private static Apparel MakeApparel(ThingDef def)
    {
        ThingDef? stuff = def.MadeFromStuff ? GenStuff.RandomStuffFor(def) : null;
        return (Apparel)ThingMaker.MakeThing(def, stuff);
    }
}
