using Koan.Data.Connector.Json;
using Microsoft.Extensions.Options;

namespace Tezuri;

// The repository Tezuri is working in: where things live inside it, what little about it is a
// choice, how a path is proved safe, and how a file is replaced without ever being half-written.

/// <summary>
/// Where things live inside a repository Tezuri manages.
///
/// This is convention, not configuration. An earlier version let a committed <c>tezuri.yaml</c> move
/// any of it, which bought nothing — every workspace used exactly these values — and cost a YAML
/// parser, a validator, and a layout contract threaded through five services.
/// </summary>
public static class WorkspaceLayout
{
    /// <summary>Repository-relative folder holding one directory per article.</summary>
    public const string ContentRoot = "src/writing";

    /// <summary>The canonical article document. Koan's JSON store owns this file.</summary>
    public const string ArticleDocumentFileName = "article.json";

    /// <summary>The generated Markdown a site build consumes. An output, never an input.</summary>
    public const string RenderedArticleFileName = "index.md";

    /// <summary>Article-owned images, beside the document that references them.</summary>
    public const string MediaDirectoryName = "media";

    public static string ArticleFolder(string articleId) => $"{ContentRoot}/{articleId}";

    public static string ArticleDocument(string articleId) =>
        $"{ContentRoot}/{articleId}/{ArticleDocumentFileName}";

    public static string RenderedArticle(string articleId) =>
        $"{ContentRoot}/{articleId}/{RenderedArticleFileName}";

    public static string MediaFolder(string articleId) =>
        $"{ContentRoot}/{articleId}/{MediaDirectoryName}";

    public static string MediaFile(string articleId, string fileName) =>
        $"{ContentRoot}/{articleId}/{MediaDirectoryName}/{fileName}";
}

/// <summary>
/// The repository this session is editing.
///
/// Resolved on first use, never at host-build time. That is what lets the window open, ask which
/// folder, and answer here — and it is why a test host can name a workspace through ordinary
/// configuration, which is applied after the builder has already run.
///
/// One session edits one repository. Opening a second repository launches a second Tezuri, which
/// costs a process and saves having to invalidate a store that was configured from this value.
/// </summary>
public sealed class SelectedWorkspace(IConfiguration configuration)
{
    private string? _root;

    public string Root => _root ??= Normalize(
        configuration["TEZURI_WORKSPACE"]
        ?? throw new InvalidOperationException(
            "No repository was chosen. Launch Tezuri with a folder, or set TEZURI_WORKSPACE."));

    /// <summary>
    /// Names the repository before anything reads it. The shell calls this once, after the folder
    /// picker and before the window is pointed at the server, so no request can observe a change.
    /// </summary>
    public void Choose(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var normalized = Normalize(root);
        if (_root is not null && !StringComparer.Ordinal.Equals(_root, normalized))
        {
            throw new InvalidOperationException(
                "This session is already editing a repository. Open the other one in a new window.");
        }

        _root = normalized;
    }

    /// <summary>Where article documents and their folders live inside the selected repository.</summary>
    public string ArticleRoot => Path.Combine(
        Root,
        WorkspaceLayout.ContentRoot.Replace('/', Path.DirectorySeparatorChar));

    private static string Normalize(string root) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
}

/// <summary>
/// Points Koan's JSON store at the selected workspace. Runs as options post-configuration so the
/// path is read after all configuration sources have been applied, not while the builder is still
/// being assembled.
/// </summary>
public sealed class WorkspaceJsonDirectory(SelectedWorkspace workspace)
    : IPostConfigureOptions<JsonDataOptions>
{
    public void PostConfigure(string? name, JsonDataOptions options)
    {
        options.DirectoryPath = workspace.ArticleRoot;
    }
}

/// <summary>
/// Everything Tezuri needs to know about a repository that convention cannot supply.
///
/// This replaces a committed <c>tezuri.yaml</c> and the hand-rolled YAML subset parser that read it.
/// Layout is now convention — articles live in <c>src/writing/&lt;slug&gt;/</c> with <c>media/</c>
/// beside them — so the only genuine choices left are the media policy and the command that builds
/// the site. Both have working defaults and bind from ordinary configuration.
/// </summary>
public sealed class WorkspaceSettings
{
    public MediaPolicy Media { get; set; } = new();

