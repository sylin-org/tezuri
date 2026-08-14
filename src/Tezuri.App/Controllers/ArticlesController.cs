using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Tezuri.Articles;
using Tezuri.Workspace;

namespace Tezuri.Controllers;

public sealed record SaveArticleRequest(
    string Title,
    string? Subtitle,
    string Body,
    bool Draft,
    string? Date,
    IList<string>? Tags,
    string? Revision);

public sealed record CreateArticleRequest(string Title);

/// <summary>
/// Articles are Koan entities, so reads are a one-liner. Writes are Tezuri's own because Koan's JSON
/// connector does not implement conditional writes and <c>EntityController</c> does not enforce
/// <c>If-Match</c> — without the revision check below, a second tab could silently overwrite the
/// first. Every successful write also regenerates <c>index.md</c> for the site build.
/// </summary>
[ApiController]
[Route("api/v1/articles")]
public sealed class ArticlesController(
    ArticleMarkdownWriter markdown,
    WorkspacePathGuard paths) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var articles = await Article.All(cancellationToken);
        return Ok(articles
            .OrderByDescending(article => article.UpdatedAt)
            .Select(Summarize));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var article = await Article.Get(id, cancellationToken);
        return article is null ? NotFound() : Ok(article);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateArticleRequest request,
        CancellationToken cancellationToken)
    {
        var title = (request?.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return Problem(statusCode: 400, title: "A new article needs a title.");
        }

        var slug = await UniqueSlugAsync(Slugify(title), cancellationToken);
        var article = new Article
        {
            Id = slug,
            Title = title,
            Draft = true,
            Date = DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Revision = NewRevision(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await Article.Upsert(article, cancellationToken);
        await markdown.WriteAsync(article, cancellationToken);
        return Created($"/api/v1/articles/{Uri.EscapeDataString(slug)}", article);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Save(
        string id,
        [FromBody] SaveArticleRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem(statusCode: 400, title: "A body is required.");
        }

        var article = await Article.Get(id, cancellationToken);
        if (article is null)
        {
            return NotFound();
        }

        // The only writer contention left is a second Tezuri session, because Markdown is an output
        // and never an input. Comparing the revision the client read is enough to catch it.
        if (!string.IsNullOrEmpty(request.Revision) &&
            !string.Equals(request.Revision, article.Revision, StringComparison.Ordinal))
        {
            return Conflict(new
            {
                title = "This article changed in another Tezuri session.",
                detail = "Reopen it to pick up the newer version. Your text is still in this tab.",
                current = article,
            });
        }

        article.Title = request.Title.Trim();
        article.Subtitle = string.IsNullOrWhiteSpace(request.Subtitle) ? null : request.Subtitle.Trim();
        article.Body = request.Body;
        article.Draft = request.Draft;
        article.Date = request.Date;
        article.Tags = request.Tags ?? [];
        article.Revision = NewRevision();
        article.UpdatedAt = DateTimeOffset.UtcNow;

        await Article.Upsert(article, cancellationToken);
        await markdown.WriteAsync(article, cancellationToken);
        return Ok(article);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var article = await Article.Get(id, cancellationToken);
        if (article is null)
        {
            return NotFound();
        }

        await Article.Remove(id, cancellationToken);

        // Koan removes article.json; the folder, its index.md, and its media are Tezuri's to clear.
        var folder = Path.GetDirectoryName(paths.Resolve(markdown.RelativePathFor(id)));
        if (folder is not null && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        return NoContent();
    }

    private static object Summarize(Article article) => new
    {
        id = article.Id,
        title = string.IsNullOrWhiteSpace(article.Title) ? article.Id : article.Title,
        subtitle = article.Subtitle,
        draft = article.Draft,
        tags = article.Tags,
        updatedAt = article.UpdatedAt,
        revision = article.Revision,
    };

    private static string NewRevision() => Guid.CreateVersion7().ToString("N");

    private static async Task<string> UniqueSlugAsync(string slug, CancellationToken cancellationToken)
    {
        var existing = (await Article.All(cancellationToken))
            .Select(article => article.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(slug))
        {
            return slug;
        }

        for (var suffix = 2; suffix < 500; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Too many articles share this title.");
    }

    /// <summary>A portable folder name: lowercase, ASCII, hyphenated, no traversal.</summary>
    private static string Slugify(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (character is ' ' or '-' or '_' or '.' or '/' or '\\')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        if (slug.Length > 80)
        {
            slug = slug[..80].TrimEnd('-');
        }

        return slug.Length == 0 ? $"article-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}" : slug;
    }
}
