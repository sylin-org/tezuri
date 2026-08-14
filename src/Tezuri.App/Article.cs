using Koan.Data.Core.Model;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Tezuri;

/// <summary>
/// The canonical article. Persisted as one <c>article.json</c> inside the article's own folder, with
/// <c>index.md</c> generated beside it and <c>media/</c> holding the images it owns.
///
/// Per ADR 0015 the flow is one way: Tezuri writes this entity, and the entity renders Markdown.
/// Markdown is never read back, so this record and the file on disk cannot disagree.
/// </summary>
public sealed class Article : Entity<Article>
{
    /// <summary>Human title. Also the source of the slug when an article is created.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional standfirst shown under the title.</summary>
    public string? Subtitle { get; set; }

    /// <summary>The article body as Markdown. This is the text a person writes.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Draft until deliberately published. New articles start as drafts.</summary>
    public bool Draft { get; set; } = true;

    /// <summary>Original publication date, in the target site's expected form.</summary>
    public string? Date { get; set; }

    public IList<string> Tags { get; set; } = [];

    /// <summary>
    /// Changes on every write. A client sends the revision it read, and the write path refuses when
    /// it no longer matches — the guard against a second Tezuri session overwriting the first.
    /// Koan's JSON connector does not implement conditional writes, so this comparison is Tezuri's.
    /// </summary>
    public string Revision { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Every metadata key Tezuri has no typed property for, captured verbatim and written back as
    /// ordinary top-level JSON. This is what keeps an imported corpus lossless: a Substack article
    /// carrying fields Tezuri has never heard of survives a read/write cycle untouched.
    ///
    /// Json.NET writes these as siblings of the modelled properties, so <c>Meta</c> itself never
    /// appears in the stored document.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken> Meta { get; set; }
        = new Dictionary<string, JToken>(StringComparer.Ordinal);
}

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
