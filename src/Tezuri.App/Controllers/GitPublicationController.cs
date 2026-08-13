using Microsoft.AspNetCore.Mvc;
using Tezuri.Domain.Git;
using Tezuri.Infrastructure.Git;

namespace Tezuri.Controllers;

[ApiController]
[Route("api/v1/git")]
public sealed class GitPublicationController(GitPublicationService publication) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<GitRepositorySnapshotV1>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitRepositorySnapshotV1>> Inspect(
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => publication.InspectAsync(cancellationToken));

    [HttpPost("commit-plans")]
    [ProducesResponseType<GitCommitPlanV1>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitCommitPlanV1>> PlanCommit(
        [FromBody] GitCommitPlanRequestV1 request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            () => publication.PlanCommitAsync(request, cancellationToken));

    [HttpPost("commits")]
    [ProducesResponseType<GitCommitReceiptV1>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitCommitReceiptV1>> PrepareCommit(
        [FromBody] PrepareGitCommitRequestV1 request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            () => publication.PrepareCommitAsync(request, cancellationToken));

    [HttpPost("pushes")]
    [ProducesResponseType<GitPushReceiptV1>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitPushReceiptV1>> Push(
        [FromBody] GitPushRequestV1 request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            () => publication.PushAsync(request, cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Ok(await operation());
        }
        catch (GitPublicationException exception)
        {
            var statusCode = exception.Failure switch
            {
                GitPublicationFailure.InvalidRequest => StatusCodes.Status400BadRequest,
                GitPublicationFailure.CommandFailed => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status409Conflict
            };
            return StatusCode(statusCode, new ProblemDetails
            {
                Status = statusCode,
                Title = TitleFor(exception.Failure),
                Detail = exception.Message,
                Type = "https://tezuri.local/problems/git-publication"
            });
        }
    }

    private static string TitleFor(GitPublicationFailure failure) => failure switch
    {
        GitPublicationFailure.InvalidRequest => "Invalid Git publication request.",
        GitPublicationFailure.NotRepository => "Workspace is not a supported Git repository.",
        GitPublicationFailure.StagedChangesPresent => "Git index already contains staged work.",
        GitPublicationFailure.Diverged => "Remote Git state diverged.",
        GitPublicationFailure.CommandFailed => "Git could not complete the requested operation.",
        _ => "Git publication precondition failed."
    };
}
