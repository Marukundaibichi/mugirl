namespace Mugirl.Features.Restraints;

// 电击 Hediff 的配置：命中时的声音、移除/追加的 Hediff、眩晕与抖动，以及持续期间的污渍。
public class HediffCompProperties_ElectricShock : HediffCompProperties
{
    public List<SoundDef>? shockSounds;
    public List<HediffDef>? hediffsToRemove;
    public List<HediffWithParams>? hediffsToApplyWithParams;
    public List<FilthEntry>? filthSpawnEntries;

    // 眩晕时长（秒）；敌对目标追加 Hediff 的严重度按 hostileStunDuration / stunDuration 计算。
    public float stunDuration = 2f;
    public float hostileStunDuration = 4f;
    public float jitterMagnitude = 0.3f;

    public HediffCompProperties_ElectricShock()
    {
        compClass = typeof(HediffComp_ElectricShock);
    }

    public class HediffWithParams
    {
        public HediffDef? hediff;

        // 小于 0 时按敌对与否取默认严重度。
        public float severity = -1f;

        // XML 写 BodyPartDef 的 defName（如 Neck）；1.6 是字符串并逐部位比较 defName，改为 Def 引用后由加载器校验拼写。
        public BodyPartDef? bodyPartTarget;
    }

    public class FilthEntry
    {
        public ThingDef? filthDef;
        public float filthAmount = 3f;
        public int spawnInterval = 60;
        public bool spawnInFacingDirection;
    }
}
