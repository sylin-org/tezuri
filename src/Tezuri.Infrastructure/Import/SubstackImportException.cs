namespace Tezuri.Infrastructure.Import;

public enum SubstackImportFailure
{
    InvalidRequest,
    MalformedExport,
    PlanChanged,
    ReviewRequired,
    Conflict
}

public sealed class SubstackImportException(
    SubstackImportFailure failure,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public SubstackImportFailure Failure { get; } = failure;
}
