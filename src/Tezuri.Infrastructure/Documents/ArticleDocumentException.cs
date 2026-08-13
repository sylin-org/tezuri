namespace Tezuri.Infrastructure.Documents;

public sealed class ArticleDocumentException(string message) : Exception(message);

public sealed class ArticleConflictException(
    string relativePath,
    string expectedSha256,
    string actualSha256)
    : Exception($"'{relativePath}' changed after it was opened. Expected {expectedSha256}, found {actualSha256}.")
{
    public string RelativePath { get; } = relativePath;
    public string ExpectedSha256 { get; } = expectedSha256;
    public string ActualSha256 { get; } = actualSha256;
}
