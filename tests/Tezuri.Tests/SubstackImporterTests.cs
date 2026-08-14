using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tezuri.App.Tests;
using Tezuri.Articles;
using Tezuri.Import;

namespace Tezuri.Import.Tests;

/// <summary>
/// The importer creates Koan entities, so it runs inside the shared host rather than against a bare
/// directory. Each test writes its own export under a unique folder and uses unique slugs, so they
/// can share one workspace without seeing each other's articles.
/// </summary>
[Collection(TezuriHostCollection.Name)]
public sealed class SubstackImporterTests(TezuriApplicationFactory factory)
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    [Fact]
    public async Task PreviewReportsWhatWouldHappenAndWritesNothing()
    {
        var export = CreateExport("preview", includeImage: true, includeScript: true);
        var before = SnapshotArticles();

        var preview = await Importer.PreviewAsync(export.Directory, TestContext.Current.CancellationToken);

        Assert.Equal(before, SnapshotArticles());
        Assert.Equal(1, preview.Discovered);
        Assert.Equal(1, preview.Ready);
        var item = Assert.Single(preview.Items);
        Assert.Equal(SubstackImportDisposition.Import, item.Disposition);
        Assert.Equal(export.FirstSlug, item.Slug);
        Assert.Equal(1, item.Images);
        Assert.False(Directory.Exists(ArticleFolder(export.FirstSlug)));
    }

    [Fact]
    public async Task ApplyCreatesAnArticleEntityWithOwnedMediaAndSanitizedBody()
    {
        var export = CreateExport("apply", includeImage: true, includeScript: true);

        var applied = await Importer.ApplyAsync(export.Directory, TestContext.Current.CancellationToken);

        Assert.Equal(1, applied.Ready);
        var item = Assert.Single(applied.Items);
        Assert.Equal(SubstackImportDisposition.Imported, item.Disposition);

        // The entity is canonical; the Markdown beside it is the generated artifact.
        var article = await Article.Get(export.FirstSlug, TestContext.Current.CancellationToken);
        Assert.NotNull(article);
        Assert.Equal("First, Post", article.Title);
        Assert.Equal("A subtitle", article.Subtitle);
        Assert.False(article.Draft);
        Assert.Equal("2026-03-17", article.Date);
        Assert.Equal(["craft", "care"], article.Tags);
        Assert.Contains("## A heading", article.Body, StringComparison.Ordinal);
        Assert.Contains("**important**", article.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", article.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert('never')", article.Body, StringComparison.Ordinal);

        // Metadata Tezuri has no control for survives in the extension data and reaches frontmatter.
        Assert.Equal(
            "https://example.test/p/" + export.FirstSlug,
            article.Meta["canonicalUrl"].ToString());

        Assert.True(File.Exists(Path.Combine(ArticleFolder(export.FirstSlug), "article.json")));
        var markdown = await File.ReadAllTextAsync(
            Path.Combine(ArticleFolder(export.FirstSlug), "index.md"),
            TestContext.Current.CancellationToken);
        Assert.Contains("![One pixel](media/", markdown, StringComparison.Ordinal);
        Assert.Contains("canonicalUrl:", markdown, StringComparison.Ordinal);

        var asset = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(ArticleFolder(export.FirstSlug), "media")));
        Assert.EndsWith(".png", asset, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(asset), markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyIsIdempotentAndNeverOverwritesAnExistingArticle()
    {
        var export = CreateExport("idempotent");
        await Importer.ApplyAsync(export.Directory, TestContext.Current.CancellationToken);
        var markdownPath = Path.Combine(ArticleFolder(export.FirstSlug), "index.md");
        var firstBytes = await File.ReadAllBytesAsync(markdownPath, TestContext.Current.CancellationToken);

        var rerun = await Importer.ApplyAsync(export.Directory, TestContext.Current.CancellationToken);

        var item = Assert.Single(rerun.Items);
        Assert.Equal(SubstackImportDisposition.Skipped, item.Disposition);
        Assert.Contains("already exists", item.Reason!, StringComparison.Ordinal);
        Assert.Equal(
            firstBytes,
            await File.ReadAllBytesAsync(markdownPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUnrelatedFolderAtTheDestinationIsLeftUntouched()
    {
        var export = CreateExport("occupied");
        var folder = ArticleFolder(export.FirstSlug);
        Directory.CreateDirectory(folder);
        var owned = Path.Combine(folder, "index.md");
        await File.WriteAllTextAsync(owned, "owner edit\n", Utf8NoBom, TestContext.Current.CancellationToken);

        var applied = await Importer.ApplyAsync(export.Directory, TestContext.Current.CancellationToken);

        var item = Assert.Single(applied.Items);
        Assert.Equal(SubstackImportDisposition.Skipped, item.Disposition);
        Assert.Equal(
            "owner edit\n",
            await File.ReadAllTextAsync(owned, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoteImageIsHeldForReviewAndNeverFetchedOrApplied()
    {
        var export = CreateExport("remote-image");
        await File.WriteAllTextAsync(
            Path.Combine(export.AbsoluteRoot, "posts", "1.html"),
            "<p>Body.</p><img src=\"https://cdn.example.test/photo.png\" alt=\"Remote\">",
            Utf8NoBom,
            TestContext.Current.CancellationToken);

        var applied = await Importer.ApplyAsync(export.Directory, TestContext.Current.CancellationToken);

        var item = Assert.Single(applied.Items);
        Assert.Equal(SubstackImportDisposition.NeedsReview, item.Disposition);
        Assert.Contains(item.Warnings, warning =>
            warning.Contains("never fetches", StringComparison.Ordinal));
        Assert.False(Directory.Exists(ArticleFolder(export.FirstSlug)));
    }

    [Fact]
    public async Task UnpublishedAndNonArticleItemsAreSkippedWithAReason()
    {
        var export = CreateExport("skipped", firstRowOverride:
            "1,\"First, Post\",A subtitle,{slug},2026-03-17T12:00:00Z,https://example.test/p/{slug},note,everyone,false,craft");

        var preview = await Importer.PreviewAsync(export.Directory, TestContext.Current.CancellationToken);

        var item = Assert.Single(preview.Items);
        Assert.Equal(SubstackImportDisposition.Skipped, item.Disposition);
        Assert.Contains("unpublished", item.Reason!, StringComparison.Ordinal);
        Assert.Equal(0, preview.Ready);
        Assert.Equal(1, preview.Skipped);
    }

    [Fact]
    public async Task InvalidUtf8BodyFailsClosed()
    {
        var export = CreateExport("bad-utf8");
        await File.WriteAllBytesAsync(
            Path.Combine(export.AbsoluteRoot, "posts", "1.html"),
            [0x3C, 0x70, 0x3E, 0xC3, 0x28, 0x3C, 0x2F, 0x70, 0x3E],
            TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<SubstackImportException>(() =>
            Importer.PreviewAsync(export.Directory, TestContext.Current.CancellationToken));

        Assert.Equal(SubstackImportFailure.MalformedExport, error.Failure);
        Assert.False(Directory.Exists(ArticleFolder(export.FirstSlug)));
    }

    [Fact]
    public async Task MalformedHtmlFailsClosedWithoutCreatingAnArticle()
    {
        var export = CreateExport("bad-html");
        await File.WriteAllTextAsync(
            Path.Combine(export.AbsoluteRoot, "posts", "1.html"),
            "<p title=\"unterminated>Body</p>",
            Utf8NoBom,
            TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<SubstackImportException>(() =>
            Importer.ApplyAsync(export.Directory, TestContext.Current.CancellationToken));

        Assert.Equal(SubstackImportFailure.MalformedExport, error.Failure);
        Assert.False(Directory.Exists(ArticleFolder(export.FirstSlug)));
    }

    private SubstackImporter Importer =>
        factory.Services.GetRequiredService<SubstackImporter>();

    private string ArticleFolder(string slug) => factory.Resolve($"src/writing/{slug}");

    private string SnapshotArticles()
    {
        var root = factory.Resolve("src/writing");
        var entries = Directory
            .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => Directory.Exists(path)
                ? $"D:{Path.GetRelativePath(root, path).Replace('\\', '/')}"
                : $"F:{Path.GetRelativePath(root, path).Replace('\\', '/')}:{Hash(File.ReadAllBytes(path))}");
        return string.Join('\n', entries);
    }

    /// <summary>
    /// One export per test, with a slug nobody else uses, so the tests can share one workspace.
    /// </summary>
    private ExportFixture CreateExport(
        string name,
        bool includeImage = false,
        bool includeScript = false,
        string? firstRowOverride = null)
    {
        var slug = $"post-{name}";
        var relative = $"imports/{name}";
        var absolute = factory.Resolve(relative);
        Directory.CreateDirectory(Path.Combine(absolute, "posts"));
        Directory.CreateDirectory(Path.Combine(absolute, "assets"));

        var row = firstRowOverride ??
                  "1,\"First, Post\",A subtitle,{slug},2026-03-17T12:00:00Z,https://example.test/p/{slug},newsletter,everyone,true,\"craft,care\"";
        File.WriteAllText(
            Path.Combine(absolute, "posts.csv"),
            string.Join(
                "\r\n",
                "post_id,title,subtitle,slug,post_date,canonical_url,type,audience,is_published,tags",
                row.Replace("{slug}", slug, StringComparison.Ordinal)) + "\r\n",
            Utf8NoBom);

        var body = "<h2>A heading</h2><p>This is <strong>important</strong>.</p>";
        if (includeScript)
        {
            body += "<script>alert('never')</script>";
        }

        if (includeImage)
        {
            body += "<figure><img src=\"../assets/pixel.png\" alt=\"One pixel\"><figcaption>A caption</figcaption></figure>";
            File.WriteAllBytes(Path.Combine(absolute, "assets", "pixel.png"), PngBytes());
        }

        File.WriteAllText(Path.Combine(absolute, "posts", "1.html"), body, Utf8NoBom);
        return new ExportFixture(relative, absolute, slug);
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

    private sealed record ExportFixture(string Directory, string AbsoluteRoot, string FirstSlug);
}
