namespace Tezuri.Publishing;

public enum GitPublicationFailure
{
    InvalidRequest,
    NotRepository,
    PreconditionFailed,
    StagedChangesPresent,
    Conflict,
    Diverged,
    CommandFailed
}

public sealed class GitPublicationException(
    GitPublicationFailure failure,
    string message)
    : Exception(message)
{
    public GitPublicationFailure Failure { get; } = failure;
}
