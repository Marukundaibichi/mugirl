using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal struct MilkFeedingLayoutEntry
    {
        public float SourceX;
        public float SourceZ;
        public float RecipientX;
        public float RecipientZ;
        public float SourceAngle;
        public float RecipientAngle;
    }

    // 喂奶姿态的镜像微调：X 随被喂者在左/右翻转，Z 保持屏幕方向。
    internal static class MilkFeedingLayout
    {
        internal static readonly MilkFeedingLayoutEntry Default = new MilkFeedingLayoutEntry();

        // StaticCacheLifecycle: 进程级开发者布局；首次读取 Config 覆盖文件，切图/读档不清理。
        // 调试窗口修改内存值；显式导出后才会写盘。
        private static bool loaded;
        private static MilkFeedingLayoutEntry current;

        internal static string OverrideDirectory => Path.Combine(GenFilePaths.ConfigFolderPath, "Mugirl");
        internal static string OverrideFilePath => Path.Combine(OverrideDirectory, "MilkFeedingLayout.xml");

        internal static MilkFeedingLayoutEntry Current
        {
            get { EnsureLoaded(); return current; }
            set { EnsureLoaded(); current = Clamp(value); }
        }

        internal static void ResetToDefaults()
        {
            EnsureLoaded();
            current = Default;
        }

        internal static string ExportXml()
        {
            MilkFeedingLayoutEntry value = Current;
            StringBuilder text = new StringBuilder();
            text.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            text.AppendLine("<!-- 喂奶姿态微调；放在 RimWorld Config/Mugirl/MilkFeedingLayout.xml。 -->");
            text.AppendLine("<MilkFeedingLayout>");
            text.AppendLine("  <sourceX>" + Number(value.SourceX) + "</sourceX>");
            text.AppendLine("  <sourceZ>" + Number(value.SourceZ) + "</sourceZ>");
            text.AppendLine("  <recipientX>" + Number(value.RecipientX) + "</recipientX>");
            text.AppendLine("  <recipientZ>" + Number(value.RecipientZ) + "</recipientZ>");
            text.AppendLine("  <sourceAngle>" + Number(value.SourceAngle) + "</sourceAngle>");
            text.AppendLine("  <recipientAngle>" + Number(value.RecipientAngle) + "</recipientAngle>");
            text.AppendLine("</MilkFeedingLayout>");
            return text.ToString();
        }

        internal static string WriteOverrideFile()
        {
            Directory.CreateDirectory(OverrideDirectory);
            File.WriteAllText(OverrideFilePath, ExportXml(), new UTF8Encoding(false));
            MugirlLog.DevMessage("Milk feeding layout exported to " + OverrideFilePath);
            return OverrideFilePath;
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            current = Default;
            try
            {
                if (!File.Exists(OverrideFilePath)) return;
                XElement root = XDocument.Load(OverrideFilePath).Root;
                if (root == null || root.Name != "MilkFeedingLayout")
                    throw new InvalidDataException("Unexpected XML root");
                current = Clamp(new MilkFeedingLayoutEntry
                {
                    SourceX = Read(root, "sourceX"),
                    SourceZ = Read(root, "sourceZ"),
                    RecipientX = Read(root, "recipientX"),
                    RecipientZ = Read(root, "recipientZ"),
                    SourceAngle = Read(root, "sourceAngle"),
                    RecipientAngle = Read(root, "recipientAngle")
                });
                MugirlLog.DevMessage("Milk feeding layout loaded from " + OverrideFilePath);
            }
            catch (Exception ex)
            {
                current = Default;
                MugirlLog.WarningOnce("MilkFeedingLayout.InvalidOverride",
                    "Invalid milk feeding layout; using defaults: " + ex.Message);
            }
        }

        private static float Read(XElement root, string name)
        {
            string value = root.Element(name)?.Value;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                || float.IsNaN(result) || float.IsInfinity(result))
                throw new InvalidDataException("Invalid " + name);
            return result;
        }

        private static MilkFeedingLayoutEntry Clamp(MilkFeedingLayoutEntry value)
        {
            value.SourceX = Mathf.Clamp(value.SourceX, -1f, 1f);
            value.SourceZ = Mathf.Clamp(value.SourceZ, -1f, 1f);
            value.RecipientX = Mathf.Clamp(value.RecipientX, -1f, 1f);
            value.RecipientZ = Mathf.Clamp(value.RecipientZ, -1f, 1f);
            value.SourceAngle = Mathf.Clamp(value.SourceAngle, -45f, 45f);
            value.RecipientAngle = Mathf.Clamp(value.RecipientAngle, -45f, 45f);
            return value;
        }

        private static string Number(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
