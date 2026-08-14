using System.Globalization;
using System.Text;
using Tezuri.Articles;
using Tezuri.Workspace;

namespace Tezuri.Articles;

/// <summary>
/// Renders an article to the <c>index.md</c> the site build consumes.
///
/// This is one way. Markdown is an output, never an input, so nothing here has to survive a round
/// trip or match bytes already on disk — which is the whole reason the byte-patch protocol is gone.
/// </summary>
public sealed class ArticleMarkdownWriter(WorkspacePathGuard paths, AtomicFileWriter writer)
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string RelativePathFor(string articleId) => WorkspaceLayout.RenderedArticle(articleId);

    public async Task WriteAsync(Article article, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(article);
        var relativePath = RelativePathFor(article.Id);
        var absolutePath = paths.Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        var bytes = Utf8NoBom.GetBytes(Render(article));
        await writer.WriteAsync(absolutePath, bytes, static _ => Task.CompletedTask, cancellationToken);
    }

    public void Delete(string articleId)
    {
        var absolutePath = paths.Resolve(RelativePathFor(articleId));
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }
    }

    /// <summary>
    /// Frontmatter plus body. Modelled fields are written in a stable order; everything the writer
    /// imported but Tezuri has no control for follows, so an imported corpus keeps its metadata.
    /// </summary>
    public static string Render(Article article)
    {
        var builder = new StringBuilder();
        builder.Append("---\n");
        builder.Append("id: ").Append(Scalar(article.Id)).Append('\n');
        builder.Append("title: ").Append(Scalar(article.Title)).Append('\n');

        if (!string.IsNullOrWhiteSpace(article.Subtitle))
        {
            builder.Append("description: ").Append(Scalar(article.Subtitle)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(article.Date))
        {
            builder.Append("date: ").Append(Scalar(article.Date)).Append('\n');
        }

        builder.Append("draft: ").Append(article.Draft ? "true" : "false").Append('\n');

        if (article.Tags.Count > 0)
        {
            builder.Append("tags:\n");
            foreach (var tag in article.Tags)
            {
                builder.Append("  - ").Append(Scalar(tag)).Append('\n');
            }
        }

        foreach (var (key, value) in article.Meta.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (IsModelled(key) || value is null)
            {
                continue;
            }

            builder.Append(key).Append(": ").Append(Scalar(value.ToString())).Append('\n');
        }

        builder.Append("---\n\n");
        builder.Append(article.Body.ReplaceLineEndings("\n").TrimEnd('\n'));
        builder.Append('\n');
        return builder.ToString();
    }

    private static bool IsModelled(string key) => key.ToLowerInvariant() switch
    {
        "id" or "title" or "description" or "subtitle" or "date" or "draft" or "tags" or "body"
            or "revision" or "updatedat" => true,
        _ => false,
    };

    /// <summary>Quotes only where a plain YAML scalar would change the meaning.</summary>
    private static string Scalar(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "\"\"";
        }

        var needsQuoting =
            trimmed.IndexOfAny([':', '#', '"', '\'', '\n']) >= 0 ||
            "-?[]{}&*!|>%@`".Contains(trimmed[0], StringComparison.Ordinal) ||
            bool.TryParse(trimmed, out _) ||
            double.TryParse(trimmed, CultureInfo.InvariantCulture, out _);

        if (!needsQuoting)
        {
            return trimmed;
        }

        var escaped = trimmed
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}
