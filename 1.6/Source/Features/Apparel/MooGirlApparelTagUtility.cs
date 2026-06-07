using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    internal static class MooGirlApparelTagUtility
    {
        internal static bool TryWearIdeoSuppressedKindApparel(Pawn pawn)
        {
            if (!IdeoSuppressesApparelRequirements(pawn) || pawn?.kindDef?.apparelTags == null || pawn.kindDef.apparelTags.Count == 0 || pawn.apparel == null)
            {
                return false;
            }

            bool wornAny = false;
            List<string> apparelTags = pawn.kindDef.apparelTags;
            for (int i = 0; i < apparelTags.Count; i++)
            {
                ThingDef chosenDef = TryChooseAllowedApparel(apparelTags[i]);
                if (chosenDef == null)
                {
                    continue;
                }

                Apparel apparel = MakeApparel(chosenDef);
                pawn.apparel.Wear(apparel, dropReplacedApparel: true, locked: ShouldLock(apparel));
                wornAny = true;
            }

            return wornAny;
        }

        private static bool IdeoSuppressesApparelRequirements(Pawn pawn)
        {
            if (pawn?.Ideo == null)
            {
                return false;
            }

            for (int i = 0; i < pawn.Ideo.memes.Count; i++)
            {
                if (pawn.Ideo.memes[i].preventApparelRequirements)
                {
                    return true;
                }
            }

            return false;
        }

        private static ThingDef TryChooseAllowedApparel(string tag)
        {
            if (tag.NullOrEmpty())
            {
                return null;
            }

            // R18 内容已常驻。此处暂不做全局缓存；后续缓存必须围绕
            // Def 加载和可选 mod 条件设计失效时机。
            ThingDef chosenDef = null;
            int matchingCandidates = 0;
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef thingDef = allDefs[i];
                if (!thingDef.IsApparel || thingDef.apparel?.tags == null || !thingDef.apparel.tags.Contains(tag))
                {
                    continue;
                }

                matchingCandidates++;
                if (Rand.Range(0, matchingCandidates) == 0)
                {
                    chosenDef = thingDef;
                }
            }

            return chosenDef;
        }

        private static Apparel MakeApparel(ThingDef def)
        {
            ThingDef stuff = def.MadeFromStuff ? GenStuff.RandomStuffFor(def) : null;
            return (Apparel)ThingMaker.MakeThing(def, stuff);
        }

        private static bool ShouldLock(Apparel apparel)
        {
            return apparel is AdvancedSlaveApparel;
        }
    }
}
