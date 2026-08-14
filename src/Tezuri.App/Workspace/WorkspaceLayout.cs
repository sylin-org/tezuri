namespace Tezuri.Workspace;

/// <summary>
/// Where things live inside a repository Tezuri manages.
///
/// This is convention, not configuration. An earlier version let a committed <c>tezuri.yaml</c> move
/// any of it, which bought nothing — every workspace used exactly these values — and cost a YAML
/// parser, a validator, and a layout contract threaded through five services.
/// </summary>
public static class WorkspaceLayout
{
    /// <summary>Repository-relative folder holding one directory per article.</summary>
    public const string ContentRoot = "src/writing";

    /// <summary>The canonical article document. Koan's JSON store owns this file.</summary>
    public const string ArticleDocumentFileName = "article.json";

    /// <summary>The generated Markdown a site build consumes. An output, never an input.</summary>
    public const string RenderedArticleFileName = "index.md";

    /// <summary>Article-owned images, beside the document that references them.</summary>
    public const string MediaDirectoryName = "media";

    public static string ArticleFolder(string articleId) => $"{ContentRoot}/{articleId}";

    public static string ArticleDocument(string articleId) =>
        $"{ContentRoot}/{articleId}/{ArticleDocumentFileName}";

    public static string RenderedArticle(string articleId) =>
        $"{ContentRoot}/{articleId}/{RenderedArticleFileName}";

    public static string MediaFolder(string articleId) =>
        $"{ContentRoot}/{articleId}/{MediaDirectoryName}";

    public static string MediaFile(string articleId, string fileName) =>
        $"{ContentRoot}/{articleId}/{MediaDirectoryName}/{fileName}";
}
