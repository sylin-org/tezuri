using Microsoft.AspNetCore.Mvc;
using Tezuri.Domain.Media;
using Tezuri.Infrastructure.Media;
using Tezuri.Infrastructure.Workspace;

namespace Tezuri.Controllers;

[ApiController]
[Route("api/v1/articles/{articleId}/media")]
public sealed class MediaController(ArticleMediaStore media) : ControllerBase
{
    [HttpGet("{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public IActionResult Read(string articleId, string fileName)
    {
        try
        {
            var asset = media.Find(articleId, fileName);
            return asset is null
                ? Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Media asset not found.")
                : PhysicalFile(asset.AbsolutePath, asset.MediaType, enableRangeProcessing: true);
        }
        catch (WorkspacePathException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unsafe media path.",
                detail: exception.Message);
        }
        catch (MediaAssetException exception)
        {
            var (statusCode, title) = MapFailure(exception.Failure);
            return Problem(
                statusCode: statusCode,
                title: title,
                detail: exception.Message);
        }
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<MediaAssetReceiptV1>(StatusCodes.Status200OK)]
    [ProducesResponseType<MediaAssetReceiptV1>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Ingest(
        string articleId,
        [FromForm(Name = "file")] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A media file is required.",
                detail: "Send one multipart form field named 'file'.");
        }

        try
        {
            await using var content = file.OpenReadStream();
            var receipt = await media.IngestAsync(
                articleId,
                file.FileName,
                content,
                cancellationToken);

            if (receipt.Deduplicated)
            {
                return Ok(receipt);
            }

            var location = $"/api/v1/articles/{Uri.EscapeDataString(articleId)}/media/" +
                           Uri.EscapeDataString(receipt.FileName);
            return Created(location, receipt);
        }
        catch (WorkspacePathException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unsafe media path.",
                detail: exception.Message);
        }
        catch (MediaAssetException exception)
        {
            var (statusCode, title) = MapFailure(exception.Failure);
            return Problem(
                statusCode: statusCode,
                title: title,
                detail: exception.Message);
        }
    }

    private static (int StatusCode, string Title) MapFailure(MediaAssetFailure failure) => failure switch
    {
        MediaAssetFailure.InvalidInput =>
            (StatusCodes.Status400BadRequest, "Invalid media upload."),
        MediaAssetFailure.UnsupportedMedia or MediaAssetFailure.ExtensionMismatch =>
            (StatusCodes.Status415UnsupportedMediaType, "Unsupported media upload."),
        MediaAssetFailure.TooLarge =>
            (StatusCodes.Status413PayloadTooLarge, "Media upload is too large."),
        MediaAssetFailure.ArticleNotFound =>
            (StatusCodes.Status404NotFound, "Article not found."),
        MediaAssetFailure.Conflict =>
            (StatusCodes.Status409Conflict, "Media path conflict."),
        _ =>
            (StatusCodes.Status400BadRequest, "Invalid media upload.")
    };
}
