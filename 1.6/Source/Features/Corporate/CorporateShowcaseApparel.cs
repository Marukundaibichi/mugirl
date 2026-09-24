using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // 用于巨企展览人员与未付款旧衣装迁移；整套衣物随 Pawn 保存，重开终端不重新抽取。
    internal static class CorporateShowcaseApparel
    {
        internal const int OutfitCount = 12;

        internal static bool IsCompleteOutfit(Pawn pawn)
        {
            if (pawn?.apparel == null) return false;
            for (int i = 0; i < OutfitCount; i++)
            {
                ThingDef[] outfit = MakeOutfit(i);
                if (pawn.apparel.WornApparel.Count == outfit.Length
                    && outfit.All(def => def != null && pawn.apparel.WornApparel.Any(a => a.def == def)))
                    return true;
            }
            return false;
        }

        internal static bool TryEnsureOutfit(Pawn pawn)
        {
            if (pawn?.kindDef != MugirlContentDefOf.Mugirl_CorporateShowcase) return false;
            return IsCompleteOutfit(pawn) || TryDress(pawn);
        }

        internal static bool TryDress(Pawn pawn) => TryDress(pawn, Rand.Range(0, OutfitCount));

        internal static bool TryDress(Pawn pawn, int outfitIndex)
        {
            if (pawn?.apparel == null || pawn.kindDef != MugirlContentDefOf.Mugirl_CorporateShowcase)
                return false;

            ThingDef[] pieces = MakeOutfit(outfitIndex);
            if (pieces == null) return false;

            // 在替换原版可能生成的衣服前，先验证 Def、身体部位和同层搭配。
            for (int i = 0; i < pieces.Length; i++)
            {
                ThingDef def = pieces[i];
                if (def?.IsApparel != true || !ApparelUtility.HasPartsToWear(pawn, def)) return false;
                for (int j = 0; j < i; j++)
                    if (!ApparelUtility.CanWearTogether(def, pieces[j], pawn.RaceProps.body)) return false;
            }

            var made = new List<Apparel>(pieces.Length);
            bool dressed = false;
            try
            {
                foreach (ThingDef piece in pieces)
                {
                    Thing thing = CorporateNetwork.MakeProduct(piece, null, pawn.kindDef.itemQuality);
                    if (!(thing is Apparel apparel))
                    {
                        CorporateNetwork.DiscardUnspawnedProduct(thing);
                        return false;
                    }
                    made.Add(apparel);
                    if (!apparel.PawnCanWear(pawn, ignoreGender: true)) return false;
                    // 新 CompColorable 默认白色但未启用，直接设白色会被原版忽略并继续取材质底色。
                    // 在未穿戴时先激活，再设白色，保留贴图原配色；不可染色的服装沿用自身渲染。
                    CompColorable colorable = apparel.TryGetComp<CompColorable>();
                    if (colorable != null)
                    {
                        if (!colorable.Active) colorable.SetColor(Color.clear);
                        colorable.SetColor(Color.white);
                    }
                }

                pawn.apparel.DestroyAll();
                foreach (Apparel apparel in made) pawn.apparel.Wear(apparel, false);
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
                        if (apparel.Destroyed) continue;
                        if (pawn.apparel.WornApparel.Contains(apparel)) pawn.apparel.Remove(apparel);
                        CorporateNetwork.DiscardUnspawnedProduct(apparel);
                    }
                }
            }
        }

        // 先抽整套，再生成全部配件；只收录预先搭好的日常、礼服与角色装束，不放入作战装备。
        private static ThingDef[] MakeOutfit(int index)
        {
            switch (index)
            {
                case 0:
                case 1:
                    return new[]
                    {
                        index == 0 ? MugirlContentDefOf.Mugirl_NunDressClassic : MugirlContentDefOf.Mugirl_NunDressSimple,
                        MugirlContentDefOf.Mugirl_NunVeil, MugirlContentDefOf.Mugirl_NunBlindfold
                    };
                case 2:
                case 3:
                    return new[]
                    {
                        index == 2 ? MugirlContentDefOf.Mugirl_PoliceBra : MugirlContentDefOf.Mugirl_PoliceZipperBra,
                        MugirlContentDefOf.Mugirl_PoliceThong, MugirlContentDefOf.Mugirl_PoliceShirt,
                        MugirlContentDefOf.Mugirl_PoliceShorts, MugirlContentDefOf.Mugirl_PoliceBoots,
                        MugirlContentDefOf.Mugirl_PoliceCap
                    };
                case 4:
                    return new[]
                    {
                        MugirlContentDefOf.Mugirl_BunnyGirlTop, MugirlContentDefOf.Mugirl_BunnyGirlStocking,
                        MugirlContentDefOf.Mugirl_BunnyGirlHeaddress
                    };
                case 5:
                    return new[]
                    {
                        MugirlContentDefOf.Mugirl_HighCutSweater, MugirlContentDefOf.Mugirl_LeatherHeels,
                        MugirlContentDefOf.Mugirl_SisterMask, MugirlContentDefOf.Mugirl_BrandBag
                    };
                case 6:
                    // OL 自带裙装、丝袜和鞋，黑色墨镜与链条手袋延续通勤配色。
                    return new[]
                    {
                        MugirlContentDefOf.Mugirl_OL, MugirlContentDefOf.Mugirl_Sunglasses,
                        MugirlContentDefOf.Mugirl_BrandBag
                    };
                case 7:
                    // 婚纱已有成套腿饰，白色金边面纱呼应领口；不再叠穿其他袜靴。
                    return new[] { MugirlContentDefOf.Mugirl_WeddingDress, MugirlContentDefOf.Mugirl_Veil };
                case 8:
                    return new[] { MugirlContentDefOf.Mugirl_Cheongsam, MugirlContentDefOf.Mugirl_BrandBag };
                case 9:
                    return new[] { MugirlContentDefOf.Mugirl_ImmortalFairy, MugirlContentDefOf.Mugirl_Veil };
                case 10:
                    // 两款仙女服保留原有长袖、衣摆和配色，共用轻薄面纱。
                    return new[] { MugirlContentDefOf.Mugirl_ImmortalFairyAzure, MugirlContentDefOf.Mugirl_Veil };
                case 11:
                    // 白色条纹衬衫配蓝色牛仔长裤；裤装贴图自带鞋饰，保持简洁的休闲造型。
                    return new[] { MugirlContentDefOf.Mugirl_Shirt, MugirlContentDefOf.Mugirl_Jeans };
                default:
                    return null;
            }
        }
    }
}
