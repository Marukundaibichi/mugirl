using System.Globalization;
using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 按着色器的材质布局检查本模组的 MaterialDef。引擎静默忽略着色器没有声明的属性（Verse 的 MaterialDef.ConfigErrors 只检查名字非空），
/// 拼错或类型不对的属性只会表现为画面不对。
/// </summary>
internal static class MaterialDefChecks
{
    private static readonly string[] TextureExtensions = [".png", ".jpg", ".jpeg", ".psd", ".dds"];

    /// <summary>运行检查。</summary>
    public static void Run(CheckPaths paths, IReadOnlyList<MaterialDefEntry> own, IReadOnlyDictionary<string, Shader> shaders, CheckReport report)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (MaterialDefEntry material in own)
        {
            CheckReport.Section($"MaterialDef {material.DefName} ({material.Source.Location})");
            foreach (string error in material.ReadErrors)
            {
                report.Fail(error);
            }

            if (!seen.Add(material.DefName))
            {
                report.Fail($"{material.DefName}: defName used by another MaterialDef.");
            }

            if (!material.DefName.StartsWith(GlslSourceChecks.Prefix, StringComparison.Ordinal))
            {
                report.Fail($"{material.DefName}: the mod's MaterialDefs must be named {GlslSourceChecks.Prefix}*.");
            }

            if (material.RenderQueue < -1)
            {
                report.Fail($"{material.DefName}: renderQueue {material.RenderQueue} is invalid.");
            }

            if (material.ShaderDefName is null || !shaders.TryGetValue(material.ShaderDefName, out Shader? shader))
            {
                report.Fail($"{material.DefName}: shader '{material.ShaderDefName}' is not a registered ShaderDef.");
                continue;
            }

            int before = report.FailureCount;
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (MaterialProperty property in material.Properties)
            {
                if (!names.Add(property.Name))
                {
                    report.Fail($"{property.Location}: {property.Name} is set twice.");
                }

                CheckProperty(paths, property, shader, report);
            }

            if (report.FailureCount == before)
            {
                string queue = material.RenderQueue >= 0 ? material.RenderQueue.ToString(CultureInfo.InvariantCulture) : $"shader's ({shader.DefaultRenderQueue})";
                CheckReport.Pass($"shader {shader.Key}, queue {queue}, {material.Properties.Count} properties match the material layout.");
            }
        }
    }

    private static void CheckProperty(CheckPaths paths, MaterialProperty property, Shader shader, CheckReport report)
    {
        MaterialLayout layout = shader.MaterialLayout;
        string where = $"{property.Location}: {property.Name}";
        if (property.Kind == MaterialPropertyKind.Texture)
        {
            if (!layout.TextureSlots.Any(slot => slot.PropertyName == property.Name))
            {
                report.Fail($"{where}: {shader.Key} has no texture {property.Name}.");
            }

            if (!TextureExists(paths, property.Value))
            {
                report.Fail($"{where}: texture '{property.Value}' not found in the mod's 1.7 load folders.");
            }

            return;
        }

        int slot = Array.FindIndex(layout.UniformSlots, candidate => candidate.PropertyName == property.Name);
        if (slot < 0)
        {
            report.Fail($"{where}: {shader.Key} declares no uniform {property.Name}; the value would be ignored.");
            return;
        }

        MaterialUniformType type = layout.UniformSlots[slot].Type;
        switch (property.Kind)
        {
            case MaterialPropertyKind.Float:
                if (type is not (MaterialUniformType.Float or MaterialUniformType.Int))
                {
                    report.Fail($"{where}: set as a float, but the shader declares {type}.");
                }
                else if (!float.TryParse(property.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    report.Fail($"{where}: '{property.Value}' is not a number.");
                }

                break;
            case MaterialPropertyKind.Color:
            case MaterialPropertyKind.Vector:
                if (type is not MaterialUniformType.Vector4)
                {
                    report.Fail($"{where}: set as a {property.Kind}, but the shader declares {type}.");
                }
                else if (!TryParseVector(property.Value, out float[] components))
                {
                    report.Fail($"{where}: '{property.Value}' is not a vector of 3 or 4 numbers.");
                }
                else if (property.Kind == MaterialPropertyKind.Color && components.Take(3).Any(component => component > 1f))
                {
                    // Verse.ParseHelper.ParseColor 在 r、g、b 任一分量大于 1 时把整个颜色按 0-255 字节解析。
                    report.Fail($"{where}: HDR colour {property.Value} under <colors> would be parsed as 0-255 bytes; use <vectors>.");
                }

                break;
        }
    }

    private static bool TryParseVector(string text, out float[] components)
    {
        string[] parts = text.Trim().TrimStart('(', 'R', 'G', 'B', 'A').TrimEnd(')').Split(',');
        components = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]))
            {
                return false;
            }
        }

        return parts.Length is 3 or 4;
    }

    private static bool TextureExists(CheckPaths paths, string texturePath)
    {
        string relative = texturePath.Replace('/', Path.DirectorySeparatorChar);
        return paths.LoadFolders()
            .SelectMany(folder => TextureExtensions.Select(extension => Path.Combine(folder.Path, "Textures", relative + extension)))
            .Any(File.Exists);
    }
}