    public ProofSettings Proof { get; set; } = new();

    /// <summary>Paths a commit may touch. Anything else is refused at publication time.</summary>
    public IReadOnlyList<string> AllowedPaths { get; set; } =
    [
        "src/writing/**",
    ];
}

public sealed class MediaPolicy
{
    public long MaximumAssetBytes { get; set; } = 26_214_400;

    public IReadOnlyList<string> AllowedExtensions { get; set; } =
    [
        ".avif", ".gif", ".jpeg", ".jpg", ".png", ".webp",
    ];
}

public sealed class ProofSettings
{
    /// <summary>Relative to the repository root.</summary>
    public string WorkingDirectory { get; set; } = ".";

    public IReadOnlyList<ProofCommand> Commands { get; set; } = [new()];
}

/// <summary>
/// A command run to build the site during Proof. Executable and arguments stay separate: a browser
/// can never contribute shell text, and nothing here is passed through a shell.
/// </summary>
public sealed class ProofCommand
{
    public string Id { get; set; } = "site-test";

    public string Executable { get; set; } = "npm";

    public IReadOnlyList<string> Arguments { get; set; } = ["test"];

    public int TimeoutSeconds { get; set; } = 300;

    public string? OutputDirectory { get; set; } = "dist";
}

public sealed class WorkspacePathGuard
{
    private readonly string _rootWithSeparator;

    public WorkspacePathGuard(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        _rootWithSeparator = Path.EndsInDirectorySeparator(Root)
            ? Root
            : Root + Path.DirectorySeparatorChar;
    }

    public string Root { get; }

    public string Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new WorkspacePathException(relativePath, "Rooted paths are not permitted.");
        }

        var resolved = Path.GetFullPath(relativePath, Root);
        if (!resolved.StartsWith(_rootWithSeparator, PathComparison) &&
            !StringComparerForPlatform.Equals(resolved, Root))
        {
            throw new WorkspacePathException(relativePath, "The path escapes the configured workspace.");
        }

        RejectExistingLinkTraversal(resolved, relativePath);
        return resolved;
    }

    public string Relative(string absolutePath)
    {
        var resolved = Path.GetFullPath(absolutePath);
        if (!resolved.StartsWith(_rootWithSeparator, PathComparison))
        {
            throw new WorkspacePathException(absolutePath, "The path is outside the configured workspace.");
        }

        return Path.GetRelativePath(Root, resolved).Replace('\\', '/');
    }

    private void RejectExistingLinkTraversal(string resolved, string requested)
    {
        var relative = Path.GetRelativePath(Root, resolved);
        var cursor = Root;
        foreach (var part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, part);
            if (!Directory.Exists(cursor) && !File.Exists(cursor))
            {
                continue;
            }

            var info = Directory.Exists(cursor)
                ? (FileSystemInfo)new DirectoryInfo(cursor)
                : new FileInfo(cursor);
            if (info.LinkTarget is not null)
            {
                throw new WorkspacePathException(
                    requested,
                    $"Symbolic-link or junction traversal is not permitted ('{Relative(cursor)}').");
            }
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer StringComparerForPlatform =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

public sealed class WorkspacePathException(string path, string reason)
    : Exception($"Unsafe workspace path '{path}': {reason}");

public class AtomicFileWriter
{
    public Task WriteAsync(
        string targetPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        WriteAsync(targetPath, content, validateBeforeReplace: null, cancellationToken);

    internal async Task WriteAsync(
        string targetPath,
        ReadOnlyMemory<byte> content,
        Func<CancellationToken, Task>? validateBeforeReplace,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"'{targetPath}' has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.tezuri-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                             }))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            await OnBeforeReplaceAsync(targetPath, cancellationToken);

            if (validateBeforeReplace is not null)
            {
                await validateBeforeReplace(cancellationToken);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    protected virtual Task OnBeforeReplaceAsync(
        string targetPath,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
