namespace Tezuri.Media;

public enum MediaAssetFailure
{
    InvalidInput,
    UnsupportedMedia,
    ExtensionMismatch,
    TooLarge,
    ArticleNotFound,
    Conflict
}

public sealed class MediaAssetException(
    MediaAssetFailure failure,
    string message)
    : Exception(message)
{
    public MediaAssetFailure Failure { get; } = failure;
}
