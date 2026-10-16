namespace Mugirl.Features.Mounting;

// 临时把 Verb.caster 换成载具，让原版射线、射程和目标判定以载具位置计算。
// Dispose 恢复进入作用域时的施放者，嵌套作用域按栈顺序还原：内层结束后外层的替换仍然有效。
// 嵌套覆盖必须恢复进入本层之前的施放者，避免外层仍在使用载具时提前切回骑手。
public readonly struct MountedVerbScope : IDisposable
{
    private readonly Verb? verb;
    private readonly Thing? previousCaster;
    private readonly bool active;

    public MountedVerbScope(Verb? verb, Thing? temporaryCaster)
    {
        this.verb = verb;
        previousCaster = verb?.caster;
        active = verb is not null && temporaryCaster is not null;
        if (active && verb!.caster != temporaryCaster)
        {
            verb.caster = temporaryCaster;
        }
    }

    public void Dispose()
    {
        if (active && verb!.caster != previousCaster)
        {
            verb.caster = previousCaster;
        }
    }
}
