using Microsoft.AspNetCore.Mvc;
using Tezuri.Domain.Import;
using Tezuri.Infrastructure.Import;
using Tezuri.Infrastructure.Workspace;

namespace Tezuri.Controllers;

[ApiController]
[Route("api/v1/imports/substack")]
public sealed class SubstackImportsController(SubstackImporter importer) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<ImportManifestV1>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ImportManifestV1>> Preview(
        [FromQuery] string exportDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var preview = await importer.PreviewAsync(exportDirectory, cancellationToken);
            Response.Headers.ETag = QuoteEntityTag(preview.PlanDigest);
            Response.Headers.Append("X-Tezuri-Import-Manifest", preview.ManifestRelativePath);
            return Ok(preview.Manifest);
        }
        catch (Exception exception) when (exception is SubstackImportException or WorkspacePathException)
        {
            return ProblemFor(exception);
        }
    }

    [HttpPost("apply")]
    [ProducesResponseType<ImportManifestV1>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<ImportManifestV1>> Apply(
        [FromQuery] string exportDirectory,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        var expected = ParseEntityTag(ifMatch);
        if (expected is null)
        {
            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "A current import preview is required.",
                detail: "Send the exact strong ETag from the preview response in If-Match.");
        }

        try
        {
            var result = await importer.ApplyAsync(exportDirectory, expected, cancellationToken);
            Response.Headers.ETag = QuoteEntityTag(result.PlanDigest);
            Response.Headers.Append("X-Tezuri-Import-Manifest", result.ManifestRelativePath);
            Response.Headers.Append("X-Tezuri-Import-Idempotent", result.Idempotent ? "true" : "false");
            return Ok(result.Manifest);
        }
        catch (Exception exception) when (exception is SubstackImportException or WorkspacePathException)
        {
            return ProblemFor(exception);
        }
    }

    private ObjectResult ProblemFor(Exception exception)
    {
        var failure = exception is SubstackImportException import
            ? import.Failure
            : SubstackImportFailure.InvalidRequest;
        var status = failure switch
        {
            SubstackImportFailure.PlanChanged => StatusCodes.Status412PreconditionFailed,
            SubstackImportFailure.ReviewRequired or SubstackImportFailure.Conflict =>
                StatusCodes.Status409Conflict,
            SubstackImportFailure.MalformedExport => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };
        return Problem(
            statusCode: status,
            title: failure switch
            {
                SubstackImportFailure.PlanChanged => "The import preview is stale.",
                SubstackImportFailure.ReviewRequired => "The import needs review.",
                SubstackImportFailure.Conflict => "The import conflicts with workspace content.",
                SubstackImportFailure.MalformedExport => "The Substack export is malformed.",
                _ => "The Substack import request is invalid."
            },
            detail: exception.Message,
            type: "https://tezuri.local/problems/substack-import");
    }

    private static string QuoteEntityTag(string digest) => $"\"{digest}\"";

    private static string? ParseEntityTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(',', StringComparison.Ordinal))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Length < 3 ||
            trimmed[0] != '"' ||
            trimmed[^1] != '"')
        {
            return null;
        }

        var digest = trimmed[1..^1];
        return digest.Length == 71 && digest.StartsWith("sha256:", StringComparison.Ordinal)
            ? digest
            : null;
    }
}
