using System.Globalization;
using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 与 Verse.ShaderDefRegistry 完全相同地注册 ShaderDef（相同的源目录、相同的 <see cref="Shader"/> 构造，因此相同的布局解析、
/// 阶段接口校验与场景抓取登记），再按游戏着色器预热的方式编译本模组着色器的每个变体。
/// </summary>
internal sealed class ShaderProgramChecks(CheckPaths paths, EngineLogCapture log, CheckReport report)
{
    private readonly Dictionary<string, Shader> shaders = new(StringComparer.Ordinal);
    private readonly List<(ShaderDefEntry Entry, Shader Shader)> own = [];

    /// <summary>能构造出来的全部 ShaderDef（原版与本模组），按 defName。</summary>
    public IReadOnlyDictionary<string, Shader> Shaders => shaders;

    /// <summary>注册成功的本模组 ShaderDef。</summary>
    public IReadOnlyList<(ShaderDefEntry Entry, Shader Shader)> Own => own;

    /// <summary><see cref="ConfigureSourceRoots"/> 设置的覆盖目录（不含引擎的 Rendering/Shaders）。</summary>
    public IReadOnlyList<string> SourceRoots { get; private set; } = [];

    /// <summary>
    /// 按游戏预热的方式编译 <paramref name="shader"/> 的全部变体，返回引擎报告的问题（Slang 编译失败只进引擎日志）。
    /// </summary>
    public IReadOnlyList<CapturedLogEntry> Warm(Shader shader, out ShaderWarmupPlan plan)
    {
        plan = new ShaderWarmupPlan();
        plan.Graphics.AddRange(PrewarmJobs(shader));
        log.Drain();
        ShaderPrewarmer.Warm(plan);
        return [.. log.Drain().Where(entry => entry.IsProblem)];
    }

    /// <summary>
    /// 设置与游戏相同的源文件搜索顺序：各模组的 Shaders 目录，后加载的在前（本模组的加载目录倒序，再是倒序的 DLC，Core 最后），
    /// 然后是引擎的 Rendering/Shaders。
    /// </summary>
    public void ConfigureSourceRoots(IReadOnlyList<string> gamePackages)
    {
        List<string> roots = [.. paths.LoadFolders().Reverse().Select(folder => Path.Combine(folder.Path, "Shaders"))];
        roots.AddRange(gamePackages.Reverse().Select(package => Path.Combine(paths.GameDir, "Data", package, "Shaders")));
        roots = [.. roots.Where(Directory.Exists)];
        SourceRoots = roots;
        ShaderSourceLoader.SetOverrideShaderDirectories(roots);
        ShaderSourceLoader.RegisterShaderSourceDirectory(Path.Combine(paths.GameDir, "Rendering", "Shaders"));
        CheckReport.Section("Shader source roots (game order)");
        foreach (string root in roots.Append(Path.Combine(paths.GameDir, "Rendering", "Shaders")))
        {
            CheckReport.Info(root);
        }
    }

    /// <summary>像游戏一样先构造原版 ShaderDef，以便发现与原版的场景抓取冲突。</summary>
    public void RegisterVanilla(IEnumerable<ShaderDefEntry> vanilla)
    {
        int constructed = 0;
        List<string> problems = [];
        foreach (ShaderDefEntry entry in vanilla.Where(entry => entry.Program is not null))
        {
            try
            {
                shaders[entry.DefName] = new Shader(entry.DefName, entry.DefName, entry.Program!, entry.Keywords, null, entry.Program!.ToPipelineState());
                constructed++;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                problems.Add($"{entry.DefName}: {exception.Message}");
            }
        }

        problems.AddRange(log.Drain().Where(entry => entry.IsProblem).Select(entry => entry.ToString()));
        CheckReport.Section("Vanilla ShaderDefs");
        CheckReport.Info($"{constructed} vanilla ShaderDefs constructed (their own problems are listed, not counted).");
        foreach (string problem in problems)
        {
            CheckReport.Info("vanilla: " + problem);
        }
    }

