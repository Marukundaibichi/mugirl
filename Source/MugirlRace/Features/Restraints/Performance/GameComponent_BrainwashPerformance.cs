namespace Mugirl.Features.Restraints;

// 播放由洗脑头盔“意识形态转换”按钮触发、不依附 Hediff 的表演，保存每场表演自己的时间轴。
public class GameComponent_BrainwashPerformance : GameComponent
{
    private List<ActiveBrainwashPerformance> activePerformances = new List<ActiveBrainwashPerformance>();

    public GameComponent_BrainwashPerformance()
    {
    }

    public GameComponent_BrainwashPerformance(Game game)
    {
    }

    public static void StartFor(Pawn? pawn, HediffDef? sourceHediffDef)
    {
        if (pawn == null || sourceHediffDef == null)
        {
            return;
        }

        if (!GameUtility.TryGetGameComponent<GameComponent_BrainwashPerformance>(out GameComponent_BrainwashPerformance? comp))
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
        {
            return;
        }

        for (int i = activePerformances.Count - 1; i >= 0; i--)
        {
            ActiveBrainwashPerformance? performance = activePerformances[i];
            if (performance == null || performance.Tick())
            {
                activePerformances.RemoveAt(i);
            }
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref activePerformances, "activePerformances", LookMode.Deep);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            activePerformances ??= new List<ActiveBrainwashPerformance>();
        }
    }

    private void StartPerformance(Pawn pawn, HediffDef sourceHediffDef)
    {
        HediffCompProperties_BrainwashPerformance? props = GetPerformanceProps(sourceHediffDef);
        if (props == null)
        {
            return;
        }

        activePerformances ??= new List<ActiveBrainwashPerformance>();

        // 同一 Pawn 只保留最新的一场表演。
        for (int i = activePerformances.Count - 1; i >= 0; i--)
        {
            ActiveBrainwashPerformance? existing = activePerformances[i];
            if (existing?.Pawn == pawn)
            {
                existing.Stop();
                activePerformances.RemoveAt(i);
            }
        }

        ActiveBrainwashPerformance performance = new ActiveBrainwashPerformance();
        performance.Start(pawn, sourceHediffDef, props);
        activePerformances.Add(performance);
    }

    private static HediffCompProperties_BrainwashPerformance? GetPerformanceProps(HediffDef? hediffDef)
    {
        return hediffDef?.CompProps<HediffCompProperties_BrainwashPerformance>();
    }

    // 一场表演的持久状态；声明类型与运行时类型相同，存档不写 Class 属性。
    public class ActiveBrainwashPerformance : IExposable
    {
        private Pawn? pawn;
        private HediffDef? sourceHediffDef;
        private BrainwashPerformancePlayer player = new BrainwashPerformancePlayer();

        // 表演所属 Def 的配置引用；运行期实例缓存，读档时从 sourceHediffDef 重建。
        private HediffCompProperties_BrainwashPerformance? performanceProps;

        public Pawn? Pawn => pawn;

        public void Start(Pawn pawn, HediffDef sourceHediffDef, HediffCompProperties_BrainwashPerformance props)
        {
            this.pawn = pawn;
            this.sourceHediffDef = sourceHediffDef;
            performanceProps = props;
            player ??= new BrainwashPerformancePlayer();
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
                player ??= new BrainwashPerformancePlayer();
            }
        }
    }
}
