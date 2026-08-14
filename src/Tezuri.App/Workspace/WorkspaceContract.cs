namespace Tezuri.Workspace;

public sealed record WorkspaceContract(
    int Version,
    string ContentRoot,
    string ArticleFileName,
    string MediaDirectoryName)
{
    public static WorkspaceContract Default { get; } = new(
        Version: 1,
        ContentRoot: "src/writing",
        ArticleFileName: "index.md",
        MediaDirectoryName: "media");
}
