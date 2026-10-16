using MugirlRace.Validation.Infrastructure;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 把本机路径写成与机器无关的来源描述（<c>repo:</c>、<c>game:</c>、<c>workshop:</c>、<c>~/</c>），使 builds.tsv 不含用户名与安装位置。
/// </summary>
internal sealed class PortablePaths
{
    private readonly (string Root, string Prefix)[] roots;

    public PortablePaths(ValidationOptions options)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        roots =
        [
            (options.ModRoot, "repo:"),
            (options.WorkshopDir, "workshop:"),
            (options.GameDir, "game:"),
            (Path.Combine(profile, ".nuget", "packages"), "nuget:"),
            (profile, "~/"),
        ];
    }

    public string Of(string path)
    {
        string full = Path.GetFullPath(path);
        foreach ((string root, string prefix) in roots)
        {
            if (root.Length > 0 && full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return prefix + Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        return full.Replace(Path.DirectorySeparatorChar, '/');
    }
}
