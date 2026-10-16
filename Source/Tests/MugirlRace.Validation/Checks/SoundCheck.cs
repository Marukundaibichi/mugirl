using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Reporting;
using Verse.Sound;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. Mod 的 SoundDef 引用的音频存在：<c>AudioGrain_Clip.clipPath</c> 是一个文件，<c>AudioGrain_Folder.clipFolderPath</c> 是非空目录。
/// 与贴图一样，1.7 按区分大小写查找 Mod 自己的音频（Windows 资源管理器里看不出大小写错误）。
/// </summary>
internal static class SoundCheck
{
    public static void Run(Report report, ContentModel content)
        => report.Run("C.sounds", "Sound clip paths of the mod's SoundDefs resolve (case-sensitive)", recorder => Check(recorder, content));

    private static void Check(CheckRecorder recorder, ContentModel content)
    {
        string root = content.Catalog.UnderTest.RootDir;
        string current = "";
        string file = "";
        int count = 0;
        var walker = new XmlTypeWalker(content.Types, content.Catalog.Full)
        {
            StringValue = (node, field, value) =>
            {
                string path = value.Trim();
                if (path.Length == 0)
                {
                    return;
                }

                if (field.DeclaringType == typeof(AudioGrain_Clip) && field.Name == nameof(AudioGrain_Clip.clipPath))
                {
                    count++;
                    Report(recorder, $"{current}: clipPath '{path}'", content.Sounds.FindClip(path), file);
                }
                else if (field.DeclaringType == typeof(AudioGrain_Folder) && field.Name == nameof(AudioGrain_Folder.clipFolderPath))
                {
                    count++;
                    Report(recorder, $"{current}: clipFolderPath '{path}'", content.Sounds.FindFolder(path), file);
                }
            },
        };

        foreach (XmlElement node in content.Corpus.Defs)
        {
            NodeSource? source = content.Corpus.SourceOf(node);
            if (!ContentModel.IsOurs(source?.Pack) || node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase)
                || !DefIndex.MayRequireSatisfied(node, content.Catalog.Full))
            {
                continue;
            }

            current = $"{node.Name}[{node["defName"]?.InnerText.Trim()}]";
            file = XmlPaths.File(root, source!.File);
            walker.WalkDef(content.Inheritance.Resolve(node), []);
        }

        recorder.Summary = $"{count} sound path(s) checked";
    }

    private static void Report(CheckRecorder recorder, string subject, TextureMatch match, string location)
    {
        switch (match)
        {
            case TextureMatch.Missing:
                recorder.Fail(subject, "Audio not found in this mod, its dependencies, the DLCs or the base game.", location);
                break;
            case TextureMatch.ContentCaseMismatch:
                recorder.Fail(subject, "Found only by ignoring case: RimWorld 1.7 looks up mod audio case-sensitively, so the sound stays silent.", location);
                break;
            case TextureMatch.CaseMismatch:
                recorder.Warn(subject, "Found only by ignoring case (base resources resolve that).", location);
                break;
            case TextureMatch.OptionalContentOnly:
                recorder.Warn(subject, "Found only in optional content (a DLC or mod the mod does not require).", location);
                break;
        }
    }
}
