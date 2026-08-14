using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Tezuri;

/// <summary>
/// Building the site the way the site builds, in a copy, under a clock.
/// </summary>
public sealed class ProofException(string message) : Exception(message);

/// <summary>The vocabulary a proof run reports back. Not a protocol — just the words.</summary>
public static class ProofStatus
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string TimedOut = "timed-out";
    public const string StartFailed = "start-failed";
}

public sealed record ProofRun(
    string RunId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    ProofProgress Progress,
    ProofResult Result);

public sealed record ProofProgress(
    string State,
    int CompletedCommands,
    int TotalCommands,
    string? CurrentCommandId);

public sealed record ProofResult(
    bool Succeeded,
    IReadOnlyList<ProofCommandResult> Commands);

public sealed record ProofCommandResult(
    string Id,
    string Executable,
    IReadOnlyList<string> Arguments,
    string Status,
    int? ExitCode,
    bool TimedOut,
    long DurationMilliseconds,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    string? OutputDirectory,
    bool OutputDirectoryExists);

/// <summary>
/// Checks proof settings before anything is copied or launched.
///
/// The load-bearing rule is the shell-interpreter refusal. Tezuri never runs a proof command through
/// a shell — executable and argument list stay separate, so no configured string can become shell
/// syntax. Naming <c>sh</c> or <c>pwsh</c> as the executable would hand that separation straight
/// back, because everything after <c>-c</c> is then a script. It is refused rather than discouraged.
///
/// The remaining checks keep a misconfigured workspace from doing work before it fails: an unsafe
/// working directory is caught here rather than after a full isolated copy has been made.
/// </summary>
internal static partial class ProofGuard
{
    private const int MaximumTimeoutSeconds = 1_800;
    private const int MaximumArgumentCharacters = 4_096;

