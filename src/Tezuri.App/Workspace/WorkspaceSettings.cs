namespace Tezuri.Workspace;

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
