using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 用游戏的 Slang 编译每个阶段（include 已由引擎展开），读回 SPIR-V 接口，与引擎从源码解析出的布局核对：
/// 材质集（set 2）只有布局里的绑定，顶点输入 location 与顶点输入布局一致，片元输入都由顶点阶段写出。
/// 引擎只用正则解析源码；这里确认编译器看到的是同一个接口。
/// </summary>
internal static class SpirvInterfaceChecks
{
    private const int MaterialSet = 2;

    /// <summary>运行检查。</summary>
    public static void Run(IReadOnlyList<(ShaderDefEntry Entry, Shader Shader)> own, CheckReport report)
    {
        CheckReport.Section("SPIR-V interface (direct Slang compile of each stage)");
        foreach ((_, Shader shader) in own)
        {
            if (!TryCompile(shader, shader.VertexShaderFileName, RuntimeShaderStage.Vertex, report, out SpirvModule? vertex)
                || !TryCompile(shader, shader.FragmentShaderFileName, RuntimeShaderStage.Fragment, report, out SpirvModule? fragment))
            {
                continue;
            }

            int before = report.FailureCount;
            HashSet<int> layoutBindings = [0, .. shader.MaterialLayout.TextureSlots.Select(slot => slot.Binding)];
            foreach ((string stage, SpirvModule module) in new[] { ("vertex", vertex), ("fragment", fragment) })
            {
                foreach (SpirvVariable variable in module.Variables.Where(variable => variable.Set == MaterialSet && variable.Binding is { } binding && !layoutBindings.Contains(binding)))
                {
                    report.Fail($"{shader.Key}: {stage} stage binds {variable.Name} at set 2 binding {variable.Binding}, which the material layout does not declare.");
                }
            }

            HashSet<int> fragmentBindings = [.. fragment.Variables.Where(variable => variable.Set == MaterialSet && variable.Binding is not null).Select(variable => variable.Binding!.Value)];
            foreach (int binding in layoutBindings.Where(binding => !fragmentBindings.Contains(binding)))
            {
                CheckReport.Info($"{shader.Key}: layout binding {binding} is not used by the compiled fragment stage (optimised away).");
            }

            HashSet<int> declaredInputs = [.. shader.VertexInputLayout.Slots.Select(slot => slot.location)];
            HashSet<int> vertexInputs = [.. Locations(vertex, SpirvStorageClass.Input)];
            foreach (int location in vertexInputs.Where(location => !declaredInputs.Contains(location)))
            {
                report.Fail($"{shader.Key}: vertex stage reads location {location}, which the vertex input layout does not declare.");
            }

            HashSet<int> vertexOutputs = [.. Locations(vertex, SpirvStorageClass.Output)];
            foreach (int location in Locations(fragment, SpirvStorageClass.Input).Where(location => !vertexOutputs.Contains(location)))
            {
                report.Fail($"{shader.Key}: fragment stage reads location {location}, which the vertex stage does not write.");
            }

            if (!vertex.Variables.Any(variable => variable.StorageClass == SpirvStorageClass.PushConstant))
            {
                report.Fail($"{shader.Key}: the vertex stage does not read the model matrix push constant.");
            }

            if (report.FailureCount == before)
            {
                CheckReport.Pass($"{shader.Key}: {Describe("vertex", vertex)}; {Describe("fragment", fragment)}.");
            }
        }
    }

    private static bool TryCompile(Shader shader, string fileName, RuntimeShaderStage stage, CheckReport report, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SpirvModule? module)
    {
        module = null;
        try
        {
            string source = ShaderSourceLoader.ReadShaderSource(fileName);
            module = SpirvModule.Parse(RuntimeShaderCompiler.Compile(source, Path.GetFileName(fileName), "main", RuntimeShaderLanguage.Glsl, stage));
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or IOException)
        {
            report.Fail($"{shader.Key}: {fileName} does not compile on its own: {exception.Message}");
            return false;
        }
    }

    private static IEnumerable<int> Locations(SpirvModule module, SpirvStorageClass storageClass)
        => module.Variables.Where(variable => variable.StorageClass == storageClass && !variable.IsBuiltIn && variable.Location is not null)
            .Select(variable => variable.Location!.Value);

    private static string Describe(string stage, SpirvModule module)
    {
        IEnumerable<string> resources = module.Variables
            .Where(variable => variable.Set is not null)
            .OrderBy(variable => variable.Set).ThenBy(variable => variable.Binding)
            .Select(variable => $"{variable.Name}@{variable.Set}.{variable.Binding}");
        string inputs = string.Join(",", Locations(module, SpirvStorageClass.Input).Order());
        string outputs = string.Join(",", Locations(module, SpirvStorageClass.Output).Order());
        return $"{stage} [{string.Join(" ", resources)}] in({inputs}) out({outputs})";
    }
}
