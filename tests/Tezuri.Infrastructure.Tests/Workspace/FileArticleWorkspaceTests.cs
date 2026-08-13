using System.Text;
using Tezuri.Domain.Documents;
using Tezuri.Domain.Workspace;
using Tezuri.Infrastructure.Documents;
using Tezuri.Infrastructure.Workspace;

namespace Tezuri.Infrastructure.Tests.Workspace;

public sealed class FileArticleWorkspaceTests
{
    [Fact]
    public async Task ListsOnlyFolderNativeArticlesInSlugOrder()
    {
        using var temporary = new TemporaryWorkspace();
        temporary.Write("src/writing/zeta/index.md", "---\ntitle: Zeta\n---\nBody\n");
        temporary.Write("src/writing/alpha/index.md", "---\ntitle: Alpha\n---\nBody\n");
        temporary.Write("src/writing/not-an-article/notes.md", "notes");
        var workspace = CreateWorkspace(temporary.Root);

        var articles = await workspace.ListAsync(TestContext.Current.CancellationToken);

        Assert.Collection(
            articles,
            article => Assert.Equal("alpha", article.Slug),
            article => Assert.Equal("zeta", article.Slug));
    }

    [Fact]
    public async Task RejectsSaveWhenFileChangedAfterOpen()
    {
        using var temporary = new TemporaryWorkspace();
        const string relativePath = "src/writing/patina/index.md";
        temporary.Write(relativePath, "---\ntitle: Patina\n---\nBefore\n");
        var workspace = CreateWorkspace(temporary.Root);
        var opened = await workspace.OpenAsync("patina", TestContext.Current.CancellationToken);
        temporary.Write(relativePath, "---\ntitle: Patina\n---\nExternal change\n");

        var error = await Assert.ThrowsAsync<ArticleConflictException>(() =>
            workspace.SaveAsync(
                "patina",
                new SourcePatchSetV1(
                    ArticleSourceProtocolV1.SourcePatchSet,
                    ArticleSourceProtocolV1.Version,
                    opened.Article.Id,
                    opened.Article.RelativePath,
                    opened.Base.Sha256,
                    []),
                TestContext.Current.CancellationToken));

        Assert.Equal(relativePath, error.RelativePath);
        Assert.NotEqual(error.ExpectedSha256, error.ActualSha256);
    }

    [Fact]
    public async Task PreservesExternalEditMadeWhileAtomicReplacementIsStaged()
    {
        using var temporary = new TemporaryWorkspace();
        const string relativePath = "src/writing/patina/index.md";
        const string original = "---\ntitle: Patina\n---\nBefore\n";
        const string external = "---\ntitle: Patina\n---\nExternal change during save\n";
        temporary.Write(relativePath, original);
        var writer = new PausingAtomicFileWriter();
        var workspace = CreateWorkspace(temporary.Root, writer);
        var opened = await workspace.OpenAsync("patina", TestContext.Current.CancellationToken);
        var patch = Replace(opened, "Before", "Tezuri save");

        var save = workspace.SaveAsync("patina", patch, TestContext.Current.CancellationToken);
        await writer.Staged.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        temporary.Write(relativePath, external);
        writer.Resume();

        var error = await Assert.ThrowsAsync<ArticleConflictException>(async () =>
        {
            await save;
        });

        Assert.Equal(ArticleDocumentCodec.HashBytes(Encoding.UTF8.GetBytes(external)), error.ActualSha256);
        Assert.Equal(external, temporary.Read(relativePath));
        Assert.Empty(Directory.EnumerateFiles(
            temporary.DirectoryFor(relativePath),
            ".index.md.tezuri-*.tmp"));
    }

    [Fact]
    public async Task SerializesConcurrentSavesForTheSameArticle()
    {
        using var temporary = new TemporaryWorkspace();
        const string relativePath = "src/writing/patina/index.md";
        temporary.Write(relativePath, "---\ntitle: Patina\n---\nBefore\n");
        var writer = new PausingAtomicFileWriter();
        var workspace = CreateWorkspace(temporary.Root, writer);
        var opened = await workspace.OpenAsync("patina", TestContext.Current.CancellationToken);
        var firstPatch = Replace(opened, "Before", "First save");
        var secondPatch = Replace(opened, "Before", "Second save");

        var firstSave = workspace.SaveAsync(
            "patina",
            firstPatch,
            TestContext.Current.CancellationToken);
        await writer.Staged.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        var secondSave = workspace.SaveAsync(
            "patina",
            secondPatch,
            TestContext.Current.CancellationToken);
        writer.Resume();

        var first = await firstSave;
        var error = await Assert.ThrowsAsync<ArticleConflictException>(async () =>
        {
            await secondSave;
        });

        Assert.Equal(first.Base.Sha256, error.ActualSha256);
        Assert.Equal(first.Base.Sha256, ArticleDocumentCodec.HashBytes(temporary.ReadBytes(relativePath)));
    }

    [Fact]
    public void RejectsTraversalOutsideWorkspace()
    {
        using var temporary = new TemporaryWorkspace();
        var paths = new WorkspacePathGuard(temporary.Root);

        var error = Assert.Throws<WorkspacePathException>(() => paths.Resolve("../outside.md"));

        Assert.Contains("escapes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static FileArticleWorkspace CreateWorkspace(
        string root,
        AtomicFileWriter? writer = null) => new(
        new WorkspacePathGuard(root),
        WorkspaceContract.Default,
        new ArticleDocumentCodec(),
        writer ?? new AtomicFileWriter());

    private static SourcePatchSetV1 Replace(
        ArticleSourceEnvelopeV1 opened,
        string expected,
        string replacement)
    {
        var source = Convert.FromBase64String(opened.Base.Utf8Base64);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var start = source.AsSpan().IndexOf(expectedBytes);
        Assert.True(start >= 0, $"Expected source text '{expected}' was not found.");

        return new SourcePatchSetV1(
            ArticleSourceProtocolV1.SourcePatchSet,
            ArticleSourceProtocolV1.Version,
            opened.Article.Id,
            opened.Article.RelativePath,
            opened.Base.Sha256,
            [
                new ReplaceSourceRangeOperationV1(
                    "replace",
                    new SourceByteRangeV1(start, start + expectedBytes.Length),
                    Convert.ToBase64String(expectedBytes),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(replacement)),
                    "test-race")
            ]);
    }

    private sealed class PausingAtomicFileWriter : AtomicFileWriter
    {
        private readonly TaskCompletionSource _staged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Staged => _staged.Task;

        public void Resume() => _resume.TrySetResult();

        protected override async Task OnBeforeReplaceAsync(
            string targetPath,
            CancellationToken cancellationToken)
        {
            _staged.TrySetResult();
            await _resume.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _safeParent;

        public TemporaryWorkspace()
        {
            _safeParent = Path.Combine(Path.GetTempPath(), "tezuri-tests");
            Root = Path.Combine(_safeParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public string DirectoryFor(string relativePath) =>
            Path.GetDirectoryName(Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)))!;

        public string Read(string relativePath) =>
            File.ReadAllText(Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)),
                Encoding.UTF8);

        public byte[] ReadBytes(string relativePath) =>
            File.ReadAllBytes(Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
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
