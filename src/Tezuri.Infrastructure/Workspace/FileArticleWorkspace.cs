using System.Collections.Concurrent;
using Tezuri.Domain.Documents;
using Tezuri.Domain.Workspace;
using Tezuri.Infrastructure.Documents;

namespace Tezuri.Infrastructure.Workspace;

public sealed class FileArticleWorkspace(
    WorkspacePathGuard paths,
    WorkspaceContract contract,
    ArticleDocumentCodec documents,
    AtomicFileWriter writer)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _saveGates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task<IReadOnlyList<ArticleEntry>> ListAsync(CancellationToken cancellationToken)
    {
        var contentRoot = paths.Resolve(contract.ContentRoot);
        if (!Directory.Exists(contentRoot))
        {
            return [];
        }

        var entries = new List<ArticleEntry>();
        foreach (var directory in Directory.EnumerateDirectories(contentRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var articlePath = Path.Combine(directory, contract.ArticleFileName);
            if (!File.Exists(articlePath))
            {
                continue;
            }

            paths.Resolve(paths.Relative(articlePath));
            var info = new FileInfo(articlePath);
            var relativePath = paths.Relative(articlePath);
            var sourceBytes = await File.ReadAllBytesAsync(articlePath, cancellationToken);
            var opened = documents.Open(relativePath, sourceBytes, Path.GetFileName(directory));
            entries.Add(new ArticleEntry(
                Id: Path.GetFileName(directory),
                Slug: Path.GetFileName(directory),
                DisplayTitle: Path.GetFileName(directory),
                RelativePath: relativePath,
                PublicationState: "unknown",
                SourceSha256: opened.Base.Sha256,
                UpdatedAt: info.LastWriteTimeUtc,
                ByteLength: info.Length));
        }

        await Task.CompletedTask;
        return entries
            .OrderBy(entry => entry.Slug, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ArticleSourceEnvelopeV1> OpenAsync(
        string articleId,
        CancellationToken cancellationToken)
    {
        var absolutePath = ResolveArticlePath(articleId);
        var bytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken);
        return documents.Open(
            paths.Relative(absolutePath),
            bytes,
            articleId: articleId,
            slug: articleId);
    }

    public async Task<ArticleSourceEnvelopeV1> SaveAsync(
        string articleId,
        SourcePatchSetV1 patchSet,
        CancellationToken cancellationToken)
    {
        var absolutePath = ResolveArticlePath(articleId);
        if (!StringComparer.Ordinal.Equals(paths.Relative(absolutePath), patchSet.RelativePath.Replace('\\', '/')))
        {
            throw new ArticleDocumentException("The requested path and patch-set path differ.");
        }

        var saveGate = _saveGates.GetOrAdd(absolutePath, static _ => new SemaphoreSlim(1, 1));
        await saveGate.WaitAsync(cancellationToken);
        try
        {
            var currentBytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken);
            var current = documents.Open(
                paths.Relative(absolutePath),
                currentBytes,
                articleId: articleId,
                slug: articleId);
            if (!StringComparer.OrdinalIgnoreCase.Equals(current.Base.Sha256, patchSet.BaseSha256))
            {
                throw new ArticleConflictException(
                    current.Article.RelativePath,
                    patchSet.BaseSha256,
                    current.Base.Sha256);
            }

            var updatedBytes = documents.Apply(current, patchSet);
            if (!updatedBytes.AsSpan().SequenceEqual(currentBytes))
            {
                await writer.WriteAsync(
                    absolutePath,
                    updatedBytes,
                    async validateCancellationToken =>
                    {
                        var latestBytes = await File.ReadAllBytesAsync(
                            absolutePath,
                            validateCancellationToken);
                        var latestSha256 = ArticleDocumentCodec.HashBytes(latestBytes);
                        if (!StringComparer.OrdinalIgnoreCase.Equals(
                                current.Base.Sha256,
                                latestSha256))
                        {
                            throw new ArticleConflictException(
                                current.Article.RelativePath,
                                patchSet.BaseSha256,
                                latestSha256);
                        }
                    },
                    cancellationToken);
            }

            return documents.Open(
                current.Article.RelativePath,
                updatedBytes,
                articleId: articleId,
                slug: articleId);
        }
        finally
        {
            saveGate.Release();
        }
    }

    private string ResolveArticlePath(string articleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(articleId);
        if (articleId is "." or ".." ||
            articleId.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
            articleId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new WorkspacePathException(articleId, "Article ids may contain only letters, digits, hyphens, and underscores.");
        }

        return paths.Resolve(Path.Combine(contract.ContentRoot, articleId, contract.ArticleFileName));
    }
}
