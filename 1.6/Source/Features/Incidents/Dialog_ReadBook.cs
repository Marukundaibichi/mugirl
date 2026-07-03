using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public class Dialog_ReadBook : Window
    {
        private static readonly string[] PageKeys =
        {
            "Mugirl.CourierDiary.Page1",
            "Mugirl.CourierDiary.Page2",
            "Mugirl.CourierDiary.Page3",
            "Mugirl.CourierDiary.Page4",
            "Mugirl.CourierDiary.Page5"
        };

        private readonly string title;
        private int currentPage;

        public Dialog_ReadBook(string title)
        {
            this.title = title;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            doCloseX = true;
            doCloseButton = false;
            draggable = true;
            resizeable = false;
        }

        public override Vector2 InitialSize => new Vector2(600f, 520f);

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 40f), title);
            y += 50f;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, y, inRect.width, 25f), "Mugirl.CourierDiary.PageCounter".Translate(currentPage + 1, PageKeys.Length));
            y += 30f;

            Rect contentRect = new Rect(0f, y, inRect.width - 20f, inRect.height - y - 60f);
            Widgets.Label(contentRect, PageKeys[currentPage].Translate());

            y = inRect.height - 55f;

            if (currentPage > 0 && Widgets.ButtonText(new Rect(0f, y, 100f, 35f), "Mugirl.CourierDiary.PreviousPage".Translate()))
            {
                currentPage--;
            }

            if (currentPage < PageKeys.Length - 1 && Widgets.ButtonText(new Rect(inRect.width - 120f, y, 100f, 35f), "Mugirl.CourierDiary.NextPage".Translate()))
            {
                currentPage++;
            }

            if (Widgets.ButtonText(new Rect((inRect.width - 100f) / 2f, y, 100f, 35f), "Mugirl.CourierDiary.Close".Translate()))
            {
                Close();
            }
        }
    }
}
