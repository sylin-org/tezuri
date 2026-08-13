using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tezuri.Infrastructure.Configuration;

public sealed partial class WorkspaceConfigurationParser
{
    public WorkspaceConfigurationV1 Parse(string source, string sourceName = "tezuri.yaml")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var root = RestrictedYamlParser.Parse(source, sourceName);
        var document = RequireMap(root, "$", sourceName);
        RequireOnlyKeys(document, "$", sourceName, "schema", "site", "articles", "media", "proof", "git");

        var site = RequireMap(Require(document, "site", "$", sourceName), "site", sourceName);
        RequireOnlyKeys(site, "site", sourceName, "url");

        var articles = RequireMap(
            Require(document, "articles", "$", sourceName),
            "articles",
            sourceName);
        RequireOnlyKeys(
            articles,
            "articles",
            sourceName,
            "root",
            "fileName",
            "mediaDirectory",
            "metadataSchema",
            "editorHints");

        var media = RequireMap(Require(document, "media", "$", sourceName), "media", sourceName);
        RequireOnlyKeys(
            media,
            "media",
            sourceName,
            "requireOwnedAssets",
            "maximumAssetBytes",
            "allowedExtensions");

        var proof = RequireMap(Require(document, "proof", "$", sourceName), "proof", sourceName);
        RequireOnlyKeys(proof, "proof", sourceName, "workingDirectory", "commands");

        var git = RequireMap(Require(document, "git", "$", sourceName), "git", sourceName);
        RequireOnlyKeys(git, "git", sourceName, "allowedPaths");

