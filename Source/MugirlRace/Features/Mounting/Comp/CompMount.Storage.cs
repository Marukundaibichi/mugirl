namespace Mugirl.Features.Mounting;

// 组件保持容器所有权；嵌套值状态同步序列化，引用仍以该组件的原加载回调为边界。
public sealed partial class CompMount
{
    private bool mountScheduleRead;
    private bool mountTurretRead;

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Deep.Look(ref mugirlMountRiders, "mugirlMountRiders", this);
        // Def 可在 Thing 保存后新增此组件；所有读取阶段都保持容器非空。
        if (Scribe.mode != LoadSaveMode.Saving)
        {
            MakeContainer();
        }

        Scribe_Values.Look(ref mugirlMountFireAtWill, "mugirlMountFireAtWill", Props?.turretFireAtWillDefault ?? true, forceSave: true);
        ExposeState(mugirlMountSchedule, "mugirlMountSchedule", ref mountScheduleRead);
        ExposeState(mugirlMountTurret, "mugirlMountTurret", ref mountTurretRead);
    }

    private static void ExposeState(IExposable state, string label, ref bool wasRead)
    {
        // CrossRefs 时 curXmlParent 已清空，EnterNode 不再证明节点存在；只解析 LoadingVars 真正登记过的组。
        if ((Scribe.mode == LoadSaveMode.ResolvingCrossRefs || Scribe.mode == LoadSaveMode.PostLoadInit) && !wasRead)
        {
            return;
        }

        bool entered = Scribe.EnterNode(label);
        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            wasRead = entered;
        }

        if (!entered)
        {
            return;
        }

        try
        {
            state.ExposeData();
        }
        finally
        {
            Scribe.ExitNode();
        }
    }

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
    }

    public ThingOwner GetDirectlyHeldThings()
    {
        return mugirlMountRiders;
    }
}
