// 仅由 EnableCorporateValidation=true 条件编译。此探针不属于正常发布版本。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public sealed class CorporateVisualValidation : GameComponent
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private bool enabled;
        private bool started;
        private bool finished;
        private bool compactRequested;
        private bool compactPass;
        private float startedAt;
        private float nextAt;
        private float nextArrivalDiagnosticAt;
        private int phase;
        private int page;
        private string outputRoot;
        private string pendingShot;
        private DateTime pendingShotSince;
        private Map map;
        private Pawn negotiator;
        private Window_CorporateComms terminal;
        private readonly VisualReport report = new VisualReport();

        public CorporateVisualValidation(Game game)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (!HasFlag(arguments, "mugirlCorporateVisual")) return;
            string requestedFolder = ArgumentValue(arguments, "savedatafolder");
            if (!AllowedSaveFolder(requestedFolder))
            {
                Log.Warning("[CorporateVisualValidation] Disabled: savedatafolder must be an explicit isolated temporary directory.");
                return;
            }
            outputRoot = Path.Combine(Path.GetFullPath(requestedFolder), "ValidationShots");
            compactRequested = HasFlag(arguments, "mugirlCorporateVisualCompact");
            enabled = true;
        }

        public override void GameComponentUpdate()
        {
            if (!enabled || finished || Current.ProgramState != ProgramState.Playing) return;
            try
            {
                if (!started)
                {
                    // 再核对引擎实际采用的路径；不相信仅传入但未生效的命令行目录。
                    string requested = Path.GetDirectoryName(outputRoot);
                    if (!string.Equals(Normalize(GenFilePaths.SaveDataFolderPath), Normalize(requested), StringComparison.OrdinalIgnoreCase))
                    {
                        enabled = false;
                        Log.Warning("[CorporateVisualValidation] Disabled: active save path does not match the isolated requested path.");
                        return;
                    }
                    Directory.CreateDirectory(outputRoot);
                    started = true;
                    startedAt = Time.realtimeSinceStartup;
                    nextAt = startedAt + 2f;
                    Application.runInBackground = true;
                    Application.logMessageReceived += OnLog;
                    Prefs.UIScale = 1f;
                    Screen.SetResolution(1920, 1080, false);
                    report.saveRoot = requested;
                    report.mode = "Actual RimWorld Unity window; isolated validation only";
                    report.steps.Add("Safety gates passed; actual save folder verified.");
                    WriteProgress("waiting-for-colony");
                    return;
                }
                if (Time.realtimeSinceStartup - startedAt > 180f) throw new TimeoutException("Visual validation exceeded 180 seconds after entering play.");
                if (Time.realtimeSinceStartup < nextAt) return;
                if (pendingShot != null)
                {
                    if (!File.Exists(pendingShot)) return;
                    FileInfo screenshot = new FileInfo(pendingShot);
                    if (screenshot.Length == 0 || screenshot.LastWriteTimeUtc < pendingShotSince) return;
                    report.screenshots.Add(pendingShot);
                    pendingShot = null;
                }
                Advance();
            }
            catch (Exception exception)
            {
                report.errors.Add(exception.ToString());
                Finish(false);
            }
        }

        private void Advance()
        {
            CorporateIntroduction introduction = CorporateIntroduction.Current;
            switch (phase)
            {
                case 0:
                    map = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
                    if (map == null || introduction == null) return;
                    // 开局信息窗会暂停原版空投舱落地；先恢复隔离场景，再等待殖民者真正出现。
                    CloseMessageBoxes();
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    negotiator = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Dead && !p.Downed
                        && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking));
                    if (negotiator == null) return;
                    if (introduction.Completed) throw new InvalidOperationException("Use a fresh validation save with an unfinished corporate introduction.");
                    // 只在一次性隔离环境准备前置；之后走真实来访服务和真实对白选项。
                    Set(introduction, "pending", true);
                    Set(introduction, "initialized", true);
                    Set(introduction, "courierChoice", 1);
                    Set(introduction, "contactTick", CorporateNetwork.Now);
                    Set(introduction, "preferredMap", map);
                    Set(introduction, "nextServiceTick", 0);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    report.steps.Add("Registered introduction and allowed its real visitor service to run.");
                    phase = 1;
                    Wait(1f);
                    return;
                case 1:
                    RecordArrivalConditions(introduction);
                    if (introduction.Representative?.Spawned != true) return;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(introduction.Representative);
                    CameraJumper.TryJumpAndSelect(introduction.Representative);
                    report.representative = introduction.Representative.LabelShortCap;
                    Capture("01-representative-arrived");
                    phase = 2;
                    return;
                case 2:
                    introduction.ShowDialog(negotiator, introduction.Representative);
                    if (!Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Any()) throw new InvalidOperationException("Introduction did not open its dialogue.");
                    report.steps.Add("Opened real introduction Dialog_NodeTree.");
                    phase = 3;
                    Wait(2f);
                    return;
                case 3:
                    Capture("02-introduction-dialogue");
                    phase = 4;
                    return;
                case 4:
                    Dialog_NodeTree dialogue = Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Last();
                    DiaNode node = (DiaNode)typeof(Dialog_NodeTree).GetField("curNode", Fields).GetValue(dialogue);
                    FieldInfo optionText = typeof(DiaOption).GetField("text", Fields);
                    string acceptText = "Mugirl.CorporateIntro.Accept".Translate();
                    DiaOption accept = node.options.Single(o => (string)optionText.GetValue(o) == acceptText);
                    if (accept.disabled) throw new InvalidOperationException("The actual introduction accept option is disabled.");
                    // Activate 关闭原始对话并调用业务 action，不直接改 completed 或调用私有 Complete。
                    typeof(DiaOption).GetMethod("Activate", Fields).Invoke(accept, null);
                    if (!introduction.Completed || CorporateNetwork.Current?.Unlocked != true)
                        throw new InvalidOperationException("Accepting the actual DiaOption did not unlock the network.");
                    report.introductionCompleted = true;
                    report.steps.Add("Activated actual DiaOption; verified introduction completion and network unlock.");
                    phase = 5;
                    Wait(1.5f);
                    return;
                case 5:
                    CloseMessageBoxes();
                    OpenTerminal();
                    page = 0;
                    phase = 6;
                    Wait(2f);
                    return;
                case 6:
                    if (terminal == null || !Find.WindowStack.Windows.Contains(terminal)) throw new InvalidOperationException("Terminal disappeared before page capture.");
                    Set(terminal, "selectedPage", page);
                    phase = 7;
                    Wait(1.5f);
                    return;
                case 7:
                    string[] names = { "overview", "supplies", "orders", "people", "finance", "missions", "story", "records" };
                    Capture((compactPass ? "compact-" : "normal-") + names[page]);
                    page++;
                    phase = page < names.Length ? 6 : 8;
                    return;
                case 8:
                    terminal.Close(false);
                    terminal = null;
                    if (compactRequested && !compactPass)
                    {
                        compactPass = true;
                        Prefs.UIScale = 1f;
                        Screen.SetResolution(1280, 720, false);
                        report.steps.Add("Requested compact pass: 1280x720 with 100% UI scaling (within the game's supported logical resolution).");
                        phase = 5;
                        Wait(3f);
                    }
                    else
                    {
                        phase = 9;
                        Wait(2f);
                    }
                    return;
                case 9:
                    Finish(report.errors.Count == 0 && report.introductionCompleted && report.screenshots.Count >= 10);
                    return;
            }
        }

        private void OpenTerminal()
        {
            // 视觉夹具直接提供有效地图上下文；此步骤不冒充通讯台寻路或据点到场的验收。
            terminal = new Window_CorporateComms(new CorporateTradeContext(map) { Negotiator = negotiator });
            Find.WindowStack.Add(terminal);
            report.steps.Add("Opened actual white terminal at logical viewport " + UI.screenWidth + "x" + UI.screenHeight + ".");
        }

        private void RecordArrivalConditions(CorporateIntroduction introduction)
        {
            if (Time.realtimeSinceStartup < nextArrivalDiagnosticAt) return;
            nextArrivalDiagnosticAt = Time.realtimeSinceStartup + 5f;
            var text = new StringBuilder();
            text.AppendLine("tick=" + CorporateNetwork.Now + "; speed=" + Find.TickManager.CurTimeSpeed
                + "; pending=" + typeof(CorporateIntroduction).GetField("pending", Fields).GetValue(introduction)
                + "; nextServiceTick=" + typeof(CorporateIntroduction).GetField("nextServiceTick", Fields).GetValue(introduction)
                + "; corporation=" + CorporateNetwork.Current?.CorporateFaction?.Name
                + "; defeated=" + CorporateNetwork.Current?.CorporateFaction?.defeated
                + "; activeThreat=" + GenHostility.AnyHostileActiveThreatToPlayer(map)
                + "; representative=" + introduction.Representative?.ThingID);
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.Downed && p.HostileTo(Faction.OfPlayer)))
                text.AppendLine("hostile=" + pawn.ThingID + "; kind=" + pawn.kindDef.defName + "; fogged=" + pawn.Fogged()
                    + "; dormancyAwake=" + (pawn.TryGetComp<CompCanBeDormant>()?.Awake.ToString() ?? "no-dormancy-comp")
                    + "; active=" + GenHostility.IsActiveThreatToPlayer(pawn) + "; job=" + pawn.CurJobDef?.defName);
            File.AppendAllText(Path.Combine(outputRoot, "arrival-diagnostics.txt"), text.ToString() + Environment.NewLine);
            WriteProgress("waiting-for-representative");
        }

        private void Capture(string name)
        {
            pendingShot = Path.Combine(outputRoot, name + ".png");
            pendingShotSince = DateTime.UtcNow;
            ScreenCapture.CaptureScreenshot(pendingShot);
            report.frames.Add(new VisualFrame
            {
                file = name + ".png", screenWidth = Screen.width, screenHeight = Screen.height,
                uiWidth = UI.screenWidth, uiHeight = UI.screenHeight, uiScale = Prefs.UIScale
            });
            WriteProgress("capturing-" + name);
            Wait(2f);
        }

        private void Wait(float seconds) { nextAt = Time.realtimeSinceStartup + seconds; }
        private static void Set(object instance, string field, object value)
        {
            FieldInfo member = instance.GetType().GetField(field, Fields);
            if (member == null) throw new MissingFieldException(instance.GetType().FullName, field);
            member.SetValue(instance, value);
        }
        private static void CloseMessageBoxes()
        {
            foreach (Dialog_MessageBox box in Find.WindowStack.Windows.OfType<Dialog_MessageBox>().ToList()) box.Close(false);
        }
        private void OnLog(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && report.errors.Count < 100)
                report.errors.Add(message + "\n" + stack);
        }
        private void WriteProgress(string step)
        {
            File.WriteAllText(Path.Combine(outputRoot, "visual-progress.txt"), DateTime.UtcNow.ToString("O") + " " + step);
        }
        private void Finish(bool success)
        {
            if (finished || !started) return;
            finished = true;
            report.success = success;
            report.seconds = Time.realtimeSinceStartup - startedAt;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(Path.Combine(outputRoot, "visual-result.json"), SerializeReport());
            File.WriteAllText(Path.Combine(outputRoot, "visual-result.txt"),
                (success ? "PASS" : "FAIL") + "\n" + string.Join("\n", report.steps) + "\n\n" + string.Join("\n", report.errors));
            // 只退出已通过路径与参数双重验证的本次隔离进程，不操作其他 RimWorld 进程。
            Application.Quit();
        }

        private string SerializeReport()
        {
            return "{\n  \"success\": " + (report.success ? "true" : "false")
                + ",\n  \"introductionCompleted\": " + (report.introductionCompleted ? "true" : "false")
                + ",\n  \"saveRoot\": " + Quote(report.saveRoot) + ",\n  \"mode\": " + Quote(report.mode)
                + ",\n  \"representative\": " + Quote(report.representative)
                + ",\n  \"seconds\": " + report.seconds.ToString("R", CultureInfo.InvariantCulture)
                + ",\n  \"screenshots\": [" + string.Join(",", report.screenshots.Select(Quote)) + "]"
                + ",\n  \"steps\": [" + string.Join(",", report.steps.Select(Quote)) + "]"
                + ",\n  \"errors\": [" + string.Join(",", report.errors.Select(Quote)) + "]"
                + ",\n  \"frames\": [" + string.Join(",", report.frames.Select(f =>
                    "{\"file\":" + Quote(f.file) + ",\"screenWidth\":" + f.screenWidth + ",\"screenHeight\":" + f.screenHeight
                    + ",\"uiWidth\":" + f.uiWidth + ",\"uiHeight\":" + f.uiHeight + ",\"uiScale\":" + f.uiScale.ToString("R", CultureInfo.InvariantCulture) + "}"))
                + "]\n}\n";
        }

        private static string Quote(string value)
        {
            if (value == null) return "null";
            StringBuilder text = new StringBuilder("\"");
            foreach (char character in value)
            {
                if (character == '\\' || character == '"') { text.Append('\\'); text.Append(character); }
                else if (character < 32) text.Append("\\u" + ((int)character).ToString("x4"));
                else text.Append(character);
            }
            return text.Append('"').ToString();
        }

        private static bool HasFlag(IEnumerable<string> arguments, string name)
        {
            return arguments.Any(a => a.TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        private static string ArgumentValue(IEnumerable<string> arguments, string name)
        {
            string prefix = name + "=";
            string match = arguments.Select(a => a.TrimStart('-')).FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return match?.Substring(prefix.Length).Trim('"');
        }
        private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static bool IsChildOf(string path, string root) => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static bool AllowedSaveFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
            try
            {
                string resolved = Normalize(path);
                for (DirectoryInfo directory = new DirectoryInfo(resolved); directory != null; directory = directory.Parent)
                    if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                string temp = Normalize(Path.GetTempPath());
                if (IsChildOf(resolved, temp)) return true;
                string modRoot = Normalize(Path.Combine(Path.GetDirectoryName(typeof(CorporateNetwork).Assembly.Location), "..", ".."));
                string repositoryTemp = Path.Combine(modRoot, "TMP");
                if (!IsChildOf(resolved, repositoryTemp)) return false;
                string relative = resolved.Substring(repositoryTemp.Length + 1);
                string firstFolder = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                return firstFolder.StartsWith("CorporateValidation", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        [Serializable]
        private sealed class VisualReport
        {
            public bool success;
            public bool introductionCompleted;
            public string saveRoot;
            public string mode;
            public string representative;
            public float seconds;
            public List<string> screenshots = new List<string>();
            public List<string> steps = new List<string>();
            public List<string> errors = new List<string>();
            public List<VisualFrame> frames = new List<VisualFrame>();
        }
        [Serializable]
        private sealed class VisualFrame
        {
            public string file;
            public int screenWidth;
            public int screenHeight;
            public int uiWidth;
            public int uiHeight;
            public float uiScale;
        }
    }
}
