using Verse.AI.Group;

namespace Mugirl.Features.Incidents;

// XML deathActionWorkerClass：惩罚者死亡时爆开，尸体消失，飞出一具雪牛尸体与钢铁、零部件、玻璃钢。
public class DeathActionWorker_Punisher : DeathActionWorker
{
    private static readonly IntRange SteelCountRange = new IntRange(80, 140);
    private static readonly IntRange ComponentCountRange = new IntRange(4, 8);
    private static readonly IntRange PlasteelCountRange = new IntRange(20, 40);

    public override void PawnDied(Corpse corpse, Lord prevLord)
    {
        Map? map = corpse?.Map;
        if (corpse == null || map == null || !corpse.Spawned)
        {
            return;
        }

        IntVec3 originCell = corpse.Position;
        PunisherDeathEffectUtility.Play(originCell.ToVector3Shifted(), map);

        List<Thing> loot = new List<Thing>();
        Corpse? muffaloCorpse = MakeMuffaloCorpse(map);
        if (muffaloCorpse != null)
        {
            loot.Add(muffaloCorpse);
        }

        loot.Add(MakeStack(ThingDefOf.Steel, SteelCountRange.RandomInRange));
        loot.Add(MakeStack(ThingDefOf.ComponentIndustrial, ComponentCountRange.RandomInRange));
        loot.Add(MakeStack(ThingDefOf.Plasteel, PlasteelCountRange.RandomInRange));

        corpse.Destroy(DestroyMode.Vanish);
        PunisherLootBurst burst = new PunisherLootBurst(map, originCell, loot);
        MapComponent_Punisher? component = map.GetComponent<MapComponent_Punisher>();
        if (component != null)
        {
            component.AddLootBurst(burst);
        }
        else
        {
            burst.FinalizeDrops();
        }
    }

    private static Corpse? MakeMuffaloCorpse(Map map)
    {
        Pawn? muffalo = PawnGenerator.GeneratePawn(PawnKindDefOf.Muffalo, null, map.Tile);
        if (muffalo == null)
        {
            return null;
        }

        // 未生成到地图、也不在任何容器中的 Pawn 被杀死时，原版会为它单独生成一具尸体（1.7 Pawn.Kill 已核对）。
        muffalo.Kill(null);
        return muffalo.Corpse;
    }

    private static Thing MakeStack(ThingDef def, int count)
    {
        Thing thing = ThingMaker.MakeThing(def);
        thing.stackCount = count;
        return thing;
    }
}