    private static readonly HashSet<string> ShellExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash",
        "cmd",
        "cmd.exe",
        "cscript",
        "cscript.exe",
        "dash",
        "fish",
        "powershell",
        "powershell.exe",
        "pwsh",
        "pwsh.exe",
        "sh",
        "wscript",
        "wscript.exe",
        "zsh"
    };

    public static void EnsureRunnable(ProofSettings proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        EnsureRepositoryPath(proof.WorkingDirectory, "proof working directory", allowCurrentDirectory: true);

        if (proof.Commands.Count == 0)
        {
            throw new ProofException("Proof needs at least one command to run.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in proof.Commands)
        {
            if (!IdentifierPattern().IsMatch(command.Id) || !ids.Add(command.Id))
            {
                throw new ProofException(
                    $"Proof command id '{command.Id}' must be a unique lowercase hyphenated name.");
            }

            EnsureExecutable(command.Executable);

            foreach (var argument in command.Arguments)
            {
                if (argument.Length > MaximumArgumentCharacters ||
                    argument.Any(character => character is '\0' or '\r' or '\n'))
                {
                    throw new ProofException(
                        $"Proof command '{command.Id}' has an argument that is oversized or contains a control line break.");
                }
            }

            if (command.TimeoutSeconds is < 1 or > MaximumTimeoutSeconds)
            {
                throw new ProofException(
                    $"Proof command '{command.Id}' must time out between 1 and {MaximumTimeoutSeconds} seconds.");
            }

            if (command.OutputDirectory is not null)
            {
                EnsureRepositoryPath(
                    command.OutputDirectory,
                    $"output directory for proof command '{command.Id}'",
                    allowCurrentDirectory: false);
            }
        }
    }

    private static void EnsureExecutable(string executable)
    {
        if (!ExecutablePattern().IsMatch(executable) ||
            executable.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(executable))
        {
            throw new ProofException(
                $"Proof executable '{executable}' must be one portable executable token.");
        }

        var name = Path.GetFileName(executable.Replace('/', Path.DirectorySeparatorChar));
        if (ShellExecutables.Contains(name))
        {
            throw new ProofException(
                $"Proof executable '{executable}' is a shell interpreter. " +
                "Configure the target executable and its arguments directly.");
        }
    }

    private static void EnsureRepositoryPath(
        string path,
        string description,
        bool allowCurrentDirectory)
    {
        if (allowCurrentDirectory && StringComparer.Ordinal.Equals(path, "."))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.Contains("//", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            path.Any(char.IsControl) ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ProofException(
                $"The {description} must be a repository-relative path using '/'.");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+/-]*$")]
    private static partial Regex ExecutablePattern();
}

public sealed class ProofRunner : IDisposable
{
    public const int MaximumCapturedCharacters = 65_536;

    private const string TruncationMarker = "\n[output truncated]";

    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        [".git", "node_modules", "dist", "bin", "obj"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex SensitiveAssignmentPattern = new(
        @"\b(token|password|secret|api[_-]?key|authorization)\b\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private readonly WorkspacePathGuard _workspace;
    private readonly ProofSettings _proof;
    private readonly string _temporaryParent;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public ProofRunner(
        WorkspacePathGuard workspace,
        WorkspaceSettings settings,
        string? temporaryParent = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _proof = settings.Proof;
        _temporaryParent = Path.GetFullPath(
            temporaryParent ?? Path.Combine(Path.GetTempPath(), "tezuri-proof-runs"));

        if (IsWithinOrEqual(_temporaryParent, _workspace.Root) ||
            IsWithinOrEqual(_workspace.Root, _temporaryParent))
        {
            throw new ProofException(
                "The proof temporary root and mounted workspace must be disjoint directories.");
        }
    }

    public async Task<ProofRun> RunAsync(CancellationToken cancellationToken = default)
    {
        // Before a byte is copied: no shell interpreter, no escaping path, no unbounded timeout.
        ProofGuard.EnsureRunnable(_proof);
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            return await RunCoreAsync(cancellationToken);
        }
        finally
        {
            _runGate.Release();
        }
    }

    public void Dispose() => _runGate.Dispose();

    private async Task<ProofRun> RunCoreAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(_temporaryParent);
        var isolatedRoot = Path.Combine(_temporaryParent, runId);
        Directory.CreateDirectory(isolatedRoot);

        try
        {
            await CopyWorkspaceAsync(
                _workspace.Root,
                isolatedRoot,
                relativeDirectory: string.Empty,
                configuredOutputPaths: GetConfiguredOutputPaths(),
                cancellationToken: cancellationToken);
            var workingDirectory = ResolveContained(
                isolatedRoot,
                _proof.WorkingDirectory);
            if (!Directory.Exists(workingDirectory))
            {
                throw new ProofException(
                    $"Configured proof working directory '{_proof.WorkingDirectory}' does not exist.");
            }

            var commandResults = new List<ProofCommandResult>(
                _proof.Commands.Count);
            foreach (var command in _proof.Commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ExecuteCommandAsync(
                    command,
                    isolatedRoot,
                    workingDirectory,
                    cancellationToken);
                commandResults.Add(result);
                if (!StringComparer.Ordinal.Equals(result.Status, ProofStatus.Passed))
                {
                    break;
                }
            }

            var succeeded = commandResults.Count == _proof.Commands.Count &&
                            commandResults.All(result =>
                                StringComparer.Ordinal.Equals(result.Status, ProofStatus.Passed));
            var status = succeeded ? ProofStatus.Passed : ProofStatus.Failed;
            var completedAt = DateTimeOffset.UtcNow;
            return new ProofRun(
                runId,
                status,
                startedAt,
                completedAt,
                new ProofProgress(
                    status,
                    commandResults.Count,
                    _proof.Commands.Count,
                    CurrentCommandId: null),
                new ProofResult(succeeded, commandResults));
        }
        finally
        {
            DeleteTemporaryRun(isolatedRoot);
        }
    }

    private async Task<ProofCommandResult> ExecuteCommandAsync(
        ProofCommand command,
        string isolatedRoot,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var commandStartedAt = DateTimeOffset.UtcNow;
        var executable = ResolveExecutable(command.Executable, isolatedRoot, workingDirectory);
        var (startInfo, sensitiveValues) = CreateStartInfo(
            executable,
            command.Arguments,
            isolatedRoot,
            workingDirectory);
        var redactor = new OutputRedactor(_workspace.Root, isolatedRoot, sensitiveValues);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return CreateStartFailure(
                    command,
                    isolatedRoot,
                    workingDirectory,
                    commandStartedAt,
                    "The configured process did not start.",
                    redactor);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CreateStartFailure(
                command,
                isolatedRoot,
                workingDirectory,
                commandStartedAt,
                exception.Message,
                redactor);
        }

        var stdoutTask = CaptureAsync(process.StandardOutput);
        var stderrTask = CaptureAsync(process.StandardError);
        var timedOut = false;
        Exception? executionError = null;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(command.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKillProcessTree(process);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
            await Task.WhenAll(stdoutTask, stderrTask);
            throw;
        }
        catch (Exception exception)
        {
            executionError = exception;
            TryKillProcessTree(process);
        }

        await WaitForExitAfterKillAsync(process);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var completedAt = DateTimeOffset.UtcNow;
        var output = DescribeOutput(command, isolatedRoot, workingDirectory);
        int? exitCode = timedOut || executionError is not null ? null : process.ExitCode;
        var status = timedOut
            ? ProofStatus.TimedOut
            : executionError is not null
                ? ProofStatus.Failed
                : exitCode == 0
                    ? ProofStatus.Passed
                    : ProofStatus.Failed;
        var errorText = executionError is null
            ? stderr.Text
            : string.Concat(stderr.Text, stderr.Text.Length == 0 ? string.Empty : "\n", executionError.Message);
        var redactedStdout = redactor.Redact(stdout.Text, stdout.Truncated);
        var redactedStderr = redactor.Redact(errorText, stderr.Truncated);

        return new ProofCommandResult(
            command.Id,
            command.Executable,
            command.Arguments.ToArray(),
            status,
            exitCode,
            timedOut,
            DurationMilliseconds(commandStartedAt, completedAt),
            redactedStdout.Text,
            redactedStderr.Text,
            redactedStdout.Truncated,
            redactedStderr.Truncated,
            output.RelativeDirectory,
            output.Exists);
    }

    private ProofCommandResult CreateStartFailure(
        ProofCommand command,
        string isolatedRoot,
        string workingDirectory,
        DateTimeOffset startedAt,
        string error,
        OutputRedactor redactor)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var output = DescribeOutput(command, isolatedRoot, workingDirectory);
        var redactedError = redactor.Redact(error, truncated: false);
        return new ProofCommandResult(
            command.Id,
            command.Executable,
            command.Arguments.ToArray(),
            ProofStatus.StartFailed,
            ExitCode: null,
            TimedOut: false,
            DurationMilliseconds(startedAt, completedAt),
            StandardOutput: string.Empty,
            StandardError: redactedError.Text,
            StandardOutputTruncated: false,
            StandardErrorTruncated: redactedError.Truncated,
            output.RelativeDirectory,
            output.Exists);
    }

    private static (ProcessStartInfo StartInfo, IReadOnlyList<string> SensitiveValues) CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        string isolatedRoot,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var sensitiveValues = new List<string>();
        foreach (var name in startInfo.Environment.Keys.ToArray())
        {
            if (!IsSensitiveEnvironmentName(name))
            {
                continue;
            }

            var value = startInfo.Environment[name];
            if (!string.IsNullOrWhiteSpace(value) && value.Length >= 8)
            {
                sensitiveValues.Add(value);
            }

            startInfo.Environment.Remove(name);
        }

        startInfo.Environment.Remove("GIT_ASKPASS");
        startInfo.Environment.Remove("SSH_ASKPASS");
        startInfo.Environment.Remove("SSH_AUTH_SOCK");
        startInfo.Environment.Remove("TEZURI_WORKSPACE");

        var home = Path.Combine(isolatedRoot, ".tezuri-proof-home");
        var temporary = Path.Combine(isolatedRoot, ".tezuri-proof-tmp");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(temporary);
        startInfo.Environment["CI"] = "true";
        startInfo.Environment["DOTNET_CLI_HOME"] = home;
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["HOME"] = home;
        startInfo.Environment["NPM_CONFIG_CACHE"] = Path.Combine(temporary, "npm");
        startInfo.Environment["TEMP"] = temporary;
        startInfo.Environment["TMP"] = temporary;
        startInfo.Environment["TMPDIR"] = temporary;
        startInfo.Environment["USERPROFILE"] = home;
        startInfo.Environment["XDG_CACHE_HOME"] = Path.Combine(temporary, "cache");
        return (startInfo, sensitiveValues);
    }

    private static bool IsSensitiveEnvironmentName(string name)
    {
        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return normalized.Contains("TOKEN", StringComparison.Ordinal) ||
               normalized.Contains("PASSWORD", StringComparison.Ordinal) ||
               normalized.Contains("SECRET", StringComparison.Ordinal) ||
               normalized.Contains("CREDENTIAL", StringComparison.Ordinal) ||
               normalized.Contains("APIKEY", StringComparison.Ordinal) ||
               normalized.Contains("ACCESSKEY", StringComparison.Ordinal) ||
               normalized.Contains("PRIVATEKEY", StringComparison.Ordinal) ||
               normalized.Contains("AUTHORIZATION", StringComparison.Ordinal);
    }

    private static string ResolveExecutable(
        string executable,
        string isolatedRoot,
        string workingDirectory)
    {
        if (!executable.Contains('/') && !executable.Contains(Path.DirectorySeparatorChar))
        {
            return executable;
        }

        var path = ResolveContained(workingDirectory, executable);
        if (!IsWithinOrEqual(path, isolatedRoot) || !File.Exists(path))
        {
            throw new ProofException(
                $"Configured proof executable '{executable}' is not a file inside the isolated workspace.");
        }

        return path;
    }

    private static OutputDirectoryState DescribeOutput(
        ProofCommand command,
        string isolatedRoot,
        string workingDirectory)
    {
        if (command.OutputDirectory is null)
        {
            return new OutputDirectoryState(null, Exists: false);
        }

        var output = ResolveContained(workingDirectory, command.OutputDirectory);
        if (!IsWithinOrEqual(output, isolatedRoot))
        {
            throw new ProofException(
                $"Configured output directory '{command.OutputDirectory}' escapes the isolated workspace.");
        }

        RejectLinkTraversal(isolatedRoot, output);
        return new OutputDirectoryState(
            Path.GetRelativePath(isolatedRoot, output).Replace('\\', '/'),
            Directory.Exists(output));
    }

    private HashSet<string> GetConfiguredOutputPaths()
    {
        var outputs = new HashSet<string>(StringComparerForPlatform);
        foreach (var command in _proof.Commands)
        {
            if (command.OutputDirectory is null)
            {
                continue;
            }

            var path = _proof.WorkingDirectory == "."
                ? command.OutputDirectory
                : $"{_proof.WorkingDirectory}/{command.OutputDirectory}";
            outputs.Add(path);
        }

        return outputs;
    }

    private static async Task CopyWorkspaceAsync(
        string sourceRoot,
        string destinationRoot,
        string relativeDirectory,
        IReadOnlySet<string> configuredOutputPaths,
        CancellationToken cancellationToken)
    {
        foreach (var entry in new DirectoryInfo(sourceRoot).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = relativeDirectory.Length == 0
                ? entry.Name
                : $"{relativeDirectory}/{entry.Name}";
            if (ExcludedDirectoryNames.Contains(entry.Name) ||
                configuredOutputPaths.Contains(relativePath))
            {
                continue;
            }

            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget is not null)
            {
                throw new ProofException(
                    $"Proof isolation does not copy symbolic links or junctions ('{entry.Name}').");
            }

            var destination = Path.Combine(destinationRoot, entry.Name);
            if (entry is DirectoryInfo directory)
            {
                Directory.CreateDirectory(destination);
                await CopyWorkspaceAsync(
                    directory.FullName,
                    destination,
                    relativePath,
                    configuredOutputPaths,
                    cancellationToken);
                continue;
            }

            if (entry is not FileInfo file)
            {
                throw new ProofException($"Unsupported workspace entry '{entry.Name}'.");
            }

            await CopyFileAsync(file.FullName, destination, cancellationToken);
            File.SetLastWriteTimeUtc(destination, file.LastWriteTimeUtc);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destination, File.GetUnixFileMode(file.FullName));
            }
        }
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<CapturedOutput> CaptureAsync(StreamReader reader)
    {
        var buffer = new char[4_096];
        var captured = new StringBuilder(MaximumCapturedCharacters);
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            var remaining = MaximumCapturedCharacters - captured.Length;
            if (remaining > 0)
            {
                captured.Append(buffer.AsSpan(0, Math.Min(read, remaining)));
            }

            truncated |= read > remaining;
        }

        return new CapturedOutput(captured.ToString(), truncated);
    }

    private static string ResolveContained(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new ProofException($"Proof path '{relativePath}' must be relative.");
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(normalized, root);
        if (!IsWithinOrEqual(resolved, root))
        {
            throw new ProofException($"Proof path '{relativePath}' escapes its isolated root.");
        }

        return resolved;
    }

    private static void RejectLinkTraversal(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var cursor = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, segment);
            if (!Directory.Exists(cursor) && !File.Exists(cursor))
            {
                continue;
            }

            var entry = Directory.Exists(cursor)
                ? (FileSystemInfo)new DirectoryInfo(cursor)
                : new FileInfo(cursor);
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget is not null)
            {
                throw new ProofException(
                    $"Proof output traverses a symbolic link or junction ('{relative.Replace('\\', '/')}').");
            }
        }
    }

    private void DeleteTemporaryRun(string runRoot)
    {
        var resolved = Path.GetFullPath(runRoot);
        var parent = Path.GetDirectoryName(resolved);
        if (!StringComparerForPlatform.Equals(parent, _temporaryParent))
        {
            throw new ProofException("Refused to clean an unexpected proof temporary path.");
        }

        if (!Directory.Exists(resolved))
        {
            return;
        }

        MakeDeletableWithoutFollowingLinks(new DirectoryInfo(resolved));
        Directory.Delete(resolved, recursive: true);
    }

    private static void MakeDeletableWithoutFollowingLinks(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget is not null)
            {
                entry.Delete();
                continue;
            }

            if (entry is DirectoryInfo child)
            {
                MakeDeletableWithoutFollowingLinks(child);
                child.Attributes = FileAttributes.Normal;
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
            }
        }

        directory.Attributes = FileAttributes.Normal;
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and the kill request.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (InvalidOperationException)
        {
            // A completed process can race with the final wait.
        }
    }

    private static long DurationMilliseconds(DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds);

    private static bool IsWithinOrEqual(string path, string root)
    {
        var resolvedPath = Path.GetFullPath(path);
        var resolvedRoot = Path.GetFullPath(root);
        if (StringComparerForPlatform.Equals(resolvedPath, resolvedRoot))
        {
            return true;
        }

        var rootWithSeparator = Path.EndsInDirectorySeparator(resolvedRoot)
            ? resolvedRoot
            : resolvedRoot + Path.DirectorySeparatorChar;
        return resolvedPath.StartsWith(rootWithSeparator, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer StringComparerForPlatform =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class OutputRedactor(
        string workspaceRoot,
        string isolatedRoot,
        IReadOnlyList<string> sensitiveValues)
    {
        public RedactedOutput Redact(string source, bool truncated)
        {
            var result = source.Replace(
                isolatedRoot,
                "[isolated-workspace]",
                PathComparison);
            result = result.Replace(
                workspaceRoot,
                "[workspace]",
                PathComparison);
            foreach (var value in sensitiveValues.OrderByDescending(value => value.Length))
            {
                result = result.Replace(value, "[REDACTED]", StringComparison.Ordinal);
            }

            try
            {
                result = SensitiveAssignmentPattern.Replace(result, "$1=[REDACTED]");
            }
            catch (RegexMatchTimeoutException)
            {
                result = "[output withheld because redaction exceeded its processing limit]";
                truncated = true;
            }
            if (result.Length > MaximumCapturedCharacters)
            {
                result = result[..MaximumCapturedCharacters];
                truncated = true;
            }

            if (!truncated)
            {
                return new RedactedOutput(result, Truncated: false);
            }

            var contentLength = MaximumCapturedCharacters - TruncationMarker.Length;
            if (result.Length > contentLength)
            {
                result = result[..contentLength];
            }

            return new RedactedOutput(result + TruncationMarker, Truncated: true);
        }
    }

    private sealed record CapturedOutput(string Text, bool Truncated);

    private sealed record RedactedOutput(string Text, bool Truncated);

    private sealed record OutputDirectoryState(string? RelativeDirectory, bool Exists);
}

[ApiController]
[Route("api/v1/proof/runs")]
public sealed class ProofRunsController(ProofRunner runner) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ProofRun>(StatusCodes.Status200OK)]
    // The request deliberately carries no executable or arguments. Execution authority
    // comes only from the validated, committed WorkspaceConfigurationV1 singleton.
    public async Task<ActionResult<ProofRun>> Run(CancellationToken cancellationToken) =>
        Ok(await runner.RunAsync(cancellationToken));
}
