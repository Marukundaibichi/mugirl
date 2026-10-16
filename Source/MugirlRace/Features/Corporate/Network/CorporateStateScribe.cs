namespace Mugirl.Features.Corporate;

// 值聚合由所属组件同步序列化，保持各加载阶段的业务顺序和引用解析时点。
internal static class CorporateStateScribe
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
