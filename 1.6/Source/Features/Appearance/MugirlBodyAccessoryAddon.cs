using AlienRace;
using System.Reflection;
using Verse;

namespace Mugirl.Features.Appearance
{
    internal static class MugirlBodyAccessoryVariants
    {
        internal const int None = 0;
        internal const int LovinBite = 1;
        internal const int LovinHandprint = 2;
        internal const int Barcode = 3;
        internal const int ChemicalScar = 5;
    }

    /// <summary>
    /// A HAR body-addon whose first variant is the empty option and whose
    /// remaining variants are selected with a configurable combined chance.
    /// Manual styling selections still use HAR's saved variant unchanged.
    /// </summary>
    public sealed class MugirlBodyAccessoryAddon : AlienPartGenerator.BodyAddon
    {
        // StaticCacheLifecycle: 进程级 HAR 字段元数据；不持有 Pawn、地图或存档对象。
        private static readonly FieldInfo NameField = typeof(AlienPartGenerator.BodyAddon).GetField(
            "name",
            BindingFlags.Instance | BindingFlags.NonPublic);

        public float experimentalMarkChance = 0.35f;
        public float chemicalScarShare = 0.5f;

        public MugirlBodyAccessoryAddon()
        {
            NameField?.SetValue(this, "身体附件");
            ColorChannel = "skin";
        }

        public override string GetPath(
            Pawn pawn,
            ref int sharedIndex,
            int? savedIndex = 0,
            string pathAppendix = null)
        {
            CompMugirlBodyAccessory markComp = pawn?.TryGetComp<CompMugirlBodyAccessory>();
            int temporaryVariant = markComp?.TemporaryVariant ?? MugirlBodyAccessoryVariants.None;
            if (temporaryVariant != MugirlBodyAccessoryVariants.None)
            {
                return base.GetPath(pawn, ref sharedIndex, temporaryVariant, pathAppendix);
            }

            if (!savedIndex.HasValue && !linkVariantIndexWithPrevious && VariantCountMax > 1)
            {
                int generatedVariant = GenerateInitialVariant(pawn);

                return base.GetPath(pawn, ref sharedIndex, generatedVariant, pathAppendix);
            }

            return base.GetPath(pawn, ref sharedIndex, savedIndex, pathAppendix);
        }

        public override Graphic GetGraphic(
            Pawn pawn,
            AlienPartGenerator.AlienComp alienComp,
            ref int sharedIndex,
            int? savedIndex = null,
            bool precheckCompare = false,
            Graphic preGraphic = null)
        {
            ShaderTypeDef overlayShader = DefDatabase<ShaderTypeDef>.GetNamedSilentFail("CutoutSkinOverlay");
            if (overlayShader != null)
            {
                ShaderType = overlayShader;
                ShaderTypeStatue = overlayShader;
            }

            return base.GetGraphic(
                pawn,
                alienComp,
                ref sharedIndex,
                savedIndex,
                precheckCompare,
                preGraphic);
        }

        private int GenerateInitialVariant(Pawn pawn)
        {
            if (pawn?.story?.Childhood != Mugirl_DefOf.Mugirl_ExperimentalChild
                || !Rand.Chance(experimentalMarkChance))
            {
                return MugirlBodyAccessoryVariants.None;
            }

            return Rand.Chance(chemicalScarShare)
                ? MugirlBodyAccessoryVariants.ChemicalScar
                : MugirlBodyAccessoryVariants.Barcode;
        }
    }
}
