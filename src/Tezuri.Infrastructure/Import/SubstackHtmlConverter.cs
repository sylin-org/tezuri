using System.Net;
using System.Text;
using Tezuri.Domain.Import;

namespace Tezuri.Infrastructure.Import;

internal sealed class SubstackHtmlConverter
{
    private static readonly HashSet<string> RemovedContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "form", "noscript", "template", "svg", "math"
    };

    private static readonly string[] PlatformChromeMarkers =
    [
        "subscription-widget",
        "subscribe-widget",
        "recommendation-widget",
        "post-footer",
        "publication-nav"
    ];

    private static readonly HashSet<string> KnownTransparentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "html", "body", "main", "article", "section", "span", "picture", "source"
    };

    public IReadOnlyList<HtmlImageReference> InspectImages(string html)
    {
        var tokens = Tokenize(html);
        var result = new List<HtmlImageReference>();
        var skip = new Stack<string>();
        foreach (var token in tokens)
        {
            if (HandleSkip(token, skip, transformations: null))
            {
                continue;
            }

            if (!token.IsEnd && token.Name == "img" && token.Attributes.TryGetValue("src", out var source))
            {
                result.Add(new HtmlImageReference(
                    result.Count,
                    source,
                    token.Attributes.GetValueOrDefault("alt"),
                    token.Attributes.GetValueOrDefault("title"),
                    $"html@{token.Offset}"));
            }
        }

        return result;
    }

    public HtmlConversionResult Convert(
        string html,
        IReadOnlyDictionary<int, HtmlImageResolution> imageResolutions)
    {
        var tokens = Tokenize(html);
        var output = new StringBuilder(html.Length);
        var transformations = new List<ImportTransformationV1>();
        var warnings = new List<ImportWarningV1>();
        var transformationKinds = new HashSet<string>(StringComparer.Ordinal);
        var warningKeys = new HashSet<string>(StringComparer.Ordinal);
        var skip = new Stack<string>();
        var links = new Stack<string?>();
        var lists = new Stack<ListState>();
        var inPre = false;
        var blockquoteDepth = 0;
        var imageIndex = 0;
        var table = new TableState();

        foreach (var token in tokens)
        {
            if (HandleSkip(token, skip, transformations, transformationKinds))
            {
                continue;
            }

            if (token.IsText)
            {
                AppendText(output, token.Text!, inPre, blockquoteDepth, warnings, warningKeys, token.Offset);
                continue;
            }

            if (token.Attributes.Keys.Any(key => key.StartsWith("on", StringComparison.OrdinalIgnoreCase)))
            {
                AddTransformation(
                    transformations,
                    transformationKinds,
                    "removed-event-handler",
                    "Removed executable HTML event-handler attributes.",
                    $"html@{token.Offset}");
            }

            var name = token.Name;
            if (!token.IsEnd)
            {
                switch (name)
                {
                    case "p" or "div" or "section" or "article":
                        EnsureBlankLine(output);
                        break;
                    case "br":
                        AppendLineBreak(output, blockquoteDepth);
                        break;
                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        EnsureBlankLine(output);
                        output.Append(new string('#', name[1] - '0')).Append(' ');
                        break;
                    case "strong" or "b":
                        output.Append("**");
                        break;
                    case "em" or "i":
                        output.Append('*');
                        break;
                    case "del" or "s" or "strike":
                        output.Append("~~");
                        break;
                    case "code" when !inPre:
                        output.Append('`');
                        break;
                    case "pre":
                        EnsureBlankLine(output);
                        output.Append("```\n");
                        inPre = true;
                        break;
                    case "blockquote":
                        EnsureBlankLine(output);
                        blockquoteDepth++;
                        AppendQuotePrefix(output, blockquoteDepth);
                        break;
                    case "ul":
                        lists.Push(new ListState(Ordered: false, Next: 1));
                        EnsureLine(output);
                        break;
                    case "ol":
                        lists.Push(new ListState(Ordered: true, Next: ParsePositive(token.Attributes.GetValueOrDefault("start"))));
                        EnsureLine(output);
                        break;
                    case "li":
                        EnsureLine(output);
                        if (lists.TryPeek(out var list))
                        {
                            output.Append(new string(' ', Math.Max(0, lists.Count - 1) * 2));
                            if (list.Ordered)
                            {
                                output.Append(list.Next).Append(". ");
                                lists.Pop();
                                lists.Push(list with { Next = list.Next + 1 });
                            }
                            else
                            {
                                output.Append("- ");
                            }
                        }
                        else
                        {
                            output.Append("- ");
                            AddWarning(
                                warnings,
                                warningKeys,
                                "orphan-list-item",
                                "warning",
                                "Converted a list item without a containing list.",
                                $"html@{token.Offset}");
                        }

                        break;
                    case "a":
                        var href = SanitizeLink(token.Attributes.GetValueOrDefault("href"));
                        links.Push(href);
                        if (href is not null)
                        {
                            output.Append('[');
                        }
                        else if (token.Attributes.ContainsKey("href"))
                        {
                            AddWarning(
                                warnings,
                                warningKeys,
                                "unsafe-link-removed",
                                "warning",
                                "Removed a link target with an unsafe or unsupported URI scheme.",
                                $"html@{token.Offset}");
                        }

                        break;
                    case "img":
                        imageResolutions.TryGetValue(imageIndex, out var resolution);
                        AppendImage(output, token, resolution, blockquoteDepth);
                        imageIndex++;
                        break;
                    case "hr":
                        EnsureBlankLine(output);
                        output.Append("---");
                        EnsureBlankLine(output);
                        break;
                    case "iframe" or "embed":
                        var embed = SanitizeLink(token.Attributes.GetValueOrDefault("src"));
                        if (embed is not null)
                        {
                            EnsureBlankLine(output);
                            output.Append("[Embedded content](").Append(EscapeLinkDestination(embed)).Append(')');
                            EnsureBlankLine(output);
                        }

                        AddTransformation(
                            transformations,
                            transformationKinds,
                            "external-embed-to-link",
                            "Converted executable embedded content into a non-executing link.",
                            $"html@{token.Offset}");
                        break;
                    case "figure":
                        EnsureBlankLine(output);
                        break;
                    case "figcaption":
                        EnsureLine(output);
                        output.Append('*');
                        break;
                    case "table":
                        EnsureBlankLine(output);
                        table = new TableState();
                        break;
                    case "tr":
                        EnsureLine(output);
                        table.InRow = true;
                        table.Cells = 0;
                        table.HeaderRow = false;
                        output.Append('|');
                        break;
                    case "th":
                        table.HeaderRow = true;
                        table.Cells++;
                        output.Append(' ');
                        break;
                    case "td":
                        table.Cells++;
                        output.Append(' ');
                        break;
                    default:
                        if (!KnownTransparentTags.Contains(name))
                        {
                            AddWarning(
                                warnings,
                                warningKeys,
                                "unsupported-html-tag",
                                "warning",
                                $"Preserved the text content of unsupported <{name}> markup but removed the tag.",
                                $"html@{token.Offset}");
                        }

                        break;
                }
            }
            else
            {
                switch (name)
                {
                    case "p" or "div" or "section" or "article" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        EnsureBlankLine(output);
                        break;
                    case "strong" or "b":
                        output.Append("**");
                        break;
                    case "em" or "i":
                        output.Append('*');
                        break;
                    case "del" or "s" or "strike":
                        output.Append("~~");
                        break;
                    case "code" when !inPre:
                        output.Append('`');
                        break;
                    case "pre":
                        TrimLineEnd(output);
                        output.Append("\n```\n\n");
                        inPre = false;
                        break;
                    case "blockquote":
                        blockquoteDepth = Math.Max(0, blockquoteDepth - 1);
                        EnsureBlankLine(output);
                        break;
                    case "ul" or "ol":
                        if (lists.Count > 0)
                        {
                            lists.Pop();
                        }

                        EnsureLine(output);
                        break;
                    case "li":
                        EnsureLine(output);
                        break;
                    case "a":
                        if (links.TryPop(out var href) && href is not null)
                        {
                            output.Append("](").Append(EscapeLinkDestination(href)).Append(')');
                        }

                        break;
                    case "figcaption":
                        output.Append('*');
                        EnsureBlankLine(output);
                        break;
                    case "th" or "td":
                        TrimSpaces(output);
                        output.Append(" |");
                        break;
                    case "tr":
                        EnsureLine(output);
                        if (table.HeaderRow && table.Cells > 0 && !table.HeaderWritten)
                        {
                            output.Append('|');
                            for (var index = 0; index < table.Cells; index++)
                            {
                                output.Append(" --- |");
                            }

                            EnsureLine(output);
                            table.HeaderWritten = true;
                        }

                        table.InRow = false;
                        break;
                    case "table":
                        EnsureBlankLine(output);
                        break;
                }
            }
        }

        if (skip.Count > 0 || inPre || links.Count > 0 || lists.Count > 0 || blockquoteDepth > 0)
        {
            throw new SubstackImportException(
                SubstackImportFailure.MalformedExport,
                "The exported article contains unclosed structural HTML markup.");
        }

        var markdown = NormalizeOutput(output.ToString());
        return new HtmlConversionResult(markdown, transformations, warnings);
    }

    private static IReadOnlyList<HtmlToken> Tokenize(string html)
    {
        var tokens = new List<HtmlToken>();
        var textStart = 0;
        var index = 0;
        while (index < html.Length)
        {
            if (html[index] != '<' ||
                index + 1 >= html.Length ||
                !(char.IsAsciiLetter(html[index + 1]) || html[index + 1] is '/' or '!' or '?'))
            {
                index++;
                continue;
            }

            if (index > textStart)
            {
                tokens.Add(HtmlToken.TextToken(html[textStart..index], textStart));
            }

            if (html.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                var commentEnd = html.IndexOf("-->", index + 4, StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    throw Malformed("The exported article contains an unclosed HTML comment.");
                }

                index = commentEnd + 3;
                textStart = index;
                continue;
            }

            var end = FindTagEnd(html, index + 1);
            if (end < 0)
            {
                throw Malformed("The exported article contains an unclosed HTML tag.");
            }

            var raw = html[(index + 1)..end].Trim();
            if (raw.StartsWith('!') || raw.StartsWith('?'))
            {
                index = end + 1;
                textStart = index;
                continue;
            }

            tokens.Add(ParseTag(raw, index));
            index = end + 1;
            textStart = index;
        }

        if (textStart < html.Length)
        {
            tokens.Add(HtmlToken.TextToken(html[textStart..], textStart));
        }

        return tokens;
    }

    private static int FindTagEnd(string html, int start)
    {
        char quote = '\0';
        for (var index = start; index < html.Length; index++)
        {
            var character = html[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static HtmlToken ParseTag(string raw, int offset)
    {
        var cursor = 0;
        var isEnd = raw.StartsWith('/');
        if (isEnd)
        {
            cursor++;
        }

        SkipSpaces(raw, ref cursor);
        var nameStart = cursor;
        while (cursor < raw.Length && (char.IsAsciiLetterOrDigit(raw[cursor]) || raw[cursor] is '-' or ':'))
        {
            cursor++;
        }

        if (cursor == nameStart)
        {
            throw Malformed($"The exported article contains an invalid HTML tag at character {offset}.");
        }

        var name = raw[nameStart..cursor].ToLowerInvariant();
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (!isEnd && cursor < raw.Length)
        {
            SkipSpaces(raw, ref cursor);
            if (cursor >= raw.Length || raw[cursor] == '/')
            {
                break;
            }

            var attributeStart = cursor;
            while (cursor < raw.Length &&
                   (char.IsAsciiLetterOrDigit(raw[cursor]) || raw[cursor] is '-' or '_' or ':' or '.'))
            {
                cursor++;
            }

            if (cursor == attributeStart)
            {
                throw Malformed($"The exported article contains an invalid attribute at character {offset + cursor}.");
            }

            var attributeName = raw[attributeStart..cursor].ToLowerInvariant();
            SkipSpaces(raw, ref cursor);
            var value = string.Empty;
            if (cursor < raw.Length && raw[cursor] == '=')
            {
                cursor++;
                SkipSpaces(raw, ref cursor);
                if (cursor >= raw.Length)
                {
                    throw Malformed($"HTML attribute '{attributeName}' has no value.");
                }

                if (raw[cursor] is '"' or '\'')
                {
                    var quote = raw[cursor++];
                    var valueStart = cursor;
                    while (cursor < raw.Length && raw[cursor] != quote)
                    {
                        cursor++;
                    }

                    if (cursor >= raw.Length)
                    {
                        throw Malformed($"HTML attribute '{attributeName}' has an unclosed quote.");
                    }

                    value = raw[valueStart..cursor++];
                }
                else
                {
                    var valueStart = cursor;
                    while (cursor < raw.Length && !char.IsWhiteSpace(raw[cursor]))
                    {
                        cursor++;
                    }

                    value = raw[valueStart..cursor];
                    if (cursor == raw.Length && value.EndsWith("/", StringComparison.Ordinal))
                    {
                        value = value[..^1];
                    }
                }
            }

            attributes.TryAdd(attributeName, WebUtility.HtmlDecode(value));
        }

        return new HtmlToken(name, isEnd, Text: null, attributes, offset);
    }

    private static bool HandleSkip(
        HtmlToken token,
        Stack<string> skip,
        ICollection<ImportTransformationV1>? transformations,
        ISet<string>? transformationKinds = null)
    {
        if (token.IsText)
        {
            return skip.Count > 0;
        }

        if (skip.Count > 0)
        {
            if (!token.IsEnd && StringComparer.OrdinalIgnoreCase.Equals(token.Name, skip.Peek()))
            {
                skip.Push(token.Name);
            }
            else if (token.IsEnd && StringComparer.OrdinalIgnoreCase.Equals(token.Name, skip.Peek()))
            {
                skip.Pop();
            }

            return true;
        }

        if (token.IsEnd)
        {
            return false;
        }

        var chrome = token.Attributes
            .Where(pair => pair.Key is "class" or "id")
            .Select(pair => pair.Value)
            .Any(value => PlatformChromeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        if (!RemovedContentTags.Contains(token.Name) && !chrome)
        {
            return false;
        }

        if (transformations is not null && transformationKinds is not null)
        {
            AddTransformation(
                transformations,
                transformationKinds,
                chrome ? "removed-platform-chrome" : $"removed-{token.Name}",
                chrome
                    ? "Removed Substack subscription, recommendation, or navigation chrome."
                    : $"Removed non-executing import of <{token.Name}> content.",
                $"html@{token.Offset}");
        }

        skip.Push(token.Name);
        return true;
    }

    private static void AppendText(
        StringBuilder output,
        string raw,
        bool inPre,
        int blockquoteDepth,
        ICollection<ImportWarningV1> warnings,
        ISet<string> warningKeys,
        int offset)
    {
        var decoded = WebUtility.HtmlDecode(raw);
        if (decoded.Contains('\uFFFD') && !raw.Contains('\uFFFD'))
        {
            AddWarning(
                warnings,
                warningKeys,
                "invalid-html-entity",
                "error",
                "An invalid HTML character reference decoded to a replacement character.",
                $"html@{offset}");
        }

        if (inPre)
        {
            output.Append(decoded.Replace("```", "``\\`", StringComparison.Ordinal));
            return;
        }

        var pendingSpace = false;
        foreach (var character in decoded)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && output.Length > 0 && output[^1] is not ' ' and not '\n')
            {
                output.Append(' ');
            }

            if (output.Length == 0 || output[^1] == '\n')
            {
                AppendQuotePrefix(output, blockquoteDepth);
            }

            AppendEscapedTextCharacter(output, character);
            pendingSpace = false;
        }
    }

    private static void AppendEscapedTextCharacter(StringBuilder output, char character)
    {
        if (character is '\\' or '*' or '_' or '[' or ']' or '`')
        {
            output.Append('\\');
        }

        output.Append(character);
    }

    private static void AppendImage(
        StringBuilder output,
        HtmlToken token,
        HtmlImageResolution? resolution,
        int blockquoteDepth)
    {
        if (output.Length == 0 || output[^1] == '\n')
        {
            AppendQuotePrefix(output, blockquoteDepth);
        }

        var alt = EscapeInline(token.Attributes.GetValueOrDefault("alt") ?? "Imported image");
        if (resolution?.MarkdownPath is not null)
        {
            output.Append("![").Append(alt).Append("](")
                .Append(EscapeLinkDestination(resolution.MarkdownPath));
            var title = token.Attributes.GetValueOrDefault("title");
            if (!string.IsNullOrWhiteSpace(title))
            {
                output.Append(" \"").Append(title.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }

            output.Append(')');
            return;
        }

        output.Append("*[Image requires local import review: ").Append(alt).Append("]*");
    }

    private static string? SanitizeLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme is "http" or "https" or "mailto" ? absolute.AbsoluteUri : null;
        }

        if (value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        return value;
    }

    private static string EscapeInline(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("]", "\\]", StringComparison.Ordinal)
        .Replace("*", "\\*", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeLinkDestination(string value) =>
        value.Replace(" ", "%20", StringComparison.Ordinal)
            .Replace("(", "%28", StringComparison.Ordinal)
            .Replace(")", "%29", StringComparison.Ordinal);

    private static string NormalizeOutput(string value)
    {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var compact = new List<string>(lines.Count);
        var blank = false;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (!blank)
                {
                    compact.Add(string.Empty);
                }

                blank = true;
            }
            else
            {
                compact.Add(line);
                blank = false;
            }
        }

        return string.Join('\n', compact) + "\n";
    }

    private static void EnsureLine(StringBuilder output)
    {
        TrimSpaces(output);
        if (output.Length > 0 && output[^1] != '\n')
        {
            output.Append('\n');
        }
    }

    private static void EnsureBlankLine(StringBuilder output)
    {
        TrimSpaces(output);
        if (output.Length == 0)
        {
            return;
        }

        if (output[^1] != '\n')
        {
            output.Append('\n');
        }

        if (output.Length < 2 || output[^2] != '\n')
        {
            output.Append('\n');
        }
    }

    private static void AppendLineBreak(StringBuilder output, int blockquoteDepth)
    {
        TrimSpaces(output);
        output.Append("  \n");
        AppendQuotePrefix(output, blockquoteDepth);
    }

    private static void AppendQuotePrefix(StringBuilder output, int depth)
    {
        for (var index = 0; index < depth; index++)
        {
            output.Append("> ");
        }
    }

    private static void TrimSpaces(StringBuilder output)
    {
        while (output.Length > 0 && output[^1] is ' ' or '\t')
        {
            output.Length--;
        }
    }

    private static void TrimLineEnd(StringBuilder output)
    {
        TrimSpaces(output);
        while (output.Length > 0 && output[^1] == '\n')
        {
            output.Length--;
        }
    }

    private static void SkipSpaces(string value, ref int cursor)
    {
        while (cursor < value.Length && char.IsWhiteSpace(value[cursor]))
        {
            cursor++;
        }
    }

    private static int ParsePositive(string? value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 1;

    private static void AddTransformation(
        ICollection<ImportTransformationV1> result,
        ISet<string> seen,
        string kind,
        string detail,
        string sourcePointer)
    {
        if (seen.Add(kind))
        {
            result.Add(new ImportTransformationV1(kind, detail, sourcePointer, null));
        }
    }

    private static void AddWarning(
        ICollection<ImportWarningV1> result,
        ISet<string> seen,
        string code,
        string severity,
        string message,
        string sourcePointer)
    {
        var key = $"{code}\0{sourcePointer}";
        if (seen.Add(key))
        {
            result.Add(new ImportWarningV1(code, severity, message, sourcePointer));
        }
    }

    private static SubstackImportException Malformed(string message) =>
        new(SubstackImportFailure.MalformedExport, message);

    private sealed record ListState(bool Ordered, int Next);

    private sealed class TableState
    {
        public bool InRow { get; set; }

        public bool HeaderRow { get; set; }

        public bool HeaderWritten { get; set; }

        public int Cells { get; set; }
    }

    private sealed record HtmlToken(
        string Name,
        bool IsEnd,
        string? Text,
        IReadOnlyDictionary<string, string> Attributes,
        int Offset)
    {
        public bool IsText => Text is not null;

        public static HtmlToken TextToken(string text, int offset) =>
            new(string.Empty, IsEnd: false, text, new Dictionary<string, string>(), offset);
    }
}

internal sealed record HtmlImageReference(
    int Index,
    string Source,
    string? Alt,
    string? Title,
    string SourcePointer);

internal sealed record HtmlImageResolution(string MarkdownPath);

internal sealed record HtmlConversionResult(
    string Markdown,
    IReadOnlyList<ImportTransformationV1> Transformations,
    IReadOnlyList<ImportWarningV1> Warnings);
