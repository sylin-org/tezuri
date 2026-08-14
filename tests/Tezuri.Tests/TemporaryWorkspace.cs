using System.Text;

namespace Tezuri.Tests;

/// <summary>
/// A disposable workspace root under the system temp directory. Disposal refuses to delete anything
/// that is not inside its own generated parent, so a bad root can never take a real directory with
/// it.
/// </summary>
internal sealed class TemporaryWorkspace : IDisposable
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
