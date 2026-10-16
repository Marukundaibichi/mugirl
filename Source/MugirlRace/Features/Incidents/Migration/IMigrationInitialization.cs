namespace Mugirl.Features.Incidents;

// 批次必须在引用解析完成后执行的持久初始化；成功后由所属组件清除。
internal interface IMigrationInitialization : IExposable
{
    void Complete(MigrationState state);
}
