namespace Mugirl.Control.Upgrade;

public sealed class MergedLeaf : IExposable
{
    private int progress;
    public void ExposeData() => Scribe_Values.Look(ref progress, "mergedProgress");
}

public sealed class LostLeaf : IExposable
{
    private int other;
    public void ExposeData() => Scribe_Values.Look(ref other, "other");
}

public sealed class PreservedSibling : IExposable
{
    private int progress;
    public void ExposeData() => Scribe_Values.Look(ref progress, "mergedProgress");
}

public sealed class DriftLeaf : IExposable
{
    private int progress = 7;
    public void ExposeData() => Scribe_Values.Look(ref progress, "mergedProgress", 7);
}
