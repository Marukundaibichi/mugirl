using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Mugirl.Features.WeaponWheel;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    [StaticConstructorOnStartup]
    internal static class MugirlExtraOutline
    {
        internal const float DefaultWidth = 1f;
        internal const float MinWidth = 0.25f;
        internal const float MaxWidth = 3f;
        private const float WidthInCells = 0.012f;
        private const float DepthOffset = 0.0005f;

        // StaticCacheLifecycle: one shader/material/block per process, created lazily on
        // the render thread. No pawn/texture/material copies or render targets retained.
        private static Material outlineMaterial;
        private static MaterialPropertyBlock properties;
        private static bool loadAttempted;
        // StaticCacheLifecycle: weak mesh metadata; releases entries with game meshes.
        private static readonly ConditionalWeakTable<Mesh, QuadInfo> quads = new ConditionalWeakTable<Mesh, QuadInfo>();
        // StaticCacheLifecycle: weak references only; used to invalidate existing atlas
        // and portrait entries when settings change, including dead/off-map pawns.
        private static readonly List<WeakReference> seenPawns = new List<WeakReference>();
        private static readonly ConditionalWeakTable<Pawn, WeakReference> pawnSeen = new ConditionalWeakTable<Pawn, WeakReference>();
        // StaticCacheLifecycle: immutable shader property IDs, valid for this process.
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");
        private static readonly int WorldPaddingU = Shader.PropertyToID("_WorldPaddingU");
        private static readonly int WorldPaddingV = Shader.PropertyToID("_WorldPaddingV");
        private static readonly int MeshUvCenter = Shader.PropertyToID("_MeshUvCenter");
        private static readonly int UvRect = Shader.PropertyToID("_UvRect");
        private static readonly int UvRadius = Shader.PropertyToID("_UvRadius");
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");

        private sealed class QuadInfo
        {
            internal bool valid;
            internal Bounds bounds;
            internal Vector2 uvMin, uvMax;
            internal Vector3 uSpan, vSpan;

            public QuadInfo(Mesh mesh)
            {
                if (mesh.vertexCount != 4 || !mesh.isReadable) return;
                bounds = mesh.bounds;
                // GraphicMeshSet uses backLift:true: its rear vertices are raised by
                // 0.0018292684, so a 0.001 flatness cutoff rejects normal pawn meshes.
                // Keep small vanilla depth offsets; only reject actual 3D geometry.
                if (bounds.size.x <= 0f || bounds.size.z <= 0f || bounds.size.y > 0.01f) return;
                Vector2[] uv = mesh.uv;
                Vector3[] vertices = mesh.vertices;
                if (uv.Length != 4) return;
                uvMin = Vector2.one * float.MaxValue;
                uvMax = Vector2.one * float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    uvMin = Vector2.Min(uvMin, uv[i]);
                    uvMax = Vector2.Max(uvMax, uv[i]);
                }
                if (uvMax.x <= uvMin.x || uvMax.y <= uvMin.y) return;
                for (int i = 0; i < 4; i++)
                {
                    // Quad padding assumes axis-aligned XZ vertices and rectangular UVs.
                    if (!AtEdge(vertices[i].x, bounds.min.x, bounds.max.x)
                        || !AtEdge(vertices[i].z, bounds.min.z, bounds.max.z)
                        || !AtEdge(uv[i].x, uvMin.x, uvMax.x)
                        || !AtEdge(uv[i].y, uvMin.y, uvMax.y)) return;
                }
                // Record actual UV-to-geometry axes, including mirrored/rotated UVs.
                // The vertex shader uses these world-space directions rather than
                // assuming vertices are still in object space after Unity batches them.
                int origin = -1, uCorner = -1, vCorner = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (uv[i].y == uvMin.y)
                    {
                        if (uv[i].x == uvMin.x) origin = i;
                        else uCorner = i;
                    }
                    else if (uv[i].x == uvMin.x) vCorner = i;
                }
                if (origin < 0 || uCorner < 0 || vCorner < 0) return;
                uSpan = vertices[uCorner] - vertices[origin];
                vSpan = vertices[vCorner] - vertices[origin];
                // Back-lift is a depth-ordering aid, not part of silhouette size.
                uSpan.y = vSpan.y = 0f;
                valid = true;
            }

            private static bool AtEdge(float value, float min, float max)
            {
                return Mathf.Abs(value - min) < 0.0001f || Mathf.Abs(value - max) < 0.0001f;
            }
        }

        internal static bool Enabled => MugirlMod.Settings?.enableExtraOutline == true;

        internal static void DrawBody(Pawn pawn, List<PawnGraphicDrawRequest> requests, PawnDrawParms parms)
        {
            if (!Enabled || !MugirlIdentity.IsMugirlPawn(pawn) || requests == null
                || parms.flags.FlagSet(PawnRenderFlags.Invisible) || parms.Statue) return;
            if (!EnsureMaterial()) return;
            Track(pawn);
            float lowest = float.MaxValue;
            for (int i = 0; i < requests.Count; i++)
            {
                PawnGraphicDrawRequest request = requests[i];
                if (!(request.node is PawnRenderNode_BackWeapon) && IsSilhouette(request.material))
                    lowest = Mathf.Min(lowest, request.preDrawnComputedMatrix.m13);
            }
            for (int i = 0; i < requests.Count; i++)
            {
                PawnGraphicDrawRequest request = requests[i];
                if (!IsSilhouette(request.material)) continue;
                Matrix4x4 matrix = request.preDrawnComputedMatrix;
                // Dilation distributes over a union. Put ALL body/apparel silhouettes
                // behind ALL normal body layers, so overlapping clothes get no seams.
                // Back weapons keep their own depth, independent of that union.
                matrix.m13 = request.node is PawnRenderNode_BackWeapon
                    ? matrix.m13 - DepthOffset : lowest - DepthOffset;
                Draw(request.mesh, matrix, request.material, parms.DrawNow, parms.tint.a);
            }
        }

        private static bool IsSilhouette(Material material)
        {
            if (material == null) return false;
            Shader shader = material.shader;
            // Wounds, foam, shadows, tattoos and additive effects are not silhouettes.
            return shader == ShaderDatabase.Cutout || shader == ShaderDatabase.CutoutComplex
                || shader == ShaderDatabase.CutoutHair || shader == ShaderDatabase.CutoutSkin
                || shader == ShaderDatabase.CutoutSkinColorOverride || shader == ShaderDatabase.CutoutWithOverlay;
        }

        internal static void DrawWeapon(Pawn pawn, Mesh mesh, Matrix4x4 matrix, Material material, float opacity = 1f)
        {
            if (!Enabled || !MugirlIdentity.IsMugirlPawn(pawn) || pawn.IsHiddenFromPlayer()
                || pawn.IsPsychologicallyInvisible() || !EnsureMaterial()) return;
            matrix.m13 -= DepthOffset;
            Draw(mesh, matrix, material, false, opacity);
        }

        private static void Draw(Mesh mesh, Matrix4x4 matrix, Material source, bool drawNow, float opacity)
        {
            if (mesh == null || source == null || source.mainTexture == null || opacity <= 0f) return;
            if (!quads.TryGetValue(mesh, out QuadInfo quad))
            {
                quad = new QuadInfo(mesh);
                quads.Add(mesh, quad);
            }
            if (!quad.valid) return;
            Vector3 worldU = matrix.MultiplyVector(quad.uSpan);
            Vector3 worldV = matrix.MultiplyVector(quad.vSpan);
            float sizeU = worldU.magnitude, sizeV = worldV.magnitude;
            if (sizeU < 0.0001f || sizeV < 0.0001f) return;
            float width = Mathf.Clamp(MugirlMod.Settings.extraOutlineWidth, MinWidth, MaxWidth) * WidthInCells;
            Vector4 paddingU = worldU * (width / sizeU);
            Vector4 paddingV = worldV * (width / sizeV);
            Vector2 center = (quad.uvMin + quad.uvMax) * 0.5f;
            Vector4 meshUvCenter = new Vector4(center.x, center.y, 0f, 0f);
            Vector2 tiling = source.mainTextureScale;
            Vector2 offset = source.mainTextureOffset;
            Vector2 a = Vector2.Scale(quad.uvMin, tiling) + offset;
            Vector2 b = Vector2.Scale(quad.uvMax, tiling) + offset;
            Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
            Vector4 rect = new Vector4(min.x, min.y, max.x, max.y);
            Vector4 radius = new Vector4(width / sizeU * (max.x - min.x),
                width / sizeV * (max.y - min.y), 0f, 0f);
            Vector4 st = new Vector4(tiling.x, tiling.y, offset.x, offset.y);
            float alpha = Mathf.Clamp01(opacity * source.color.a);
            if (drawNow)
            {
                // GenDraw's DrawNow branch ignores MaterialPropertyBlock. Set every
                // value directly for atlas/portrait baking; deferred draws override all
                // these values via their own copied property block.
                outlineMaterial.SetTexture(MainTex, source.mainTexture);
                outlineMaterial.SetVector(MainTexST, st);
                outlineMaterial.SetVector(WorldPaddingU, paddingU);
                outlineMaterial.SetVector(WorldPaddingV, paddingV);
                outlineMaterial.SetVector(MeshUvCenter, meshUvCenter);
                outlineMaterial.SetVector(UvRect, rect);
                outlineMaterial.SetVector(UvRadius, radius);
                outlineMaterial.SetFloat(Opacity, alpha);
                GenDraw.DrawMeshNowOrLater(mesh, matrix, outlineMaterial, true);
            }
            else
            {
                properties.SetTexture(MainTex, source.mainTexture);
                properties.SetVector(MainTexST, st);
                properties.SetVector(WorldPaddingU, paddingU);
                properties.SetVector(WorldPaddingV, paddingV);
                properties.SetVector(MeshUvCenter, meshUvCenter);
                properties.SetVector(UvRect, rect);
                properties.SetVector(UvRadius, radius);
                properties.SetFloat(Opacity, alpha);
                GenDraw.DrawMeshNowOrLater(mesh, matrix, outlineMaterial, false, properties);
            }
        }

        private static bool EnsureMaterial()
        {
            if (loadAttempted) return outlineMaterial != null;
            loadAttempted = true;
            AssetBundle bundle = null;
            try
            {
                string platform = Application.platform == RuntimePlatform.OSXPlayer ? "MacOS"
                    : Application.platform == RuntimePlatform.LinuxPlayer ? "Linux" : "Windows";
                string path = Path.Combine(MugirlMod.ContentRoot, "1.6", "Resources", "Outline", platform, "mugirloutline");
                if (File.Exists(path)) bundle = AssetBundle.LoadFromFile(path);
                Shader shader = bundle?.LoadAsset<Shader>("Assets/MugirlExtraOutline.shader");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("Outline shader is missing or unsupported: " + platform);
                outlineMaterial = new Material(shader) { name = "Mugirl extra outline", hideFlags = HideFlags.HideAndDontSave };
                properties = new MaterialPropertyBlock();
                return true;
            }
            catch (Exception ex)
            {
                MugirlLog.WarningOnce("ExtraOutline.Shader", "Mugirl.Outline.Unavailable".Translate(ex.Message).ToString());
                return false;
            }
            finally
            {
                if (bundle != null) bundle.Unload(false);
            }
        }

        private static void Track(Pawn pawn)
        {
            if (pawnSeen.TryGetValue(pawn, out _)) return;
            var reference = new WeakReference(pawn);
            pawnSeen.Add(pawn, reference);
            if (seenPawns.Count % 64 == 0)
                for (int i = seenPawns.Count - 1; i >= 0; i--)
                    if (!seenPawns[i].IsAlive) seenPawns.RemoveAt(i);
            seenPawns.Add(reference);
        }

        internal static void SettingsChanged()
        {
            // Called only after an actual UI change, never during normal ticking/drawing.
            for (int i = seenPawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = seenPawns[i].Target as Pawn;
                if (pawn == null || pawn.Destroyed) { seenPawns.RemoveAt(i); continue; }
                GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
                PortraitsCache.SetDirty(pawn);
            }
            // Pawns rendered while the feature was disabled have not been tracked yet.
            if (!MugirlGameUtility.IsPlaying()) return;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
            {
                if (!MugirlIdentity.IsMugirlPawn(pawn)) continue;
                GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
                PortraitsCache.SetDirty(pawn);
            }
        }
    }
}
