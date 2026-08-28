using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Mugirl
{
    /// <summary>
    /// 可复用于后续雪牛娘机械体的分节机械尾配置。
    /// 所有坐标均为 PawnRenderTree 的屏幕平面坐标（x, z）。
    /// </summary>
    public sealed class CompProperties_SegmentedMechTail : CompProperties
    {
        public string segmentTexPath;
        public string tipTexPath;
        public string activeTipTexPath;

        public int segmentCount = 6;
        public float segmentLength = 0.25f;
        public float segmentOverlap = 1.08f;
        public float texturePixelSize = 512f;
        public float segmentVisibleWidthPixels = 222f;
        public Vector2 segmentVisibleCenterPixels = new Vector2(274f, 238f);
        public Vector2 tipCanvasDrawSize = new Vector2(1.3f, 1.3f);
        public Vector2 tipHingePixels = new Vector2(87f, 252f);

        public Vector2 northAnchor = new Vector2(0f, -0.38f);
        public Vector2 eastAnchor = new Vector2(-0.40f, -0.10f);
        public Vector2 southAnchor = new Vector2(0f, 0.38f);

        public float northStartAngle = 200f;
        public float eastStartAngle = 180f;
        public float southStartAngle = 20f;
        public float northBendPerSegment = -30f;
        public float eastBendPerSegment = -13f;
        public float southBendPerSegment = -30f;

        public float idleWagDegrees = 11f;
        public float idleWagSpeed = 0.075f;
        public int idleAnimationIntervalTicks = 2;
        public int attackAnimationTicks = 24;
        public float attackTipBackoff = 0.42f;
        public int assistCooldownTicks = 30;
        public float assistDamage = 9f;
        public float assistArmorPenetration = 0.28f;

        public float segmentLayer = -3f;
        public float tipLayer = 5f;
        public BodyPartDef requiredTailPart;
        public BodyPartGroupDef tailBodyPartGroup;

        public CompProperties_SegmentedMechTail()
        {
            compClass = typeof(Comp_SegmentedMechTail);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            if (segmentTexPath.NullOrEmpty())
            {
                yield return parentDef.defName + " has a segmented mech tail without segmentTexPath.";
            }
            if (tipTexPath.NullOrEmpty())
            {
                yield return parentDef.defName + " has a segmented mech tail without tipTexPath.";
            }
            if (segmentCount < 2 || segmentCount > 16)
            {
                yield return parentDef.defName + " segmented mech tail segmentCount must be between 2 and 16.";
            }
            if (segmentLength <= 0f
                || segmentOverlap <= 0f
                || segmentVisibleWidthPixels <= 0f
                || texturePixelSize <= 0f)
            {
                yield return parentDef.defName + " segmented mech tail has invalid segment dimensions.";
            }
            if (tipCanvasDrawSize.x <= 0f || tipCanvasDrawSize.y <= 0f)
            {
                yield return parentDef.defName + " segmented mech tail has invalid tipCanvasDrawSize.";
            }
            if (attackAnimationTicks <= 0)
            {
                yield return parentDef.defName + " segmented mech tail attackAnimationTicks must be positive.";
            }
            if (idleAnimationIntervalTicks < 1 || idleAnimationIntervalTicks > 10)
            {
                yield return parentDef.defName + " segmented mech tail idleAnimationIntervalTicks must be between 1 and 10.";
            }
            if (attackTipBackoff < 0f || assistCooldownTicks < 0)
            {
                yield return parentDef.defName + " segmented mech tail has invalid attack timing or reach values.";
            }
            if (assistDamage < 0f || assistArmorPenetration < 0f)
            {
                yield return parentDef.defName + " segmented mech tail assist damage values cannot be negative.";
            }
            if (!AllFinite())
            {
                yield return parentDef.defName + " segmented mech tail contains a non-finite numeric value.";
            }
            if (!PixelAnchorInsideTexture(segmentVisibleCenterPixels)
                || !PixelAnchorInsideTexture(tipHingePixels))
            {
                yield return parentDef.defName + " segmented mech tail has a sprite anchor outside its texture canvas.";
            }
        }

        private bool AllFinite()
        {
            return IsFinite(segmentLength)
                && IsFinite(segmentOverlap)
                && IsFinite(texturePixelSize)
                && IsFinite(segmentVisibleWidthPixels)
                && IsFinite(segmentVisibleCenterPixels.x)
                && IsFinite(segmentVisibleCenterPixels.y)
                && IsFinite(tipCanvasDrawSize.x)
                && IsFinite(tipCanvasDrawSize.y)
                && IsFinite(tipHingePixels.x)
                && IsFinite(tipHingePixels.y)
                && IsFinite(northAnchor.x)
                && IsFinite(northAnchor.y)
                && IsFinite(eastAnchor.x)
                && IsFinite(eastAnchor.y)
                && IsFinite(southAnchor.x)
                && IsFinite(southAnchor.y)
                && IsFinite(northStartAngle)
                && IsFinite(eastStartAngle)
                && IsFinite(southStartAngle)
                && IsFinite(northBendPerSegment)
                && IsFinite(eastBendPerSegment)
                && IsFinite(southBendPerSegment)
                && IsFinite(idleWagDegrees)
                && IsFinite(idleWagSpeed)
                && IsFinite(attackTipBackoff)
                && IsFinite(assistDamage)
                && IsFinite(assistArmorPenetration)
                && IsFinite(segmentLayer)
                && IsFinite(tipLayer);
        }

        private bool PixelAnchorInsideTexture(Vector2 anchor)
        {
            return anchor.x >= 0f
                && anchor.y >= 0f
                && anchor.x <= texturePixelSize
                && anchor.y <= texturePixelSize;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
