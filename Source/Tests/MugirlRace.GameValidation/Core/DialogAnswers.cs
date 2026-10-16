using System.Reflection;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>无人值守地回答对话树窗口：选第一个可用选项，沿链接继续，直到窗口关闭。</summary>
/// <remarks>
/// 有些任务在对话未作答而被关闭时会在下一个 Tick 重新弹出同一对话（例如必须二选一的加入请求），
/// 单纯关闭会让游戏每帧只走一个 Tick。<see cref="ValidationContext.AnswerDialogs"/> 打开时，会话推进 Tick 期间用本类作答。
/// 选项的 Activate 是受保护成员，按名字反射调用，与玩家点击选项的效果相同。只在主线程使用。
/// </remarks>
internal static class DialogAnswers
{
    // 对话可能多级嵌套；超过这个深度仍未关闭就交给调用方关闭。
    private const int MaxDepth = 8;

    // StaticCacheLifecycle: 进程级反射句柄，类型结构在进程内不变。
    private static readonly FieldInfo? CurrentNode = AccessTools.Field(typeof(Dialog_NodeTree), "curNode");
    private static readonly FieldInfo? OptionText = AccessTools.Field(typeof(DiaOption), "text");
    private static readonly MethodInfo? Activate = AccessTools.Method(typeof(DiaOption), "Activate");

    /// <summary>选第一个可用选项作答；返回是否至少选过一个选项，<paramref name="choices"/> 为依次选择的选项文本。</summary>
    public static bool TryAnswer(Dialog_NodeTree dialog, out string choices)
    {
        var chosen = new List<string>();
        if (CurrentNode is null || Activate is null)
        {
            choices = string.Empty;
            return false;
        }

        for (int depth = 0; depth < MaxDepth && Find.WindowStack.IsOpen(dialog); depth++)
        {
            if (CurrentNode.GetValue(dialog) is not DiaNode node || node.options.FirstOrDefault(o => !o.disabled) is not { } option)
            {
                break;
            }

            option.dialog ??= dialog;
            chosen.Add(OptionText?.GetValue(option) as string ?? "?");
            Activate.Invoke(option, null);
            if (option.link is null && option.linkLateBind is null && !option.resolveTree)
            {
                // 既不跳转也不关闭的选项（例如只播放声音）：再选一次也不会前进。
                break;
            }
        }

        choices = string.Join(" > ", chosen);
        return chosen.Count > 0;
    }
}
