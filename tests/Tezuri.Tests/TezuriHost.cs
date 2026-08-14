using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Tezuri.Tests;

/// <summary>
/// One running Tezuri over a throwaway repository.
///
/// Article reads and writes go through Koan's static entity facade, which binds to the host that
/// built it, so tests that touch articles share this host rather than each standing one up. The
/// collection below is what keeps them from running at the same time.
/// </summary>
public sealed class TezuriApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _safeParent = Path.Combine(Path.GetTempPath(), "tezuri-app-tests");

    public TezuriApplicationFactory()
    {
        WorkspaceRoot = Path.Combine(_safeParent, Guid.NewGuid().ToString("N"));
        var articleDirectory = Path.Combine(WorkspaceRoot, "src", "writing", "patina");
        Directory.CreateDirectory(articleDirectory);
        File.WriteAllText(
            Path.Combine(articleDirectory, "index.md"),
            "---\ntitle: Patina\n---\n\nA kept paragraph.\n",
            new UTF8Encoding(false));
    }

    public string WorkspaceRoot { get; }

    public string Resolve(string relativePath) => Path.Combine(
        WorkspaceRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebHostDefaults.StaticWebAssetsKey, string.Empty);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TEZURI_WORKSPACE"] = WorkspaceRoot,
                // No window: a test host is exactly the case the server mode exists for.
                ["TEZURI_SHELL"] = "server"
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        var resolved = Path.GetFullPath(WorkspaceRoot);
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

[CollectionDefinition(Name)]
public sealed class TezuriHostCollection : ICollectionFixture<TezuriApplicationFactory>
{
    public const string Name = "tezuri-host";
}
