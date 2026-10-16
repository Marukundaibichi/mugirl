using System.Net;

namespace RenameApply.Xml;

// Span-preserving XML reader: every element, attribute value, text run and comment keeps its exact character range in the
// source, so edits are applied to the original text (formatting, entities, comments, encoding untouched).
public sealed class XElem
{
    public required string Name { get; init; }
    public required int NameStart { get; init; }
    public int EndNameStart { get; set; } = -1;      // -1 for self-closing elements
    public required int Start { get; init; }         // '<'
    public int End { get; set; }                     // after '>' of the end tag (or of the self-closing tag)
    public int ContentStart { get; set; }            // after the start tag
    public int ContentEnd { get; set; }              // before the end tag
    public XElem? Parent { get; init; }
    public List<XAttr> Attrs { get; } = new();
    public List<XElem> Children { get; } = new();
    public List<(int Start, int Length)> Texts { get; } = new();      // direct text runs (raw, entities not decoded)
    public List<(int Start, int Length)> CData { get; } = new();      // CDATA content spans

    public XAttr? Attr(string name) => Attrs.FirstOrDefault(a => a.Name == name);
    public bool IsLeaf => Children.Count == 0;
    public IEnumerable<XElem> Descendants() => Children.SelectMany(c => new[] { c }.Concat(c.Descendants()));

    public string Path
    {
        get
        {
            var parts = new List<string>();
            for (XElem? e = this; e != null; e = e.Parent)
            {
                parts.Insert(0, e.Name);
            }
            return string.Join("/", parts);
        }
    }
}

public sealed class XAttr
{
    public required string Name { get; init; }
    public required int NameStart { get; init; }
    public required int ValueStart { get; init; }
    public required int ValueLength { get; init; }
    public required string RawValue { get; init; }
    public string Value => WebUtility.HtmlDecode(RawValue);
}

public sealed class XDoc
{
    public required string Text { get; init; }
    public XElem? Root { get; set; }
    public List<(int Start, int Length)> Comments { get; } = new();   // content spans (without <!-- -->)

    public IEnumerable<XElem> AllElements() => Root == null ? Enumerable.Empty<XElem>() : new[] { Root }.Concat(Root.Descendants());

