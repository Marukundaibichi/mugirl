namespace Mugirl.Features.Restraints;

// R18 标记仅作为 Def 元数据保留，供审计和未来内容工具使用；
// 它不是运行时门禁，用户已确认 R18 内容常驻。
public class AdultContentExtension : DefModExtension
{
    public bool adultOnly = true;
}