    /// <summary>检查、注册并编译本模组的 ShaderDef。</summary>
    public void CheckOwn(IReadOnlyList<ShaderDefEntry> ownDefs)
    {
        foreach (ShaderDefEntry entry in ownDefs)
        {
            CheckReport.Section($"ShaderDef {entry.DefName} ({entry.Source.Location})");
            if (TryConstruct(entry) is { } shader)
            {
                own.Add((entry, shader));
                CheckFiles(entry, shader);
                CheckSceneGrab(entry, shader);
                PrintLayout(shader);
            }
        }

        // 经 ShaderPrewarmer.Warm 编译：它用引擎自己的 ShaderModuleCache.BuildVariantSource 生成每个变体的源码（展开 include、注入宏），
        // 再交给 Slang 编译。
        CheckReport.Section("Compile (Slang to SPIR-V through the game's shader prewarm)");
        foreach ((_, Shader shader) in own)
        {
            IReadOnlyList<CapturedLogEntry> problems = Warm(shader, out ShaderWarmupPlan plan);
            if (problems.Count > 0)
            {
                report.Fail($"{shader.Key}: compile failed: {string.Join(" | ", problems)}");
            }
            else
            {
                string variants = string.Join(", ", plan.Graphics.Select(job =>
                    $"{Path.GetFileName(job.SourceFile)}{(job.Variant.OutlineMrt ? " +OUTLINE_MRT" : string.Empty)}{(job.Variant.Instanced ? " +INSTANCED" : string.Empty)}"));
                CheckReport.Pass($"{shader.Key}: {plan.Graphics.Count} variants compiled ({variants}).");
            }
        }
    }

    // 游戏为无关键字着色器预热的变体：Waylith 1.7 ShaderVariantEnumerator.EmitShaderVariants（顶点、片元、OUTLINE_MRT 片元；顶点阶段
    // 支持实例化时再各一份实例化变体），不做跨着色器去重，以便把每个变体归到具体 ShaderDef。
    private static IEnumerable<GraphicsShaderJob> PrewarmJobs(Shader shader)
    {
        string vertexSource = ShaderSourceLoader.ReadShaderSource(shader.VertexShaderFileName);
        bool instancing = vertexSource.Contains("INSTANCED", StringComparison.Ordinal)
                          || vertexSource.Contains("gl_InstanceIndex", StringComparison.Ordinal);
        foreach (bool instanced in instancing ? new[] { false, true } : [false])
        {
            yield return Job(shader, shader.VertexShaderFileName, RuntimeShaderStage.Vertex, outlineMrt: false, instanced);
            yield return Job(shader, shader.FragmentShaderFileName, RuntimeShaderStage.Fragment, outlineMrt: false, instanced);
            yield return Job(shader, shader.FragmentShaderFileName, RuntimeShaderStage.Fragment, outlineMrt: true, instanced);
        }
    }

    private static GraphicsShaderJob Job(Shader shader, string file, RuntimeShaderStage stage, bool outlineMrt, bool instanced)
        => new(file, stage, new ShaderVariantKey(shader, 0L, string.Empty, shader.UseTexture, outlineMrt, instanced));

    private Shader? TryConstruct(ShaderDefEntry entry)
    {
        foreach (string error in entry.ReadErrors)
        {
            report.Fail(error);
        }

        if (!entry.DefName.StartsWith(GlslSourceChecks.Prefix, StringComparison.Ordinal))
        {
            report.Fail($"{entry.DefName}: the mod's ShaderDefs must be named {GlslSourceChecks.Prefix}*.");
        }

        if (entry.Program is null)
        {
            report.Fail($"{entry.DefName}: no <shaderProgram>; the ShaderDef would not be registered with the engine.");
            return null;
        }

        if (shaders.ContainsKey(entry.DefName))
        {
            report.Fail($"{entry.DefName}: defName already used by another ShaderDef.");
            return null;
        }

        log.Drain();
        Shader shader;
        try
        {
            shader = new Shader(entry.DefName, entry.DefName, entry.Program, entry.Keywords, null, entry.Program.ToPipelineState());
            ShaderRegistry.Register(shader);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            report.Fail($"{entry.DefName}: the engine cannot build the shader: {exception.Message}");
            return null;
        }

        shaders[entry.DefName] = shader;
        foreach (CapturedLogEntry problem in log.Drain().Where(item => item.IsProblem))
        {
            report.Fail($"{entry.DefName}: engine reported {problem}");
        }

        if (shader.MaterialLayout.HasErrors)
        {
            report.Fail($"{entry.DefName}: material layout of {shader.FragmentShaderFileName} has errors.");
        }

        if (shader.VertexInputLayout.HasErrors)
        {
            report.Fail($"{entry.DefName}: vertex input layout of {shader.VertexShaderFileName} has errors.");
        }

        if (ShaderSourceLoader.HasStageInterfaceErrors(shader.VertexShaderFileName, shader.FragmentShaderFileName))
        {
            report.Fail($"{entry.DefName}: fragment inputs do not match the vertex outputs.");
        }

        if (shader.HasErrors)
        {
            report.Fail($"{entry.DefName}: Shader.HasErrors is set; the game would draw it with the fallback shader.");
        }
        else
        {
            CheckReport.Pass($"registered: queue {shader.DefaultRenderQueue}, transparent {shader.Transparent}, depth test/write {shader.DepthTest}/{shader.DepthWrite}, cull {entry.Program.cullMode}, blend {entry.Program.blendMode} ({entry.Program.blendSrcFactor}/{entry.Program.blendDstFactor}), flipTextureST {shader.FlipTextureST}, useTexture {shader.UseTexture}.");
        }

        return shader;
    }

