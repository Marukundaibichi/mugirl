// Runs the actual production renderer and bundled shader in Unity. Game stubs below
// isolate graphics from RimWorld startup; they do not replace the rendering algorithm.
using System;
using System.Collections.Generic;
using System.IO;
using Mugirl;
using Mugirl.Features.WeaponWheel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

public static class OutlineRenderValidation
{
    private static Mesh quad, flipped;
    private static Material body, clothes, weapon;
    private static Camera camera;
    private static readonly Pawn pawn = new Pawn();
    private static string output;

    public static void Run()
    {
        output = Path.Combine(MugirlMod.ContentRoot, "TMP/OutlineShaderBuild/QA");
        Directory.CreateDirectory(output);
        File.WriteAllText("Assets/QACutout.shader", @"
Shader ""MugirlQA/Cutout"" {
Properties { _MainTex (""Texture"", 2D) = ""white"" {} _Color (""Color"", Color) = (1,1,1,1) }
SubShader { Tags { ""Queue""=""AlphaTest"" } Cull Off ZWrite On ZTest LEqual
Pass { CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include ""UnityCG.cginc""
sampler2D _MainTex; float4 _Color;
struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord.xy; return o; }
float4 frag(v2f i):SV_Target { float4 c=tex2D(_MainTex,i.uv)*_Color; clip(c.a-.5); return c; }
ENDCG } } }");
        AssetDatabase.Refresh();
        ShaderDatabase.Cutout = Shader.Find("MugirlQA/Cutout");
        Check(ShaderDatabase.Cutout != null, "QA cutout loaded");
        quad = MakeQuad(false); flipped = MakeQuad(true);
        body = Mat((u,v) => (u-.5f)*(u-.5f)+(v-.5f)*(v-.5f)<.16f, new Color(.22f,.76f,.60f));
        clothes = Mat((u,v) => u>.25f && u<.90f && v>.25f && v<.75f, new Color(.4f,.6f,.95f));
        weapon = Mat((u,v) => u>.42f && u<.58f && v>.1f && v<.9f, new Color(.9f,.4f,.3f));
        camera = new GameObject("Outline QA camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0,10,0);
        camera.transform.rotation = Quaternion.Euler(90,0,0);
        camera.orthographic = true; camera.orthographicSize = 1.1f;
        camera.aspect = 1; camera.nearClipPlane = .1f; camera.farClipPlane = 20;
        camera.enabled = false;
        var baseline = Render("baseline", false, false, 1, false, false);
        var thin = Render("thin", true, false, 1, false, false);
        var thick = Render("thick", true, false, 3, false, false);
        Check(BlackCount(thin) > 300, "visible black outer outline");
        Check(BlackCount(thick) > BlackCount(thin)*2, "thickness increases outline area");
        for (int i=0;i<baseline.Length;i++)
            if (baseline[i].a>.99f) CheckNear(baseline[i], thick[i], "body/apparel overlap has no internal seam");
        var deferred = Render("deferred", true, true, 3, false, false);
        int differences = 0;
        for (int i=0;i<thick.Length;i++) if (Distance(thick[i],deferred[i])>.03f) differences++;
        Check(differences < 10, "deferred and atlas DrawNow paths match");
        var mirrored = Render("flipped", true, false, 3, true, false);
        Check(Math.Abs(BlackCount(mirrored)-BlackCount(thick))<30, "flipped UV thickness is preserved");
        var armed = Render("weapon", true, false, 3, false, true);
        Check(BlackCount(armed)>BlackCount(thick)+100, "front weapon has an independent outline over clothing");
        // RimWorld GraphicMeshSet uses MeshMakerPlanes.NewPlaneMesh(backLift:true),
        // NOT the completely flat quads used in the original smoke test.
        quad = MakeQuad(false, true); flipped = MakeQuad(true, true);
        var lifted = Render("rimworld-backlift", true, false, 3, false, false);
        Check(BlackCount(lifted)>BlackCount(thick)*.95f,
            "RimWorld's 0.0018292684 back-lift mesh must retain its body/apparel outlines");
        var invisible = Render("invisible", true, false, 3, false, false, PawnRenderFlags.Invisible);
        for (int i=0;i<baseline.Length;i++) CheckNear(baseline[i], invisible[i], "invisible flag suppresses outlines");
        // Opaque art touching every texture edge must still expand outside the quad.
        body.mainTexture = Texture2D.whiteTexture;
        var edge = Render("texture-edge", true, false, 3, false, false);
        Check(BlackCount(edge)>600, "edge-touching sprites have unclipped outlines");
        MugirlExtraOutline.SettingsChanged();
        Check(GlobalTextureAtlasManager.DirtyCount>0 && RimWorld.PortraitsCache.DirtyCount>0,
            "settings invalidate both atlas and portrait caches");
        Debug.Log("MUGIRL_OUTLINE_RENDER_OK: union, thickness, independent weapon, flipped UVs, texture edges, invisible flag, cache invalidation, DrawNow/deferred parity");
    }

    private static Color[] Render(string name, bool enabled, bool deferred, float width,
        bool flip, bool armed, PawnRenderFlags flags = PawnRenderFlags.None)
    {
        MugirlMod.Settings.enableExtraOutline = enabled;
        MugirlMod.Settings.extraOutlineWidth = width;
        var requests = new List<PawnGraphicDrawRequest>();
        requests.Add(Request(body, new Vector3(0,.03f,0), new Vector3(1.25f,1,1.65f), flip ? flipped : quad));
        requests.Add(Request(clothes, new Vector3(0,.05f,0), new Vector3(1.25f,1,1.65f), flip ? flipped : quad));
        if (armed)
        {
            var r = Request(weapon, new Vector3(.18f,.07f,-.1f), new Vector3(.8f,1,.8f), quad);
            r.node = new PawnRenderNode_BackWeapon();
            r.preDrawnComputedMatrix *= Matrix4x4.Rotate(Quaternion.Euler(0,45,0));
            requests.Add(r);
        }
        var target = new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);
        target.Create();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        GL.Clear(true,true,Color.clear);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(camera.projectionMatrix);
        GL.modelview = camera.worldToCameraMatrix;
        if (deferred)
        {
            GenDraw.Commands = new CommandBuffer();
            GenDraw.Commands.SetRenderTarget(target);
            GenDraw.Commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
        }
        MugirlExtraOutline.DrawBody(pawn,requests,new PawnDrawParms { DrawNow=!deferred, tint=Color.white, flags=flags });
        foreach (var r in requests)
            GenDraw.DrawMeshNowOrLater(r.mesh,r.preDrawnComputedMatrix,r.material,!deferred);
        if (deferred)
        {
            Graphics.ExecuteCommandBuffer(GenDraw.Commands);
            GenDraw.Commands.Dispose(); GenDraw.Commands=null;
        }
        GL.PopMatrix();
        var result = new Texture2D(512,512,TextureFormat.RGBA32,false);
        result.ReadPixels(new Rect(0,0,512,512),0,0); result.Apply();
        Color[] pixels = result.GetPixels();
        // White QA background makes black outlines visible in artifact previews.
        var preview = new Color[pixels.Length];
        for (int i=0;i<pixels.Length;i++) preview[i]=Color.Lerp(Color.white,pixels[i],pixels[i].a);
        result.SetPixels(preview); result.Apply();
        File.WriteAllBytes(Path.Combine(output,name+".png"),result.EncodeToPNG());
        RenderTexture.active=previous;
        UnityEngine.Object.DestroyImmediate(result); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        return pixels;
    }

