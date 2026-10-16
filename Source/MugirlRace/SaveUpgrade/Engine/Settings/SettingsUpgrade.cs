using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// Mod 设置文件 <c>Config/Mod_&lt;文件夹名&gt;_MugirlMod.xml</c> 的升级：在 Mod 构造函数中、<c>GetSettings</c> 之前运行。
/// </summary>
/// <remarks>
/// <para>
/// 文件名公式与 <c>LoadedModManager.GetSettingsFilename</c> 相同（1.7.4977 反编译）：
/// <c>Path.Combine(GenFilePaths.ConfigFolderPath, GenText.SanitizeFilename($"Mod_{Content.FolderName}_{Mod 类短名}.xml"))</c>；
/// 本地文件夹与创意工坊文件夹（数字 id）各有一个文件。内容为 <c>&lt;SettingsBlock&gt;&lt;ModSettings Class="…"&gt;</c>，标签直接写在里面，没有 meta。
/// </para>
/// <para>
/// 按 <c>settingsSchema</c> 版本升级：文件版本低于 <see cref="SaveSchema.SettingsCurrent"/> 时，依次应用版本更高的行（Class 改名、
/// 标签改名或删除）；有行改动了文件时写入当前版本号，先写临时文件再替换，只写一次。没有任何行改动时不写文件：
/// 版本号由设置类自己保存时写入。冻结的 1.6 构建与 1.7 共用同一个设置文件：1.6 写回时会去掉版本号与新标签，
/// 下次 1.7 启动再升级一次；1.6 每次启动对改名后的设置类报一次“找不到类”，改名的设置项回到默认值（已记录的已知后果）。
/// MooGirl 时代的 <c>Mod_*_MooGirlMod.xml</c> 不迁移。
/// </para>
/// </remarks>
internal static class SettingsUpgrade
{
    /// <summary><paramref name="mod"/> 的设置文件路径。</summary>
    internal static string FilePathFor(Mod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        return Path.Combine(GenFilePaths.ConfigFolderPath, GenText.SanitizeFilename($"Mod_{mod.Content.FolderName}_{mod.GetType().Name}.xml"));
    }

    /// <summary>升级 <paramref name="path"/> 处的设置文件。IO 异常由调用方处理。</summary>
    internal static SettingsUpgradeResult Run(string path, UpgradeTables tables)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(tables);
        if (tables.SettingsRows.Count == 0)
        {
            return SettingsUpgradeResult.NoTarget;
        }

        if (!File.Exists(path))
        {
            return SettingsUpgradeResult.NoFile;
        }

        var document = new XmlDocument { XmlResolver = null };
        try
        {
            document.Load(path);
        }
        catch (XmlException)
        {
            return SettingsUpgradeResult.Unreadable;
        }

        if (document.DocumentElement is not { Name: "SettingsBlock" } || document.DocumentElement["ModSettings"] is null)
        {
            return SettingsUpgradeResult.Unreadable;
        }

        if (!Upgrade(document, tables))
        {
            return SettingsUpgradeResult.UpToDate;
        }

        Write(document, path);
        return SettingsUpgradeResult.Upgraded;
    }

    /// <summary>就地升级设置文档；没有任何行改动文档时返回 false（文档不变）。</summary>
    internal static bool Upgrade(XmlDocument document, UpgradeTables tables)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(tables);
        if (document.DocumentElement?["ModSettings"] is not { } settings)
        {
            return false;
        }

        int fileSchema = ReadSchema(settings);
        if (fileSchema >= SaveSchema.SettingsCurrent)
        {
            return false;
        }

        bool changed = false;
        foreach (SettingsRow row in tables.SettingsRows.Where(row => row.Schema > fileSchema).OrderBy(row => row.Schema))
        {
            switch (row.Kind)
            {
                case SettingsRowKind.ClassRename:
                    if (string.Equals(settings.GetAttribute("Class"), row.Old, StringComparison.OrdinalIgnoreCase))
                    {
                        settings.SetAttribute("Class", row.New);
                        changed = true;
                    }

                    break;
                case SettingsRowKind.LabelRename:
                    if (settings[row.Old] is { } old)
                    {
                        if (settings[row.New!] is null)
                        {
                            Rename(old, row.New!);
                        }
                        else
                        {
                            settings.RemoveChild(old);
                        }

                        changed = true;
                    }

                    break;
                case SettingsRowKind.LabelDrop:
                    if (settings[row.Old] is { } dropped)
                    {
                        settings.RemoveChild(dropped);
                        changed = true;
                    }

                    break;
                default:
                    break;
            }
        }

        if (!changed)
        {
            return false;
        }

        string target = SaveSchema.SettingsCurrent.ToString(CultureInfo.InvariantCulture);
        if (settings[SaveSchema.SettingsStampLabel] is { } stamp)
        {
            stamp.InnerText = target;
        }
        else
        {
            XmlElement created = document.CreateElement(SaveSchema.SettingsStampLabel);
            created.InnerText = target;
            settings.AppendChild(created);
        }

        return true;
    }

    private static int ReadSchema(XmlElement settings) =>
        settings[SaveSchema.SettingsStampLabel] is { } stamp
        && int.TryParse(stamp.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;

    private static void Rename(XmlElement element, string name)
    {
        XmlElement created = element.OwnerDocument.CreateElement(name);
        foreach (XmlAttribute attribute in element.Attributes)
        {
            created.SetAttribute(attribute.Name, attribute.Value);
        }

        while (element.FirstChild is { } child)
        {
            created.AppendChild(child);
        }

        element.ParentNode!.ReplaceChild(created, element);
    }

    private static void Write(XmlDocument document, string path)
    {
        string temporary = path + ".mugirl-upgrade.tmp";
        var settings = new XmlWriterSettings { Indent = true, IndentChars = "\t", Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
        using (var writer = XmlWriter.Create(temporary, settings))
        {
            document.Save(writer);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
