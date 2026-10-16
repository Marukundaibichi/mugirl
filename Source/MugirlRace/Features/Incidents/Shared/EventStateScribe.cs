namespace Mugirl.Features.Incidents;

// 聚合在所属组件的原回调中同步暴露，保持交叉引用与加载后维护的顺序。
internal static class EventStateScribe
{
    internal static void Look(IExposable state, string label, ref bool wasRead)
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
}
