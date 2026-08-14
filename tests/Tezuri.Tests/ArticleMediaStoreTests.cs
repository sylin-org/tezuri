using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tezuri.Media;
using Tezuri.Workspace;

namespace Tezuri.Media.Tests;

public sealed class ArticleMediaStoreTests
{
    [Fact]
    public async Task StoresStreamAtExactArticleOwnedContentHashPath()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var content = PngBytes();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var store = CreateStore(workspace.Root);
        await using var input = new MemoryStream(content, writable: false);

        var receipt = await store.IngestAsync(
            "patina",
            "cover.png",
            input,
            TestContext.Current.CancellationToken);

        var expectedFileName = expectedSha256 + ".png";
        var expectedRelativePath = $"src/writing/patina/media/{expectedFileName}";
        Assert.Equal(MediaAssetProtocolV1.Receipt, receipt.Protocol);
        Assert.Equal(MediaAssetProtocolV1.Version, receipt.Version);
        Assert.Equal("patina", receipt.ArticleId);
        Assert.Equal("cover.png", receipt.OriginalFileName);
        Assert.Equal(expectedFileName, receipt.FileName);
        Assert.Equal(expectedRelativePath, receipt.RelativePath);
        Assert.Equal("image/png", receipt.MediaType);
        Assert.Equal(expectedSha256, receipt.Sha256);
        Assert.Equal(content.LongLength, receipt.ByteLength);
        Assert.False(receipt.Deduplicated);
        Assert.Equal(content, await File.ReadAllBytesAsync(
            workspace.Resolve(expectedRelativePath),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindsOnlyStoredHashNamedArticleMedia()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var content = PngBytes();
        var store = CreateStore(workspace.Root);
        var receipt = await store.IngestAsync(
            "patina",
            "cover.png",
            content,
            TestContext.Current.CancellationToken);

        var asset = store.Find("patina", receipt.FileName);

        Assert.NotNull(asset);
        Assert.Equal("image/png", asset.MediaType);
        Assert.Equal(content, await File.ReadAllBytesAsync(
            asset.AbsolutePath,
            TestContext.Current.CancellationToken));
        Assert.Null(store.Find(
            "patina",
            new string('0', 64) + ".png"));
        var error = Assert.Throws<MediaAssetException>(() =>
            store.Find("patina", "cover.png"));
        Assert.Equal(MediaAssetFailure.InvalidInput, error.Failure);
    }

    [Fact]
    public async Task DeduplicatesIdenticalContentWithoutWritingAnotherFile()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var content = PngBytes();
        var store = CreateStore(workspace.Root);

        var first = await store.IngestAsync(
            "patina",
            "first.png",
            content,
            TestContext.Current.CancellationToken);
        var second = await store.IngestAsync(
            "patina",
            "renamed.png",
            content,
            TestContext.Current.CancellationToken);

        Assert.False(first.Deduplicated);
        Assert.True(second.Deduplicated);
        Assert.Equal(first.FileName, second.FileName);
        Assert.Equal(first.RelativePath, second.RelativePath);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Single(Directory.EnumerateFiles(workspace.Resolve("src/writing/patina/media")));
    }

    [Theory]
    [InlineData("../outside", "photo.png")]
    [InlineData("patina/other", "photo.png")]
    [InlineData("patina", "../photo.png")]
    [InlineData("patina", "nested/photo.png")]
    [InlineData("patina", "CON.png")]
    [InlineData("CON", "photo.png")]
    public async Task RejectsNonPortableArticleIdsAndFileNames(
        string articleId,
        string fileName)
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var store = CreateStore(workspace.Root);

        var error = await Assert.ThrowsAsync<MediaAssetException>(() => store.IngestAsync(
            articleId,
            fileName,
            PngBytes(),
            TestContext.Current.CancellationToken));

