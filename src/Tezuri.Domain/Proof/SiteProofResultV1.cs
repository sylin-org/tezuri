namespace Tezuri.Domain.Proof;

public sealed record SiteProofResultV1(
    bool Succeeded,
    IReadOnlyList<SiteProofCommandResultV1> Commands);

public sealed record SiteProofCommandResultV1(
    string Id,
    string Executable,
    IReadOnlyList<string> Arguments,
    string Status,
    int? ExitCode,
    bool TimedOut,
    long DurationMilliseconds,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    string? OutputDirectory,
    bool OutputDirectoryExists);
