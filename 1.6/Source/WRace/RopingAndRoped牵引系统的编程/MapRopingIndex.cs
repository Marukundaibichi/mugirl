using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    public sealed class MapRopingIndex : MapComponent
    {
        private readonly Dictionary<Pawn, List<Pawn>> ropeesByRoper = new Dictionary<Pawn, List<Pawn>>();
        private readonly Dictionary<Pawn, Pawn> roperByRopee = new Dictionary<Pawn, Pawn>();
        private readonly HashSet<Pawn> ropedToSpot = new HashSet<Pawn>();
        private readonly HashSet<Pawn> pendingSpotRope = new HashSet<Pawn>();

        public MapRopingIndex(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            // 索引是运行期缓存；低频重建用于兜底处理原版或其他 mod 直接改 RopeTracker 的情况。
            if (Find.TickManager.TicksGame % 250 == 0)
            {
                RebuildFromMap();
            }
        }

        public void RebuildFromMap()
        {
            ropeesByRoper.Clear();
            roperByRopee.Clear();
            ropedToSpot.Clear();

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                Pawn_RopeTracker roping = pawn.roping;
                if (roping == null)
                {
                    continue;
                }

                List<Pawn> ropees = roping.Ropees;
                if (ropees != null)
                {
                    for (int j = 0; j < ropees.Count; j++)
                    {
                        RegisterPawnRope(pawn, ropees[j]);
                    }
                }

                if (roping.IsRopedToSpot)
                {
                    RegisterRopedToSpot(pawn);
                }
            }
        }

        public void RegisterPawnRope(Pawn roper, Pawn ropee)
        {
            if (!CanIndex(roper) || !CanIndex(ropee) || roper.Map != map || ropee.Map != map)
            {
                return;
            }

            RemoveRopeeLink(ropee);
            ropedToSpot.Remove(ropee);

            List<Pawn> ropees;
            if (!ropeesByRoper.TryGetValue(roper, out ropees))
            {
                ropees = new List<Pawn>();
                ropeesByRoper.Add(roper, ropees);
            }

            if (!ropees.Contains(ropee))
            {
                ropees.Add(ropee);
            }
            roperByRopee[ropee] = roper;
        }

        public void RegisterRopedToSpot(Pawn ropee)
        {
            if (!CanIndex(ropee) || ropee.Map != map)
            {
                return;
            }

            RemoveRopeeLink(ropee);
            ropedToSpot.Add(ropee);
        }

        public void RemovePawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            pendingSpotRope.Remove(pawn);
            ropedToSpot.Remove(pawn);
            RemoveRopeeLink(pawn);

            List<Pawn> ropees;
            if (ropeesByRoper.TryGetValue(pawn, out ropees))
            {
                for (int i = 0; i < ropees.Count; i++)
                {
                    roperByRopee.Remove(ropees[i]);
                }
                ropeesByRoper.Remove(pawn);
            }
        }

        public List<Pawn> RopeesFor(Pawn roper)
        {
            if (roper == null)
            {
                return null;
            }

            List<Pawn> ropees;
            return ropeesByRoper.TryGetValue(roper, out ropees) ? ropees : null;
        }

        public Pawn RoperFor(Pawn ropee)
        {
            if (ropee == null)
            {
                return null;
            }

            Pawn roper;
            return roperByRopee.TryGetValue(ropee, out roper) ? roper : null;
        }

        public bool IsRopedToSpot(Pawn pawn)
        {
            return pawn != null && ropedToSpot.Contains(pawn);
        }

        public void MarkPendingSpotRope(Pawn pawn)
        {
            if (CanIndex(pawn) && pawn.Map == map)
            {
                pendingSpotRope.Add(pawn);
            }
        }

        public void ClearPendingSpotRope(Pawn pawn)
        {
            if (pawn != null)
            {
                pendingSpotRope.Remove(pawn);
            }
        }

        public bool IsPendingSpotRope(Pawn pawn)
        {
            return pawn != null && pendingSpotRope.Contains(pawn);
        }

        private void RemoveRopeeLink(Pawn ropee)
        {
            Pawn roper;
            if (!roperByRopee.TryGetValue(ropee, out roper))
            {
                return;
            }

            roperByRopee.Remove(ropee);

            List<Pawn> ropees;
            if (ropeesByRoper.TryGetValue(roper, out ropees))
            {
                ropees.Remove(ropee);
                if (ropees.Count == 0)
                {
                    ropeesByRoper.Remove(roper);
                }
            }
        }

        private static bool CanIndex(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map != null;
        }
    }
}