    private static PawnGraphicDrawRequest Request(Material material, Vector3 pos, Vector3 scale, Mesh mesh)
    {
        return new PawnGraphicDrawRequest { material=material, mesh=mesh, node=new PawnRenderNode(),
            preDrawnComputedMatrix=Matrix4x4.TRS(pos,Quaternion.identity,scale) };
    }
    private static Mesh MakeQuad(bool flip, bool backLift = false)
    {
        var mesh=new Mesh();
        mesh.vertices=new[] { new Vector3(-.5f,0,-.5f), new Vector3(-.5f,backLift ? .0018292684f : 0f,.5f),
            new Vector3(.5f,backLift ? .0018292684f : 0f,.5f), new Vector3(.5f,backLift ? .0007317074f : 0f,-.5f) };
        mesh.uv=flip ? new[] {new Vector2(1,0),new Vector2(1,1),new Vector2(0,1),new Vector2(0,0)}
            : new[] {new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
        mesh.triangles=new[] {0,1,2,0,2,3}; mesh.RecalculateBounds(); return mesh;
    }
    private static Material Mat(Func<float,float,bool> shape, Color color)
    {
        var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);
        texture.filterMode=FilterMode.Bilinear; texture.wrapMode=TextureWrapMode.Clamp;
        var pixels=new Color[128*128];
        for(int y=0;y<128;y++) for(int x=0;x<128;x++) pixels[y*128+x]=shape((x+.5f)/128,(y+.5f)/128)?color:Color.clear;
        texture.SetPixels(pixels);texture.Apply();
        return new Material(ShaderDatabase.Cutout) { mainTexture=texture };
    }
    private static int BlackCount(Color[] pixels)
    { int count=0;foreach(var c in pixels) if(c.a>.5f && c.r<.05f && c.g<.05f && c.b<.05f)count++;return count; }
    private static float Distance(Color a,Color b) => Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.a-b.a);
    private static void CheckNear(Color a,Color b,string message) { if(Distance(a,b)>.04f)throw new Exception(message); }
    private static void Check(bool valid,string message) { if(!valid)throw new Exception(message); }
}

