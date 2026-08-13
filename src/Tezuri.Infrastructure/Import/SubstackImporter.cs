using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tezuri.Domain.Import;
using Tezuri.Domain.Media;
using Tezuri.Domain.Workspace;
using Tezuri.Infrastructure.Configuration;
using Tezuri.Infrastructure.Media;
using Tezuri.Infrastructure.Workspace;

namespace Tezuri.Infrastructure.Import;

public sealed class SubstackImporter(
    WorkspacePathGuard workspace,
    WorkspaceContract contract,
    WorkspaceConfigurationV1 configuration,
    AtomicFileWriter writer,
    TimeProvider timeProvider)
{
    private const string ImportRecordsDirectory = ".tezuri-imports";
    private const string ImporterVersion = "substack-export/v1";

    private static readonly UTF8Encoding Utf8NoBom = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly SubstackExportReader _reader = new(workspace);
    private readonly SubstackHtmlConverter _html = new();

    public async Task<SubstackImportPreview> PreviewAsync(
        string exportDirectory,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(exportDirectory, cancellationToken);
        return new SubstackImportPreview(plan.Manifest, plan.PlanDigest, plan.ManifestRelativePath);
    }

    public async Task<SubstackImportApplyResult> ApplyAsync(
        string exportDirectory,
        string expectedPlanDigest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedPlanDigest))
        {
            throw new SubstackImportException(
                SubstackImportFailure.InvalidRequest,
                "Apply requires the exact ETag returned by a current import preview.");
        }

        var plan = await BuildPlanAsync(exportDirectory, cancellationToken);
        if (!StringComparer.Ordinal.Equals(plan.PlanDigest, expectedPlanDigest))
        {
            throw new SubstackImportException(
                SubstackImportFailure.PlanChanged,
                "The export, workspace targets, or relevant workspace configuration changed after preview.");
        }

        if (plan.Manifest.Articles.Any(article =>
                article.Disposition is ImportManifestProtocolV1.Failed or ImportManifestProtocolV1.ReviewRequired))
        {
            throw new SubstackImportException(
                SubstackImportFailure.ReviewRequired,
                "The preview contains failed or review-required articles; resolve them before applying the import.");
        }

        var existingManifest = await TryReadExistingManifestAsync(plan, cancellationToken);
        if (existingManifest is not null && plan.Articles.All(article => article.TargetState == TargetState.Exact))
        {
            return new SubstackImportApplyResult(
                existingManifest,
                plan.PlanDigest,
                plan.ManifestRelativePath,
                Idempotent: true);
        }

        var missing = plan.Articles
            .Where(article => article.TargetState == TargetState.Missing)
            .ToArray();
        if (plan.Articles.Any(article => article.TargetState == TargetState.Conflict))
        {
            throw new SubstackImportException(
                SubstackImportFailure.Conflict,
                "At least one destination article differs from the planned import; no files were overwritten.");
        }

        if (missing.Length > 0)
        {
            await StageAndInstallAsync(plan, missing, cancellationToken);
        }

        var completedAt = UtcTimestamp(timeProvider.GetUtcNow());
        var succeeded = plan.Manifest with
        {
            State = ImportManifestProtocolV1.Succeeded,
            CompletedAt = completedAt
        };
        ValidateManifest(succeeded);
        var manifestBytes = SerializeManifest(succeeded);
        var manifestPath = workspace.Resolve(plan.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var idempotentManifest = await WriteManifestWithoutOverwriteAsync(
            manifestPath,
            manifestBytes,
            succeeded,
            cancellationToken);

        return new SubstackImportApplyResult(
            idempotentManifest ?? succeeded,
            plan.PlanDigest,
            plan.ManifestRelativePath,
            Idempotent: idempotentManifest is not null || missing.Length == 0);
    }

    private async Task<ImportPlan> BuildPlanAsync(
        string exportDirectory,
        CancellationToken cancellationToken)
    {
        var snapshot = await _reader.ReadAsync(exportDirectory, cancellationToken);
        using var previewWorkspace = TemporaryImportWorkspace.Create();
        var previewStore = CreateMediaStore(previewWorkspace.Root, contract.ContentRoot);
        var articles = new List<PlannedArticle>(snapshot.Posts.Count);
        var manifestArticles = new List<ImportArticleV1>(snapshot.Posts.Count);
        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var post in snapshot.Posts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var planned = await PlanArticleAsync(
                snapshot,
                post,
                previewWorkspace.Root,
                previewStore,
                usedSlugs,
                cancellationToken);
            articles.Add(planned);
            manifestArticles.Add(planned.Manifest);
        }

        var identityDigest = ComputeIdentityDigest(snapshot, articles);
        var importId = "substack-" + identityDigest["sha256:".Length..][..32];
        var manifestRelativePath = JoinRepositoryPath(
            contract.ContentRoot,
            ImportRecordsDirectory,
            importId + ".json");
        var manifestState = await FingerprintFileAsync(manifestRelativePath, cancellationToken);
        var planDigest = ComputePlanDigest(identityDigest, articles, manifestRelativePath, manifestState);
        var startedAt = UtcTimestamp(timeProvider.GetUtcNow());
        var summary = new ImportSummaryV1(
            Discovered: manifestArticles.Count,
            Imported: manifestArticles.Count(article => article.Disposition == ImportManifestProtocolV1.Imported),
            Skipped: manifestArticles.Count(article => article.Disposition == ImportManifestProtocolV1.Skipped),
            Failed: manifestArticles.Count(article => article.Disposition == ImportManifestProtocolV1.Failed),
            ReviewRequired: manifestArticles.Count(article => article.Disposition == ImportManifestProtocolV1.ReviewRequired));
        var manifest = new ImportManifestV1(
            ImportManifestProtocolV1.Schema,
            importId,
            new ImportSourceV1(
                "substack-export",
                FeedUrl: null,
                ArchiveUrl: null,
                ExportDigest: snapshot.ExportDigest,
                DiscoveredAt: startedAt),
            ImportManifestProtocolV1.AwaitingApproval,
            startedAt,
            CompletedAt: null,
            summary,
            manifestArticles,
            Exclusions: []);
        ValidateManifest(manifest);

        return new ImportPlan(
            snapshot,
            manifest,
            planDigest,
            manifestRelativePath,
            articles);
    }

    private async Task<PlannedArticle> PlanArticleAsync(
        SubstackExportSnapshot snapshot,
        SubstackExportPost post,
        string previewRoot,
        ArticleMediaStore previewStore,
        ISet<string> usedSlugs,
        CancellationToken cancellationToken)
    {
        var sourceId = post.SourceId ?? post.CanonicalUrl;
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 500)
        {
            throw Malformed($"posts.csv row {post.RowNumber} needs a source id or canonical URL of at most 500 characters.");
        }

        if (string.IsNullOrWhiteSpace(post.Title) || post.Title.Length > 1_000)
        {
            throw Malformed($"posts.csv row {post.RowNumber} needs a title of at most 1,000 characters.");
        }

        var sourceUrl = NormalizeHttpUrl(post.CanonicalUrl);
        var metadata = JsonSerializer.SerializeToElement(post.Metadata, JsonOptions);
        var source = new ImportSourceArticleV1(
            sourceId,
            sourceUrl,
            post.Title,
            NormalizeDateTime(post.PublishedAt),
            SourceDigest: null,
            metadata);

        if (IsFalse(post.IsPublished) || IsExcludedType(post.Type))
        {
            return PlannedArticle.NonWriting(new ImportArticleV1(
                source,
                ImportManifestProtocolV1.Skipped,
                IsFalse(post.IsPublished)
                    ? "The export marks this item as unpublished."
                    : $"Substack item type '{post.Type}' is not an authored publication article.",
                DestinationPath: null,
                ResultDigest: null,
                ResultMetadata: null,
                Transformations: [],
                Warnings: [],
                Fidelity: [new ImportFidelityV1("body", "unverified", "The item was intentionally skipped.")],
                Assets: []));
        }

        if (IsPaidOnly(post.Audience))
        {
            return PlannedArticle.NonWriting(ReviewRequired(
                source,
                "The export marks this item as paid/private; review publication authority before import."));
        }

        byte[] bodyBytes;
        try
        {
            bodyBytes = await _reader.ReadVerifiedBodyAsync(snapshot, post, cancellationToken);
        }
        catch (SubstackImportException exception) when (exception.Failure == SubstackImportFailure.MalformedExport)
        {
            return PlannedArticle.NonWriting(ReviewRequired(source, exception.Message));
        }

        var bodyName = post.BodyRelativePath ?? $"posts.csv#row-{post.RowNumber}";
        var bodyHtml = SubstackExportReader.DecodeBody(bodyBytes, bodyName);
        var sourceDigest = Sha256(bodyBytes);
        source = source with { SourceDigest = sourceDigest };

        var slug = NormalizeSlug(post.Slug, post.Title, sourceId);
        if (!usedSlugs.Add(slug))
        {
            return PlannedArticle.NonWriting(ReviewRequired(
                source,
                $"More than one exported article maps to destination slug '{slug}'."));
        }

        var transformations = new List<ImportTransformationV1>();
        if (!StringComparer.Ordinal.Equals(post.Slug, slug))
        {
            transformations.Add(new ImportTransformationV1(
                "normalized-slug",
                $"Mapped the source slug to portable destination slug '{slug}'.",
                "posts.csv#/slug",
                null));
        }

        var articlePlaceholder = Path.Combine(
            previewRoot,
            contract.ContentRoot.Replace('/', Path.DirectorySeparatorChar),
            slug,
            contract.ArticleFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(articlePlaceholder)!);
        await File.WriteAllTextAsync(articlePlaceholder, string.Empty, Utf8NoBom, cancellationToken);

        var resolutions = new Dictionary<int, HtmlImageResolution>();
        var plannedAssets = new List<PlannedAsset>();
        var manifestAssets = new List<ImportAssetV1>();
        var assetReviewReasons = new List<string>();
        foreach (var image in _html.InspectImages(bodyHtml))
        {
            var sourceAsset = ResolveLocalAsset(snapshot, post, image);
            if (sourceAsset is null)
            {
                var reason = $"Image '{image.Source}' has no guarded local export copy and was not fetched.";
                assetReviewReasons.Add(reason);
                var unresolvedUrl = ResolveAssetSourceUrl(post.CanonicalUrl, image.Source);
                if (unresolvedUrl is not null)
                {
                    manifestAssets.Add(new ImportAssetV1(
                        unresolvedUrl,
                        SourceDigest: null,
                        ImportManifestProtocolV1.ReviewRequired,
                        reason,
                        DestinationPath: null,
                        ResultDigest: null,
                        Transformations: [],
                        Warnings: [new ImportWarningV1(
                            "media-review-required",
                            "error",
                            reason,
                            image.SourcePointer)]));
                }

                continue;
            }

            try
            {
                var bytes = await _reader.ReadVerifiedAssetAsync(
                    snapshot,
                    sourceAsset.RelativePath,
                    configuration.Media.MaximumAssetBytes,
                    cancellationToken);
                var extension = Path.GetExtension(sourceAsset.RelativePath).ToLowerInvariant();
                var receipt = await previewStore.IngestAsync(
                    slug,
                    $"asset-{image.Index + 1:D4}{extension}",
                    bytes,
                    cancellationToken);
                var destination = JoinRepositoryPath(
                    contract.ContentRoot,
                    slug,
                    contract.MediaDirectoryName,
                    receipt.FileName);
                resolutions[image.Index] = new HtmlImageResolution(
                    JoinRepositoryPath(contract.MediaDirectoryName, receipt.FileName));
                plannedAssets.Add(new PlannedAsset(
                    sourceAsset.RelativePath,
                    destination,
                    receipt.FileName,
                    "sha256:" + receipt.Sha256));
                manifestAssets.Add(new ImportAssetV1(
                    sourceAsset.SourceUrl,
                    "sha256:" + receipt.Sha256,
                    ImportManifestProtocolV1.Imported,
                    Reason: null,
                    destination,
                    "sha256:" + receipt.Sha256,
                    [new ImportTransformationV1(
                        "localized-owned-copy",
                        "Copied the exported image into deterministic article-owned media.",
                        image.SourcePointer,
                        destination)],
                    Warnings: []));
            }
            catch (Exception exception) when (
                exception is MediaAssetException or WorkspacePathException)
            {
                assetReviewReasons.Add($"Image '{image.Source}' could not be imported safely: {exception.Message}");
            }
        }

        var converted = _html.Convert(bodyHtml, resolutions);
        transformations.AddRange(converted.Transformations);
        transformations.Add(new ImportTransformationV1(
            "html-to-markdown",
            "Converted the exported Substack HTML body to canonical Markdown.",
            bodyName,
            JoinRepositoryPath(contract.ContentRoot, slug, contract.ArticleFileName)));
        var warnings = converted.Warnings.ToList();
        foreach (var reason in assetReviewReasons)
        {
            warnings.Add(new ImportWarningV1("media-review-required", "error", reason, bodyName));
        }

        var frontmatter = BuildFrontmatter(post, sourceId, sourceUrl, sourceDigest, slug);
        var articleBytes = Utf8NoBom.GetBytes(frontmatter + converted.Markdown);
        var destinationPath = JoinRepositoryPath(contract.ContentRoot, slug, contract.ArticleFileName);
        var resultDigest = Sha256(articleBytes);
        var resultMetadata = JsonSerializer.SerializeToElement(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["author"] = post.Author,
            ["canonicalUrl"] = sourceUrl,
            ["slug"] = slug,
            ["subtitle"] = post.Subtitle,
            ["tags"] = ParseTags(post.Tags),
            ["title"] = post.Title
        }, JsonOptions);

        var manifest = new ImportArticleV1(
            source,
            assetReviewReasons.Count == 0 && !warnings.Any(warning => warning.Severity == "error")
                ? ImportManifestProtocolV1.Imported
                : ImportManifestProtocolV1.ReviewRequired,
            assetReviewReasons.Count == 0 && !warnings.Any(warning => warning.Severity == "error")
                ? null
                : "One or more article structures or media assets require review before apply.",
            destinationPath,
            resultDigest,
            resultMetadata,
            transformations,
            warnings,
            [
                new ImportFidelityV1("metadata", "preserved", "All source CSV fields are retained in source.metadata."),
                new ImportFidelityV1("body", converted.Warnings.Count == 0 ? "transformed" : "degraded", "HTML was converted to Markdown with recorded warnings."),
                new ImportFidelityV1("media", assetReviewReasons.Count == 0 ? "preserved" : "missing", assetReviewReasons.Count == 0 ? "All displayed images have local owned copies." : "At least one displayed image lacks a safe local copy.")
            ],
            manifestAssets);

        var expectedFiles = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [contract.ArticleFileName] = resultDigest
        };
        foreach (var asset in plannedAssets)
        {
            expectedFiles[JoinRepositoryPath(contract.MediaDirectoryName, asset.FileName)] = asset.Sha256;
        }

        var target = await InspectTargetAsync(slug, expectedFiles, cancellationToken);
        if (target.State == TargetState.Conflict)
        {
            manifest = manifest with
            {
                Disposition = ImportManifestProtocolV1.ReviewRequired,
                Reason = $"Destination article '{slug}' already exists with different or additional content."
            };
        }

        return new PlannedArticle(
            slug,
            manifest,
            articleBytes,
            plannedAssets,
            expectedFiles,
            target.State,
            target.Fingerprint);
    }

    private async Task StageAndInstallAsync(
        ImportPlan plan,
        IReadOnlyList<PlannedArticle> missing,
        CancellationToken cancellationToken)
    {
        var stagingName = $".tezuri-import-staging-{Guid.NewGuid():N}";
        var stagingRootRelative = JoinRepositoryPath(contract.ContentRoot, stagingName);
        var stagingRoot = workspace.Resolve(stagingRootRelative.Replace('/', Path.DirectorySeparatorChar));
        var stagingStore = CreateMediaStore(workspace.Root, stagingRootRelative);
        Directory.CreateDirectory(stagingRoot);

        try
        {
            foreach (var article in missing)
            {
                var stagedArticlePath = workspace.Resolve(JoinRepositoryPath(
                    stagingRootRelative,
                    article.Slug,
                    contract.ArticleFileName).Replace('/', Path.DirectorySeparatorChar));
                await writer.WriteAsync(stagedArticlePath, article.ArticleBytes, cancellationToken);

                for (var assetIndex = 0; assetIndex < article.Assets.Count; assetIndex++)
                {
                    var asset = article.Assets[assetIndex];
                    var bytes = await _reader.ReadVerifiedAssetAsync(
                        plan.Snapshot,
                        asset.SourceRelativePath,
                        configuration.Media.MaximumAssetBytes,
                        cancellationToken);
                    var extension = Path.GetExtension(asset.SourceRelativePath).ToLowerInvariant();
                    var receipt = await stagingStore.IngestAsync(
                        article.Slug,
                        $"asset-{assetIndex + 1:D4}{extension}",
                        bytes,
                        cancellationToken);
                    if (!StringComparer.Ordinal.Equals(receipt.FileName, asset.FileName))
                    {
                        throw new SubstackImportException(
                            SubstackImportFailure.PlanChanged,
                            $"Export asset '{asset.SourceRelativePath}' changed after preview.");
                    }
                }
            }

            foreach (var article in missing)
            {
                var current = await InspectTargetAsync(article.Slug, article.ExpectedFiles, cancellationToken);
                if (current.State != TargetState.Missing)
                {
                    throw new SubstackImportException(
                        SubstackImportFailure.PlanChanged,
                        $"Destination article '{article.Slug}' changed while the import was staged.");
                }
            }

            foreach (var article in missing)
            {
                var stagedDirectory = workspace.Resolve(JoinRepositoryPath(
                    stagingRootRelative,
                    article.Slug).Replace('/', Path.DirectorySeparatorChar));
                var targetDirectory = workspace.Resolve(JoinRepositoryPath(
                    contract.ContentRoot,
                    article.Slug).Replace('/', Path.DirectorySeparatorChar));
                Directory.Move(stagedDirectory, targetDirectory);
            }
        }
        finally
        {
            DeleteOwnedStagingDirectory(stagingRoot, stagingName);
        }
    }

    private ArticleMediaStore CreateMediaStore(string root, string contentRoot)
    {
        var stagingContract = contract with { ContentRoot = contentRoot };
        return new ArticleMediaStore(
            new WorkspacePathGuard(root),
            stagingContract,
            configuration,
            writer);
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
        if (!snapshot.Files.ContainsKey(exportRelative) ||
            !Uri.TryCreate(post.CanonicalUrl, UriKind.Absolute, out var articleUrl) ||
            articleUrl.Scheme is not "http" and not "https" ||
            !Uri.TryCreate(articleUrl, image.Source, out var sourceUrl) ||
            sourceUrl.Scheme is not "http" and not "https")
        {
            return null;
        }

        return new LocalAsset(exportRelative, sourceUrl.AbsoluteUri);
    }

    private static string? ResolveAssetSourceUrl(string? canonicalUrl, string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme is "http" or "https" ? absolute.AbsoluteUri : null;
        }

        return Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var article) &&
               article.Scheme is "http" or "https" &&
               Uri.TryCreate(article, source, out var resolved) &&
               resolved.Scheme is "http" or "https"
            ? resolved.AbsoluteUri
            : null;
    }

    private async Task<TargetInspection> InspectTargetAsync(
        string slug,
        IReadOnlyDictionary<string, string> expectedFiles,
        CancellationToken cancellationToken)
    {
        var relative = JoinRepositoryPath(contract.ContentRoot, slug);
        var target = workspace.Resolve(relative.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(target) && !File.Exists(target))
        {
            return new TargetInspection(TargetState.Missing, "missing");
        }

        if (!Directory.Exists(target))
        {
            return new TargetInspection(TargetState.Conflict, "non-directory");
        }

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(target);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Dequeue()).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var guarded = workspace.Resolve(workspace.Relative(entry).Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(guarded))
                {
                    pending.Enqueue(guarded);
                    continue;
                }

                var key = Path.GetRelativePath(target, guarded).Replace('\\', '/');
                actual[key] = await HashFilePathAsync(guarded, cancellationToken);
            }
        }

        var fingerprint = HashStrings(actual.Select(pair => $"{pair.Key}\0{pair.Value}"));
        var exact = actual.Count == expectedFiles.Count &&
                    actual.All(pair => expectedFiles.TryGetValue(pair.Key, out var digest) && digest == pair.Value);
        return new TargetInspection(exact ? TargetState.Exact : TargetState.Conflict, fingerprint);
    }

    private async Task<ImportManifestV1?> TryReadExistingManifestAsync(
        ImportPlan plan,
        CancellationToken cancellationToken)
    {
        var path = workspace.Resolve(plan.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<ImportManifestV1>(
                await File.ReadAllBytesAsync(path, cancellationToken),
                JsonOptions);
            if (manifest is null ||
                manifest.Schema != ImportManifestProtocolV1.Schema ||
                manifest.ImportId != plan.Manifest.ImportId ||
                manifest.State != ImportManifestProtocolV1.Succeeded)
            {
                throw new JsonException("The existing manifest does not identify the completed import.");
            }

            ValidateManifest(manifest);
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new SubstackImportException(
                SubstackImportFailure.Conflict,
                $"Existing import manifest '{plan.ManifestRelativePath}' is incompatible with this plan.",
                exception);
        }
    }

    private async Task<ImportManifestV1?> WriteManifestWithoutOverwriteAsync(
        string targetPath,
        byte[] bytes,
        ImportManifestV1 expected,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath))
        {
            return await ReadExistingManifestAtPathAsync(targetPath, expected, cancellationToken);
        }

        var stagePath = targetPath + $".tezuri-{Guid.NewGuid():N}.tmp";
        try
        {
            await writer.WriteAsync(stagePath, bytes, cancellationToken);
            try
            {
                File.Move(stagePath, targetPath, overwrite: false);
                return null;
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                return await ReadExistingManifestAtPathAsync(targetPath, expected, cancellationToken);
            }
        }
        finally
        {
            if (File.Exists(stagePath))
            {
                File.Delete(stagePath);
            }
        }
    }

    private static async Task<ImportManifestV1> ReadExistingManifestAtPathAsync(
        string path,
        ImportManifestV1 expected,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = JsonSerializer.Deserialize<ImportManifestV1>(
                await File.ReadAllBytesAsync(path, cancellationToken),
                JsonOptions);
            if (existing is null ||
                existing.Schema != expected.Schema ||
                existing.ImportId != expected.ImportId ||
                existing.State != ImportManifestProtocolV1.Succeeded)
            {
                throw new JsonException("Manifest identity or state differs.");
            }

            return existing;
        }
        catch (JsonException exception)
        {
            throw new SubstackImportException(
                SubstackImportFailure.Conflict,
                "A different file already occupies the deterministic import manifest path.",
                exception);
        }
    }

    private string ComputeIdentityDigest(
        SubstackExportSnapshot snapshot,
        IReadOnlyList<PlannedArticle> articles)
    {
        var values = new List<string>
        {
            ImporterVersion,
            snapshot.ExportDirectory,
            snapshot.ExportDigest,
            configuration.Schema,
            contract.Version.ToString(CultureInfo.InvariantCulture),
            contract.ContentRoot,
            contract.ArticleFileName,
            contract.MediaDirectoryName,
            configuration.Articles.MetadataSchema,
            configuration.Media.RequireOwnedAssets.ToString(CultureInfo.InvariantCulture),
            configuration.Media.MaximumAssetBytes.ToString(CultureInfo.InvariantCulture)
        };
        values.AddRange(configuration.Media.AllowedExtensions.Order(StringComparer.Ordinal));
        foreach (var article in articles)
        {
            values.Add(article.Manifest.Source.Id);
            values.Add(article.Manifest.Disposition);
            foreach (var file in article.ExpectedFiles)
            {
                values.Add(file.Key);
                values.Add(file.Value);
            }
        }

        return HashStrings(values);
    }

    private static string ComputePlanDigest(
        string identityDigest,
        IReadOnlyList<PlannedArticle> articles,
        string manifestPath,
        string manifestState) => HashStrings(
        [
            identityDigest,
            .. articles.SelectMany(article => new[]
            {
                article.Slug,
                article.TargetState.ToString(),
                article.TargetFingerprint
            }),
            manifestPath,
            manifestState
        ]);

    private async Task<string> FingerprintFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = workspace.Resolve(relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return "missing";
        }

        return File.Exists(path)
            ? await HashFilePathAsync(path, cancellationToken)
            : "directory";
    }

    private static async Task<string> HashFilePathAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
    }

    private static byte[] SerializeManifest(ImportManifestV1 manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOptions) + "\n";
        return Utf8NoBom.GetBytes(json);
    }

    private static void ValidateManifest(ImportManifestV1 manifest)
    {
        if (manifest.Schema != ImportManifestProtocolV1.Schema ||
            manifest.ImportId.Length is < 1 or > 128 ||
            manifest.Source.Kind != "substack-export" ||
            !IsDigest(manifest.Source.ExportDigest) ||
            manifest.Summary.Discovered != manifest.Articles.Count ||
            manifest.Summary.Imported != manifest.Articles.Count(article => article.Disposition == "imported") ||
            manifest.Summary.Skipped != manifest.Articles.Count(article => article.Disposition == "skipped") ||
            manifest.Summary.Failed != manifest.Articles.Count(article => article.Disposition == "failed") ||
            manifest.Summary.ReviewRequired != manifest.Articles.Count(article => article.Disposition == "review-required") ||
            (manifest.State == ImportManifestProtocolV1.Succeeded) != (manifest.CompletedAt is not null))
        {
            throw new SubstackImportException(
                SubstackImportFailure.MalformedExport,
                "The assembled import manifest does not satisfy the v1 contract.");
        }

        foreach (var article in manifest.Articles)
        {
            if (string.IsNullOrWhiteSpace(article.Source.Id) ||
                string.IsNullOrWhiteSpace(article.Source.Title) ||
                article.Transformations is null ||
                article.Warnings is null ||
                article.Fidelity is null ||
                article.Assets is null ||
                (article.Disposition == "imported" &&
                 (article.DestinationPath is null || !IsDigest(article.ResultDigest))) ||
                (article.Disposition is "skipped" or "failed" or "review-required" &&
                 string.IsNullOrWhiteSpace(article.Reason)))
            {
                throw new SubstackImportException(
                    SubstackImportFailure.MalformedExport,
                    $"Import article '{article.Source.Id}' does not satisfy the v1 manifest contract.");
            }
        }
    }

    private static ImportArticleV1 ReviewRequired(ImportSourceArticleV1 source, string reason) => new(
        source,
        ImportManifestProtocolV1.ReviewRequired,
        reason,
        DestinationPath: null,
        ResultDigest: null,
        ResultMetadata: null,
        Transformations: [],
        Warnings: [],
        Fidelity: [new ImportFidelityV1("body", "unverified", reason)],
        Assets: []);

    private static string BuildFrontmatter(
        SubstackExportPost post,
        string sourceId,
        string? sourceUrl,
        string sourceDigest,
        string slug)
    {
        var result = new StringBuilder();
        result.AppendLine("---");
        AppendYaml(result, "id", "substack:" + sourceId);
        AppendYaml(result, "title", post.Title!);
        AppendYaml(result, "description", post.Subtitle);
        AppendYaml(result, "author", post.Author);
        AppendYaml(result, "date", NormalizeDateTime(post.PublishedAt));
        AppendYaml(result, "updated", NormalizeDateTime(post.UpdatedAt));
        AppendYaml(result, "slug", slug);
        AppendYaml(result, "canonicalUrl", sourceUrl);
        var tags = ParseTags(post.Tags);
        if (tags.Count > 0)
        {
            result.AppendLine("tags:");
            foreach (var tag in tags)
            {
                result.Append("  - ").AppendLine(YamlString(tag));
            }
        }

        result.AppendLine("tezuriImport:");
        result.Append("  sourceKind: ").AppendLine(YamlString("substack-export"));
        result.Append("  sourceId: ").AppendLine(YamlString(sourceId));
        if (sourceUrl is not null)
        {
            result.Append("  sourceUrl: ").AppendLine(YamlString(sourceUrl));
        }

        result.Append("  sourceDigest: ").AppendLine(YamlString(sourceDigest));
        result.AppendLine("---");
        return result.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void AppendYaml(StringBuilder result, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            result.Append(key).Append(": ").AppendLine(YamlString(value));
        }
    }

    private static string YamlString(string value) => JsonSerializer.Serialize(value);

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
                    return parsed.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).ToArray();
                }
            }
            catch (JsonException)
            {
                // Preserve the unparsed source in manifest metadata; fall back to a scalar tag below.
            }
        }

        return tags.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string NormalizeSlug(string? sourceSlug, string title, string sourceId)
    {
        if (!string.IsNullOrWhiteSpace(sourceSlug) &&
            sourceSlug.Length <= 100 &&
            sourceSlug[0] is >= 'a' and <= 'z' &&
            sourceSlug.All(character => character is >= 'a' and <= 'z' || char.IsAsciiDigit(character) || character == '-') &&
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

    private static string? NormalizeHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not "http" and not "https")
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

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
            ? parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
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

    private static string JoinRepositoryPath(params string[] segments) =>
        string.Join('/', segments.Select(segment => segment.Trim('/')));

    private static string UtcTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static bool IsDigest(string? value) =>
        value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).ToString().All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string HashStrings(IEnumerable<string> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in values)
        {
            hash.AppendData(Utf8NoBom.GetBytes(value));
            hash.AppendData([0]);
        }

        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void DeleteOwnedStagingDirectory(string path, string expectedName)
    {
        if (!StringComparer.Ordinal.Equals(Path.GetFileName(path), expectedName) ||
            !expectedName.StartsWith(".tezuri-import-staging-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to clean an unexpected import staging directory.");
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static SubstackImportException Malformed(string message) =>
        new(SubstackImportFailure.MalformedExport, message);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record ImportPlan(
        SubstackExportSnapshot Snapshot,
        ImportManifestV1 Manifest,
        string PlanDigest,
        string ManifestRelativePath,
        IReadOnlyList<PlannedArticle> Articles);

    private sealed record PlannedArticle(
        string Slug,
        ImportArticleV1 Manifest,
        byte[] ArticleBytes,
        IReadOnlyList<PlannedAsset> Assets,
        IReadOnlyDictionary<string, string> ExpectedFiles,
        TargetState TargetState,
        string TargetFingerprint)
    {
        public static PlannedArticle NonWriting(ImportArticleV1 manifest) => new(
            Slug: string.Empty,
            manifest,
            ArticleBytes: [],
            Assets: [],
            ExpectedFiles: new Dictionary<string, string>(),
            TargetState.Exact,
            TargetFingerprint: "not-applicable");
    }

    private sealed record PlannedAsset(
        string SourceRelativePath,
        string DestinationPath,
        string FileName,
        string Sha256);

    private sealed record LocalAsset(string RelativePath, string SourceUrl);

    private sealed record TargetInspection(TargetState State, string Fingerprint);

    private enum TargetState
    {
        Missing,
        Exact,
        Conflict
    }

    private sealed class TemporaryImportWorkspace : IDisposable
    {
        private const string ParentName = "tezuri-import-preview";

        private TemporaryImportWorkspace(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryImportWorkspace Create()
        {
            var parent = Path.Combine(Path.GetTempPath(), ParentName);
            var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporaryImportWorkspace(root);
        }

        public void Dispose()
        {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), ParentName)) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(Root);
            if (resolved.StartsWith(parent, PathComparison) && Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}

public sealed record SubstackImportPreview(
    ImportManifestV1 Manifest,
    string PlanDigest,
    string ManifestRelativePath);

public sealed record SubstackImportApplyResult(
    ImportManifestV1 Manifest,
    string PlanDigest,
    string ManifestRelativePath,
    bool Idempotent);