        return new WorkspaceConfigurationV1(
            Schema: RequireScalar(document, "schema", "$", sourceName),
            Site: new SiteConfiguration(RequireScalar(site, "url", "site", sourceName)),
            Articles: new ArticleLayoutConfiguration(
                Root: RequireScalar(articles, "root", "articles", sourceName),
                FileName: RequireScalar(articles, "fileName", "articles", sourceName),
                MediaDirectory: RequireScalar(articles, "mediaDirectory", "articles", sourceName),
                MetadataSchema: RequireScalar(articles, "metadataSchema", "articles", sourceName),
                EditorHints: OptionalScalar(articles, "editorHints", "articles", sourceName)),
            Media: new MediaPolicyConfiguration(
                RequireOwnedAssets: RequireBoolean(
                    media,
                    "requireOwnedAssets",
                    "media",
                    sourceName),
                MaximumAssetBytes: RequireInt64(
                    media,
                    "maximumAssetBytes",
                    "media",
                    sourceName),
                AllowedExtensions: RequireScalarList(
                    media,
                    "allowedExtensions",
                    "media",
                    sourceName)),
            Proof: new ProofConfiguration(
                WorkingDirectory: RequireScalar(
                    proof,
                    "workingDirectory",
                    "proof",
                    sourceName),
                Commands: ParseProofCommands(
                    RequireSequence(Require(proof, "commands", "proof", sourceName), "proof.commands", sourceName),
                    sourceName)),
            Git: new GitPublicationConfiguration(
                RequireScalarList(git, "allowedPaths", "git", sourceName)));
    }

    private static IReadOnlyList<ProofCommandConfiguration> ParseProofCommands(
        YamlSequenceNode commands,
        string sourceName)
    {
        var result = new List<ProofCommandConfiguration>(commands.Items.Count);
        for (var index = 0; index < commands.Items.Count; index++)
        {
            var path = $"proof.commands[{index}]";
            var command = RequireMap(commands.Items[index], path, sourceName);
            RequireOnlyKeys(
                command,
                path,
                sourceName,
                "id",
                "executable",
                "arguments",
                "timeoutSeconds",
                "outputDirectory");

            result.Add(new ProofCommandConfiguration(
                Id: RequireScalar(command, "id", path, sourceName),
                Executable: RequireScalar(command, "executable", path, sourceName),
                Arguments: RequireScalarList(command, "arguments", path, sourceName),
                TimeoutSeconds: RequireInt32(command, "timeoutSeconds", path, sourceName),
                OutputDirectory: OptionalScalar(command, "outputDirectory", path, sourceName)));
        }

        return result;
    }

    private static YamlNode Require(
        YamlMapNode map,
        string key,
        string path,
        string sourceName)
    {
        if (map.Properties.TryGetValue(key, out var value))
        {
            return value;
        }

        throw Error(sourceName, map.Line, $"{ChildPath(path, key)} is required.");
    }

    private static string RequireScalar(
        YamlMapNode map,
        string key,
        string path,
        string sourceName) =>
        RequireScalar(Require(map, key, path, sourceName), ChildPath(path, key), sourceName);

    private static string RequireScalar(YamlNode node, string path, string sourceName)
    {
        if (node is YamlScalarNode scalar && scalar.Value.Length > 0)
        {
            return scalar.Value;
        }

        throw Error(sourceName, node.Line, $"{path} must be a non-empty scalar.");
    }

    private static string? OptionalScalar(
        YamlMapNode map,
        string key,
        string path,
        string sourceName)
    {
        return map.Properties.TryGetValue(key, out var value)
            ? RequireScalar(value, ChildPath(path, key), sourceName)
            : null;
    }

    private static bool RequireBoolean(
        YamlMapNode map,
        string key,
        string path,
        string sourceName)
    {
        var value = RequireScalar(map, key, path, sourceName);
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw Error(
                sourceName,
                map.Properties[key].Line,
                $"{ChildPath(path, key)} must be true or false.")
        };
    }

    private static int RequireInt32(
        YamlMapNode map,
        string key,
        string path,
        string sourceName)
    {
        var value = RequireScalar(map, key, path, sourceName);
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw Error(
            sourceName,
            map.Properties[key].Line,
            $"{ChildPath(path, key)} must be a base-10 integer.");
    }

    private static long RequireInt64(
        YamlMapNode map,
        string key,
        string path,
        string sourceName)
    {
        var value = RequireScalar(map, key, path, sourceName);
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw Error(
            sourceName,
            map.Properties[key].Line,
            $"{ChildPath(path, key)} must be a base-10 integer.");
    }

    private static IReadOnlyList<string> RequireScalarList(
        YamlMapNode map,
        string key,
        string path,
        string sourceName)
    {
        var listPath = ChildPath(path, key);
        var sequence = RequireSequence(Require(map, key, path, sourceName), listPath, sourceName);
        return sequence.Items
            .Select((item, index) => RequireScalar(item, $"{listPath}[{index}]", sourceName))
            .ToArray();
    }

    private static YamlMapNode RequireMap(YamlNode node, string path, string sourceName)
    {
        return node as YamlMapNode ??
               throw Error(sourceName, node.Line, $"{path} must be a mapping.");
    }

    private static YamlSequenceNode RequireSequence(YamlNode node, string path, string sourceName)
    {
        return node as YamlSequenceNode ??
               throw Error(sourceName, node.Line, $"{path} must be a sequence.");
    }

    private static void RequireOnlyKeys(
        YamlMapNode map,
        string path,
        string sourceName,
        params string[] permitted)
    {
        var allowed = permitted.ToHashSet(StringComparer.Ordinal);
        foreach (var (key, value) in map.Properties)
        {
            if (!allowed.Contains(key))
            {
                throw Error(sourceName, value.Line, $"{ChildPath(path, key)} is not supported by v1.");
            }
        }
    }

    private static string ChildPath(string path, string key) => path == "$" ? key : $"{path}.{key}";

    private static WorkspaceConfigurationException Error(string sourceName, int line, string message) =>
        new($"{sourceName}:{line}: {message}");

    private abstract record YamlNode(int Line);

    private sealed record YamlScalarNode(string Value, int SourceLine) : YamlNode(SourceLine);

    private sealed record YamlMapNode(
        IReadOnlyDictionary<string, YamlNode> Properties,
        int SourceLine) : YamlNode(SourceLine);

    private sealed record YamlSequenceNode(
        IReadOnlyList<YamlNode> Items,
        int SourceLine) : YamlNode(SourceLine);

    private sealed record YamlLine(int Number, int Indent, string Content);

    private static partial class RestrictedYamlParser
    {
        private const int MaximumSourceCharacters = 262_144;
        private const int MaximumLogicalLines = 4_096;
        private const int MaximumIndent = 20;

        public static YamlNode Parse(string source, string sourceName)
        {
            if (source.Length > MaximumSourceCharacters)
            {
                throw new WorkspaceConfigurationException(
                    $"{sourceName}: configuration exceeds {MaximumSourceCharacters} characters.");
            }

            var lines = ReadLines(source, sourceName);
            if (lines.Count == 0)
            {
                throw new WorkspaceConfigurationException($"{sourceName}: configuration is empty.");
            }

            if (lines[0].Indent != 0)
            {
                throw Error(sourceName, lines[0].Number, "The root mapping must start at column one.");
            }

            var index = 0;
            var root = ParseBlock(lines, ref index, expectedIndent: 0, sourceName);
            if (index != lines.Count)
            {
                throw Error(sourceName, lines[index].Number, "Unexpected content after the root mapping.");
            }

            return root;
        }

        private static IReadOnlyList<YamlLine> ReadLines(string source, string sourceName)
        {
            var physicalLines = source.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
            var result = new List<YamlLine>();
            for (var index = 0; index < physicalLines.Length; index++)
            {
                var raw = physicalLines[index];
                if (raw.Contains('\t', StringComparison.Ordinal))
                {
                    throw Error(sourceName, index + 1, "Tabs are not permitted in workspace YAML.");
                }

                var trimmedEnd = raw.TrimEnd();
                var indent = 0;
                while (indent < trimmedEnd.Length && trimmedEnd[indent] == ' ')
                {
                    indent++;
                }

                var content = trimmedEnd[indent..];
                if (content.Length == 0 || content.StartsWith('#'))
                {
                    continue;
                }

                if (indent % 2 != 0)
                {
                    throw Error(sourceName, index + 1, "Indentation must use two-space steps.");
                }

                if (indent > MaximumIndent)
                {
                    throw Error(sourceName, index + 1, $"Indentation exceeds the v1 limit of {MaximumIndent} spaces.");
                }

                if (content is "---" or "...")
                {
                    throw Error(sourceName, index + 1, "YAML document markers are not permitted.");
                }

                result.Add(new YamlLine(index + 1, indent, content));
                if (result.Count > MaximumLogicalLines)
                {
                    throw new WorkspaceConfigurationException(
                        $"{sourceName}: configuration exceeds {MaximumLogicalLines} logical lines.");
                }
            }

            return result;
        }

        private static YamlNode ParseBlock(
            IReadOnlyList<YamlLine> lines,
            ref int index,
            int expectedIndent,
            string sourceName)
        {
            if (index >= lines.Count || lines[index].Indent != expectedIndent)
            {
                var line = index < lines.Count ? lines[index].Number : lines[^1].Number;
                throw Error(sourceName, line, $"Expected content indented {expectedIndent} spaces.");
            }

            return IsSequenceLine(lines[index].Content)
                ? ParseSequence(lines, ref index, expectedIndent, sourceName)
                : ParseMap(lines, ref index, expectedIndent, sourceName);
        }

        private static YamlMapNode ParseMap(
            IReadOnlyList<YamlLine> lines,
            ref int index,
            int expectedIndent,
            string sourceName)
        {
            var firstLine = lines[index].Number;
            var properties = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
            ParseMapEntries(lines, ref index, expectedIndent, sourceName, properties);
            return new YamlMapNode(properties, firstLine);
        }

        private static void ParseMapEntries(
            IReadOnlyList<YamlLine> lines,
            ref int index,
            int expectedIndent,
            string sourceName,
            IDictionary<string, YamlNode> properties)
        {
            while (index < lines.Count && lines[index].Indent == expectedIndent)
            {
                var line = lines[index];
                if (IsSequenceLine(line.Content))
                {
                    throw Error(sourceName, line.Number, "A mapping entry was expected here.");
                }

                var (key, value) = SplitMapping(line, sourceName);
                if (properties.ContainsKey(key))
                {
                    throw Error(sourceName, line.Number, $"Duplicate key '{key}' is not permitted.");
                }

                index++;
                properties.Add(
                    key,
                    value.Length == 0
                        ? ParseRequiredChild(lines, ref index, expectedIndent + 2, sourceName, line.Number)
                        : ParseScalar(value, line.Number, sourceName));
            }

            if (index < lines.Count && lines[index].Indent > expectedIndent)
            {
                throw Error(sourceName, lines[index].Number, "Unexpected indentation.");
            }
        }

        private static YamlSequenceNode ParseSequence(
            IReadOnlyList<YamlLine> lines,
            ref int index,
            int expectedIndent,
            string sourceName)
        {
            var firstLine = lines[index].Number;
            var items = new List<YamlNode>();
            while (index < lines.Count && lines[index].Indent == expectedIndent)
            {
                var line = lines[index];
                if (!IsSequenceLine(line.Content))
                {
                    throw Error(sourceName, line.Number, "A sequence item was expected here.");
                }

                var value = line.Content.Length == 1 ? string.Empty : line.Content[2..].Trim();
                index++;
                if (value.Length == 0)
                {
                    items.Add(ParseRequiredChild(lines, ref index, expectedIndent + 2, sourceName, line.Number));
                    continue;
                }

                if (TrySplitMapping(value, line.Number, sourceName, out var key, out var firstValue))
                {
                    var properties = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
                    properties.Add(
                        key,
                        firstValue.Length == 0
                            ? ParseRequiredChild(lines, ref index, expectedIndent + 4, sourceName, line.Number)
                            : ParseScalar(firstValue, line.Number, sourceName));
                    ParseMapEntries(lines, ref index, expectedIndent + 2, sourceName, properties);
                    items.Add(new YamlMapNode(properties, line.Number));
                    continue;
                }

                items.Add(ParseScalar(value, line.Number, sourceName));
            }

            if (index < lines.Count && lines[index].Indent > expectedIndent)
            {
                throw Error(sourceName, lines[index].Number, "Unexpected indentation.");
            }

            return new YamlSequenceNode(items, firstLine);
        }

        private static YamlNode ParseRequiredChild(
            IReadOnlyList<YamlLine> lines,
            ref int index,
            int expectedIndent,
            string sourceName,
            int parentLine)
        {
            if (index >= lines.Count || lines[index].Indent != expectedIndent)
            {
                throw Error(sourceName, parentLine, "A key without a value must have one nested block.");
            }

            return ParseBlock(lines, ref index, expectedIndent, sourceName);
        }

        private static (string Key, string Value) SplitMapping(YamlLine line, string sourceName)
        {
            if (!TrySplitMapping(line.Content, line.Number, sourceName, out var key, out var value))
            {
                throw Error(sourceName, line.Number, "Expected a 'key: value' mapping entry.");
            }

            return (key, value);
        }

        private static bool TrySplitMapping(
            string content,
            int line,
            string sourceName,
            out string key,
            out string value)
        {
            var colon = content.IndexOf(':');
            if (colon <= 0 || (colon + 1 < content.Length && !char.IsWhiteSpace(content[colon + 1])))
            {
                key = string.Empty;
                value = string.Empty;
                return false;
            }

            key = content[..colon].Trim();
            if (!KeyPattern().IsMatch(key))
            {
                throw Error(sourceName, line, $"Unsupported mapping key '{key}'.");
            }

            value = content[(colon + 1)..].Trim();
            return true;
        }

        private static YamlScalarNode ParseScalar(string value, int line, string sourceName)
        {
            if (value.StartsWith('"'))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<string>(value);
                    return parsed is null
                        ? throw Error(sourceName, line, "Null scalar values are not permitted.")
                        : new YamlScalarNode(parsed, line);
                }
                catch (JsonException exception)
                {
                    throw Error(sourceName, line, $"Invalid double-quoted scalar: {exception.Message}");
                }
            }

            if (value.StartsWith('\''))
            {
                if (value.Length < 2 || !value.EndsWith('\''))
                {
                    throw Error(sourceName, line, "Unterminated single-quoted scalar.");
                }

                return new YamlScalarNode(value[1..^1].Replace("''", "'", StringComparison.Ordinal), line);
            }

            if (value.Contains('#', StringComparison.Ordinal))
            {
                throw Error(sourceName, line, "Inline comments are not permitted; use a separate comment line.");
            }

            if (value is "null" or "~" ||
                value.StartsWith('{') ||
                value.StartsWith('[') ||
                value.StartsWith('&') ||
                value.StartsWith('*') ||
                value.StartsWith('!') ||
                value.StartsWith('|') ||
                value.StartsWith('>'))
            {
                throw Error(
                    sourceName,
                    line,
                    "This YAML feature is outside the deterministic Tezuri v1 subset.");
            }

            if (value.Any(char.IsControl))
            {
                throw Error(sourceName, line, "Control characters are not permitted in scalars.");
            }

            return new YamlScalarNode(value, line);
        }

        private static bool IsSequenceLine(string content) =>
            content == "-" || content.StartsWith("- ", StringComparison.Ordinal);

        [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
        private static partial Regex KeyPattern();
    }
}
