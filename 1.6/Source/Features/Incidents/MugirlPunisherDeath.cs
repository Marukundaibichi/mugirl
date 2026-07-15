using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Mugirl
{
    public class DeathActionWorker_MugirlPunisher : DeathActionWorker
    {
        private static readonly IntRange SteelCountRange = new IntRange(80, 140);
        private static readonly IntRange ComponentCountRange = new IntRange(4, 8);
        private static readonly IntRange PlasteelCountRange = new IntRange(20, 40);

        public override void PawnDied(Corpse corpse, Lord prevLord)
        {
            Map map = corpse?.Map;
            if (corpse == null || map == null || !corpse.Spawned)
            {
                return;
            }

            IntVec3 originCell = corpse.Position;
            Vector3 origin = originCell.ToVector3Shifted();
            FleckMaker.Static(origin, map, MugirlContentDefOf.ShockwaveFast, 0.025f);
            FleckMaker.Static(origin, map, FleckDefOf.ExplosionFlash, 4.5f);
            FleckMaker.ThrowLightningGlow(origin, map, 3f);
            FleckMaker.ThrowSmoke(origin, map, 3.4f);
            FleckMaker.ThrowDustPuff(origin, map, 4.2f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 burstPosition = origin + Gen.RandomHorizontalVector(Rand.Range(0.3f, 1.8f));
                FleckMaker.ThrowMicroSparks(burstPosition, map);
                if (i % 2 == 0)
                {
                    FleckMaker.ThrowDustPuffThick(burstPosition, map, Rand.Range(0.7f, 1.5f), Color.white);
                }
            }

            List<Thing> loot = new List<Thing>();
            Corpse muffaloCorpse = MakeMuffaloCorpse(map);
            if (muffaloCorpse != null)
            {
                loot.Add(muffaloCorpse);
            }

            loot.Add(MakeStack(ThingDefOf.Steel, SteelCountRange.RandomInRange));
            loot.Add(MakeStack(ThingDefOf.ComponentIndustrial, ComponentCountRange.RandomInRange));
            loot.Add(MakeStack(ThingDefOf.Plasteel, PlasteelCountRange.RandomInRange));

            corpse.Destroy(DestroyMode.Vanish);
            MugirlPunisherLootBurst burst = new MugirlPunisherLootBurst(map, origin, originCell, loot);
            MapComponent_MugirlPunisher component = map.GetComponent<MapComponent_MugirlPunisher>();
            if (component != null)
            {
                component.AddLootBurst(burst);
            }
            else
            {
                burst.FinalizeDrops();
            }
        }

        private static Corpse MakeMuffaloCorpse(Map map)
        {
            Pawn muffalo = PawnGenerator.GeneratePawn(PawnKindDefOf.Muffalo, null, map.Tile);
            if (muffalo == null)
            {
                return null;
            }

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

    internal sealed class MugirlPunisherLootBurst
    {
        private const int DurationTicks = 28;

        private readonly Map map;
        private readonly Vector3 origin;
        private readonly List<DebrisArc> arcs = new List<DebrisArc>();
        private readonly int startTick;

        internal bool Expired { get; private set; }

        internal MugirlPunisherLootBurst(Map map, Vector3 origin, IntVec3 centerCell, List<Thing> payloads)
        {
            this.map = map;
            this.origin = origin;
            startTick = MugirlTickUtility.CurrentGameTickOrFallback(0);
            BuildArcs(centerCell, payloads);
            if (arcs.Count == 0)
            {
                Expired = true;
            }
        }

        internal void Tick()
        {
            if (Expired || map == null)
            {
                Expired = true;
                return;
            }

            int age = MugirlTickUtility.CurrentGameTickOrFallback(startTick) - startTick;
            if (age >= DurationTicks)
            {
                FinalizeDrops();
                Expired = true;
            }
        }

        internal void Draw()
        {
            if (Expired || !MugirlGameUtility.IsCurrentMap(map))
            {
                return;
            }

            int age = MugirlTickUtility.CurrentGameTickOrFallback(startTick) - startTick;
            float t = Mathf.Clamp01(age / (float)Mathf.Max(1, DurationTicks - 1));
            for (int i = 0; i < arcs.Count; i++)
            {
                DebrisArc arc = arcs[i];
                Vector3 drawPos = EvaluateArcPos(arc, t);
                if (arc.thing is Corpse)
                {
                    arc.thing.DrawNowAt(drawPos);
                }
                else
                {
                    float extraRotation = Mathf.Lerp(0f, arc.spin, t);
                    arc.thing.Graphic?.Draw(drawPos, Rot4.North, arc.thing, extraRotation);
                }
            }
        }

        private void BuildArcs(IntVec3 centerCell, List<Thing> payloads)
        {
            for (int i = 0; i < payloads.Count; i++)
            {
                Thing thing = payloads[i];
                if (thing == null)
                {
                    continue;
                }

                bool corpseLike = thing is Corpse;
                IntVec3 targetCell;
                if (corpseLike)
                {
                    bool foundCell = CellFinder.TryFindRandomCellNear(
                        centerCell,
                        map,
                        2,
                        cell => cell.InBounds(map) && cell.Walkable(map) && cell != centerCell,
                        out targetCell);
                    if (!foundCell)
                    {
                        targetCell = centerCell.ClampInsideMap(map);
                    }
                }
                else
                {
                    targetCell = CellFinder.RandomClosewalkCellNear(centerCell, map, 8);
                }

                float launchHeight = corpseLike ? Rand.Range(0.95f, 1.35f) : Rand.Range(0.65f, 1.08f);
                float sideSwing = corpseLike ? Rand.Range(-0.42f, 0.42f) : Rand.Range(-0.26f, 0.26f);
                float spin = corpseLike ? 0f : Rand.Range(-240f, 240f);
                arcs.Add(new DebrisArc(thing, targetCell, launchHeight, sideSwing, spin));
            }
        }

        internal void FinalizeDrops()
        {
            for (int i = 0; i < arcs.Count; i++)
            {
                DebrisArc arc = arcs[i];
                if (arc.thing.Destroyed)
                {
                    continue;
                }

                GenPlace.TryPlaceThing(
                    arc.thing,
                    arc.targetCell,
                    map,
                    ThingPlaceMode.Near,
                    rot: arc.thing.def.rotatable ? Rot4.Random : Rot4.North);
            }
        }

        private Vector3 EvaluateArcPos(DebrisArc arc, float t)
        {
            Vector3 target = arc.targetCell.ToVector3Shifted();
            Vector3 linear = Vector3.Lerp(origin, target, t);
            Vector3 path = target - origin;
            path.y = 0f;
            Vector3 normal = path.MagnitudeHorizontalSquared() > 0.001f
                ? path.normalized.RotatedBy(90f)
                : new Vector3(1f, 0f, 0f);
            linear += normal * Mathf.Sin(t * Mathf.PI) * arc.sideSwing;
            linear += Vector3.forward * Mathf.Sin(t * Mathf.PI) * arc.launchHeight;
            linear.y = AltitudeLayer.Pawn.AltitudeFor() + 0.04f + Mathf.Sin(t * Mathf.PI) * 0.10f;
            return linear;
        }

        private readonly struct DebrisArc
        {
            internal readonly Thing thing;
            internal readonly IntVec3 targetCell;
            internal readonly float launchHeight;
            internal readonly float sideSwing;
            internal readonly float spin;

            internal DebrisArc(Thing thing, IntVec3 targetCell, float launchHeight, float sideSwing, float spin)
            {
                this.thing = thing;
                this.targetCell = targetCell;
                this.launchHeight = launchHeight;
                this.sideSwing = sideSwing;
                this.spin = spin;
            }
        }
    }
}
