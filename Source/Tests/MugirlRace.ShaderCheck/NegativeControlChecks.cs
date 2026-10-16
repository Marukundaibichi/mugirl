using System.Text;
using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 编译检查的对照，否则“编译通过”不能证明任何事：
/// <list type="bullet">
/// <item>直接编译：最小的正确片元着色器必须通过，引用未定义标识符的版本必须失败。</item>
/// <item>预热路径：在临时目录注册一个片元阶段损坏的 ShaderDef，按游戏预热编译，引擎日志必须报告问题（游戏中 Slang 编译失败只进引擎日志，
/// 并以品红回退色绘制）。</item>
/// </list>
/// 另外把 Slang 对 HLSL 写法（frac、lerp、float2、saturate）的宽容程度作为信息输出：Slang 接受它们，所以“能编译”不代表移植干净。
/// </summary>
internal static class NegativeControlChecks
{
    private const string Header = "#version 450\nlayout(location = 0) in vec2 vUv;\nlayout(location = 0) out vec4 outColor;\nvoid main()\n{\n";

    private const string ControlVertex = "#version 450\n"
        + "layout(location = 0) in vec3 inPosition; // @vertex Position required\n"
        + "layout(location = 3) in vec4 inUv0;      // @vertex TexCoord0 required\n"
        + "layout(push_constant) uniform Push { mat4 modelMatrix; } push;\n"
        + "layout(set = 0, binding = 0) uniform VertexUniforms\n{\n    mat4 ViewProj;\n} uVertex;\n"
        + "layout(location = 0) out vec2 vUv;\n"
        + "void main()\n{\n    gl_Position = uVertex.ViewProj * (push.modelMatrix * vec4(inPosition, 1.0));\n    vUv = inUv0.xy;\n}\n";

    /// <summary>运行检查。</summary>
    public static void Run(ShaderProgramChecks programs, CheckReport report)
    {
        CheckReport.Section("Compile controls (Slang)");
        bool valid = Compiles("Mugirl_PositiveControl.frag", Header + "    outColor = vec4(vUv, 0.0, 1.0);\n}\n", out string? validProblem);
        if (!valid)
        {
            report.Fail($"positive control does not compile, so the compile checks are meaningless: {validProblem}");
        }

        bool broken = Compiles("Mugirl_NegativeControl.frag", Header + "    outColor = vec4(vUv, mugirlUndefinedIdentifier, 1.0);\n}\n", out _);
        if (broken)
        {
            report.Fail("negative control compiled: an undefined identifier was accepted, so a passing compile proves nothing.");
        }

        if (valid && !broken)
        {
            CheckReport.Pass("direct compile: a valid fragment compiles and an undefined identifier is rejected.");
        }

        RunPrewarmControl(programs, report);

        foreach ((string name, string body) in new[]
                 {
                     ("frac", "    outColor = vec4(frac(vUv), 0.0, 1.0);\n}\n"),
                     ("lerp", "    outColor = vec4(lerp(vUv, vUv.yx, 0.5), 0.0, 1.0);\n}\n"),
                     ("float2", "    float2 uv = vUv;\n    outColor = vec4(uv, 0.0, 1.0);\n}\n"),
                     ("saturate", "    outColor = vec4(saturate(vUv), 0.0, 1.0);\n}\n"),
                 })
        {
            bool accepted = Compiles("Mugirl_LeniencyProbe_" + name + ".frag", Header + body, out _);
            CheckReport.Info($"Slang leniency probe: HLSL '{name}' in GLSL is {(accepted ? "ACCEPTED (review ports by hand)" : "rejected")}.");
        }
    }

    private static void RunPrewarmControl(ShaderProgramChecks programs, CheckReport report)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Mugirl.ShaderCheck." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(Path.Combine(directory, "Mugirl_PrewarmControl.vert.glsl"), ControlVertex, utf8);
            File.WriteAllText(Path.Combine(directory, "Mugirl_PrewarmControl.frag.glsl"), Header + "    outColor = vec4(vUv, mugirlUndefinedIdentifier, 1.0);\n}\n", utf8);
            ShaderSourceLoader.SetOverrideShaderDirectories([directory, .. programs.SourceRoots]);
            var program = new ShaderProgramDefinition
            {
                vertexShaderFileName = "Mugirl_PrewarmControl.vert.glsl",
                fragmentShaderFileName = "Mugirl_PrewarmControl.frag.glsl",
                transparent = true,
            };
            var shader = new Shader("Mugirl_PrewarmControl", "Mugirl_PrewarmControl", program, null, null, program.ToPipelineState());
            IReadOnlyList<CapturedLogEntry> problems = programs.Warm(shader, out _);
            if (problems.Count == 0)
            {
                report.Fail("prewarm control: a fragment stage with an undefined identifier produced no engine problem, so the prewarm compile check proves nothing.");
            }
            else
            {
                CheckReport.Pass($"prewarm control: a broken fragment stage is reported by the engine ({problems.Count} problem(s), e.g. {Shorten(problems[0].ToString())}).");
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            report.Fail("prewarm control could not run: " + exception.Message);
        }
        finally
        {
            ShaderSourceLoader.SetOverrideShaderDirectories(programs.SourceRoots);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Shorten(string text) => text.Length <= 160 ? text : text[..160] + "...";

    private static bool Compiles(string sourceName, string source, out string? problem)
    {
        try
        {
            RuntimeShaderCompiler.Compile(source, sourceName, "main", RuntimeShaderLanguage.Glsl, RuntimeShaderStage.Fragment);
            problem = null;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            problem = exception.Message;
            return false;
        }
    }
}
