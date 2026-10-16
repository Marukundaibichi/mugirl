using System.Reflection;
using AlienRace;
using HarmonyLib;

namespace Mugirl.Features.Appearance;

/// <summary>
/// HAR 身体附件：变体 0 为空，临时 Lovin 印记优先于存档变体；首次生成时“增产实验对象”背景按概率获得永久标记。
/// 梳妆台中的手动选择仍保存在 HAR 的变体里，不受影响。
/// </summary>
public sealed class BodyAccessoryAddon : AlienPartGenerator.BodyAddon
{
    private const string DisplayNameKey = "Mugirl.Appearance.BodyAccessoryAddon";

    // StaticCacheLifecycle: 进程级 HAR 私有字段访问器，只缓存程序集元数据，不持有 Pawn、地图或存档对象。
    // HAR 改名或删除该字段时为 null，梳妆台改用 HAR 默认的文件名标签，不影响 XML 加载。
    private static readonly AccessTools.FieldRef<AlienPartGenerator.BodyAddon, string>? NameField = ResolveNameField();

    public float experimentalMarkChance = 0.35f;
    public float chemicalScarShare = 0.5f;

    // 显示名需要 Keyed 文本，而 XML 构造本对象时语言数据尚未加载；首次取图时再写入。
    private bool displayNameApplied;

    public BodyAccessoryAddon()
    {
        ColorChannel = "skin";
    }

    public override string GetPath(
        Pawn? pawn,
        ref int sharedIndex,
        int? savedIndex = 0,
        string? pathAppendix = null)
    {
        EnsureDisplayName();
        int temporaryVariant = pawn?.TryGetComp<CompBodyAccessory>()?.TemporaryVariant
            ?? BodyAccessoryVariants.None;
        if (temporaryVariant != BodyAccessoryVariants.None)
        {
            return base.GetPath(pawn, ref sharedIndex, temporaryVariant, pathAppendix);
        }

        if (!savedIndex.HasValue && !linkVariantIndexWithPrevious && VariantCountMax > 1)
        {
            return base.GetPath(pawn, ref sharedIndex, GenerateInitialVariant(pawn), pathAppendix);
        }

        return base.GetPath(pawn, ref sharedIndex, savedIndex, pathAppendix);
    }

    public override Graphic GetGraphic(
        Pawn pawn,
        AlienPartGenerator.AlienComp alienComp,
        ref int sharedIndex,
        int? savedIndex = null,
        bool precheckCompare = false,
        Graphic? preGraphic = null)
    {
        EnsureDisplayName();
        // 印记贴图以皮肤叠加层绘制；雕像也用同一着色器，保持与身体同色。
        ShaderTypeDef overlayShader = AppearanceDefOf.CutoutSkinOverlay;
        ShaderType = overlayShader;
        ShaderTypeStatue = overlayShader;

        return base.GetGraphic(
            pawn,
            alienComp,
            ref sharedIndex,
            savedIndex,
            precheckCompare,
            preGraphic);
    }

    private int GenerateInitialVariant(Pawn? pawn)
    {
        if (pawn?.story?.Childhood != MugirlDefOf.Mugirl_ExperimentalChild
            || !Rand.Chance(experimentalMarkChance))
        {
            return BodyAccessoryVariants.None;
        }

        return Rand.Chance(chemicalScarShare)
            ? BodyAccessoryVariants.ChemicalScar
            : BodyAccessoryVariants.Barcode;
    }

    // HAR 梳妆台直接显示 BodyAddon.Name，不做翻译；这里写入当前语言的标签（1.6 固定写入中文）。
    // 可能在渲染工作线程上调用：只读语言数据，写入的是同一个字符串引用，重复写入无害。
    private void EnsureDisplayName()
    {
        if (displayNameApplied || NameField is null || LanguageDatabase.activeLanguage is null)
        {
            return;
        }

        NameField(this) = DisplayNameKey.Translate().Resolve();
        displayNameApplied = true;
    }

    private static AccessTools.FieldRef<AlienPartGenerator.BodyAddon, string>? ResolveNameField()
    {
        FieldInfo? field = AccessTools.DeclaredField(typeof(AlienPartGenerator.BodyAddon), "name");
        return field?.FieldType == typeof(string)
            ? AccessTools.FieldRefAccess<AlienPartGenerator.BodyAddon, string>(field)
            : null;
    }
}
