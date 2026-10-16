using System.Text;
using System.Text.RegularExpressions;

namespace RenameApply;

public sealed class ToolException(string message) : Exception(message);

// Repository-relative paths always use '/', never a leading slash, and keep the on-disk casing.
public sealed class RepoPaths
{
    public string Root { get; }

    public RepoPaths(string root)
    {
        Root = Path.GetFullPath(root).TrimEnd('\\', '/');
    }

    public string Full(string rel) => Path.GetFullPath(Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar)));

    public string Rel(string full)
    {
        string f = Path.GetFullPath(full);
        if (!f.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return f.Replace('\\', '/');
        }
        return f.Substring(Root.Length + 1).Replace('\\', '/');
    }

    public bool IsInside(string full) => Path.GetFullPath(full).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static string Norm(string rel) => rel.Replace('\\', '/').TrimStart('/');

    private static readonly string[] FrozenPrefixes =
    {
        "1.6/", "Bio_1.6/", "Odyssey_1.6/", "Versions/1.6/", "DevData/", "docs/", ".git/",
    };

    // Owner directive: never edited by this tool (docs are only reported).
    public static bool IsFrozen(string rel)
    {
        rel = Norm(rel);
        if (rel.Equals("LoadFolders.xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        foreach (string p in FrozenPrefixes)
        {
            if (rel.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return HasSegment(rel, "bin") || HasSegment(rel, "obj");
    }

    public static bool HasSegment(string rel, string segment) =>
        Norm(rel).Split('/').Any(s => s.Equals(segment, StringComparison.OrdinalIgnoreCase));

    // The only places the tool writes to.
    public static bool IsEditable(string rel)
    {
        rel = Norm(rel);
        if (IsFrozen(rel))
        {
            return false;
        }
        if (rel.StartsWith("Source/Tests/MugirlRace.Validation/Baselines/legacy/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (rel.StartsWith("Source/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("1.7/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return rel.StartsWith("tools/", StringComparison.OrdinalIgnoreCase) && rel.Count(c => c == '/') == 1 && rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }
}

// Glob with '**' (any depth), '*' (within a segment) and '?'. Case-insensitive, '/' separators.
public static class Glob
{
    private static readonly Dictionary<string, Regex> Cache = new();

    public static bool IsMatch(string pattern, string rel)
    {
        if (!Cache.TryGetValue(pattern, out Regex? rx))
        {
            var sb = new StringBuilder("^");
            string p = RepoPaths.Norm(pattern);
            for (int i = 0; i < p.Length; i++)
            {
                char c = p[i];
                if (c == '*' && i + 1 < p.Length && p[i + 1] == '*')
                {
                    sb.Append(".*");
                    i++;
                    if (i + 1 < p.Length && p[i + 1] == '/')
                    {
                        i++;
                        sb.Append("/?");
                    }
                }
                else if (c == '*')
                {
                    sb.Append("[^/]*");
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }
            sb.Append('$');
            rx = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Cache[pattern] = rx;
        }
        return rx.IsMatch(RepoPaths.Norm(rel));
    }
}

// A text file as read from disk: decoded strictly as UTF-8, BOM state and dominant line ending remembered.
public sealed class TextFileData
{
    public required string Text { get; init; }
    public required bool Bom { get; init; }
    public required string NewLine { get; init; }

    private static readonly UTF8Encoding Strict = new(false, true);

    public static TextFileData Read(string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text;
        try
        {
            text = Strict.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            throw new ToolException($"not valid UTF-8: {fullPath}");
        }
        return new TextFileData { Text = text, Bom = bom, NewLine = DetectNewLine(text) };
    }

    public static string DetectNewLine(string text)
    {
        int crlf = 0, lf = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                if (i > 0 && text[i - 1] == '\r')
                {
                    crlf++;
                }
                else
                {
                    lf++;
                }
            }
        }
        return lf > crlf ? "\n" : "\r\n";
    }

    // Spec texts are written with LF; files may use CRLF.
    public static string ToNewLine(string text, string nl) => text.Replace("\r\n", "\n").Replace("\n", nl);

    public static void Write(string fullPath, string text, bool bom)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        byte[] body = new UTF8Encoding(false).GetBytes(text);
        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        if (bom)
        {
            fs.Write(new byte[] { 0xEF, 0xBB, 0xBF });
        }
        fs.Write(body);
    }
}

public sealed record TextEdit(int Start, int Length, string NewText, string Kind, string? Note = null)
{
    public int End => Start + Length;
}

public static class TextEdits
{
    // Sorts, removes exact duplicates and rejects overlaps (two different edits touching the same characters).
    public static List<TextEdit> Normalize(IEnumerable<TextEdit> edits, string fileForErrors)
    {
        var list = edits
            .GroupBy(e => (e.Start, e.Length, e.NewText))
            .Select(g => g.First())
            .OrderBy(e => e.Start).ThenBy(e => e.Length)
            .ToList();
        for (int i = 1; i < list.Count; i++)
        {
            TextEdit a = list[i - 1], b = list[i];
            bool overlap = b.Start < a.End || (b.Start == a.Start && a.Length == 0 && b.Length == 0 && a.NewText != b.NewText);
            if (overlap)
            {
                // Two insertions at the same point are concatenated deterministically; real overlaps are fatal.
                if (a.Length == 0 && b.Length == 0)
                {
                    list[i - 1] = a with { NewText = a.NewText + b.NewText };
                    list.RemoveAt(i);
                    i--;
                    continue;
                }
                throw new ToolException($"{fileForErrors}: overlapping edits [{a.Start},{a.End}) '{a.Kind}' and [{b.Start},{b.End}) '{b.Kind}'");
            }
        }
        return list;
    }

    public static string Apply(string text, IReadOnlyList<TextEdit> sorted)
    {
        var sb = new StringBuilder(text.Length + 256);
        int pos = 0;
        foreach (TextEdit e in sorted)
        {
            sb.Append(text, pos, e.Start - pos);
            sb.Append(e.NewText);
            pos = e.End;
        }
        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }

    // Position in the edited text of an original position that is not inside an edit.
    public static int MapPosition(IReadOnlyList<TextEdit> sorted, int oldPos)
    {
        int delta = 0;
        foreach (TextEdit e in sorted)
        {
            if (e.End <= oldPos && !(e.Length == 0 && e.Start == oldPos))
            {
                delta += e.NewText.Length - e.Length;
            }
            else if (e.Length == 0 && e.Start == oldPos)
            {
                // Insertions at exactly this point come before it.
                delta += e.NewText.Length;
            }
            else
            {
                break;
            }
        }
        return oldPos + delta;
    }
}

public sealed class LineIndex
{
    private readonly List<int> _starts = new() { 0 };
    private readonly string _text;

    public LineIndex(string text)
    {
        _text = text;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                _starts.Add(i + 1);
            }
        }
    }

    // 1-based line and column.
    public (int Line, int Col) Get(int pos)
    {
        int idx = _starts.BinarySearch(pos);
        if (idx < 0)
        {
            idx = ~idx - 1;
        }
        return (idx + 1, pos - _starts[idx] + 1);
    }

    public string LineText(int line)
    {
        int s = _starts[line - 1];
        int e = line < _starts.Count ? _starts[line] : _text.Length;
        return _text.Substring(s, e - s).TrimEnd('\r', '\n');
    }

    public int LineStart(int line) => _starts[line - 1];
}

// Token-bounded search/replace helpers shared by every pass.
public static class Tokens
{
    public static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';

    // Old full type name (dotted, may contain '+' or '`'): not preceded by an identifier char or '.', not followed by an identifier
    // char or a backtick. A following '.', '+', '/', ':' etc. is allowed (member access, nested type, IL syntax).
    public static Regex TypeName(string fullName) =>
        new(@"(?<![\w.`])" + Regex.Escape(fullName) + @"(?![\w`])", RegexOptions.CultureInvariant);

    // Identifier-like names (defNames, abstract Names, short type names): bounded by non-identifier characters.
    public static Regex Ident(string name) =>
        new(@"(?<![\w])" + Regex.Escape(name) + @"(?![\w])", RegexOptions.CultureInvariant);

    // Keyed keys contain dots; "A.B" must not match inside "A.B.C" or "X.A.B".
    public static Regex Key(string key) =>
        new(@"(?<![\w.])" + Regex.Escape(key) + @"(?![\w]|\.\w)", RegexOptions.CultureInvariant);
}
