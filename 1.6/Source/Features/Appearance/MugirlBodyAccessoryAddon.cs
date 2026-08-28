using AlienRace;
using System.Reflection;
using Verse;

namespace Mugirl.Features.Appearance
{
    /// <summary>
    /// A HAR body-addon whose first variant is the empty option and whose
    /// remaining variants are selected with a configurable combined chance.
    /// Manual styling selections still use HAR's saved variant unchanged.
    /// </summary>
    public sealed class MugirlBodyAccessoryAddon : AlienPartGenerator.BodyAddon
    {
        private static readonly FieldInfo NameField = typeof(AlienPartGenerator.BodyAddon).GetField(
            "name",
            BindingFlags.Instance | BindingFlags.NonPublic);

        public float generationChance = 0.35f;

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
            if (!savedIndex.HasValue && !linkVariantIndexWithPrevious && VariantCountMax > 1)
            {
                int generatedVariant = Rand.Chance(generationChance)
                    ? Rand.Range(1, VariantCountMax)
                    : 0;

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
    }
}
