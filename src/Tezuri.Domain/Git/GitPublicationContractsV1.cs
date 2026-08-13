namespace Tezuri.Domain.Git;

public static class GitPublicationProtocolV1
{
    public const int Version = 1;
    public const string RepositorySnapshot = "tezuri.git-repository-snapshot";
    public const string CommitPlan = "tezuri.git-commit-plan";
    public const string CommitReceipt = "tezuri.git-commit-receipt";
    public const string PushReceipt = "tezuri.git-push-receipt";
}

public sealed record GitChangedPathV1(
    string Path,
    string IndexStatus,
    string WorkTreeStatus,
    bool Allowed);

public sealed record GitRemoteBranchV1(
    string Remote,
    string Branch,
    string Sha);

public sealed record GitRepositorySnapshotV1(
    string Protocol,
    int Version,
    string? HeadSha,
    bool IsUnborn,
    bool IsDetached,
    string? Branch,
    string? Upstream,
    IReadOnlyList<string> Remotes,
    IReadOnlyList<GitRemoteBranchV1> RemoteBranches,
    IReadOnlyList<GitChangedPathV1> Changes);

public sealed record GitCommitPlanRequestV1(IReadOnlyList<string> SelectedPaths);

public sealed record GitCommitPlanV1(
    string Protocol,
    int Version,
    string HeadSha,
    string Branch,
    string PlanSha256,
    IReadOnlyList<string> SelectedPaths,
    IReadOnlyList<GitChangedPathV1> Changes);

public sealed record PrepareGitCommitRequestV1(
    string ExpectedHeadSha,
    string ExpectedPlanSha256,
    string Message,
    IReadOnlyList<string> SelectedPaths);

public sealed record GitCommitReceiptV1(
    string Protocol,
    int Version,
    string BeforeSha,
    string AfterSha,
    string Branch,
    string PlanSha256,
    IReadOnlyList<string> SelectedPaths,
    bool Created);

public sealed record GitPushRequestV1(
    string Remote,
    string Branch,
    string ExpectedHeadSha,
    string ExpectedRemoteSha);

public sealed record GitPushReceiptV1(
    string Protocol,
    int Version,
    string Remote,
    string Branch,
    string LocalSha,
    string RemoteBeforeSha,
    string RemoteAfterSha,
    bool Pushed);
