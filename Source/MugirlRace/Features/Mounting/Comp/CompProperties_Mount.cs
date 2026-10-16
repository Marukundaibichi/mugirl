namespace Mugirl.Features.Mounting;

// XML 中 Class="Mugirl.Features.Mounting.CompProperties_Mount"；字段名是 XML 节点名，不能改。
// 偏移字段保持 UnityEngine.Vector3（UVector3）类型，由原版 XML 解析器读取。
public class CompProperties_Mount : CompProperties
{
    public int physiologicalTickInterval = 150;
    public int safetyCheckInterval = 250;
    public int turretTickInterval = 60;
    public int turretMinAimTicks = 15;
    public bool tickPhysiology = true;
    public bool autoDismount = true;
    public bool turretFireAtWillDefault = true;
    public float riderAltitudeOffset = 0.022f;
    public float northRiderAltitudeOffset = 0.04f;
    public float southRiderAltitudeOffset = -0.04f;
    public UVector3 northOffset = new(0f, 0f, 0.08f);
    public UVector3 eastOffset = new(-0.20f, 0f, 0.02f);
    public UVector3 southOffset = new(0f, 0f, 0.14f);
    public UVector3 westOffset = new(0.20f, 0f, 0.02f);

    public CompProperties_Mount()
    {
        compClass = typeof(CompMount);
    }
}
