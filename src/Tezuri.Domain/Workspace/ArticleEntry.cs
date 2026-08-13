namespace Tezuri.Domain.Workspace;

public sealed record ArticleEntry(
    string Id,
    string Slug,
    string DisplayTitle,
    string RelativePath,
    string PublicationState,
    string SourceSha256,
    DateTimeOffset UpdatedAt,
    long ByteLength);

public sealed record ArticleListEnvelopeV1(
    string Protocol,
    int Version,
    IReadOnlyList<ArticleEntry> Articles);
