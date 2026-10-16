using System.Globalization;
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

    internal static void UpgradeCompleted(Game game, string backupPath)
    {
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            // 主线程真正显示时仍须是完成升级的那一局，避免切换游戏后收到上一局的提示。
            if (!GameUtility.IsCurrentGame(game)) { return; }
            string title = Translate(CompletedTitleKey, "Mugirl save upgraded");
            string text = Translate(CompletedBodyKey,
                "Your save has been automatically upgraded for the current version of Mugirl. The original save has been backed up.\n\nBackup file: {0}\n\nThe upgrade marker will be saved with your game. Loading that updated save will not send this notice again.", backupPath);
            game.letterStack.ReceiveLetter(title, text, LetterDefOf.PositiveEvent);
        });
    }

    internal static void UpgradeFailed(string stepId) => Show(
        FailedKey,
        "The Mugirl save upgrade failed at step {0}. Do not overwrite this save: keep playing only after reporting the problem, and send the log together with the .bak backup next to the save file.",
        stepId);

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
