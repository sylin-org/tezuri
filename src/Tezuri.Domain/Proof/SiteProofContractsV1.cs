namespace Tezuri.Domain.Proof;

public static class SiteProofProtocolV1
{
    public const int Version = 1;
    public const string RunReceipt = "tezuri.site-proof-run";

    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string TimedOut = "timed-out";
    public const string StartFailed = "start-failed";
}

public sealed record SiteProofRunReceiptV1(
    string Protocol,
    int Version,
    string RunId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    SiteProofProgressV1 Progress,
    SiteProofResultV1 Result);
