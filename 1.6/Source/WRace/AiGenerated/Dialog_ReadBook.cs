using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    public class Dialog_ReadBook : Window
    {
        private Thing book;
        private string title;
        private int currentPage = 0;

        private static string[] defaultPages = new string[]
        {
            "【第一页】\n\n欢迎阅读《运货员日记》！\n\n这本日记记录了一个底层运货员在巨型企业工作期间的种种经历，包含大量实用技巧和生存经验。",
            "【第二页】\n\n关于巨型企业：\n\n巨型企业是一个庞大的企业联合体，其武装力量遍布各个星球。他们拥有独特的OPIC和PMC系列的武器装备。\n\n遭遇袭击时请注意敌人的装备等级——携带PMC前缀武器的敌人往往更危险。",
            "【第三页】\n\n关于雪牛娘：\n\n雪牛娘是基因工程的产物，她们分泌的乳汁具有非凡的营养价值。可以通过制作各种雪牛娘奶制品来获得额外加成。\n\n雪牛娘奶布丁能提升心情，奶酪可以解毒，陈年奶酪和奶片能增强免疫力。",
            "【第四页】\n\n关于束具钥匙：\n\n粗糙束具钥匙可以在机械加工台或锻造台制作，精致束具钥匙需要更高级的工作台。\n\n如果你捡到了运气好的运货员携带的钥匙，可以用它们来解开雪牛娘身上的束具。",
            "【第五页】\n\n生存建议：\n\n1. 发展雪牛娘养殖以获得稳定的奶源\n2. 积攒足够的防御力量应对巨型企业袭击\n3. 制作各种奶制品以增强殖民者的能力\n4. 束具钥匙可以用来招募雪牛娘奴隶\n\n祝你在这个残酷的世界中活下来！"
        };

        public Dialog_ReadBook(string title, Thing book)
        {
            this.title = title;
            this.book = book;
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

            // Page counter
            Widgets.Label(new Rect(0f, y, inRect.width, 25f),
                "第" + (currentPage + 1).ToString() + "页 / 共" + defaultPages.Length.ToString() + "页");
            y += 30f;

            // Content area
            Rect contentRect = new Rect(0f, y, inRect.width - 20f, inRect.height - y - 60f);
            Widgets.Label(contentRect, defaultPages[currentPage]);

            y = inRect.height - 55f;

            // Previous page button
            if (currentPage > 0)
            {
                if (Widgets.ButtonText(new Rect(0f, y, 100f, 35f), "上一页"))
                {
                    currentPage--;
                }
            }

            // Next page button
            if (currentPage < defaultPages.Length - 1)
            {
                if (Widgets.ButtonText(new Rect(inRect.width - 120f, y, 100f, 35f), "下一页"))
                {
                    currentPage++;
                }
            }

            // Close button
            if (Widgets.ButtonText(new Rect((inRect.width - 100f) / 2f, y, 100f, 35f), "关闭"))
            {
                Close();
            }

        }
    }
}
