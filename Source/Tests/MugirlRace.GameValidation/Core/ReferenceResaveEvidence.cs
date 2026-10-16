using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>零 tick 旧行为校准的文件与时间边界；不转换存档数据。</summary>
internal static class ReferenceResaveEvidence
{
    public static int Tick(XDocument document)
    {
        XElement game = document.Root?.Name == "savegame" && document.Root.Elements("game").Count() == 1
            ? document.Root.Element("game")! : throw new InvalidOperationException("Reference input needs exactly one savegame/game.");
        XElement[] managers = [.. game.Elements("tickManager")];
        XElement[] ticks = managers.Length == 1 ? [.. managers[0].Elements("ticksGame")] : [];
        if (ticks.Length != 1 || ticks[0].HasAttributes || ticks[0].HasElements
            || !int.TryParse(ticks[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick))
        {
            throw new InvalidOperationException("Reference input needs one unambiguous ticksGame integer.");
        }
        return tick;
    }

    public static void RequireDifferentFiles(string input, string output)
    {
        if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Reference resave must never overwrite its source input.");
        }
    }

    public static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static JsonObject FileIdentity(string path)
        => new() { ["path"] = Path.GetFullPath(path), ["bytes"] = new FileInfo(path).Length, ["sha256"] = Sha256(path) };
}
