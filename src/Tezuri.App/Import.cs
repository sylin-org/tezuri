using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace Tezuri;

/// <summary>
/// Reading a Substack export and turning it into articles.
/// </summary>
public enum SubstackImportFailure
{
    /// <summary>The caller named a directory that is not a usable export.</summary>
    InvalidRequest,

    /// <summary>The export is present but does not hold together — bad CSV, bad UTF-8, bad HTML.</summary>
    MalformedExport,

    /// <summary>A file changed underneath the importer while it was being read.</summary>
    ExportChanged
}

public sealed class SubstackImportException(
    SubstackImportFailure failure,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public SubstackImportFailure Failure { get; } = failure;
}

/// <summary>What the HTML converter changed on its way to Markdown, and what it could not keep.</summary>
public sealed record ImportTransformation(
    string Kind,
    string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourcePointer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultPointer);

public sealed record ImportWarning(
    string Code,
    string Severity,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourcePointer);

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
                SubstackImportFailure.ExportChanged,
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

/// <summary>
/// Turns a Substack export into Tezuri articles.
///
/// <para><b>Preview</b> reads and reports. It writes nothing, anywhere.</para>
///
/// <para><b>Apply</b> creates one <see cref="Article"/> per importable post, ingests the images that
/// post owns, and renders the Markdown beside it — the same path the editor takes, so an imported
/// article is indistinguishable from a written one.</para>
///
/// <para>An article that already exists is left alone. That single rule is the whole safety story:
/// because a re-run cannot overwrite work, the import needs no plan digest, no staging tree, no
/// two-phase commit, and no manifest file recording what it did. Git already records that.</para>
/// </summary>
public sealed class SubstackImporter(
    WorkspacePathGuard workspace,
    WorkspaceSettings settings,
    ArticleMediaStore media,
    ArticleMarkdownWriter markdown)
{
    private const int MaximumSourceIdCharacters = 500;
    private const int MaximumTitleCharacters = 1_000;

    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    private readonly SubstackExportReader _reader = new(workspace);
    private readonly SubstackHtmlConverter _html = new();

    /// <summary>Reports what an import would do. Never touches the workspace.</summary>
    public async Task<SubstackImportReport> PreviewAsync(
        string exportDirectory,
        CancellationToken cancellationToken = default)
    {
        var plans = await PlanAsync(exportDirectory, cancellationToken);
        return new SubstackImportReport(
            exportDirectory,
            plans.Select(plan => plan.Item).ToArray());
    }

    /// <summary>Creates every article the preview marked importable, and skips everything else.</summary>
    public async Task<SubstackImportReport> ApplyAsync(
        string exportDirectory,
        CancellationToken cancellationToken = default)
    {
        var plans = await PlanAsync(exportDirectory, cancellationToken);
        var items = new List<SubstackImportItem>(plans.Count);

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.Post is null ||
                !StringComparer.Ordinal.Equals(plan.Item.Disposition, SubstackImportDisposition.Import))
            {
                items.Add(plan.Item);
                continue;
            }

            items.Add(await ImportAsync(plan, cancellationToken));
        }

        return new SubstackImportReport(exportDirectory, items);
    }

    private async Task<IReadOnlyList<PlannedPost>> PlanAsync(
        string exportDirectory,
        CancellationToken cancellationToken)
    {
        var snapshot = await _reader.ReadAsync(exportDirectory, cancellationToken);
        var plans = new List<PlannedPost>(snapshot.Posts.Count);
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var post in snapshot.Posts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plans.Add(await PlanPostAsync(snapshot, post, slugs, cancellationToken));
        }

        return plans;
    }

    private async Task<PlannedPost> PlanPostAsync(
        SubstackExportSnapshot snapshot,
        SubstackExportPost post,
        ISet<string> usedSlugs,
        CancellationToken cancellationToken)
    {
        var sourceId = post.SourceId ?? post.CanonicalUrl;
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > MaximumSourceIdCharacters)
        {
            throw Malformed(
                $"posts.csv row {post.RowNumber} needs a source id or canonical URL of at most {MaximumSourceIdCharacters} characters.");
        }

        if (string.IsNullOrWhiteSpace(post.Title) || post.Title.Length > MaximumTitleCharacters)
        {
            throw Malformed(
                $"posts.csv row {post.RowNumber} needs a title of at most {MaximumTitleCharacters} characters.");
        }

        if (IsFalse(post.IsPublished))
        {
            return PlannedPost.Only(Item(
                sourceId,
                post.Title,
                slug: string.Empty,
                SubstackImportDisposition.Skipped,
                "The export marks this item as unpublished."));
        }

        if (IsExcludedType(post.Type))
        {
            return PlannedPost.Only(Item(
                sourceId,
                post.Title,
                slug: string.Empty,
                SubstackImportDisposition.Skipped,
                $"A Substack '{post.Type}' is not an authored article."));
        }

        if (IsPaidOnly(post.Audience))
        {
            return PlannedPost.Only(Item(
                sourceId,
                post.Title,
                slug: string.Empty,
                SubstackImportDisposition.NeedsReview,
                "This item is paid or private. Confirm you mean to republish it before importing."));
        }

        byte[] bodyBytes;
        try
        {
            bodyBytes = await _reader.ReadVerifiedBodyAsync(snapshot, post, cancellationToken);
        }
        catch (SubstackImportException exception)
            when (exception.Failure == SubstackImportFailure.MalformedExport)
        {
            return PlannedPost.Only(Item(
                sourceId,
                post.Title,
                slug: string.Empty,
                SubstackImportDisposition.NeedsReview,
                exception.Message));
        }

        // Strict UTF-8 and the HTML tokenizer both fail closed here rather than importing a body
        // nobody can vouch for. Neither has written anything at this point.
        var bodyName = post.BodyRelativePath ?? $"posts.csv#row-{post.RowNumber}";
        var bodyHtml = SubstackExportReader.DecodeBody(bodyBytes, bodyName);
        var images = _html.InspectImages(bodyHtml);

        var slug = NormalizeSlug(post.Slug, post.Title, sourceId);
        if (!usedSlugs.Add(slug))
        {
            return PlannedPost.Only(Item(
                sourceId,
                post.Title,
                slug,
                SubstackImportDisposition.NeedsReview,
                $"More than one exported article wants the slug '{slug}'."));
        }

        var assets = new List<PlannedAsset>(images.Count);
        var unresolved = new List<string>();
        foreach (var image in images)
        {
            var local = ResolveLocalAsset(snapshot, post, image);
            if (local is null)
            {
                unresolved.Add(
                    $"Image '{image.Source}' has no local copy in the export. Tezuri never fetches from the network.");
                continue;
            }

            assets.Add(new PlannedAsset(image.Index, local));
        }

        if (unresolved.Count > 0)
        {
            return new PlannedPost(
                Item(
                    sourceId,
                    post.Title,
                    slug,
                    SubstackImportDisposition.NeedsReview,
                    "One or more images cannot be imported safely.",
                    images.Count,
                    unresolved),
                post,
                snapshot,
                bodyHtml,
                assets);
        }

        return new PlannedPost(
            Item(sourceId, post.Title, slug, SubstackImportDisposition.Import, Reason: null, images.Count),
            post,
            snapshot,
            bodyHtml,
            assets);
    }

    private async Task<SubstackImportItem> ImportAsync(
        PlannedPost plan,
        CancellationToken cancellationToken)
    {
        var slug = plan.Item.Slug;
        var post = plan.Post!;

        if (Directory.Exists(workspace.Resolve(WorkspaceLayout.ArticleFolder(slug))))
        {
            return plan.Item with
            {
                Disposition = SubstackImportDisposition.Skipped,
                Reason = $"An article already exists at '{slug}'. Nothing was overwritten."
            };
        }

        // The article is created first so its folder exists, then its images move in, and only then
        // is the body written — because the body links each image by the content name it was given.
        var article = CreateArticle(slug, post, plan.Item.SourceId);
        await Article.Upsert(article, cancellationToken);
        await markdown.WriteAsync(article, cancellationToken);

        var resolutions = new Dictionary<int, HtmlImageResolution>();
        var warnings = new List<string>();
        var assetNumber = 0;
        foreach (var asset in plan.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assetNumber++;
            try
            {
                var bytes = await _reader.ReadVerifiedAssetAsync(
                    plan.Snapshot!,
                    asset.Source.RelativePath,
                    settings.Media.MaximumAssetBytes,
                    cancellationToken);
                var extension = Path.GetExtension(asset.Source.RelativePath).ToLowerInvariant();
                var receipt = await media.IngestAsync(
                    slug,
                    $"asset-{assetNumber:D4}{extension}",
                    bytes,
                    cancellationToken);
                resolutions[asset.ImageIndex] =
                    new HtmlImageResolution($"{WorkspaceLayout.MediaDirectoryName}/{receipt.FileName}");
            }
            catch (Exception exception) when (
                exception is MediaAssetException or WorkspacePathException or SubstackImportException)
            {
                warnings.Add(
                    $"Image '{asset.Source.RelativePath}' was left out: {exception.Message}");
            }
        }

        var converted = _html.Convert(plan.BodyHtml!, resolutions);
        warnings.AddRange(converted.Warnings.Select(warning => warning.Message));

        article.Body = converted.Markdown;
        article.Revision = NewRevision();
        article.UpdatedAt = DateTimeOffset.UtcNow;
        await Article.Upsert(article, cancellationToken);
        await markdown.WriteAsync(article, cancellationToken);

        return plan.Item with
        {
            Disposition = SubstackImportDisposition.Imported,
            Warnings = warnings
        };
    }

    private static Article CreateArticle(string slug, SubstackExportPost post, string sourceId)
    {
        var article = new Article
        {
            Id = slug,
            Title = post.Title!,
            Subtitle = Blank(post.Subtitle),
            Draft = false,
            Date = NormalizeDateTime(post.PublishedAt),
            Tags = ParseTags(post.Tags).ToList(),
            Revision = NewRevision(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Whatever Substack knew that Tezuri has no control for rides along in the extension data,
        // so it survives every later save and lands in the generated frontmatter untouched.
        SetMeta(article, "author", Blank(post.Author));
        SetMeta(article, "canonicalUrl", NormalizeHttpUrl(post.CanonicalUrl));
        SetMeta(article, "substackId", sourceId);
        return article;
    }

    private static void SetMeta(Article article, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            article.Meta[key] = JToken.FromObject(value);
        }
    }

    private LocalAsset? ResolveLocalAsset(
        SubstackExportSnapshot snapshot,
        SubstackExportPost post,
        HtmlImageReference image)
    {
        if (Uri.TryCreate(image.Source, UriKind.Absolute, out _))
        {
            return null;
        }

        var pathPart = image.Source.Split(['?', '#'], 2)[0];
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(pathPart);
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(decoded) ||
            decoded.Contains('\\', StringComparison.Ordinal) ||
            Path.IsPathRooted(decoded))
        {
            return null;
        }

        var bodyDirectory = post.BodyRelativePath is null
            ? string.Empty
            : Path.GetDirectoryName(post.BodyRelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var absolute = Path.GetFullPath(Path.Combine(snapshot.AbsoluteRoot, bodyDirectory, decoded));
        var rootWithSeparator = Path.EndsInDirectorySeparator(snapshot.AbsoluteRoot)
            ? snapshot.AbsoluteRoot
            : snapshot.AbsoluteRoot + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(rootWithSeparator, PathComparison))
        {
            return null;
        }

        var repositoryRelative = workspace.Relative(absolute);
        var guarded = workspace.Resolve(repositoryRelative.Replace('/', Path.DirectorySeparatorChar));
        var exportRelative = Path.GetRelativePath(snapshot.AbsoluteRoot, guarded).Replace('\\', '/');
        return snapshot.Files.ContainsKey(exportRelative) ? new LocalAsset(exportRelative) : null;
    }

    private static SubstackImportItem Item(
        string sourceId,
        string? title,
        string slug,
        string disposition,
        string? Reason,
        int images = 0,
        IReadOnlyList<string>? warnings = null) => new(
        sourceId,
        title ?? sourceId,
        slug,
        disposition,
        Reason,
        images,
        warnings ?? []);

    private static string NewRevision() => Guid.CreateVersion7().ToString("N");

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> ParseTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        if (tags.TrimStart().StartsWith('['))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(tags);
                if (parsed is not null)
                {
                    return parsed
                        .Where(tag => !string.IsNullOrWhiteSpace(tag))
                        .Select(tag => tag.Trim())
                        .ToArray();
                }
            }
            catch (JsonException)
            {
                // Not a JSON array after all; fall through to the delimited reading below.
            }
        }

        return tags.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string NormalizeSlug(string? sourceSlug, string title, string sourceId)
    {
        if (!string.IsNullOrWhiteSpace(sourceSlug) &&
            sourceSlug.Length <= 100 &&
            sourceSlug[0] is >= 'a' and <= 'z' &&
            sourceSlug.All(character =>
                character is >= 'a' and <= 'z' || char.IsAsciiDigit(character) || character == '-') &&
            !sourceSlug.Contains("--", StringComparison.Ordinal) &&
            sourceSlug[^1] != '-')
        {
            return sourceSlug;
        }

        var normalized = new StringBuilder();
        var pendingHyphen = false;
        foreach (var character in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingHyphen && normalized.Length > 0)
                {
                    normalized.Append('-');
                }

                normalized.Append(character);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }

            if (normalized.Length >= 72)
            {
                break;
            }
        }

        var baseSlug = normalized.ToString().Trim('-');
        if (baseSlug.Length == 0)
        {
            baseSlug = "article";
        }

        var suffix = Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(sourceId)))
            .ToLowerInvariant()[..8];
        return $"{baseSlug}-{suffix}";
    }

    private static string? NormalizeHttpUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : null;

    private static string? NormalizeDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsFalse(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "false" or "0" or "no";

    private static bool IsPaidOnly(string? value) =>
        value is not null &&
        (value.Contains("paid", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("private", StringComparison.OrdinalIgnoreCase));

    private static bool IsExcludedType(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "note" or "chat" or "thread" or "comment";

    private static SubstackImportException Malformed(string message) =>
        new(SubstackImportFailure.MalformedExport, message);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record PlannedPost(
        SubstackImportItem Item,
        SubstackExportPost? Post,
        SubstackExportSnapshot? Snapshot,
        string? BodyHtml,
        IReadOnlyList<PlannedAsset> Assets)
    {
        /// <summary>A decided item with nothing left to import — skipped, or held for review.</summary>
        public static PlannedPost Only(SubstackImportItem item) =>
            new(item, Post: null, Snapshot: null, BodyHtml: null, Assets: []);
    }

    private sealed record PlannedAsset(int ImageIndex, LocalAsset Source);

    private sealed record LocalAsset(string RelativePath);
}

public static class SubstackImportDisposition
{
    /// <summary>Preview only: this post is ready to import.</summary>
    public const string Import = "import";

    /// <summary>Apply only: this post became an article.</summary>
    public const string Imported = "imported";

    public const string Skipped = "skipped";
    public const string NeedsReview = "needs-review";
}

public sealed record SubstackImportItem(
    string SourceId,
    string Title,
    string Slug,
    string Disposition,
    string? Reason,
    int Images,
    IReadOnlyList<string> Warnings);

public sealed record SubstackImportReport(
    string ExportDirectory,
    IReadOnlyList<SubstackImportItem> Items)
{
    public int Discovered => Items.Count;

    public int Ready => Items.Count(item =>
        item.Disposition is SubstackImportDisposition.Import or SubstackImportDisposition.Imported);

    public int Skipped => Items.Count(item => item.Disposition == SubstackImportDisposition.Skipped);

    public int NeedsReview => Items.Count(item =>
        item.Disposition == SubstackImportDisposition.NeedsReview);
}

/// <summary>
/// Preview reads; apply creates articles. There is no token between them because apply cannot
/// overwrite anything — an existing article is skipped — so a stale preview costs a re-read, not a
/// lost draft.
/// </summary>
[ApiController]
[Route("api/v1/imports/substack")]
public sealed class SubstackImportsController(SubstackImporter importer) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<SubstackImportReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubstackImportReport>> Preview(
        [FromQuery] string exportDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await importer.PreviewAsync(exportDirectory, cancellationToken));
        }
        catch (Exception exception) when (exception is SubstackImportException or WorkspacePathException)
        {
            return ProblemFor(exception);
        }
    }

    [HttpPost("apply")]
    [ProducesResponseType<SubstackImportReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubstackImportReport>> Apply(
        [FromQuery] string exportDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await importer.ApplyAsync(exportDirectory, cancellationToken));
        }
        catch (Exception exception) when (exception is SubstackImportException or WorkspacePathException)
        {
            return ProblemFor(exception);
        }
    }

    private ObjectResult ProblemFor(Exception exception)
    {
        var failure = exception is SubstackImportException import
            ? import.Failure
            : SubstackImportFailure.InvalidRequest;
        return Problem(
            statusCode: failure switch
            {
                SubstackImportFailure.MalformedExport => StatusCodes.Status422UnprocessableEntity,
                SubstackImportFailure.ExportChanged => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            },
            title: failure switch
            {
                SubstackImportFailure.MalformedExport => "The Substack export is malformed.",
                SubstackImportFailure.ExportChanged => "The export changed while it was being read.",
                _ => "The Substack import request is invalid."
            },
            detail: exception.Message,
            type: "https://tezuri.local/problems/substack-import");
    }
}
