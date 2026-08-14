namespace Tezuri.Media;

public static class MediaAssetProtocolV1
{
    public const int Version = 1;
    public const string Receipt = "tezuri.media-asset-receipt";
}

public sealed record MediaAssetReceiptV1(
    string Protocol,
    int Version,
    string ArticleId,
    string OriginalFileName,
    string FileName,
    string RelativePath,
    string MediaType,
    string Sha256,
    long ByteLength,
    bool Deduplicated);
