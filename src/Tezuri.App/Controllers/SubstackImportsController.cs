using Microsoft.AspNetCore.Mvc;
using Tezuri.Import;
using Tezuri.Workspace;

namespace Tezuri.Controllers;

/// <summary>
/// Preview reads; apply creates articles. There is no token between them because apply cannot
/// overwrite anything — an existing article is skipped — so a stale preview costs a re-read, not a
/// lost draft.
/// </summary>
[ApiController]
[Route("api/v1/imports/substack")]
public sealed class SubstackImportsController(SubstackImporter importer) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<SubstackImportReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubstackImportReport>> Preview(
        [FromQuery] string exportDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await importer.PreviewAsync(exportDirectory, cancellationToken));
        }
        catch (Exception exception) when (exception is SubstackImportException or WorkspacePathException)
        {
            return ProblemFor(exception);
        }
    }

    [HttpPost("apply")]
    [ProducesResponseType<SubstackImportReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubstackImportReport>> Apply(
        [FromQuery] string exportDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await importer.ApplyAsync(exportDirectory, cancellationToken));
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
        return Problem(
            statusCode: failure switch
            {
                SubstackImportFailure.MalformedExport => StatusCodes.Status422UnprocessableEntity,
                SubstackImportFailure.ExportChanged => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            },
            title: failure switch
            {
                SubstackImportFailure.MalformedExport => "The Substack export is malformed.",
                SubstackImportFailure.ExportChanged => "The export changed while it was being read.",
                _ => "The Substack import request is invalid."
            },
            detail: exception.Message,
            type: "https://tezuri.local/problems/substack-import");
    }
}
