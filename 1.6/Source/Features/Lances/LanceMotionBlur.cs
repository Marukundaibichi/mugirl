using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl.Features.Lances
{
    internal static class LanceMotionBlur
    {
        // StaticCacheLifecycle: 固定程序集访问器及单一 GPU 材质/属性块，进程结束释放，不持有角色或地图。
        private static readonly AccessTools.FieldRef<PawnRenderTree, List<PawnGraphicDrawRequest>> Requests =
            AccessTools.FieldRefAccess<PawnRenderTree, List<PawnGraphicDrawRequest>>("drawRequests");
        private static Material material;
        private static MaterialPropertyBlock properties;
        private static bool loadAttempted;
        // StaticCacheLifecycle: 网格元数据使用弱引用，随原版网格释放，不复制或缓存人物贴图。
        private static readonly ConditionalWeakTable<Mesh, Quad> quads = new ConditionalWeakTable<Mesh, Quad>();
        // StaticCacheLifecycle: 落地视觉状态弱关联 Pawn，换局或回收结束即删除；不写入存档、不影响玩法。
        private static readonly ConditionalWeakTable<Pawn, Recovery> recoveries = new ConditionalWeakTable<Pawn, Recovery>();
        internal const int RecoveryTicks = 18;

        private sealed class Recovery
        {
            internal Map map;
            internal int startTick;
            internal Vector3 travel;
        }

        internal static void BeginRecovery(Pawn pawn, Vector3 travel)
        {
            if (pawn == null || travel.sqrMagnitude < 0.0001f || !MugirlTickUtility.TryGetCurrentGameTick(out int tick)) return;
            recoveries.Remove(pawn);
            recoveries.Add(pawn, new Recovery { map = pawn.MapHeld, startTick = tick, travel = travel });
        }

        internal static float RecoveryScale(Pawn pawn)
        {
            if (pawn == null || !recoveries.TryGetValue(pawn, out Recovery recovery)) return 0f;
            int elapsed = MugirlTickUtility.CurrentGameTickOrFallback(recovery.startTick + RecoveryTicks) - recovery.startTick;
            if (recovery.map != pawn.MapHeld || elapsed < 0 || elapsed >= RecoveryTicks)
            {
                recoveries.Remove(pawn);
                return 0f;
            }
            float remaining = 1f - (float)elapsed / RecoveryTicks;
            return remaining * remaining * (3f - 2f * remaining);
        }

        internal static void DrawRecovery(Pawn pawn)
        {
            float scale = RecoveryScale(pawn);
            if (scale <= 0f || !pawn.Spawned || pawn.ParentHolder is PawnFlyer_LanceCharge) return;
            if (recoveries.TryGetValue(pawn, out Recovery recovery))
                Draw(pawn, recovery.travel * scale, Mathf.Sqrt(scale));
        }

        private sealed class Quad
        {
            internal bool valid;
            internal Vector3 u, v;
            internal Vector2 min, max;

            internal Quad(Mesh mesh)
            {
                if (!mesh.isReadable || mesh.vertexCount != 4) return;
                Vector2[] uv = mesh.uv;
                Vector3[] vertices = mesh.vertices;
                if (uv.Length != 4) return;
                min = Vector2.one * float.MaxValue;
                max = Vector2.one * float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    min = Vector2.Min(min, uv[i]);
                    max = Vector2.Max(max, uv[i]);
                }
                int origin = -1, uCorner = -1, vCorner = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (uv[i] == min) origin = i;
                    else if (uv[i].x == max.x && uv[i].y == min.y) uCorner = i;
                    else if (uv[i].x == min.x && uv[i].y == max.y) vCorner = i;
                }
                if (origin < 0 || uCorner < 0 || vCorner < 0) return;
                u = vertices[uCorner] - vertices[origin];
                v = vertices[vCorner] - vertices[origin];
                u.y = v.y = 0f;
                valid = u.sqrMagnitude > 0.0001f && v.sqrMagnitude > 0.0001f;
            }
        }

        internal static int Draw(Pawn pawn, Vector3 travel, float opacity = 1f, Vector3? drawPosition = null)
        {
            if (pawn?.Drawer?.renderer?.renderTree == null || travel.sqrMagnitude < 0.0001f || !EnsureMaterial()) return 0;
            PawnRenderer renderer = pawn.Drawer.renderer;
            renderer.EnsureGraphicsInitialized();
            // 原版可能只画人物图集，renderTree 内仍是图集原点的矩阵。每帧只准备一次世界位置，
            // 随后每层一次 GPU 卷积；不调用 renderTree.Draw，也不生成额外人物副本。
            PawnDrawParms parms = PawnDrawParms.DefaultFor(pawn);
            parms.facing = pawn.Rotation;
            parms.posture = PawnPosture.Standing;
            parms.flags |= PawnRenderFlags.NeverAimWeapon;
            parms.rotDrawMode = renderer.CurRotDrawMode;
            parms.matrix = Matrix4x4.TRS((drawPosition ?? pawn.DrawPos) + pawn.ageTracker.CurLifeStage.bodyDrawOffset,
                Quaternion.identity, Vector3.one);
            renderer.renderTree.ParallelPreDraw(parms);
            List<PawnGraphicDrawRequest> requests = Requests(renderer.renderTree);
            int layers = 0;
            for (int i = 0; i < requests.Count; i++)
            {
                PawnGraphicDrawRequest request = requests[i];
                Material source = request.material;
                Mesh mesh = request.mesh;
                if (source == null || source.mainTexture == null || mesh == null) continue;
                Shader shader = source.shader;
                if (shader != ShaderDatabase.Cutout && shader != ShaderDatabase.CutoutComplex
                    && shader != ShaderDatabase.CutoutHair && shader != ShaderDatabase.CutoutSkin
                    && shader != ShaderDatabase.CutoutSkinColorOverride && shader != ShaderDatabase.CutoutWithOverlay) continue;
                if (!quads.TryGetValue(mesh, out Quad quad))
                {
                    quad = new Quad(mesh);
                    quads.Add(mesh, quad);
                }
                if (!quad.valid) continue;
                Matrix4x4 matrix = request.preDrawnComputedMatrix;
                Vector3 worldU = matrix.MultiplyVector(quad.u);
                Vector3 worldV = matrix.MultiplyVector(quad.v);
                worldU.y = worldV.y = 0f;
                float determinant = worldU.x * worldV.z - worldU.z * worldV.x;
                if (Mathf.Abs(determinant) < 0.0001f) continue;
                float alongU = (travel.x * worldV.z - travel.z * worldV.x) / determinant;
                float alongV = (worldU.x * travel.z - worldU.z * travel.x) / determinant;
                Vector2 tiling = source.mainTextureScale;
                Vector2 offset = source.mainTextureOffset;
                Vector2 uvSpan = Vector2.Scale(quad.max - quad.min, tiling);
                Vector2 a = Vector2.Scale(quad.min, tiling) + offset;
                Vector2 b = Vector2.Scale(quad.max, tiling) + offset;
                Vector2 uvMin = Vector2.Min(a, b), uvMax = Vector2.Max(a, b);
                Vector2 center = (quad.min + quad.max) * 0.5f;
                Texture mask = source.GetMaskTexture();
                properties.Clear();
                properties.SetTexture("_MainTex", source.mainTexture);
                properties.SetTexture("_MaskTex", mask ?? Texture2D.blackTexture);
                properties.SetFloat("_HasMask", mask == null ? 0f : 1f);
                properties.SetColor("_Color", source.color * pawn.Drawer.renderer.flasher.CurColor);
                properties.SetColor("_ColorTwo", source.HasProperty("_ColorTwo") ? source.GetColor("_ColorTwo") : Color.white);
                properties.SetVector("_MainTex_ST", new Vector4(tiling.x, tiling.y, offset.x, offset.y));
                properties.SetVector("_MeshUvCenter", new Vector4(center.x, center.y, 0f, 0f));
                properties.SetVector("_UvRect", new Vector4(uvMin.x, uvMin.y, uvMax.x, uvMax.y));
                properties.SetVector("_BlurUV", new Vector4(alongU * uvSpan.x, alongV * uvSpan.y, 0f, 0f));
                properties.SetVector("_Travel", travel);
                properties.SetVector("_PaddingU", worldU * Mathf.Abs(alongU) * 0.5f);
                properties.SetVector("_PaddingV", worldV * Mathf.Abs(alongV) * 0.5f);
                properties.SetFloat("_Opacity", 0.8f * opacity * InvisibilityUtility.GetAlpha(pawn));
                // 复用当前帧已计算的部件和矩阵，每层只提交一次方向卷积，不重绘多个角色或重算动画。
                GenDraw.DrawMeshNowOrLater(mesh, matrix, material, false, properties);
                layers++;
            }
            return layers;
        }

        private static bool EnsureMaterial()
        {
            if (loadAttempted) return material != null;
            loadAttempted = true;
            AssetBundle bundle = null;
            try
            {
                string platform = Application.platform == RuntimePlatform.OSXPlayer ? "MacOS"
                    : Application.platform == RuntimePlatform.LinuxPlayer ? "Linux" : "Windows";
                string path = Path.Combine(MugirlMod.ContentRoot, "1.6", "Resources", "LanceMotionBlur", platform, "mugirllanceblur");
                if (File.Exists(path)) bundle = AssetBundle.LoadFromFile(path);
                Shader shader = bundle?.LoadAsset<Shader>("Assets/MugirlLanceMotionBlur.shader");
                if (shader == null || !shader.isSupported) return false;
                material = new Material(shader) { name = "Mugirl lance motion blur", hideFlags = HideFlags.HideAndDontSave };
                properties = new MaterialPropertyBlock();
                return true;
            }
            catch (Exception exception)
            {
                MugirlLog.WarningOnce("Lance.MotionBlur", "Mugirl.Lance.MotionBlur.Unavailable".Translate(exception.Message).ToString());
                return false;
            }
            finally
            {
                if (bundle != null) bundle.Unload(false);
            }
        }
    }
}