        Assert.Equal(MediaAssetFailure.InvalidInput, error.Failure);
        Assert.False(File.Exists(workspace.Resolve("outside/photo.png")));
    }

    [Fact]
    public async Task RejectsExistingMediaDirectorySymlink()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var mediaPath = workspace.Resolve("src/writing/patina/media");
        Directory.CreateDirectory(workspace.Outside);
        try
        {
            Directory.CreateSymbolicLink(mediaPath, workspace.Outside);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var store = CreateStore(workspace.Root);

        await Assert.ThrowsAsync<WorkspacePathException>(() => store.IngestAsync(
            "patina",
            "photo.png",
            PngBytes(),
            TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(workspace.Outside));
    }

    [Fact]
    public async Task RejectsAssetBeyondConfiguredByteLimit()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var store = CreateStore(workspace.Root, maximumAssetBytes: 8);

        var error = await Assert.ThrowsAsync<MediaAssetException>(() => store.IngestAsync(
            "patina",
            "photo.png",
            PngBytes(),
            TestContext.Current.CancellationToken));

        Assert.Equal(MediaAssetFailure.TooLarge, error.Failure);
        Assert.False(Directory.Exists(workspace.Resolve("src/writing/patina/media")));
    }

    [Fact]
    public async Task RejectsExtensionThatDoesNotMatchContentSignature()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var store = CreateStore(workspace.Root, allowedExtensions: [".jpg", ".png"]);

        var error = await Assert.ThrowsAsync<MediaAssetException>(() => store.IngestAsync(
            "patina",
            "photo.jpg",
            PngBytes(),
            TestContext.Current.CancellationToken));

        Assert.Equal(MediaAssetFailure.ExtensionMismatch, error.Failure);
    }

    [Theory]
    [MemberData(nameof(TruncatedPngs))]
    public async Task RejectsTruncatedPngBeforeWriting(byte[] content)
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var store = CreateStore(workspace.Root);

        var error = await Assert.ThrowsAsync<MediaAssetException>(() => store.IngestAsync(
            "patina",
            "photo.png",
            content,
            TestContext.Current.CancellationToken));

        Assert.Equal(MediaAssetFailure.ExtensionMismatch, error.Failure);
        Assert.False(Directory.Exists(workspace.Resolve("src/writing/patina/media")));
    }

    [Theory]
    [InlineData("drawing.svg")]
    [InlineData("payload.js")]
    [InlineData("page.html")]
    public async Task RejectsSvgAndScriptExtensionsEvenWhenConfigured(string fileName)
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var extension = Path.GetExtension(fileName);
        var store = CreateStore(workspace.Root, allowedExtensions: [extension]);

        var error = await Assert.ThrowsAsync<MediaAssetException>(() => store.IngestAsync(
            "patina",
            fileName,
            Encoding.UTF8.GetBytes("<script>alert('no')</script>"),
            TestContext.Current.CancellationToken));

        Assert.Equal(MediaAssetFailure.UnsupportedMedia, error.Failure);
    }

    [Fact]
    public async Task NeverOverwritesDifferentFileAtDeterministicPath()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreateArticle("patina");
        var content = PngBytes();
        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var target = workspace.Resolve($"src/writing/patina/media/{sha256}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var occupant = content.ToArray();
        occupant[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(target, occupant, TestContext.Current.CancellationToken);
        var store = CreateStore(workspace.Root);

        var error = await Assert.ThrowsAsync<MediaAssetException>(() => store.IngestAsync(
            "patina",
            "photo.png",
            content,
            TestContext.Current.CancellationToken));

        Assert.Equal(MediaAssetFailure.Conflict, error.Failure);
        Assert.Equal(occupant, await File.ReadAllBytesAsync(
            target,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ReceiptRoundTripsAsJson()
    {
        var receipt = new MediaAssetReceiptV1(
            MediaAssetProtocolV1.Receipt,
            MediaAssetProtocolV1.Version,
            "patina",
            "cover.png",
            "abc.png",
            "src/writing/patina/media/abc.png",
            "image/png",
            "abc",
            12,
            false);

        var json = JsonSerializer.Serialize(receipt);
        var roundTrip = JsonSerializer.Deserialize<MediaAssetReceiptV1>(json);

        Assert.Equal(receipt, roundTrip);
    }

    private static ArticleMediaStore CreateStore(
        string root,
        long maximumAssetBytes = 1_048_576,
        IReadOnlyList<string>? allowedExtensions = null)
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
            new MediaPolicyConfiguration(
                RequireOwnedAssets: true,
                MaximumAssetBytes: maximumAssetBytes,
                AllowedExtensions: allowedExtensions ?? [".png"]),
            new ProofConfiguration(
                ".",
                [new ProofCommandConfiguration("test", "npm", ["test"], 300, "dist")]),
            new GitPublicationConfiguration(["src/writing/**"]));

        return new ArticleMediaStore(
            new WorkspacePathGuard(root),
            contract,
            configuration,
            new AtomicFileWriter());
    }

    public static TheoryData<byte[]> TruncatedPngs => new()
    {
        {
            [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
            ]
        },
        {
            [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01
            ]
        }
    };

    private static byte[] PngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _safeParent;

        public TemporaryWorkspace()
        {
            _safeParent = Path.Combine(Path.GetTempPath(), "tezuri-media-tests");
            Root = Path.Combine(_safeParent, Guid.NewGuid().ToString("N"));
            Outside = Path.Combine(_safeParent, Guid.NewGuid().ToString("N") + "-outside");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Outside { get; }

        public void CreateArticle(string articleId)
        {
            var article = Resolve($"src/writing/{articleId}/index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(article)!);
            File.WriteAllText(article, "---\ntitle: Test\n---\nBody\n", new UTF8Encoding(false));
        }

        public string Resolve(string relativePath) => Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose()
        {
            DeleteGuarded(Root);
            DeleteGuarded(Outside);
        }

        private void DeleteGuarded(string target)
        {
            var resolved = Path.GetFullPath(target);
            var expectedParent = Path.GetFullPath(_safeParent) + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(
                    expectedParent,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal) &&
                Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
