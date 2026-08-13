namespace Tezuri.Domain.Proof;

public sealed record SiteProofProgressV1(
    string State,
    int CompletedCommands,
    int TotalCommands,
    string? CurrentCommandId);