namespace Mugirl
{
    internal static class MugirlMod
    { internal static readonly SettingsStub Settings=new SettingsStub(); internal static string ContentRoot=>File.ReadAllText("mod-root.txt").Trim(); }
    internal class SettingsStub { internal bool enableExtraOutline; internal float extraOutlineWidth; }
    internal static class MugirlIdentity { internal static bool IsMugirlPawn(Pawn p)=>p!=null; }
    internal static class MugirlGameUtility { internal static bool IsPlaying()=>true; }
    internal static class MugirlLog { internal static void WarningOnce(string key,string message) { throw new Exception(message); } }
}
namespace Mugirl.Features.WeaponWheel { public class PawnRenderNode_BackWeapon:PawnRenderNode {} }
namespace RimWorld
{
    public static class PawnsFinder { public static List<Pawn> AllMapsWorldAndTemporary_AliveOrDead = new List<Pawn>(); }
    public static class PortraitsCache { public static int DirtyCount; public static void SetDirty(Pawn p) { DirtyCount++; } }
}
namespace Verse
{
    public class StaticConstructorOnStartup : Attribute {}
    public class Pawn { public bool Destroyed; public bool IsHiddenFromPlayer()=>false; public bool IsPsychologicallyInvisible()=>false; }
    public class PawnRenderNode {}
    public struct PawnGraphicDrawRequest { public PawnRenderNode node;public Mesh mesh;public Material material;public Matrix4x4 preDrawnComputedMatrix; }
    [Flags]public enum PawnRenderFlags { None=0,Invisible=1 }
    public struct PawnDrawParms { public PawnRenderFlags flags;public bool Statue,DrawNow;public Color tint; }
    public static class Extensions
    { public static bool FlagSet(this PawnRenderFlags a,PawnRenderFlags b)=>(a&b)!=0;public static string Translate(this string s,params object[] args)=>s; }
    public static class Current { public static object Game=new object(); }
    public static class GlobalTextureAtlasManager
    { public static int DirtyCount;public static void TryMarkPawnFrameSetDirty(Pawn p) { DirtyCount++; } }
    public static class ShaderDatabase
    { public static Shader Cutout,CutoutComplex,CutoutHair,CutoutSkin,CutoutSkinColorOverride,CutoutWithOverlay; }
    public static class GenDraw
    {
        public static CommandBuffer Commands;
        public static void DrawMeshNowOrLater(Mesh mesh,Matrix4x4 matrix,Material mat,bool drawNow,MaterialPropertyBlock props=null)
        {
            if(drawNow) { mat.SetPass(0);Graphics.DrawMeshNow(mesh,matrix); }
            else Commands.DrawMesh(mesh,matrix,mat,0,0,props);
        }
    }
}
