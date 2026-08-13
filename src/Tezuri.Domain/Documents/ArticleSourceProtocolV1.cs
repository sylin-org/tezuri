namespace Tezuri.Domain.Documents;

public static class ArticleSourceProtocolV1
{
    public const int Version = 1;
    public const string ArticleSource = "tezuri.article-source";
    public const string SourcePatchSet = "tezuri.source-patch-set";
    public const string AppliedSourcePatch = "tezuri.applied-source-patch";
    public const string SourcePatchConflict = "tezuri.source-patch-conflict";
    public const string ArticleList = "tezuri.article-list";
}

public readonly record struct SourceByteRangeV1(long Start, long EndExclusive)
{
    public long Length => checked(EndExclusive - Start);
}

public sealed record CanonicalSourceBytesV1(
    string Encoding,
    string Bom,
    string LineEndings,
    long ByteLength,
    string Sha256,
    string Utf8Base64);

public sealed record SourceSliceV1(
    SourceByteRangeV1 Range,
    string Sha256,
    string Utf8Base64);

public sealed record ArticleSourceSegmentV1(
    string Kind,
    string Id,
    SourceByteRangeV1 Range,
    SourceSliceV1 Source,
    string? Syntax = null,
    string? SyntaxHint = null,
    string? Notice = null);

public sealed record SourceDiagnosticV1(
    string Code,
    string Severity,
    string Message,
    SourceByteRangeV1? Range = null);

public sealed record ArticleDescriptorV1(
    string Id,
    string Slug,
    string DisplayTitle,
    string RelativePath);

public sealed record ArticleSourceProjectionV1(
    SourceSliceV1 Frontmatter,
    SourceSliceV1 Body,
    IReadOnlyList<ArticleSourceSegmentV1> Segments);

public sealed record ArticleSourceCapabilitiesV1(
    string RichEditing,
    int ProtectedSegmentCount);

public sealed record ArticleSourceEnvelopeV1(
    string Protocol,
    int Version,
    ArticleDescriptorV1 Article,
    CanonicalSourceBytesV1 Base,
    ArticleSourceProjectionV1 Projection,
    ArticleSourceCapabilitiesV1 Capabilities,
    IReadOnlyList<SourceDiagnosticV1> Diagnostics);

public sealed record ReplaceSourceRangeOperationV1(
    string Kind,
    SourceByteRangeV1 Range,
    string ExpectedUtf8Base64,
    string ReplacementUtf8Base64,
    string Intent,
    string? SegmentId = null);

public sealed record SourcePatchSetV1(
    string Protocol,
    int Version,
    string ArticleId,
    string RelativePath,
    string BaseSha256,
    IReadOnlyList<ReplaceSourceRangeOperationV1> Operations);

public sealed record AppliedSourcePatchV1(
    string Protocol,
    int Version,
    DateTimeOffset SavedAt,
    string PreviousSha256,
    ArticleSourceEnvelopeV1 Current);

public sealed record SourcePatchConflictV1(
    string Protocol,
    int Version,
    string ArticleId,
    string ExpectedBaseSha256,
    ArticleSourceEnvelopeV1 Current,
    string Message);
