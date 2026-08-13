using Tezuri.Domain.Workspace;

namespace Tezuri.Infrastructure.Configuration;

public sealed record WorkspaceConfigurationV1(
    string Schema,
    SiteConfiguration Site,
    ArticleLayoutConfiguration Articles,
    MediaPolicyConfiguration Media,
    ProofConfiguration Proof,
    GitPublicationConfiguration Git)
{
    public const string SchemaName = "tezuri.workspace/v1";

    public WorkspaceContract ToWorkspaceContract() => new(
        Version: 1,
        ContentRoot: Articles.Root,
        ArticleFileName: Articles.FileName,
        MediaDirectoryName: Articles.MediaDirectory);
}

public sealed record SiteConfiguration(string Url);

public sealed record ArticleLayoutConfiguration(
    string Root,
    string FileName,
    string MediaDirectory,
    string MetadataSchema,
    string? EditorHints = null);

public sealed record MediaPolicyConfiguration(
    bool RequireOwnedAssets,
    long MaximumAssetBytes,
    IReadOnlyList<string> AllowedExtensions);

public sealed record ProofConfiguration(
    string WorkingDirectory,
    IReadOnlyList<ProofCommandConfiguration> Commands);

public sealed record ProofCommandConfiguration(
    string Id,
    string Executable,
    IReadOnlyList<string> Arguments,
    int TimeoutSeconds,
    string? OutputDirectory);

public sealed record GitPublicationConfiguration(IReadOnlyList<string> AllowedPaths);
