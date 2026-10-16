using MugirlRace.Validation.Content;
using MugirlRace.Validation.Reporting;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. Defs 与 Patches 文件不能被同一 Mod 另一个加载目录中相同相对路径的文件遮蔽：游戏按优先级遍历加载目录，以相对路径去重
/// （<c>DirectXmlLoader.XmlAssetsInModFolder</c> 的 <c>TryAdd</c>），低优先级的文件被静默丢弃，其中的 Def 与补丁都不存在。
/// 改名或移动 XML 文件时最容易发生。按完整配置（全部条件目录启用）检查。
/// </summary>
internal static class ContentShadowingCheck
{
    private static readonly string[] Roots = ["Defs", "Patches"];

    public static void Run(Report report, ContentModel content)
        => report.Run("C.content-shadowing", "No Defs or Patches file is shadowed by a same-named file in another load folder", recorder => Check(recorder, content));

    private static void Check(CheckRecorder recorder, ContentModel content)
    {
        ContentPack mod = content.Catalog.UnderTest;
        IReadOnlyList<string> folders = content.Catalog.FoldersFor(mod, content.Catalog.Full);
        int files = 0;
        foreach (string root in Roots)
        {
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string folder in folders)
            {
                string directory = Path.Combine(folder, root);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    files++;
                    string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                    string location = XmlPaths.File(mod.RootDir, file);
                    if (owner.TryGetValue(relative, out string? winner))
                    {
                        recorder.Fail(location, $"Shadowed by {winner}: the game loads only the higher-priority file with the relative path {relative}; every def and patch in this one is silently dropped. Rename one of them.", location);
                    }
                    else
                    {
                        owner[relative] = location;
                    }
                }
            }
        }

        recorder.Summary = $"{files} Defs/Patches file(s) in {folders.Count} load folder(s)";
    }
}
