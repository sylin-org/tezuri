using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tezuri.Import;
using Tezuri.Workspace;

namespace Tezuri.Import.Tests;

public sealed class SubstackImporterTests
{
    [Fact]
    public async Task PreviewDoesNotMutateWorkspaceAndApplyOwnsMediaIdempotently()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateExport(articleCount: 1, includeImage: true, includeScript: true);
        var before = workspace.Snapshot();
        var importer = workspace.CreateImporter();

        var preview = await importer.PreviewAsync("imports/substack", TestContext.Current.CancellationToken);

        Assert.Equal(before, workspace.Snapshot());
        Assert.Equal(ImportManifestProtocolV1.AwaitingApproval, preview.Manifest.State);
        Assert.Equal(1, preview.Manifest.Summary.Imported);
        Assert.Matches("^sha256:[a-f0-9]{64}$", preview.PlanDigest);

        var applied = await importer.ApplyAsync(
            "imports/substack",
            preview.PlanDigest,
            TestContext.Current.CancellationToken);

        Assert.Equal(ImportManifestProtocolV1.Succeeded, applied.Manifest.State);
        Assert.False(applied.Idempotent);
        var article = await File.ReadAllTextAsync(
            workspace.Resolve("src/writing/first-post/index.md"),
            TestContext.Current.CancellationToken);
        Assert.Contains("## A heading", article, StringComparison.Ordinal);
        Assert.Contains("**important**", article, StringComparison.Ordinal);
        Assert.Contains("![One pixel](media/", article, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", article, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert('never')", article, StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(workspace.Resolve("src/writing/first-post/media")));

        var rerunPreview = await importer.PreviewAsync(
            "imports/substack",
            TestContext.Current.CancellationToken);
        var rerun = await importer.ApplyAsync(
            "imports/substack",
            rerunPreview.PlanDigest,
            TestContext.Current.CancellationToken);