    // 本模组命名的文件必须解析到本模组；其他名字是有意复用的原版源文件。
    private void CheckFiles(ShaderDefEntry entry, Shader shader)
    {
        IReadOnlyList<string> ownRoots = GlslSourceChecks.OwnShaderRoots(paths);
        foreach (string fileName in new[] { shader.VertexShaderFileName, shader.FragmentShaderFileName })
        {
            string resolved = ShaderSourceLoader.ResolveShaderPath(fileName);
            bool inMod = ownRoots.Any(root => resolved.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            bool ownName = Path.GetFileName(fileName).StartsWith(GlslSourceChecks.Prefix, StringComparison.Ordinal);
            if (ownName && !inMod)
            {
                report.Fail($"{entry.DefName}: {fileName} resolves outside the mod: {resolved}.");
            }
            else if (!ownName && inMod)
            {
                report.Fail($"{entry.DefName}: {fileName} resolves to a mod file without the {GlslSourceChecks.Prefix} prefix: {resolved}.");
            }
            else
            {
                CheckReport.Info($"{fileName} -> {resolved}{(ownName ? string.Empty : "  (vanilla source, reused deliberately)")}");
            }
        }
    }

    private void CheckSceneGrab(ShaderDefEntry entry, Shader shader)
    {
        string? grabName = entry.Program?.sceneGrab?.name;
        string? expectedSampler = string.IsNullOrEmpty(grabName) ? null : "_Grab" + grabName;
        foreach (MaterialTextureSlot slot in shader.MaterialLayout.TextureSlots.Where(slot => slot.PropertyName.StartsWith("_Grab", StringComparison.Ordinal)))
        {
            if (slot.PropertyName != expectedSampler)
            {
                report.Fail($"{entry.DefName}: samples {slot.PropertyName}, but the ShaderDef declares {(expectedSampler is null ? "no scene grab" : "scene grab " + grabName)}.");
            }
        }

        if (grabName is null)
        {
            return;
        }

        int declaredQueue = entry.Program!.sceneGrab!.queue;
        if (!shader.MaterialLayout.TextureSlots.Any(slot => slot.PropertyName == expectedSampler))
        {
            report.Fail($"{entry.DefName}: declares scene grab {grabName} but never samples {expectedSampler}.");
        }

        if (!SceneGrabRegistry.TryResolve(grabName, out SceneGrabPoint? point) || point.Queue != declaredQueue)
        {
            report.Fail($"{entry.DefName}: scene grab {grabName} is registered at queue {point?.Queue.ToString(CultureInfo.InvariantCulture) ?? "none"}, not {declaredQueue} (a grab point keeps the queue of its first definition).");
        }
        else if (shader.DefaultRenderQueue < declaredQueue)
        {
            report.Fail($"{entry.DefName}: queue {shader.DefaultRenderQueue} samples scene grab {grabName}, which is taken later (queue {declaredQueue}).");
        }
        else
        {
            CheckReport.Pass($"scene grab {grabName} at queue {declaredQueue}, sampled as {expectedSampler}.");
        }
    }

    private static void PrintLayout(Shader shader)
    {
        CheckReport.Info($"material uniforms ({shader.MaterialLayout.UniformSize} bytes):");
        foreach (MaterialUniformSlot slot in shader.MaterialLayout.UniformSlots)
        {
            string value = slot.HasAuthoredDefault ? "default " + FormatValue(slot.DefaultValue) : "no default (global value)";
            CheckReport.Info($"  {slot.Offset,4} {slot.Type,-9} {slot.PropertyName,-22} {value}");
        }

        foreach (MaterialTextureSlot slot in shader.MaterialLayout.TextureSlots)
        {
            CheckReport.Info($"  texture binding {slot.Binding} {slot.PropertyName} (default {slot.Default})");
        }

        CheckReport.Info("vertex inputs: " + string.Join(", ", shader.VertexInputLayout.Slots.Select(slot => $"{slot.semantic}@{slot.location}{(slot.required ? " (required)" : string.Empty)}")));
    }

    private static string FormatValue(MaterialUniformValue value) => value.Type switch
    {
        MaterialUniformType.Float => value.Float.ToString("R", CultureInfo.InvariantCulture),
        MaterialUniformType.Int => value.Int.ToString(CultureInfo.InvariantCulture),
        MaterialUniformType.Vector2 or MaterialUniformType.Vector3 or MaterialUniformType.Vector4 or MaterialUniformType.TextureST
            => string.Create(CultureInfo.InvariantCulture, $"({value.Vector4.X}, {value.Vector4.Y}, {value.Vector4.Z}, {value.Vector4.W})"),
        _ => value.Type.ToString(),
    };
}
