// 独立临时探针模组；通过反射测试正式 DLL，不编入正式模组或修改玩家配置。
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mugirl;
using UnityEngine;
using Verse;

namespace MugirlInputProbe
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        internal static string Output;
        internal static ProbeWindow Current;
        internal static string Field;
        internal static int FieldId;
        internal static Type UI;
        internal static Assembly Target;
        internal static bool Baseline;

        static Bootstrap()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlCorporateInputProbe")) return;
            string modRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(CorporateTradeContext).Assembly.Location), "../.."));
            Output = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            if (!Output.StartsWith(Path.Combine(modRoot, "TMP") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            Baseline = GenCommandLine.CommandLineArgPassed("mugirlCorporateInputBaseline");
            Target = Baseline ? Assembly.LoadFile(Path.Combine(modRoot, "DevData/Backups/CorporateIMEFollowup-20260930/MugirlRace-before.dll"))
                : typeof(CorporateTradeContext).Assembly;
            UI = Target.GetType("Mugirl.CorporateUI");
            var patch = new Harmony("mugirl.corporate.input.runtime.probe");
            MethodInfo textField = typeof(GUI).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(m => m.Name == "DoTextField" && m.GetParameters().Length == 8);
            patch.Patch(textField, prefix: new HarmonyMethod(typeof(Bootstrap), "CaptureFieldId"));
            patch.Patch(typeof(UIRoot_Entry).GetMethod("UIRootUpdate"),
                postfix: new HarmonyMethod(typeof(Bootstrap), "OpenProbe"));
        }

        public static void OpenProbe()
        {
            if (Current != null || Find.WindowStack == null || LongEventHandler.AnyEventNowOrWaiting) return;
            File.WriteAllText(Path.Combine(Output, "input-events.txt"), "DLL=" + Target.Location + " baseline=" + Baseline + "\n");
            Application.runInBackground = true;
            Find.WindowStack.Add(Current = new ProbeWindow());
        }

        public static void CaptureFieldId(int id, Rect position)
        {
            if (Current == null || Field == null) return;
            FieldId = id;
            if (Field == "search" && Event.current.rawType != EventType.Repaint && Event.current.rawType != EventType.Layout)
                Write("native=" + Event.current.type + " rect=" + position + " mouse=" + Event.current.mousePosition + " enabled=" + GUI.enabled);
        }

        internal static object Invoke(string name, params object[] args)
        {
            return UI.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        }

        internal static void Write(string text)
        {
            File.AppendAllText(Path.Combine(Output, "input-events.txt"), text + "\n");
        }
    }

    public class ProbeWindow : Window
    {
        private readonly object motion = Activator.CreateInstance(Bootstrap.Target.GetType("Mugirl.CorporateMotion"), true);
        private readonly RimWorld.QuickSearchWidget nativeSearch = new RimWorld.QuickSearchWidget { maxSearchTextLength = int.MaxValue };
        private string search = "", quantity = "1";
        private Vector2 scroll;
        private float nextAt = -1f;
        private int phase, focusId;
        private bool outside;
        private bool finished;
        private Event pending;
        protected override float Margin { get { return 0f; } }
        public override Vector2 InitialSize { get { return new Vector2(800f, 500f); } }

        public ProbeWindow()
        {
            absorbInputAroundWindow = true;
            closeOnAccept = closeOnCancel = doCloseX = false;
            forcePause = true;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (finished) return;
            if (pending != null) return;
            if (nextAt < 0f) nextAt = Time.realtimeSinceStartup + 2f;
            if (Time.realtimeSinceStartup < nextAt) return;
            nextAt = Time.realtimeSinceStartup + 0.5f;
            Event ev;
            switch (phase++)
            {
                case 0: ev = new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(windowRect.x + 60f, windowRect.y + 94f) }; break;
                case 1: ev = new Event { type = EventType.MouseUp, button = 0, mousePosition = new Vector2(windowRect.x + 60f, windowRect.y + 94f) }; break;
                case 2: outside = false; ev = Key('a', KeyCode.A); break;
                case 3: outside = true; ev = Key('b', KeyCode.B); break;
                case 4: outside = false; ev = Key('钢', KeyCode.None); break;
                case 5: outside = true; ev = Key('铁', KeyCode.None); break;
                case 6: outside = true; ev = Key('\0', KeyCode.None); break;
                case 7: outside = true; ev = Key('\0', KeyCode.None); break;
                case 8: outside = true; ev = Key('\0', KeyCode.None); break;
                default:
                    finished = true;
                    string result = search == "ab钢铁" && quantity == "1" ? "PASS" : "FAIL";
                    File.WriteAllText(Path.Combine(Bootstrap.Output, "input-result.txt"), result + " search=" + search + " quantity=" + quantity);
                    Application.Quit();
                    return;
            }
            pending = ev;
        }

        private static Event Key(char character, KeyCode key)
        {
            return new Event { type = EventType.KeyDown, character = character, keyCode = key };
        }

        public override void DoWindowContents(Rect inRect)
        {
            Event original = Event.current;
            // Player 的 QueueEvent 不派发给原生 OnGUI；在真实窗口回调中替换本次事件，
            // 仍由 Unity 分配 ID、维护 TextEditor 和处理文本，不模拟这些实现。
            if (pending != null && original.type == EventType.Repaint)
            {
                Event.current = pending;
                pending = null;
                if (Event.current.rawType == EventType.MouseDown || Event.current.rawType == EventType.MouseUp)
                    Event.current.mousePosition = new Vector2(60f, 94f);
            }
            try { DrawFixture(inRect); }
            finally { Event.current = original; }
        }

        private void DrawFixture(Rect inRect)
        {
            Event ev = Event.current;
            if (ev.rawType == EventType.KeyDown)
                ev.mousePosition = outside ? new Vector2(-1000f, -1000f) : new Vector2(60f, 94f);
            Text.Font = GameFont.Small;
            using ((IDisposable)Bootstrap.Invoke("BeginFrame", motion, "probe", 1f, true))
            {
                Bootstrap.Invoke("DrawGeometry", inRect, 0.42f, false);
                if (!Bootstrap.Baseline) DrawSearch();
                object[] args = { inRect, scroll, new Rect(0f, 0f, inRect.width - 18f, 780f), "orders/page-scroll" };
                Bootstrap.Invoke("BeginScrollView", args);
                scroll = (Vector2)args[1];
                try
                {
                    Bootstrap.Invoke("Button", new Rect(0f, 0f, 350f, 34f), "物资订单", true, true, "orders/catalog");
                    Bootstrap.Invoke("Button", new Rect(354f, 0f, 350f, 34f), "我的订单", true, false, "orders/receipts");
                    if (Bootstrap.Baseline) DrawSearch();
                    Bootstrap.Field = "quantity";
                    quantity = (string)Bootstrap.Invoke("Input", new Rect(300f, 80f, 90f, 32f), quantity, null, "orders/quantity");
                    Bootstrap.Field = null;
                }
                finally { Bootstrap.Invoke("EndScrollView"); }
            }
        }

        private void DrawSearch()
        {
            Event ev = Event.current;
            Bootstrap.Field = "search";
            search = Bootstrap.Baseline
                ? (string)Bootstrap.Invoke("Input", new Rect(30f, 80f, 260f, 32f), search, "搜索", "orders/search")
                : (string)Bootstrap.Invoke("Search", new Rect(30f, 80f, 260f, 32f), search, nativeSearch, "搜索");
            int fieldId = Bootstrap.FieldId;
            // 合成 MouseDown 不能让原生 GUI.Window 获得 OS 焦点，显式设定一次
            // 字段焦点；后续文本、空字符键盘事件与焦点保持由真实 TextEditor 处理，
            // 不等于实际微软输入法组合串或候选窗测试。
            if (ev.rawType == EventType.MouseDown)
            {
                GUI.FocusWindow(ID);
                GUIUtility.keyboardControl = fieldId;
                focusId = fieldId;
            }
            if (ev.rawType == EventType.KeyDown || ev.rawType == EventType.MouseDown)
                Bootstrap.Write("phase=" + phase + " event=" + ev.rawType + " outside=" + outside + " field=" + fieldId
                    + " keyboard=" + GUIUtility.keyboardControl + " initial=" + focusId + " search=" + search);
            Bootstrap.Field = null;
        }
    }
}
