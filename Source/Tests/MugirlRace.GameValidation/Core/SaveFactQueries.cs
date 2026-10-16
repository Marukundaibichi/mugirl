namespace Mugirl.GameValidation.Core;

/// <summary>
/// <see cref="SaveFacts"/> 读存档时用的名字：被测构建的 Def 名集合、束缚服装 Def 与它们的默认值，以及按功能列出的 Scribe 标签。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Legacy"/> 是 MugirlRace 时代各发布构建（0.4.7 … 1004）写存档时用的标签，取自这些构建的源码（1.6/Source 与 git 历史），
/// 与升级器无关：夹具的事实必须用独立的旧名查询得出，不能来自升级器的输出（否则是循环论证）。
/// 读升级后的重存档时，用升级清单（K1）把这些名字换成新名（<see cref="Translate"/>），再与夹具的事实比较。
/// </para>
/// <para>标签只描述“在哪里读”；事实的键不含这些标签（见 <see cref="SaveFacts"/>），所以标签改名不改变键。</para>
/// </remarks>
internal sealed record SaveFactQueries
{
    /// <summary>束缚服装的锁次数、上锁标志、破解标志（RestraintApparel 及其子类）与电击项圈组件的计时。</summary>
    public const string LockCountKey = "lockCount";

    public const string IsLockedKey = "isLocked";

    public const string IsCrackedKey = "isCracked";

    public const string TimerKey = "timer";

    /// <summary>被测构建定义的全部 Def 名（任意 Def 类型）。</summary>
    public required IReadOnlySet<string> ModDefs { get; init; }

    /// <summary>被测构建定义的 ThingDef 名（含种族、隐含的尸体等）。</summary>
    public required IReadOnlySet<string> ModThingDefs { get; init; }

    /// <summary>
    /// 束缚服装的 ThingDef 名 → 各事实（<see cref="LockCountKey"/> 等）在节点中没有写出时的值（构建的默认值；锁次数的默认值取决于 Def，
    /// 由播种套件在旧构建中按 Def 求出，写进夹具清单）。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RestraintDefaults { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    /// <summary>Pawn 节点上的奶量标签（CompMilkProduction 的 SaveKey，写为 "milkFullness"）。</summary>
    public string? MilkFullnessLabel { get; init; } = "milkFullness";

    /// <summary>武器轮盘：槽位引用列表、备用武器容器、已解锁槽位。</summary>
    public string? WheelSlotsLabel { get; init; } = "weaponWheelSlots";

    public string? WheelReserveLabel { get; init; } = "weaponWheelReserveWeapons";

    public string? WheelUnlockedLabel { get; init; } = "weaponWheelUnlockedSlots";

    public bool WheelRecords { get; init; }

    public bool NestedComponentFacts { get; init; }

    /// <summary>束缚服装节点中各事实对应的标签（键见 <see cref="LockCountKey"/> 等）。</summary>
    public IReadOnlyDictionary<string, string?> RestraintLabels { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        [LockCountKey] = "lockCount",
        [IsLockedKey] = "isLocked",
        [IsCrackedKey] = "isCracked",
        [TimerKey] = "ticks",
    };

    /// <summary>原版 Hediff 的标签；Mod Hediff 的其他直接标量子元素（HediffComp 的计时、触发标志等）记为事实。</summary>
    public IReadOnlySet<string> VanillaHediffLabels { get; init; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "def", "loadID", "ageTicks", "tickAdded", "severity", "part", "source", "sourceBodyPartGroup", "sourceHediffDef",
        "sourceLabel", "sourceToolLabel", "combatLogEntry", "combatLogText", "causesNoPain", "visible", "canBeThreateningToPart",
    };

    /// <summary>MugirlRace 时代发布构建的读取名。</summary>
    public static SaveFactQueries Legacy(IEnumerable<string> modDefs, IEnumerable<string> modThingDefs, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? restraintDefaults = null)
        => new()
        {
            ModDefs = new HashSet<string>(modDefs, StringComparer.Ordinal),
            ModThingDefs = new HashSet<string>(modThingDefs, StringComparer.Ordinal),
            RestraintDefaults = restraintDefaults ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal),
        };

    /// <summary>当前结构的事实摘要；深层完整性仍由逐节点比较检查，不由摘要计数推断。</summary>
    public static SaveFactQueries Current(IEnumerable<string> modDefs, IEnumerable<string> modThingDefs, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? restraintDefaults = null)
        => Legacy(modDefs, modThingDefs, restraintDefaults) with
        {
            MilkFullnessLabel = "mugirlMilkFullness",
            WheelSlotsLabel = "mugirlWheelSlotRecords",
            WheelReserveLabel = "mugirlWheelReserveWeapons",
            WheelUnlockedLabel = null,
            WheelRecords = true,
            NestedComponentFacts = true,
            RestraintLabels = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [LockCountKey] = "mugirlRestraintLockCount", [IsLockedKey] = "mugirlRestraintLocked",
                [IsCrackedKey] = "mugirlRestraintCracked", [TimerKey] = "mugirlShockCheckTicks",
            },
        };

    /// <summary>
    /// 用升级清单把读取名换成新名：Def 名按根类型（ThingDef 与任意根类型一致的映射）、标签按物品级 owner（组件或物品类）的标签行；
    /// 被删除或消费的标签记为 null（对应的事实读不到，比较方按“已删除”处理）。
    /// </summary>
    public SaveFactQueries Translate(SaveNameMap map) => this with
    {
        ModDefs = new HashSet<string>(ModDefs.Select(d => map.DefAnyRoot(d, out _)).OfType<string>().Concat(ModThingDefs.Select(d => map.Def("ThingDef", d)).OfType<string>()), StringComparer.Ordinal),
        ModThingDefs = new HashSet<string>(ModThingDefs.Select(d => map.Def("ThingDef", d)).OfType<string>(), StringComparer.Ordinal),
        RestraintDefaults = RestraintDefaults
            .Where(p => map.Def("ThingDef", p.Key) is not null)
            .ToDictionary(p => map.Def("ThingDef", p.Key)!, p => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(p.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        MilkFullnessLabel = ThingLabel(map, MilkFullnessLabel),
        WheelSlotsLabel = ThingLabel(map, WheelSlotsLabel),
        WheelReserveLabel = ThingLabel(map, WheelReserveLabel),
        WheelUnlockedLabel = ThingLabel(map, WheelUnlockedLabel),
        RestraintLabels = RestraintLabels.ToDictionary(p => p.Key, p => ThingLabel(map, p.Value), StringComparer.Ordinal),
    };

    // 物品节点中的标签：组件标签（ThingComp）或物品类自己的标签（Class）。
    private static string? ThingLabel(SaveNameMap map, string? label)
    {
        if (label is null)
        {
            return null;
        }

        SaveLabelFate fate = map.LabelFate(label, owner => owner.ThingCompClass is not null || owner.ClassNames.Count > 0);
        return fate.Kind switch
        {
            SaveLabelFateKind.Renamed => fate.NewLabel,
            SaveLabelFateKind.Dropped or SaveLabelFateKind.Consumed => null,
            _ => label,
        };
    }
}
