using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Tezuri.Infrastructure.Workspace;

namespace Tezuri.Infrastructure.Import;

internal sealed class SubstackExportReader(WorkspacePathGuard workspace)
{
    private const int MaximumInputFiles = 20_000;
    private const long MaximumTotalInputBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumCsvBytes = 16 * 1024 * 1024;
    private const int MaximumBodyBytes = 32 * 1024 * 1024;
    private const int MaximumRows = 10_000;
    private const int MaximumColumns = 256;
    private const int MaximumFieldCharacters = 4 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<SubstackExportSnapshot> ReadAsync(
        string exportDirectory,
        CancellationToken cancellationToken)
    {
        var normalizedDirectory = NormalizeRepositoryDirectory(exportDirectory);
        var absoluteRoot = workspace.Resolve(normalizedDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(absoluteRoot))
        {
            throw Invalid($"Substack export directory '{normalizedDirectory}' does not exist.");
        }

        var files = await InventoryFilesAsync(absoluteRoot, cancellationToken);
        if (!files.TryGetValue("posts.csv", out var postsFile))
        {
            throw Malformed("A Substack export must contain 'posts.csv' at its root.");
        }

        if (postsFile.ByteLength > MaximumCsvBytes)
        {
            throw Malformed($"posts.csv exceeds the {MaximumCsvBytes}-byte import limit.");
        }

        var postsBytes = await ReadVerifiedBytesAsync(postsFile, MaximumCsvBytes, cancellationToken);
        var csv = DecodeStrictUtf8(postsBytes, "posts.csv");
        var rows = ParseCsv(csv);
        if (rows.Count < 2)
        {
            throw Malformed("posts.csv must contain a header and at least one data row.");
        }

        var headers = rows[0];
        if (headers.Count == 0 || headers.Count > MaximumColumns)
        {
            throw Malformed($"posts.csv must contain between 1 and {MaximumColumns} columns.");
        }

        var headerLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index].Trim();
            if (header.Length == 0)
            {
                throw Malformed($"posts.csv column {index + 1} has an empty header.");
            }

