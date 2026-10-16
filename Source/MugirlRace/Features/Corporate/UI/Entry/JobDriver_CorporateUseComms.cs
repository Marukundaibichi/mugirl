using Verse.AI;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 走到通讯台交互格后打开巨企终端。类型名由 1.7/Defs 的 driverClass 引用，并作为 curDriver 的类名写入存档，不能改名或移动命名空间。
/// </summary>
public class JobDriver_CorporateUseComms : JobDriver
{
    private Building_CommsConsole? Console => job.GetTarget(TargetIndex.A).Thing as Building_CommsConsole;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Building_CommsConsole? console = Console;
        return console is not null
            && CorporateCommsAccess.CheckConsole(console, pawn) == CorporateCommsAccess.ConsoleBlock.None
            && pawn.Reserve(console, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        // 每 tick 检查：只取判定结果，不翻译失败原因。
        this.FailOn(() => CorporateCommsAccess.CheckConsole(Console, pawn) != CorporateCommsAccess.ConsoleBlock.None);
        yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.InteractionCell);
        yield return Toils_General.Do(() => CorporateCommsAccess.OpenConsole(Console, pawn));
    }
}
