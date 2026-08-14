namespace Tezuri.Import;

public enum SubstackImportFailure
{
    /// <summary>The caller named a directory that is not a usable export.</summary>
    InvalidRequest,

    /// <summary>The export is present but does not hold together — bad CSV, bad UTF-8, bad HTML.</summary>
    MalformedExport,

    /// <summary>A file changed underneath the importer while it was being read.</summary>
    ExportChanged
}

public sealed class SubstackImportException(
    SubstackImportFailure failure,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public SubstackImportFailure Failure { get; } = failure;
}