            if (!headerLookup.TryAdd(header, index))
            {
                throw Malformed($"posts.csv repeats header '{header}'.");
            }
        }

        var posts = new List<SubstackExportPost>(rows.Count - 1);
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.Count != headers.Count)
            {
                throw Malformed(
                    $"posts.csv row {rowIndex + 1} has {row.Count} fields; {headers.Count} were expected.");
            }

            var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal);
            for (var column = 0; column < headers.Count; column++)
            {
                metadata[headers[column]] = row[column];
            }

            var sourceId = First(row, headerLookup, "post_id", "id");
            var canonicalUrl = First(row, headerLookup, "canonical_url", "url");
            var title = First(row, headerLookup, "title");
            var slug = First(row, headerLookup, "slug");
            var inlineBody = First(row, headerLookup, "body_html", "html_body", "body");
            var bodyPath = inlineBody is null
                ? FindBodyPath(files, sourceId, slug)
                : null;

            posts.Add(new SubstackExportPost(
                RowNumber: rowIndex + 1,
                SourceId: sourceId,
                CanonicalUrl: canonicalUrl,
                Title: title,
                Subtitle: First(row, headerLookup, "subtitle", "description"),
                Slug: slug,
                Author: First(row, headerLookup, "author", "byline", "author_name"),
                PublishedAt: First(row, headerLookup, "post_date", "published_at", "publish_date"),
                UpdatedAt: First(row, headerLookup, "updated_at", "last_updated_at"),
                Type: First(row, headerLookup, "type", "post_type"),
                Audience: First(row, headerLookup, "audience"),
                IsPublished: First(row, headerLookup, "is_published", "published"),
                Tags: First(row, headerLookup, "tags", "categories"),
                InlineBodyHtml: inlineBody,
                BodyRelativePath: bodyPath,
                Metadata: metadata));
        }

        var exportDigest = ComputeInventoryDigest(files.Values);
        return new SubstackExportSnapshot(
            normalizedDirectory,
            absoluteRoot,
            exportDigest,
            files,
            posts);
    }

    public async Task<byte[]> ReadVerifiedBodyAsync(
        SubstackExportSnapshot snapshot,
        SubstackExportPost post,
        CancellationToken cancellationToken)
    {
        if (post.InlineBodyHtml is not null)
        {
            var bytes = StrictUtf8.GetBytes(post.InlineBodyHtml);
            if (bytes.Length > MaximumBodyBytes)
            {
                throw Malformed($"posts.csv row {post.RowNumber} contains a body beyond the import limit.");
            }

            return bytes;
        }

        if (post.BodyRelativePath is null || !snapshot.Files.TryGetValue(post.BodyRelativePath, out var bodyFile))
        {
            throw Malformed($"posts.csv row {post.RowNumber} has no matching exported HTML body.");
        }

        return await ReadVerifiedBytesAsync(bodyFile, MaximumBodyBytes, cancellationToken);
    }

    public async Task<byte[]> ReadVerifiedAssetAsync(
        SubstackExportSnapshot snapshot,
        string relativePath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!snapshot.Files.TryGetValue(relativePath, out var file))
        {
            throw Malformed($"Export asset '{relativePath}' is missing from the inventoried export.");
        }

        return await ReadVerifiedBytesAsync(file, maximumBytes, cancellationToken);
    }

    public static string DecodeBody(byte[] bytes, string sourceName) => DecodeStrictUtf8(bytes, sourceName);

    private async Task<IReadOnlyDictionary<string, SubstackExportFile>> InventoryFilesAsync(
        string absoluteRoot,
        CancellationToken cancellationToken)
    {
        var result = new SortedDictionary<string, SubstackExportFile>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(absoluteRoot);
        long totalBytes = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Dequeue();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var repositoryRelative = workspace.Relative(entry);
                var guarded = workspace.Resolve(repositoryRelative.Replace('/', Path.DirectorySeparatorChar));
                if (!IsWithinExport(absoluteRoot, guarded))
                {
                    throw Invalid($"Export entry '{repositoryRelative}' escapes its selected directory.");
                }

                var exportRelative = Path.GetRelativePath(absoluteRoot, guarded).Replace('\\', '/');
                if (Directory.Exists(guarded))
                {
                    pending.Enqueue(guarded);
                    continue;
                }

                if (!File.Exists(guarded))
                {
                    throw Malformed($"Export entry '{exportRelative}' is not a regular file.");
                }

                if (result.Count >= MaximumInputFiles)
                {
                    throw Malformed($"The export contains more than {MaximumInputFiles} files.");
                }

                var info = new FileInfo(guarded);
                if (info.Length < 0 || totalBytes > MaximumTotalInputBytes - info.Length)
                {
                    throw Malformed($"The export exceeds the {MaximumTotalInputBytes}-byte aggregate limit.");
                }

                totalBytes += info.Length;
                var digest = await HashFileAsync(guarded, cancellationToken);
                result.Add(exportRelative, new SubstackExportFile(exportRelative, guarded, info.Length, digest));
            }
        }

        return result;
    }

    private static async Task<byte[]> ReadVerifiedBytesAsync(
        SubstackExportFile file,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (file.ByteLength > maximumBytes || file.ByteLength > int.MaxValue)
        {
            throw Malformed($"Export file '{file.RelativePath}' exceeds its {maximumBytes}-byte limit.");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(file.AbsolutePath, cancellationToken);
        }
        catch (DecoderFallbackException exception)
        {
            throw Malformed($"Export file '{file.RelativePath}' is not valid UTF-8.", exception);
        }

        if (bytes.LongLength != file.ByteLength ||
            !StringComparer.Ordinal.Equals(Sha256(bytes), file.Sha256))
        {
            throw new SubstackImportException(
                SubstackImportFailure.PlanChanged,
                $"Export file '{file.RelativePath}' changed while it was being inspected.");
        }

        return bytes;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
    }

    private static string ComputeInventoryDigest(IEnumerable<SubstackExportFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(long)];
        foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            Append(hash, file.RelativePath);
            BinaryPrimitives.WriteInt64BigEndian(length, file.ByteLength);
            hash.AppendData(length);
            hash.AppendData(Convert.FromHexString(file.Sha256["sha256:".Length..]));
        }

        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(StrictUtf8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static string? FindBodyPath(
        IReadOnlyDictionary<string, SubstackExportFile> files,
        string? sourceId,
        string? slug)
    {
        foreach (var candidate in new[] { sourceId, slug })
        {
            if (!IsPortableCandidate(candidate))
            {
                continue;
            }

            var relativePath = $"posts/{candidate}.html";
            if (files.ContainsKey(relativePath))
            {
                return relativePath;
            }
        }

        return null;
    }

    private static bool IsPortableCandidate(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 200 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string? First(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headers,
        params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (headers.TryGetValue(alias, out var index))
            {
                var value = row[index].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string source)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var quoteClosed = false;

        void CompleteField()
        {
            if (field.Length > MaximumFieldCharacters)
            {
                throw Malformed($"posts.csv contains a field beyond {MaximumFieldCharacters} characters.");
            }

            row.Add(field.ToString());
            field.Clear();
            quoteClosed = false;
        }

        void CompleteRow()
        {
            CompleteField();
            if (!(row.Count == 1 && row[0].Length == 0))
            {
                rows.Add(row.ToArray());
                if (rows.Count > MaximumRows + 1)
                {
                    throw Malformed($"posts.csv contains more than {MaximumRows} data rows.");
                }
            }

            row.Clear();
        }

        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                    continue;
                }

                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                    continue;
                }

                quoted = false;
                quoteClosed = true;
                continue;
            }

            if (quoteClosed && character is not ',' and not '\r' and not '\n')
            {
                throw Malformed("posts.csv contains text after a closing quote.");
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;
                case '"':
                    throw Malformed("posts.csv contains a quote inside an unquoted field.");
                case ',':
                    CompleteField();
                    break;
                case '\r':
                    if (index + 1 < source.Length && source[index + 1] == '\n')
                    {
                        index++;
                    }

                    CompleteRow();
                    break;
                case '\n':
                    CompleteRow();
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (quoted)
        {
            throw Malformed("posts.csv ends inside a quoted field.");
        }

        if (field.Length > 0 || row.Count > 0 || quoteClosed)
        {
            CompleteRow();
        }

        return rows;
    }

    private static string DecodeStrictUtf8(byte[] bytes, string sourceName)
    {
        try
        {
            var source = StrictUtf8.GetString(bytes);
            return source.Length > 0 && source[0] == '\uFEFF' ? source[1..] : source;
        }
        catch (DecoderFallbackException exception)
        {
            throw Malformed($"Export file '{sourceName}' is not valid UTF-8.", exception);
        }
    }

    private static string NormalizeRepositoryDirectory(string exportDirectory)
    {
        if (string.IsNullOrWhiteSpace(exportDirectory) ||
            Path.IsPathRooted(exportDirectory) ||
            exportDirectory.StartsWith('/') ||
            exportDirectory.Contains('\\', StringComparison.Ordinal) ||
            exportDirectory.Any(char.IsControl))
        {
            throw Invalid("The export directory must be a repository-relative path using '/'.");
        }

        var segments = exportDirectory.Split('/');
        if (segments.Any(segment =>
                segment is "" or "." or ".." ||
                segment.IndexOfAny(['*', '?', '[', ']']) >= 0))
        {
            throw Invalid("The export directory contains an unsafe path segment.");
        }

        return string.Join('/', segments);
    }

    private static bool IsWithinExport(string root, string candidate)
    {
        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, PathComparison);
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static SubstackImportException Invalid(string message) =>
        new(SubstackImportFailure.InvalidRequest, message);

    private static SubstackImportException Malformed(string message, Exception? exception = null) =>
        new(SubstackImportFailure.MalformedExport, message, exception);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

internal sealed record SubstackExportSnapshot(
    string ExportDirectory,
    string AbsoluteRoot,
    string ExportDigest,
    IReadOnlyDictionary<string, SubstackExportFile> Files,
    IReadOnlyList<SubstackExportPost> Posts);

internal sealed record SubstackExportFile(
    string RelativePath,
    string AbsolutePath,
    long ByteLength,
    string Sha256);

internal sealed record SubstackExportPost(
    int RowNumber,
    string? SourceId,
    string? CanonicalUrl,
    string? Title,
    string? Subtitle,
    string? Slug,
    string? Author,
    string? PublishedAt,
    string? UpdatedAt,
    string? Type,
    string? Audience,
    string? IsPublished,
    string? Tags,
    string? InlineBodyHtml,
    string? BodyRelativePath,
    IReadOnlyDictionary<string, string> Metadata);
