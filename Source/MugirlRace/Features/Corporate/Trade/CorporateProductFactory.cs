namespace Mugirl.Features.Corporate;

// 生成、特化与回收共用同一个资产工厂，失败时按生产顺序回收已生成的货物。
internal static class CorporateProductFactory
{
    // StaticCacheLifecycle: 线程局部的嵌套生成作用域；finally 恢复调用前的种子，不改变殖民地随机序列。
    [ThreadStatic]
    private static int? orderProductSeed;

    internal static int ResolveProductSeed(int vanillaSeed) => orderProductSeed ?? vanillaSeed;

    internal static Thing MakeProduct(ThingDef def, ThingDef? stuff, QualityCategory quality)
    {
        Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff ?? GenStuff.DefaultStuffFor(def) : null);
        thing.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
        return thing;
    }

    internal static void DiscardUnspawnedProduct(Thing? thing)
    {
        if (thing == null || thing.Destroyed)
        {
            return;
        }

        if (thing.Spawned || thing is Pawn)
        {
            throw new InvalidOperationException("Corporate product cleanup requires an unspawned item.");
        }

        if (thing.def.destroyable)
        {
            thing.Destroy(DestroyMode.Vanish);
        }
        else
        {
            thing.holdingOwner?.Remove(thing);
        }
    }

    internal static void DiscardUnspawnedProducts(List<Thing> things)
    {
        foreach (Thing thing in things)
        {
            DiscardUnspawnedProduct(thing);
        }
    }

    internal static bool TryMakeGoods(ThingDef def, ThingDef? stuff, QualityCategory quality, int quantity, out List<Thing> goods)
    {
        goods = new List<Thing>();
        try
        {
            int left = quantity;
            while (left > 0)
            {
                Thing thing = MakeOrderProduct(def, stuff, quality);
                thing.stackCount = Math.Min(left, Math.Max(1, def.stackLimit));
                left -= thing.stackCount;
                goods.Add(thing);
            }

            return true;
        }
        catch (Exception ex)
        {
            DiscardUnspawnedProducts(goods);
            goods.Clear();
            MugirlLog.WarningOnce("Corporate.MakeProduct." + def.defName,
                "Mugirl.Corporate.GenerationError".Translate(def.LabelCap, ex.Message));
            return false;
        }
    }

    internal static Thing MakeOrderProduct(ThingDef def, ThingDef? stuff, QualityCategory quality)
    {
        int seed = Gen.HashCombineInt(def.shortHash, ((stuff?.shortHash ?? 0) * 7) + (int)quality);
        int? previous = orderProductSeed;
        try
        {
            orderProductSeed = seed;
            using (Rand.Block(seed))
            {
                return MakeProduct(def, stuff, quality);
            }
        }
        finally
        {
            orderProductSeed = previous;
        }
    }
}