        Assert.True(rerun.Idempotent);
        Assert.Equal(applied.Manifest.ImportId, rerun.Manifest.ImportId);
    }

    [Fact]
    public async Task ApplyRejectsExportDriftWithoutWritingArticle()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateExport(articleCount: 1);
        var importer = workspace.CreateImporter();
        var preview = await importer.PreviewAsync("imports/substack", TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(
            workspace.Resolve("imports/substack/posts/1.html"),
            "<p>Changed after preview.</p>",
            TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<SubstackImportException>(() => importer.ApplyAsync(
            "imports/substack",
            preview.PlanDigest,
            TestContext.Current.CancellationToken));

        Assert.Equal(SubstackImportFailure.PlanChanged, error.Failure);
        Assert.False(Directory.Exists(workspace.Resolve("src/writing/first-post")));
    }

    [Fact]
    public async Task ExistingDifferentArticleRequiresReviewAndIsNeverOverwritten()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateExport(articleCount: 1);
        var target = workspace.Resolve("src/writing/first-post/index.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "owner edit\n", TestContext.Current.CancellationToken);
        var importer = workspace.CreateImporter();

        var preview = await importer.PreviewAsync("imports/substack", TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<SubstackImportException>(() => importer.ApplyAsync(
            "imports/substack",
            preview.PlanDigest,
            TestContext.Current.CancellationToken));

        Assert.Equal(1, preview.Manifest.Summary.ReviewRequired);
        Assert.Equal(SubstackImportFailure.ReviewRequired, error.Failure);
        Assert.Equal("owner edit\n", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PartialExactImportCanResumeWithoutOverwritingCompletedArticle()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateExport(articleCount: 2);
        var importer = workspace.CreateImporter();
        var preview = await importer.PreviewAsync("imports/substack", TestContext.Current.CancellationToken);
        var first = await importer.ApplyAsync(
            "imports/substack",
            preview.PlanDigest,
            TestContext.Current.CancellationToken);
        var firstArticle = workspace.Resolve("src/writing/first-post/index.md");
        var firstBytes = await File.ReadAllBytesAsync(firstArticle, TestContext.Current.CancellationToken);
        var manifest = workspace.Resolve(first.ManifestRelativePath);
        File.Delete(manifest);
        Directory.Delete(workspace.Resolve("src/writing/second-post"), recursive: true);

        var resumePreview = await importer.PreviewAsync(
            "imports/substack",
            TestContext.Current.CancellationToken);
        var resumed = await importer.ApplyAsync(
            "imports/substack",
            resumePreview.PlanDigest,
            TestContext.Current.CancellationToken);

        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(firstArticle, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(workspace.Resolve("src/writing/second-post/index.md")));
        Assert.True(File.Exists(workspace.Resolve(resumed.ManifestRelativePath)));
    }

    [Fact]
    public async Task InvalidUtf8BodyFailsClosed()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateExport(articleCount: 1);
        await File.WriteAllBytesAsync(
            workspace.Resolve("imports/substack/posts/1.html"),
            [0x3C, 0x70, 0x3E, 0xC3, 0x28, 0x3C, 0x2F, 0x70, 0x3E],
            TestContext.Current.CancellationToken);
        var importer = workspace.CreateImporter();

        var error = await Assert.ThrowsAsync<SubstackImportException>(() => importer.PreviewAsync(
            "imports/substack",
            TestContext.Current.CancellationToken));

        Assert.Equal(SubstackImportFailure.MalformedExport, error.Failure);
        Assert.False(Directory.Exists(workspace.Resolve("src/writing/first-post")));
    }

    [Fact]
    public async Task MalformedHtmlFailsClosedWithoutWorkspaceOutput()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateExport(articleCount: 1);
        await File.WriteAllTextAsync(
            workspace.Resolve("imports/substack/posts/1.html"),
            "<p title=\"unterminated>Body</p>",
            TestContext.Current.CancellationToken);
        var importer = workspace.CreateImporter();

        var error = await Assert.ThrowsAsync<SubstackImportException>(() => importer.PreviewAsync(
            "imports/substack",
            TestContext.Current.CancellationToken));

        Assert.Equal(SubstackImportFailure.MalformedExport, error.Failure);
        Assert.False(Directory.Exists(workspace.Resolve("src/writing/first-post")));
    }

    [Fact]
    public async Task RemoteImageIsManifestedForReviewAndNeverFetchedOrApplied()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateExport(articleCount: 1);
        await File.WriteAllTextAsync(
            workspace.Resolve("imports/substack/posts/1.html"),
            "<p>Body.</p><img src=\"https://cdn.example.test/photo.png\" alt=\"Remote\">",
            TestContext.Current.CancellationToken);
        var importer = workspace.CreateImporter();

        var preview = await importer.PreviewAsync("imports/substack", TestContext.Current.CancellationToken);
        var article = Assert.Single(preview.Manifest.Articles);
        var asset = Assert.Single(article.Assets);
        var error = await Assert.ThrowsAsync<SubstackImportException>(() => importer.ApplyAsync(
            "imports/substack",
            preview.PlanDigest,
            TestContext.Current.CancellationToken));

        Assert.Equal(ImportManifestProtocolV1.ReviewRequired, article.Disposition);
        Assert.Equal("https://cdn.example.test/photo.png", asset.SourceUrl);
        Assert.Equal(ImportManifestProtocolV1.ReviewRequired, asset.Disposition);
        Assert.Equal(SubstackImportFailure.ReviewRequired, error.Failure);
        Assert.False(Directory.Exists(workspace.Resolve("src/writing/first-post")));
    }

    [Fact]
    public void OptionalManifestMembersAreOmittedRatherThanSerializedAsNull()
    {
        var manifest = new ImportManifestV1(
            ImportManifestProtocolV1.Schema,
            "substack-test",
            new ImportSourceV1("substack-export", null, null, "sha256:" + new string('a', 64), null),
            ImportManifestProtocolV1.AwaitingApproval,
            "2026-08-13T20:00:00Z",
            null,
            new ImportSummaryV1(0, 0, 0, 0, 0),
            [],
            []);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("completedAt", out _));
        Assert.False(root.GetProperty("source").TryGetProperty("feedUrl", out _));
        Assert.False(root.GetProperty("source").TryGetProperty("archiveUrl", out _));
        Assert.False(root.GetProperty("source").TryGetProperty("discoveredAt", out _));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);
        private readonly string _safeParent;

        public TemporaryWorkspace()
        {
            _safeParent = Path.Combine(Path.GetTempPath(), "tezuri-import-tests");
            Root = Path.Combine(_safeParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void CreateExport(int articleCount, bool includeImage = false, bool includeScript = false)
        {
            var export = Resolve("imports/substack");
            Directory.CreateDirectory(Path.Combine(export, "posts"));
            Directory.CreateDirectory(Path.Combine(export, "assets"));
            var rows = new List<string>
            {
                "post_id,title,subtitle,slug,post_date,canonical_url,type,audience,is_published,tags",
                "1,\"First, Post\",A subtitle,first-post,2026-03-17T12:00:00Z,https://example.test/p/first-post,newsletter,everyone,true,\"craft,care\""
            };
            if (articleCount > 1)
            {
                rows.Add("2,Second Post,,second-post,2026-03-18T12:00:00Z,https://example.test/p/second-post,newsletter,everyone,true,architecture");
            }

            File.WriteAllText(Path.Combine(export, "posts.csv"), string.Join("\r\n", rows) + "\r\n", Utf8NoBom);
            var body = "<h2>A heading</h2><p>This is <strong>important</strong>.</p>";
            if (includeScript)
            {
                body += "<script>alert('never')</script>";
            }

            if (includeImage)
            {
                body += "<figure><img src=\"../assets/pixel.png\" alt=\"One pixel\"><figcaption>A caption</figcaption></figure>";
                File.WriteAllBytes(Path.Combine(export, "assets", "pixel.png"), PngBytes());
            }

            File.WriteAllText(Path.Combine(export, "posts", "1.html"), body, Utf8NoBom);
            if (articleCount > 1)
            {
                File.WriteAllText(Path.Combine(export, "posts", "2.html"), "<p>Second body.</p>", Utf8NoBom);
            }
        }

        public SubstackImporter CreateImporter()
        {
            var contract = WorkspaceContract.Default;
            var configuration = new WorkspaceConfigurationV1(
                WorkspaceConfigurationV1.SchemaName,
                new SiteConfiguration("https://example.test"),
                new ArticleLayoutConfiguration(
                    contract.ContentRoot,
                    contract.ArticleFileName,
                    contract.MediaDirectoryName,
                    "schemas/article-v1.schema.json"),
                new MediaPolicyConfiguration(true, 1_048_576, [".png"]),
                new ProofConfiguration(".", [new ProofCommandConfiguration("test", "npm", ["test"], 30, "dist")]),
                new GitPublicationConfiguration(["src/writing/**"]));
            return new SubstackImporter(
                new WorkspacePathGuard(Root),
                contract,
                configuration,
                new AtomicFileWriter(),
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 13, 20, 0, 0, TimeSpan.Zero)));
        }

        public string Snapshot()
        {
            var entries = Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => Directory.Exists(path)
                    ? $"D:{Path.GetRelativePath(Root, path).Replace('\\', '/')}"
                    : $"F:{Path.GetRelativePath(Root, path).Replace('\\', '/')}:{Hash(File.ReadAllBytes(path))}");
            return string.Join('\n', entries);
        }

        public string Resolve(string relativePath) => Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var expectedParent = Path.GetFullPath(_safeParent) + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private static byte[] PngBytes() =>
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x04, 0x00, 0x00, 0x00, 0xB5, 0x1C, 0x0C,
            0x02, 0x00, 0x00, 0x00, 0x0B, 0x49, 0x44, 0x41,
            0x54, 0x78, 0xDA, 0x63, 0x64, 0xF8, 0x0F, 0x00,
            0x01, 0x05, 0x01, 0x01, 0x27, 0x18, 0xE3, 0x66,
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,
            0xAE, 0x42, 0x60, 0x82
        ];
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
