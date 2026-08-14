using Koan.Data.Connector.Json;
using Microsoft.Extensions.Options;

namespace Tezuri.Workspace;

/// <summary>
/// The repository Tezuri is currently working in.
///
/// Resolved lazily rather than at host-build time. That is what lets a desktop session pick a folder,
/// and later switch between several repositories, without the store being nailed down before the
/// person has chosen anything. It also means test hosts can override the root through ordinary
/// configuration, which runs after the builder has already executed.
/// </summary>
public sealed class SelectedWorkspace(IConfiguration configuration)
{
    private const string DefaultRoot = "/workspace";

    private string? _root;

    public string Root => _root ??= Normalize(configuration["TEZURI_WORKSPACE"] ?? DefaultRoot);

    /// <summary>Where article documents and their folders live inside the selected repository.</summary>
    public string ArticleRoot => Path.Combine(Root, "src", "writing");

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
