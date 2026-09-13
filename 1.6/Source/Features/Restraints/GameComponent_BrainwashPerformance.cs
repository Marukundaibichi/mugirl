using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    public class GameComponent_BrainwashPerformance : GameComponent
    {
        private List<ActiveBrainwashPerformance> activePerformances = new List<ActiveBrainwashPerformance>();

        public GameComponent_BrainwashPerformance()
        {
        }

        public GameComponent_BrainwashPerformance(Game game)
        {
        }

        public static void StartFor(Pawn pawn, HediffDef sourceHediffDef)
        {
            if (pawn == null || sourceHediffDef == null)
                return;

            if (!MugirlGameUtility.TryGetGameComponent(out GameComponent_BrainwashPerformance comp))
            {
                MugirlLog.WarningOnce(
                    "BrainwashPerformance.GameComponentMissing",
                    "Tried to start a Mugirl brainwash performance while its GameComponent was unavailable.");
                return;
            }

            comp.StartPerformance(pawn, sourceHediffDef);
        }

        public override void GameComponentTick()
        {
            if (activePerformances == null || activePerformances.Count == 0)
                return;

            for (int i = activePerformances.Count - 1; i >= 0; i--)
            {
                ActiveBrainwashPerformance performance = activePerformances[i];
                if (performance == null || performance.Tick())
                {
                    activePerformances.RemoveAt(i);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref activePerformances, "activeBrainwashPerformances", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && activePerformances == null)
            {
                activePerformances = new List<ActiveBrainwashPerformance>();
            }
        }

        private void StartPerformance(Pawn pawn, HediffDef sourceHediffDef)
        {
            CompProperties_PerformanceEffect props = GetPerformanceProps(sourceHediffDef);
            if (props == null)
                return;

            if (activePerformances == null)
            {
                activePerformances = new List<ActiveBrainwashPerformance>();
            }

            for (int i = activePerformances.Count - 1; i >= 0; i--)
            {
                if (activePerformances[i]?.Pawn == pawn)
                {
                    activePerformances[i].Stop();
                    activePerformances.RemoveAt(i);
                }
            }

            ActiveBrainwashPerformance performance = new ActiveBrainwashPerformance();
            performance.Start(pawn, sourceHediffDef, props);
            activePerformances.Add(performance);
        }

        private static CompProperties_PerformanceEffect GetPerformanceProps(HediffDef hediffDef)
        {
            if (hediffDef?.comps == null)
                return null;

            foreach (HediffCompProperties compProps in hediffDef.comps)
            {
                if (compProps is CompProperties_PerformanceEffect performanceProps)
                {
                    return performanceProps;
                }
            }

            return null;
        }

        public class ActiveBrainwashPerformance : IExposable
        {
            private Pawn pawn;
            private HediffDef sourceHediffDef;
            private BrainwashPerformancePlayer player = new BrainwashPerformancePlayer();
            // 表演所属 Def 的配置引用；运行期实例缓存，读档时从 sourceHediffDef 重建。
            private CompProperties_PerformanceEffect performanceProps;

            public Pawn Pawn => pawn;

            public void Start(Pawn pawn, HediffDef sourceHediffDef, CompProperties_PerformanceEffect props)
            {
                this.pawn = pawn;
                this.sourceHediffDef = sourceHediffDef;
                performanceProps = props;
                if (player == null)
                {
                    player = new BrainwashPerformancePlayer();
                }
                player.Start(props);
            }

            public bool Tick()
            {
                if (player == null)
                {
                    player = new BrainwashPerformancePlayer();
                    player.Start(performanceProps);
                }

                return player.Tick(pawn, performanceProps);
            }

            public void Stop()
            {
                player?.Stop();
            }

            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn");
                Scribe_Defs.Look(ref sourceHediffDef, "sourceHediffDef");
                Scribe_Deep.Look(ref player, "player");

                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    performanceProps = GetPerformanceProps(sourceHediffDef);
                    if (player == null)
                    {
                        player = new BrainwashPerformancePlayer();
                    }
                }
            }
        }
    }
}
