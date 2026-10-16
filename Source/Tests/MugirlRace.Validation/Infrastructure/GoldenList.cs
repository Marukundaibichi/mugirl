using MugirlRace.Validation.Reporting;

namespace MugirlRace.Validation.Infrastructure;

/// <summary>
/// <c>Baselines/</c> 中生成的黄金文件（每行一项，按序数排序）。实际结果不同即失败；<c>--update-baselines</c> 重写文件，
/// 之后像代码改动一样审阅差异。
/// </summary>
internal static class GoldenList
{
    public static void Compare(string directory, string fileName, IEnumerable<string> actualItems, bool update, CheckRecorder recorder, string header)
    {
        List<string> actual = actualItems.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        string path = Path.Combine(directory, fileName);
        if (update)
        {
            // 固定 LF：生成结果与运行平台无关，重新生成时差异只含内容（检出换行由 Git 的 core.autocrlf 处理）。
            IEnumerable<string> lines = header.Split('\n').Select(line => "# " + line.TrimEnd()).Concat(actual);
            File.WriteAllText(path, string.Join('\n', lines) + "\n");
            recorder.Info("Baselines/" + fileName, $"Rewritten with {actual.Count} entries (--update-baselines). Review the diff.");
            return;
        }

        if (!File.Exists(path))
        {
            recorder.Fail("Baselines/" + fileName, $"Golden file missing; run with --update-baselines to create it ({actual.Count} entries), then review it.");
            return;
        }

        var expected = new HashSet<string>(
            File.ReadLines(path).Select(line => line.TrimEnd()).Where(line => line.Length > 0 && !line.StartsWith('#')),
            StringComparer.Ordinal);
        foreach (string missing in expected.Except(actual).Order(StringComparer.Ordinal))
        {
            recorder.Fail("- " + missing, $"In Baselines/{fileName} but not produced by this build.");
        }

        foreach (string added in actual.Except(expected).Order(StringComparer.Ordinal))
        {
            recorder.Fail("+ " + added, $"Produced by this build but not in Baselines/{fileName} (review, then --update-baselines).");
        }
    }
}
