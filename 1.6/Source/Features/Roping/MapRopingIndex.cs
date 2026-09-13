using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Mugirl
{
    public sealed class MapRopingIndex : MapComponent
    {
        private const int ReconcileIntervalTicks = 250;
        private const int ReconcileBatchTicks = 10;
        private const int ReconcileBatches = ReconcileIntervalTicks / ReconcileBatchTicks;

        private readonly Dictionary<Pawn, List<Pawn>> ropeesByRoper = new Dictionary<Pawn, List<Pawn>>();
        private readonly Dictionary<Pawn, Pawn> roperByRopee = new Dictionary<Pawn, Pawn>();
        private readonly HashSet<Pawn> ropedToSpot = new HashSet<Pawn>();
        private readonly HashSet<Pawn> pendingSpotRope = new HashSet<Pawn>();
        private readonly List<Pawn> tmpPendingSpotRopeRemovals = new List<Pawn>();
        private readonly List<Pawn> tmpInvalidPawns = new List<Pawn>();
        private int reconcileTickCounter;
        private int pruneTickCounter;
        private int reconcilePawnIndex;
        private int reconcileWorkRemainder;

        public MapRopingIndex(Map map) : base(map)
        {
            // 不同地图分散到不同 tick；这些调度字段及索引只属于当前地图，不写入存档。
            int offset = (map?.uniqueID ?? 0) & int.MaxValue;
            reconcileTickCounter = offset % ReconcileBatchTicks;
            pruneTickCounter = offset % ReconcileIntervalTicks;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RebuildFromMap();
        }

        public override void MapComponentTick()
        {
            // 通知负责即时更新；始终轮询地图人物，发现其他 mod 绕过通知直接修改 tracker 的关系。
            // 每 250 tick 分摊一次全图的检查量，空索引也不禁用扫描；不再集中清空/重建列表。
            if (++reconcileTickCounter >= ReconcileBatchTicks)
            {
                reconcileTickCounter = 0;
                ReconcileNextBatch();
            }

            if (++pruneTickCounter >= ReconcileIntervalTicks)
            {
                pruneTickCounter = 0;
                PruneInvalidIndexedRopes();
                PruneInvalidPendingSpotRopes();
            }
        }

        public void RebuildFromMap()
        {
            PruneInvalidIndexedRopes();
            reconcilePawnIndex = 0;
            reconcileWorkRemainder = 0;

            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                PruneInvalidPendingSpotRopes();
                return;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                ReconcilePawn(pawns[i]);
            }

            PruneInvalidPendingSpotRopes();
        }

        private void ReconcileNextBatch()
        {
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            int pawnCount = pawns?.Count ?? 0;
            if (pawnCount == 0)
            {
                reconcilePawnIndex = 0;
                reconcileWorkRemainder = 0;
                return;
            }

            // 保留除法余数，使小地图也只是每 250 tick 检查一遍，而不是每 tick 重复扫描。
            reconcileWorkRemainder += pawnCount;
            int budget = reconcileWorkRemainder / ReconcileBatches;
            reconcileWorkRemainder %= ReconcileBatches;
            for (int i = 0; i < budget; i++)
            {
                if (reconcilePawnIndex >= pawnCount)
                {
                    reconcilePawnIndex = 0;
                }
                ReconcilePawn(pawns[reconcilePawnIndex++]);
            }
        }

        private void ReconcilePawn(Pawn pawn)
        {
            if (!CanIndex(pawn) || pawn.Map != map)
            {
                RemovePawn(pawn);
                return;
            }

            Pawn_RopeTracker tracker = pawn.roping;
            Pawn roper = tracker?.RopedByPawn;
            // ropee 的目标是原版绳索真值；不依赖可能已陈旧的牵引者 Ropees 副本。
            if (CanIndex(roper) && roper.Map == map)
            {
                RegisterPawnRope(roper, pawn);
            }
            else if (tracker?.IsRopedToSpot == true)
            {
                RegisterRopedToSpot(pawn);
            }
            else
            {
                RemoveRopeeLink(pawn);
                ropedToSpot.Remove(pawn);
                // 尚未完成的系绳 job 没有 tracker 关系，保留 pending 状态。
            }
        }

        public void RegisterPawnRope(Pawn roper, Pawn ropee)
        {
            if (!CanIndex(roper) || !CanIndex(ropee) || roper.Map != map || ropee.Map != map)
            {
                return;
            }

            ropedToSpot.Remove(ropee);
            pendingSpotRope.Remove(ropee);

            Pawn currentRoper;
            if (roperByRopee.TryGetValue(ropee, out currentRoper) && currentRoper == roper)
            {
                return;
            }

            RemoveRopeeLink(ropee);

            List<Pawn> ropees;
            if (!ropeesByRoper.TryGetValue(roper, out ropees))
            {
                ropees = new List<Pawn>();
                ropeesByRoper.Add(roper, ropees);
            }

            ropees.Add(ropee);
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
            pendingSpotRope.Remove(ropee);
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
                ropees.Clear();
                ropeesByRoper.Remove(pawn);
            }
        }

        public void RemovePawnAsRopee(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            pendingSpotRope.Remove(pawn);
            ropedToSpot.Remove(pawn);
            RemoveRopeeLink(pawn);
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

        private void PruneInvalidPendingSpotRopes()
        {
            if (pendingSpotRope.Count == 0)
            {
                return;
            }

            tmpPendingSpotRopeRemovals.Clear();
            foreach (Pawn pawn in pendingSpotRope)
            {
                if (!CanIndex(pawn) || pawn.Map != map || pawn.roping?.IsRopedToSpot == true)
                {
                    tmpPendingSpotRopeRemovals.Add(pawn);
                }
            }

            for (int i = 0; i < tmpPendingSpotRopeRemovals.Count; i++)
            {
                pendingSpotRope.Remove(tmpPendingSpotRopeRemovals[i]);
            }

            tmpPendingSpotRopeRemovals.Clear();
        }

        private void PruneInvalidIndexedRopes()
        {
            // 只检查索引中的关系，清理已不在 AllPawnsSpawned 中的销毁/跨图人物。
            // 复用移除缓冲区，避免遍历 Dictionary/HashSet 时修改集合。
            tmpInvalidPawns.Clear();
            foreach (KeyValuePair<Pawn, List<Pawn>> group in ropeesByRoper)
            {
                Pawn roper = group.Key;
                List<Pawn> ropees = group.Value;
                bool validRoper = CanIndex(roper) && roper.Map == map;
                int retained = 0;
                for (int i = 0; i < ropees.Count; i++)
                {
                    Pawn ropee = ropees[i];
                    if (validRoper && CanIndex(ropee) && ropee.Map == map && ropee.roping?.RopedByPawn == roper)
                    {
                        if (retained != i)
                        {
                            ropees[retained] = ropee;
                        }
                        retained++;
                    }
                    else
                    {
                        roperByRopee.Remove(ropee);
                    }
                }

                // 原地压紧，保留共享列表及次序；整组离图时避免逐个 Remove 的重复移动。
                if (retained < ropees.Count)
                {
                    ropees.RemoveRange(retained, ropees.Count - retained);
                }
                if (retained == 0)
                {
                    tmpInvalidPawns.Add(roper);
                }
            }
            for (int i = 0; i < tmpInvalidPawns.Count; i++)
            {
                ropeesByRoper.Remove(tmpInvalidPawns[i]);
            }

            tmpInvalidPawns.Clear();
            foreach (Pawn pawn in ropedToSpot)
            {
                if (!CanIndex(pawn) || pawn.Map != map || pawn.roping?.IsRopedToSpot != true)
                {
                    tmpInvalidPawns.Add(pawn);
                }
            }
            for (int i = 0; i < tmpInvalidPawns.Count; i++)
            {
                ropedToSpot.Remove(tmpInvalidPawns[i]);
            }
            tmpInvalidPawns.Clear();
        }

        private static bool CanIndex(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map != null;
        }
    }
}
