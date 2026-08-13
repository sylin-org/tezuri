using Microsoft.AspNetCore.Mvc;
using Tezuri.Domain.Documents;
using Tezuri.Domain.Workspace;
using Tezuri.Infrastructure.Documents;
using Tezuri.Infrastructure.Workspace;

namespace Tezuri.Controllers;

[ApiController]
[Route("api/v1/articles")]
public sealed class ArticlesController(FileArticleWorkspace workspace) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var articles = await workspace.ListAsync(cancellationToken);
        return Ok(new ArticleListEnvelopeV1(
            ArticleSourceProtocolV1.ArticleList,
            ArticleSourceProtocolV1.Version,
            articles));
    }

    [HttpGet("{articleId}/source")]
    public async Task<IActionResult> Open(
        string articleId,
        CancellationToken cancellationToken) =>
        Ok(await workspace.OpenAsync(articleId, cancellationToken));

    [HttpPost("{articleId}/source-patches")]
    public async Task<IActionResult> Save(
        string articleId,
        [FromBody] SourcePatchSetV1 patches,
        CancellationToken cancellationToken)
    {
        try
        {
            var previousSha256 = patches.BaseSha256;
            var saved = await workspace.SaveAsync(articleId, patches, cancellationToken);
            return Ok(new AppliedSourcePatchV1(
                ArticleSourceProtocolV1.AppliedSourcePatch,
                ArticleSourceProtocolV1.Version,
                DateTimeOffset.UtcNow,
                previousSha256,
                saved));
        }
        catch (ArticleConflictException conflict)
        {
            var current = await workspace.OpenAsync(articleId, cancellationToken);
            return Conflict(new SourcePatchConflictV1(
                ArticleSourceProtocolV1.SourcePatchConflict,
                ArticleSourceProtocolV1.Version,
                articleId,
                conflict.ExpectedSha256,
                current,
                conflict.Message));
        }
    }
}
