using Verse;

namespace MooGirl
{
    // R18 标记仅作为 Def 元数据保留，供审计和未来内容工具使用；
    // 它不是运行时门禁，用户已确认 R18 内容常驻。
    public class AdultContentExtension : DefModExtension
    {
        public bool adultOnly = true;
    }

    public class CompProperties_AdultContentControl : CompProperties
    {
        public CompProperties_AdultContentControl()
        {
            compClass = typeof(CompAdultContentControl);
        }
    }

    // XML 仍引用此 comp。R18 常驻后这里故意不注册运行时钩子，
    // 只保留 Def 加载有效性。
    public class CompAdultContentControl : ThingComp
    {
    }
}
