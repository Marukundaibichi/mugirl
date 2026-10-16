namespace Mugirl.Features.Incidents;

// XML 中 Mugirl_CourierDiary 的 comp；DefInjected 句柄使用 compClass 的短名 CompCourierDiary，不能改名。
public class CompProperties_CourierDiary : CompProperties
{
    public string bookTitle = "Mugirl.CourierDiary.Title";

    public CompProperties_CourierDiary()
    {
        compClass = typeof(CompCourierDiary);
    }
}
