using System.Globalization;
using System.Linq;
using Mugirl.Core;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 给玩家的升级提示。读档在工作线程上运行，界面通过 <c>LongEventHandler.ExecuteWhenFinished</c> 排到主线程；
/// 闭包不保留升级会话或存档 DOM。翻译键缺失时显示英文原文。
/// </summary>
internal static class SaveUpgradeNotices
{
    internal const string FailedKey = "Mugirl.SaveUpgrade.Failed";
    internal const string NewerKey = "Mugirl.SaveUpgrade.NewerSave";
    internal const string MooGirlEraKey = "Mugirl.SaveUpgrade.MooGirlEra";
    internal const string NotInstalledKey = "Mugirl.SaveUpgrade.NotInstalled";
    internal const string CompletedTitleKey = "Mugirl.SaveUpgrade.CompletedTitle";
    internal const string CompletedBodyKey = "Mugirl.SaveUpgrade.CompletedBody";
    internal const string SourceWarningsKey = "Mugirl.SaveUpgrade.SourceWarnings";

    internal static void UpgradeCompleted(Game game, string backupPath, int warningCount = 0)
    {
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            // 主线程真正显示时仍须是完成升级的那一局，避免切换游戏后收到上一局的提示。
            if (!GameUtility.IsCurrentGame(game)) { return; }
            string title = Translate(CompletedTitleKey, "Mugirl save upgraded");
            string text = Translate(CompletedBodyKey,
                "Your save has been automatically upgraded for the current version of Mugirl. The original save has been backed up.\n\nBackup file: {0}\n\nThe upgrade marker will be saved with your game. Loading that updated save will not send this notice again.", backupPath);
            if (warningCount > 0)
            {
                text += "\n\n" + Translate(SourceWarningsKey,
                    "{0} task AI sources could not be restored. Their original keys and saved task progress have been retained. Pawns may choose new tasks when their AI next evaluates what to do. Details are recorded in the log.",
                    warningCount.ToString(CultureInfo.InvariantCulture));
            }
            game.letterStack.ReceiveLetter(title, text, LetterDefOf.PositiveEvent);
        });
    }

    internal static void UpgradeFailed(string stepId, string reason) => Show(
        FailedKey,
        "A Mugirl save compatibility step could not finish.\nStep: {0}\nReason: {1}\n\nKeep the original save, this log and any existing .bak backup for investigation before overwriting the original.\nDiagnostic build: {2}",
        stepId, NoticeReason(reason), typeof(SaveUpgradeNotices).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..8]);

    internal static string NoticeReason(string reason)
    {
        string firstLine = reason.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? reason;
        return firstLine.Length <= 320 ? firstLine : firstLine[..320] + "…";
    }

    internal static void NewerSave(int schema) => Show(
        NewerKey,
        "This save was written by a newer version of Mugirl (save schema {0}). Saving it with this version loses the newer data; update the mod before saving.",
        schema.ToString(CultureInfo.InvariantCulture));

    internal static void MooGirlEra() => Show(
        MooGirlEraKey,
        "This save contains content from the MooGirl era of the mod (0.4.4 and earlier), which is no longer supported. That content was discarded while loading; everything else was kept.");

    internal static void NotInstalled(string missing) => Show(
        NotInstalledKey,
        "Mugirl could not install its save upgrade hooks ({0}). Loading saves from older Mugirl versions is unsafe in this session; check the log for Harmony conflicts.",
        missing);

    private static void Show(string key, string fallback, params string[] args)
    {
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            string text = Translate(key, fallback, args);
            Find.WindowStack?.Add(new Dialog_MessageBox(text));
        });
    }

    private static string Translate(string key, string fallback, params string[] args)
    {
        if (key.CanTranslate())
        {
            var named = new NamedArgument[args.Length];
            for (int i = 0; i < args.Length; i++) { named[i] = args[i]; }
            return key.Translate(named).ToString();
        }
        object[] formatArgs = [.. args];
        return string.Format(CultureInfo.InvariantCulture, fallback, formatArgs);
    }
}
