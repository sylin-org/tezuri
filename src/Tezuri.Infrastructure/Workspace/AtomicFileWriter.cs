namespace Tezuri.Infrastructure.Workspace;

public class AtomicFileWriter
{
    public Task WriteAsync(
        string targetPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken) =>
        WriteAsync(targetPath, content, validateBeforeReplace: null, cancellationToken);

    internal async Task WriteAsync(
        string targetPath,
        ReadOnlyMemory<byte> content,
        Func<CancellationToken, Task>? validateBeforeReplace,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"'{targetPath}' has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.tezuri-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                             }))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            await OnBeforeReplaceAsync(targetPath, cancellationToken);

            if (validateBeforeReplace is not null)
            {
                await validateBeforeReplace(cancellationToken);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    protected virtual Task OnBeforeReplaceAsync(
        string targetPath,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
