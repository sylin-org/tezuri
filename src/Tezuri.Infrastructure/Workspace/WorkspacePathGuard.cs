namespace Tezuri.Infrastructure.Workspace;

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