    // Leaf text: the trimmed text of an element without child elements (raw span of the trimmed part, decoded value).
    public (int Start, int Length, string Value)? LeafText(XElem e)
    {
        if (!e.IsLeaf || e.EndNameStart < 0 || e.CData.Count > 0)
        {
            return null;
        }
        var runs = e.Texts.Where(t => Text.AsSpan(t.Start, t.Length).Trim().Length > 0).ToList();
        if (runs.Count != 1)
        {
            return null;
        }
        var (s, l) = runs[0];
        string raw = Text.Substring(s, l);
        int lead = raw.Length - raw.TrimStart().Length;
        string trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }
        return (s + lead, trimmed.Length, WebUtility.HtmlDecode(trimmed));
    }

    public static XDoc Parse(string text)
    {
        var doc = new XDoc { Text = text };
        var stack = new Stack<XElem>();
        int i = 0, n = text.Length;
        while (i < n)
        {
            if (text[i] != '<')
            {
                int j = text.IndexOf('<', i);
                if (j < 0)
                {
                    j = n;
                }
                if (stack.Count > 0)
                {
                    stack.Peek().Texts.Add((i, j - i));
                }
                i = j;
                continue;
            }
            if (Starts(text, i, "<!--"))
            {
                int j = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (j < 0)
                {
                    throw new FormatException($"unterminated comment at {i}");
                }
                doc.Comments.Add((i + 4, j - i - 4));
                i = j + 3;
                continue;
            }
            if (Starts(text, i, "<![CDATA["))
            {
                int j = text.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                if (j < 0)
                {
                    throw new FormatException($"unterminated CDATA at {i}");
                }
                if (stack.Count > 0)
                {
                    stack.Peek().CData.Add((i + 9, j - i - 9));
                }
                i = j + 3;
                continue;
            }
            if (Starts(text, i, "<?"))
            {
                int j = text.IndexOf("?>", i + 2, StringComparison.Ordinal);
                if (j < 0)
                {
                    throw new FormatException($"unterminated processing instruction at {i}");
                }
                i = j + 2;
                continue;
            }
            if (Starts(text, i, "<!"))
            {
                int j = text.IndexOf('>', i + 2);
                if (j < 0 || text.IndexOf('[', i, j - i) >= 0)
                {
                    throw new FormatException($"unsupported DOCTYPE at {i}");
                }
                i = j + 1;
                continue;
            }
            if (Starts(text, i, "</"))
            {
                int ns = i + 2;
                int ne = ns;
                while (ne < n && !char.IsWhiteSpace(text[ne]) && text[ne] != '>')
                {
                    ne++;
                }
                string name = text.Substring(ns, ne - ns);
                int gt = text.IndexOf('>', ne);
                if (gt < 0 || stack.Count == 0 || stack.Peek().Name != name)
                {
                    throw new FormatException($"mismatched end tag </{name}> at {i}");
                }
                XElem e = stack.Pop();
                e.EndNameStart = ns;
                e.ContentEnd = i;
                e.End = gt + 1;
                i = gt + 1;
                continue;
            }
            // Start tag.
            {
                int ns = i + 1;
                int ne = ns;
                while (ne < n && !char.IsWhiteSpace(text[ne]) && text[ne] != '>' && text[ne] != '/')
                {
                    ne++;
                }
                var e = new XElem { Name = text.Substring(ns, ne - ns), NameStart = ns, Start = i, Parent = stack.Count > 0 ? stack.Peek() : null };
                int k = ne;
                bool selfClosing = false;
                while (true)
                {
                    while (k < n && char.IsWhiteSpace(text[k]))
                    {
                        k++;
                    }
                    if (k >= n)
                    {
                        throw new FormatException($"unterminated start tag <{e.Name}> at {i}");
                    }
                    if (text[k] == '>')
                    {
                        k++;
                        break;
                    }
                    if (text[k] == '/' && k + 1 < n && text[k + 1] == '>')
                    {
                        selfClosing = true;
                        k += 2;
                        break;
                    }
                    int an = k;
                    while (k < n && text[k] != '=' && !char.IsWhiteSpace(text[k]) && text[k] != '>' && text[k] != '/')
                    {
                        k++;
                    }
                    string attrName = text.Substring(an, k - an);
                    while (k < n && char.IsWhiteSpace(text[k]))
                    {
                        k++;
                    }
                    if (k >= n || text[k] != '=')
                    {
                        throw new FormatException($"attribute without value in <{e.Name}> at {an}");
                    }
                    k++;
                    while (k < n && char.IsWhiteSpace(text[k]))
                    {
                        k++;
                    }
                    char q = text[k];
                    if (q != '"' && q != '\'')
                    {
                        throw new FormatException($"unquoted attribute value in <{e.Name}> at {k}");
                    }
                    int vs = k + 1;
                    int ve = text.IndexOf(q, vs);
                    if (ve < 0)
                    {
                        throw new FormatException($"unterminated attribute value in <{e.Name}> at {vs}");
                    }
                    e.Attrs.Add(new XAttr { Name = attrName, NameStart = an, ValueStart = vs, ValueLength = ve - vs, RawValue = text.Substring(vs, ve - vs) });
                    k = ve + 1;
                }
                e.ContentStart = k;
                if (stack.Count > 0)
                {
                    stack.Peek().Children.Add(e);
                }
                else if (doc.Root == null)
                {
                    doc.Root = e;
                }
                else
                {
                    throw new FormatException("several root elements");
                }
                if (selfClosing)
                {
                    e.End = k;
                    e.ContentEnd = k;
                }
                else
                {
                    stack.Push(e);
                }
                i = k;
            }
        }
        if (stack.Count > 0)
        {
            throw new FormatException($"unclosed element <{stack.Peek().Name}>");
        }
        return doc;
    }

    private static bool Starts(string text, int i, string s) => string.CompareOrdinal(text, i, s, 0, s.Length) == 0;
}
