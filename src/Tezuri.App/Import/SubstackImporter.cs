using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Tezuri.Articles;
using Tezuri.Media;
using Tezuri.Workspace;

namespace Tezuri.Import;

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
