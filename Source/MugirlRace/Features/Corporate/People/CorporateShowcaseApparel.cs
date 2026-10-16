using System.Linq;
using Mugirl.Features.Appearance;

namespace Mugirl.Features.Corporate;

// 用于巨企展览人员与未付款展示报价的整装；整套衣物随 Pawn 保存，重开终端不重新抽取。
internal static class CorporateShowcaseApparel
{
    internal const int OutfitCount = 12;

    internal static bool IsCompleteOutfit(Pawn? pawn)
    {
        if (pawn?.apparel == null)
        {
            return false;
        }

        List<Apparel> worn = pawn.apparel.WornApparel;
        for (int i = 0; i < OutfitCount; i++)
        {
            ThingDef[]? outfit = MakeOutfit(i);
            if (outfit != null && worn.Count == outfit.Length
                && outfit.All(def => def != null && worn.Any(a => a.def == def)))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryEnsureOutfit(Pawn? pawn)
    {
        if (pawn?.kindDef != CorporateDefOf.Mugirl_CorporateShowcase)
        {
            return false;
        }

        return IsCompleteOutfit(pawn) || TryDress(pawn);
    }

    internal static bool TryDress(Pawn pawn) => TryDress(pawn, Rand.Range(0, OutfitCount));

    internal static bool TryDress(Pawn? pawn, int outfitIndex)
    {
        if (pawn?.apparel == null || pawn.kindDef != CorporateDefOf.Mugirl_CorporateShowcase)
        {
            return false;
        }

        ThingDef[]? pieces = MakeOutfit(outfitIndex);
        if (pieces == null)
        {
            return false;
        }

        // 在替换原版可能生成的衣服前，先验证 Def、身体部位和同层搭配。
        for (int i = 0; i < pieces.Length; i++)
        {
            ThingDef def = pieces[i];
            if (def?.IsApparel != true || !ApparelUtility.HasPartsToWear(pawn, def))
            {
                return false;
            }

            for (int j = 0; j < i; j++)
            {
                if (!ApparelUtility.CanWearTogether(def, pieces[j], pawn.RaceProps.body))
                {
                    return false;
                }
            }
        }

        var made = new List<Apparel>(pieces.Length);
        bool dressed = false;
        try
        {
            foreach (ThingDef piece in pieces)
            {
                Thing thing = CorporateNetwork.MakeProduct(piece, null, pawn.kindDef.itemQuality);
                if (thing is not Apparel apparel)
                {
                    CorporateNetwork.DiscardUnspawnedProduct(thing);
                    return false;
                }

                made.Add(apparel);
                if (!apparel.PawnCanWear(pawn, ignoreGender: true))
                {
                    return false;
                }

                // 新 CompColorable 默认白色但未启用，直接设白色会被原版忽略并继续取材质底色。
                // 在未穿戴时先激活，再设白色，保留贴图原配色；不可染色的服装沿用自身渲染。
                // 原版 SetColor 以精确 == 判断“未改变”：透明与白色不同，激活技巧在 1.7 仍然成立。
                CompColorable? colorable = apparel.TryGetComp<CompColorable>();
                if (colorable != null)
                {
                    if (!colorable.Active)
                    {
                        colorable.SetColor(UnityColor.Clear);
                    }

                    colorable.SetColor(UnityColor.White);
                }
            }

            pawn.apparel.DestroyAll();
            foreach (Apparel apparel in made)
            {
                pawn.apparel.Wear(apparel, false);
            }

            dressed = made.TrueForAll(apparel => pawn.apparel.WornApparel.Contains(apparel))
                && IsCompleteOutfit(pawn);
            return dressed;
        }
        catch (Exception ex)
        {
            MugirlLog.WarningOnce("Corporate.ShowcaseApparel",
                "Mugirl.CorporatePeople.GenerationError".Translate(ex.Message));
            return false;
        }
        finally
        {
            if (!dressed)
            {
                foreach (Apparel apparel in made)
                {
                    if (apparel.Destroyed)
                    {
                        continue;
                    }

                    if (pawn.apparel.WornApparel.Contains(apparel))
                    {
                        pawn.apparel.Remove(apparel);
                    }

                    CorporateNetwork.DiscardUnspawnedProduct(apparel);
                }
            }
        }
    }

    // 先抽整套，再生成全部配件；只收录预先搭好的日常、礼服与角色装束，不放入作战装备。
    // 每次调用新建数组：Def 在切换语言时会整体重建，不缓存 Def 引用。
    private static ThingDef[]? MakeOutfit(int index)
    {
        switch (index)
        {
            case 0:
            case 1:
                return
                [
                    index == 0 ? AppearanceDefOf.Mugirl_NunDressClassic : AppearanceDefOf.Mugirl_NunDressSimple,
                    AppearanceDefOf.Mugirl_NunVeil, AppearanceDefOf.Mugirl_NunBlindfold,
                ];
            case 2:
            case 3:
                return
                [
                    index == 2 ? AppearanceDefOf.Mugirl_PoliceBra : AppearanceDefOf.Mugirl_PoliceZipperBra,
                    AppearanceDefOf.Mugirl_PoliceThong, AppearanceDefOf.Mugirl_PoliceShirt,
                    AppearanceDefOf.Mugirl_PoliceShorts, AppearanceDefOf.Mugirl_PoliceBoots,
                    AppearanceDefOf.Mugirl_PoliceCap,
                ];
            case 4:
                return
                [
                    AppearanceDefOf.Mugirl_BunnyGirlTop, AppearanceDefOf.Mugirl_BunnyGirlStocking,
                    AppearanceDefOf.Mugirl_BunnyGirlHeaddress,
                ];
            case 5:
                return
                [
                    AppearanceDefOf.Mugirl_HighCutSweater, AppearanceDefOf.Mugirl_LeatherHeels,
                    AppearanceDefOf.Mugirl_SisterMask, AppearanceDefOf.Mugirl_BrandBag,
                ];
            case 6:
                // OL 自带裙装、丝袜和鞋，黑色墨镜与链条手袋延续通勤配色。
                return [AppearanceDefOf.Mugirl_OL, AppearanceDefOf.Mugirl_Sunglasses, AppearanceDefOf.Mugirl_BrandBag];
            case 7:
                // 婚纱已有成套腿饰，白色金边面纱呼应领口；不再叠穿其他袜靴。
                return [AppearanceDefOf.Mugirl_WeddingDress, AppearanceDefOf.Mugirl_Veil];
            case 8:
                return [AppearanceDefOf.Mugirl_Cheongsam, AppearanceDefOf.Mugirl_BrandBag];
            case 9:
                return [AppearanceDefOf.Mugirl_ImmortalFairy, AppearanceDefOf.Mugirl_Veil];
            case 10:
                // 两款仙女服保留原有长袖、衣摆和配色，共用轻薄面纱。
                return [AppearanceDefOf.Mugirl_ImmortalFairyAzure, AppearanceDefOf.Mugirl_Veil];
            case 11:
                // 白色条纹衬衫配蓝色牛仔长裤；裤装贴图自带鞋饰，保持简洁的休闲造型。
                return [AppearanceDefOf.Mugirl_Shirt, AppearanceDefOf.Mugirl_Jeans];
            default:
                return null;
        }
    }
}
