using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tezuri.Domain.Import;

public static class ImportManifestProtocolV1
{
    public const string Schema = "tezuri.import-manifest/v1";

    public const string AwaitingApproval = "awaiting-approval";
    public const string Succeeded = "succeeded";

    public const string Imported = "imported";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
    public const string ReviewRequired = "review-required";
}

public sealed record ImportManifestV1(
    string Schema,
    string ImportId,
    ImportSourceV1 Source,
    string State,
    string StartedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CompletedAt,
    ImportSummaryV1 Summary,
    IReadOnlyList<ImportArticleV1> Articles,
    IReadOnlyList<ImportExclusionV1> Exclusions);

public sealed record ImportSourceV1(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FeedUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ArchiveUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExportDigest,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DiscoveredAt);

public sealed record ImportSummaryV1(
    int Discovered,
    int Imported,
    int Skipped,
    int Failed,
    int ReviewRequired);

public sealed record ImportSourceArticleV1(
    string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url,
    string Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PublishedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceDigest,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Metadata);

public sealed record ImportArticleV1(
    ImportSourceArticleV1 Source,
    string Disposition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DestinationPath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultDigest,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? ResultMetadata,
    IReadOnlyList<ImportTransformationV1> Transformations,
    IReadOnlyList<ImportWarningV1> Warnings,
    IReadOnlyList<ImportFidelityV1> Fidelity,
    IReadOnlyList<ImportAssetV1> Assets);

public sealed record ImportAssetV1(
    string SourceUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceDigest,
    string Disposition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DestinationPath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultDigest,
    IReadOnlyList<ImportTransformationV1> Transformations,
    IReadOnlyList<ImportWarningV1> Warnings);

public sealed record ImportTransformationV1(
    string Kind,
    string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourcePointer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultPointer);

public sealed record ImportWarningV1(
    string Code,
    string Severity,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourcePointer);

public sealed record ImportFidelityV1(
    string Area,
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Evidence);

public sealed record ImportExclusionV1(
    string SourceId,
    string Reason,
    string ReviewedAt,
    string ReviewedBy);
