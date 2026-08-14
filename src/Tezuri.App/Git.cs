using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Tezuri;

/// <summary>
/// Publication: inspect the repository, plan a commit, make exactly that commit, push it.
/// </summary>
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

public sealed record GitChangedPath(
    string Path,
    string IndexStatus,
    string WorkTreeStatus,
    bool Allowed);

public sealed record GitRemoteBranch(
    string Remote,
    string Branch,
    string Sha);

public sealed record GitRepositorySnapshot(
    string? HeadSha,
    bool IsUnborn,
    bool IsDetached,
    string? Branch,
    string? Upstream,
    IReadOnlyList<string> Remotes,
    IReadOnlyList<GitRemoteBranch> RemoteBranches,
    IReadOnlyList<GitChangedPath> Changes);

public sealed record GitCommitPlanRequest(IReadOnlyList<string> SelectedPaths);

public sealed record GitCommitPlan(
    string HeadSha,
    string Branch,
    string PlanSha256,
    IReadOnlyList<string> SelectedPaths,
    IReadOnlyList<GitChangedPath> Changes);

public sealed record PrepareGitCommitRequest(
    string ExpectedHeadSha,
    string ExpectedPlanSha256,
    string Message,
    IReadOnlyList<string> SelectedPaths);

public sealed record GitCommitReceipt(
    string BeforeSha,
    string AfterSha,
    string Branch,
    string PlanSha256,
    IReadOnlyList<string> SelectedPaths,
    bool Created);

public sealed record GitPushRequest(
    string Remote,
    string Branch,
    string ExpectedHeadSha,
    string ExpectedRemoteSha);

public sealed record GitPushReceipt(
    string Remote,
    string Branch,
    string LocalSha,
    string RemoteBeforeSha,
    string RemoteAfterSha,
    bool Pushed);

internal static class GitAllowedPathMatcher
{
    public static bool IsMatch(string pattern, string path)
    {
        var patternSegments = pattern.Split('/');
        var pathSegments = path.Split('/');
        var states = new Dictionary<(int Pattern, int Path), bool>();
        return Match(patternSegments, pathSegments, 0, 0, states);
    }

    private static bool Match(
        IReadOnlyList<string> pattern,
        IReadOnlyList<string> path,
        int patternIndex,
        int pathIndex,
        IDictionary<(int Pattern, int Path), bool> states)
    {
        var key = (patternIndex, pathIndex);
        if (states.TryGetValue(key, out var cached))
        {
            return cached;
        }

        bool result;
        if (patternIndex == pattern.Count)
        {
            result = pathIndex == path.Count;
        }
        else if (pattern[patternIndex] == "**")
        {
            result = Match(pattern, path, patternIndex + 1, pathIndex, states) ||
                     (pathIndex < path.Count &&
                      Match(pattern, path, patternIndex, pathIndex + 1, states));
        }
        else
        {
            result = pathIndex < path.Count &&
                     SegmentMatches(pattern[patternIndex], path[pathIndex]) &&
                     Match(pattern, path, patternIndex + 1, pathIndex + 1, states);
        }

        states[key] = result;
        return result;
    }

    private static bool SegmentMatches(string pattern, string value)
    {
        var expression = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            switch (pattern[index])
            {
                case '*':
                    expression.Append(".*");
                    break;
                case '?':
                    expression.Append('.');
                    break;
                case '[':
                    var close = pattern.IndexOf(']', index + 1);
                    if (close < 0)
                    {
                        expression.Append("\\[");
                        break;
                    }

                    var members = pattern[(index + 1)..close];
                    if (members.Length == 0)
                    {
                        expression.Append("\\[\\]");
                    }
                    else
                    {
                        expression.Append('[');
                        foreach (var member in members.Where(member => member != '-'))
                        {
                            expression.Append(Regex.Escape(member.ToString()));
                        }

                        if (members.Contains('-'))
                        {
                            expression.Append("\\-");
                        }

                        expression.Append(']');
                    }

                    index = close;
                    break;
                default:
                    expression.Append(Regex.Escape(pattern[index].ToString()));
                    break;
            }
        }

        expression.Append('$');
        var options = RegexOptions.CultureInvariant;
        if (OperatingSystem.IsWindows())
        {
            options |= RegexOptions.IgnoreCase;
        }

        return Regex.IsMatch(
            value,
            expression.ToString(),
            options,
            TimeSpan.FromMilliseconds(100));
    }
}

public sealed class GitCommandRunner(WorkspacePathGuard workspace)
{
    public const int MaximumCapturedCharacters = 65_536;

    private const string TruncationMarker = "\n[output truncated]";

    private static readonly Regex SensitiveAssignmentPattern = new(
        @"\b(token|password|secret|api[_-]?key|authorization)\b\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex UrlAuthorityPattern = new(
        @"(?<=://)[^/\s@]+@",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    internal async Task<GitCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspace.Root,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        RemoveSensitiveEnvironment(startInfo);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        startInfo.Environment["SSH_ASKPASS_REQUIRE"] = "never";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new GitPublicationException(
                    GitPublicationFailure.CommandFailed,
                    "Git did not start.");
            }
        }
        catch (GitPublicationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new GitPublicationException(
                GitPublicationFailure.CommandFailed,
                "Git did not start: " + Redact(exception.Message));
        }

        process.StandardInput.Close();
        var stdoutTask = CaptureAsync(process.StandardOutput);
        var stderrTask = CaptureAsync(process.StandardError);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
            var timedOutError = await stderrTask;
            await stdoutTask;
            var detail = Redact(timedOutError.Text, timedOutError.Truncated);
            throw new GitPublicationException(
                GitPublicationFailure.CommandFailed,
                detail.Length == 0
                    ? "Git exceeded its operation timeout."
                    : "Git exceeded its operation timeout: " + detail);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
            await Task.WhenAll(stdoutTask, stderrTask);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new GitCommandResult(
            process.ExitCode,
            Bound(stdout.Text, stdout.Truncated),
            Bound(stderr.Text, stderr.Truncated));
    }

    private static void RemoveSensitiveEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var name in startInfo.Environment.Keys.ToArray())
        {
            if (IsSensitiveEnvironmentName(name))
            {
                startInfo.Environment.Remove(name);
            }
        }

        // Ambient agents/helpers are the supported credential delegation boundary.
        // Explicit askpass programs are disabled because Tezuri does not provide secrets.
        startInfo.Environment.Remove("GIT_ASKPASS");
        startInfo.Environment.Remove("SSH_ASKPASS");
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

    internal static string RedactOutput(string source)
    {
        var result = source;
        try
        {
            result = UrlAuthorityPattern.Replace(result, "[REDACTED]@");
            result = SensitiveAssignmentPattern.Replace(result, "$1=[REDACTED]");
        }
        catch (RegexMatchTimeoutException)
        {
            return "[output withheld because redaction exceeded its processing limit]";
        }

        return Bound(result, truncated: false);
    }

    private static string Redact(string source, bool truncated = false) =>
        Bound(RedactOutput(source), truncated);

    private static string Bound(string source, bool truncated)
    {
        var result = source;

        if (result.Length > MaximumCapturedCharacters)
        {
            result = result[..MaximumCapturedCharacters];
            truncated = true;
        }

        if (!truncated)
        {
            return result;
        }

        var contentLength = MaximumCapturedCharacters - TruncationMarker.Length;
        if (result.Length > contentLength)
        {
            result = result[..contentLength];
        }

        return result + TruncationMarker;
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
            // The process exited between the state check and kill request.
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

    private sealed record CapturedOutput(string Text, bool Truncated);
}

internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class GitPublicationService : IDisposable
{
    // Bounds exist to keep a runaway request from pinning the process, not to shape editorial
    // work. A full corpus import touches an article folder plus its media per article, so the
    // path ceiling has to clear that comfortably.
    private const int MaximumSelectedPaths = 4_096;
    private const int MaximumPathCharacters = 1_024;
    private const int MaximumCommitMessageCharacters = 4_096;
    private const string CommitIdentityName = "Tezuri";
    private const string CommitIdentityEmail = "tezuri@localhost.invalid";

    private static readonly TimeSpan LocalOperationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RemoteOperationTimeout = TimeSpan.FromMinutes(2);

    private static readonly HashSet<string> ReservedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly WorkspacePathGuard _workspace;
    private readonly IReadOnlyList<string> _allowedPaths;
    private readonly GitCommandRunner _commands;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public GitPublicationService(
        WorkspacePathGuard workspace,
        WorkspaceSettings settings,
        GitCommandRunner commands)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _allowedPaths = settings.AllowedPaths;
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public async Task<GitRepositorySnapshot> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureRepositoryAsync(cancellationToken);
            return (await ReadRepositoryStateAsync(cancellationToken)).Snapshot;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<GitCommitPlan> PlanCommitAsync(
        GitCommitPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureRepositoryAsync(cancellationToken);
            var state = await ReadRepositoryStateAsync(cancellationToken);
            EnsureCommitReady(state);
            var selectedPaths = ValidateSelectedPaths(request.SelectedPaths, state.StatusEntries);
            var entries = await CreateContentPlanEntriesAsync(
                selectedPaths,
                state.StatusEntries,
                staged: false,
                cancellationToken);
            var planSha256 = ComputePlanSha256(state.Snapshot.HeadSha!, entries);
            return new GitCommitPlan(
                state.Snapshot.HeadSha!,
                state.Snapshot.Branch!,
                planSha256,
                selectedPaths,
                selectedPaths
                    .Select(path => ToContractWithAllowed(state.StatusEntries[path]))
                    .ToArray());
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<GitCommitReceipt> PrepareCommitAsync(
        PrepareGitCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureObjectId(request.ExpectedHeadSha, "expected HEAD");
        EnsureSha256(request.ExpectedPlanSha256, "expected plan hash");
        EnsureCommitMessage(request.Message);

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureRepositoryAsync(cancellationToken);
            var state = await ReadRepositoryStateAsync(cancellationToken);
            EnsureNoStagedOrUnmergedChanges(state);
            var selectedPaths = ValidatePathList(request.SelectedPaths);
            var expectedHead = request.ExpectedHeadSha.ToLowerInvariant();
            var expectedPlan = request.ExpectedPlanSha256.ToLowerInvariant();

            if (!StringComparer.OrdinalIgnoreCase.Equals(state.Snapshot.HeadSha, expectedHead))
            {
                var retry = await TryCreateIdempotentReceiptAsync(
                    request,
                    selectedPaths,
                    state,
                    cancellationToken);
                if (retry is not null)
                {
                    return retry;
                }

                throw new GitPublicationException(
                    GitPublicationFailure.PreconditionFailed,
                    "HEAD changed after the commit plan was reviewed. No paths were staged.");
            }

            EnsureCommitReady(state);
            selectedPaths = ValidateSelectedPaths(selectedPaths, state.StatusEntries);
            var contentPlan = await CreateContentPlanEntriesAsync(
                selectedPaths,
                state.StatusEntries,
                staged: false,
                cancellationToken);
            var actualPlan = ComputePlanSha256(expectedHead, contentPlan);
            if (!StringComparer.OrdinalIgnoreCase.Equals(actualPlan, expectedPlan))
            {
                throw new GitPublicationException(
                    GitPublicationFailure.PreconditionFailed,
                    "The selected content changed after the commit plan was reviewed. No paths were staged.");
            }

            var stagedByService = false;
            var committed = false;
            try
            {
                var addArguments = new List<string> { "add", "--" };
                addArguments.AddRange(selectedPaths);
                await RunRequiredAsync("stage selected paths", addArguments, LocalOperationTimeout, cancellationToken);
                stagedByService = true;

                var stagedState = await ReadRepositoryStateAsync(cancellationToken);
                EnsureHeadEquals(stagedState, expectedHead, "HEAD changed while selected paths were staged.");
                EnsureExactStagedPlan(stagedState, selectedPaths);
                var stagedPlanEntries = await CreateContentPlanEntriesAsync(
                    selectedPaths,
                    stagedState.StatusEntries,
                    staged: true,
                    cancellationToken);
                var stagedPlan = ComputePlanSha256(expectedHead, stagedPlanEntries);
                if (!StringComparer.OrdinalIgnoreCase.Equals(stagedPlan, expectedPlan))
                {
                    throw new GitPublicationException(
                        GitPublicationFailure.PreconditionFailed,
                        "The staged content does not match the reviewed plan.");
                }

                var disabledHooksPath = Path.Combine(
                    Path.GetTempPath(),
                    $"tezuri-disabled-hooks-{Guid.NewGuid():N}");
                var commitArguments = new[]
                {
                    "-c", $"core.hooksPath={disabledHooksPath}",
                    "-c", "commit.gpgSign=false",
                    "-c", $"user.name={CommitIdentityName}",
                    "-c", $"user.email={CommitIdentityEmail}",
                    "commit",
                    "--no-verify",
                    "--no-gpg-sign",
                    "--cleanup=verbatim",
                    "--message", request.Message
                };
                await RunRequiredAsync(
                    "create the reviewed commit",
                    commitArguments,
                    LocalOperationTimeout,
                    cancellationToken);
                committed = true;

                var after = await ReadRepositoryStateAsync(cancellationToken);
                if (after.Snapshot.HeadSha is null ||
                    StringComparer.OrdinalIgnoreCase.Equals(after.Snapshot.HeadSha, expectedHead))
                {
                    throw new GitPublicationException(
                        GitPublicationFailure.CommandFailed,
                        "Git reported success but did not create a commit.");
                }

                await EnsureCommitMatchesAsync(
                    after.Snapshot.HeadSha,
                    expectedHead,
                    expectedPlan,
                    request.Message,
                    selectedPaths,
                    after,
                    cancellationToken);
                return CreateCommitReceipt(
                    expectedHead,
                    after.Snapshot.HeadSha,
                    after.Snapshot.Branch!,
                    expectedPlan,
                    selectedPaths,
                    created: true);
            }
            catch
            {
                if (stagedByService && !committed)
                {
                    await TryRestoreServiceStagingAsync(
                        expectedHead,
                        expectedPlan,
                        selectedPaths,
                        CancellationToken.None);
                }

                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<GitPushReceipt> PushAsync(
        GitPushRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureRemoteName(request.Remote);
        EnsureBranchName(request.Branch);
        EnsureObjectId(request.ExpectedHeadSha, "expected HEAD");
        EnsureObjectId(request.ExpectedRemoteSha, "expected remote SHA");

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureRepositoryAsync(cancellationToken);
            var state = await ReadRepositoryStateAsync(cancellationToken);
            EnsurePushState(state, request);
            if (!state.Snapshot.Remotes.Contains(request.Remote, StringComparer.Ordinal))
            {
                throw new GitPublicationException(
                    GitPublicationFailure.InvalidRequest,
                    "The requested Git remote is not configured in this workspace.");
            }

            var transportOptions = TransportSafetyArguments();
            var fetchArguments = new List<string>(transportOptions)
            {
                "fetch",
                "--no-tags",
                request.Remote,
                $"refs/heads/{request.Branch}"
            };
            await RunRequiredAsync(
                "fetch the expected remote branch",
                fetchArguments,
                RemoteOperationTimeout,
                cancellationToken);
            var fetched = await RunRequiredAsync(
                "inspect the fetched remote tip",
                ["rev-parse", "--verify", "FETCH_HEAD"],
                LocalOperationTimeout,
                cancellationToken);
            var fetchedSha = NormalizeObjectId(fetched.StandardOutput, "fetched remote tip");
            if (!StringComparer.OrdinalIgnoreCase.Equals(fetchedSha, request.ExpectedRemoteSha))
            {
                throw new GitPublicationException(
                    GitPublicationFailure.Diverged,
                    "The remote branch moved after it was reviewed. Nothing was pushed.");
            }

            var afterFetch = await ReadRepositoryStateAsync(cancellationToken);
            EnsurePushState(afterFetch, request);
            var ancestor = await _commands.RunAsync(
                GitArguments([
                    "merge-base",
                    "--is-ancestor",
                    fetchedSha,
                    request.ExpectedHeadSha
                ]),
                LocalOperationTimeout,
                cancellationToken);
            if (ancestor.ExitCode == 1)
            {
                throw new GitPublicationException(
                    GitPublicationFailure.Diverged,
                    "The reviewed remote tip is not an ancestor of local HEAD. Nothing was pushed.");
            }

            if (ancestor.ExitCode != 0)
            {
                throw CommandFailure("compare local and remote history", ancestor);
            }

            var localSha = request.ExpectedHeadSha.ToLowerInvariant();
            var remoteBefore = request.ExpectedRemoteSha.ToLowerInvariant();
            if (StringComparer.OrdinalIgnoreCase.Equals(localSha, remoteBefore))
            {
                return CreatePushReceipt(
                    request,
                    localSha,
                    remoteBefore,
                    localSha,
                    pushed: false);
            }

            var pushArguments = new List<string>(transportOptions)
            {
                "push",
                "--porcelain",
                request.Remote,
                $"{localSha}:refs/heads/{request.Branch}"
            };
            await RunRequiredAsync(
                "push the reviewed commit",
                pushArguments,
                RemoteOperationTimeout,
                cancellationToken);

            var verifyArguments = new List<string>(transportOptions)
            {
                "ls-remote",
                "--exit-code",
                request.Remote,
                $"refs/heads/{request.Branch}"
            };
            var verified = await RunRequiredAsync(
                "verify the pushed remote tip",
                verifyArguments,
                RemoteOperationTimeout,
                cancellationToken);
            var remoteAfter = NormalizeLsRemoteSha(verified.StandardOutput);
            if (!StringComparer.OrdinalIgnoreCase.Equals(remoteAfter, localSha))
            {
                throw new GitPublicationException(
                    GitPublicationFailure.Diverged,
                    "The remote tip did not match the reviewed local commit after push.");
            }

            return CreatePushReceipt(
                request,
                localSha,
                remoteBefore,
                remoteAfter,
                pushed: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose() => _operationGate.Dispose();

    private async Task EnsureRepositoryAsync(CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(_workspace.Root);
        if (!root.Exists || root.LinkTarget is not null ||
            (root.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new GitPublicationException(
                GitPublicationFailure.NotRepository,
                "The mounted workspace root must be an existing non-link directory.");
        }

        string gitDirectory;
        try
        {
            gitDirectory = _workspace.Resolve(".git");
        }
        catch (WorkspacePathException)
        {
            throw new GitPublicationException(
                GitPublicationFailure.NotRepository,
                "The mounted workspace does not contain a safe Git metadata directory.");
        }

        if (!Directory.Exists(gitDirectory))
        {
            throw new GitPublicationException(
                GitPublicationFailure.NotRepository,
                "The mounted workspace is not a supported Git repository. Git worktree pointer files are not supported in V1.");
        }

        try
        {
            foreach (var metadataPath in new[]
                     {
                         ".git/HEAD",
                         ".git/config",
                         ".git/index",
                         ".git/objects",
                         ".git/refs",
                         ".git/packed-refs"
                     })
            {
                _workspace.Resolve(metadataPath);
            }
        }
        catch (WorkspacePathException)
        {
            throw new GitPublicationException(
                GitPublicationFailure.NotRepository,
                "Git metadata required for publication must not traverse symbolic links or junctions.");
        }

        if (File.Exists(Path.Combine(gitDirectory, "commondir")) ||
            File.Exists(Path.Combine(gitDirectory, "objects", "info", "alternates")))
        {
            throw new GitPublicationException(
                GitPublicationFailure.NotRepository,
                "Git common-directory and alternate-object-store indirection are outside the mounted-repository V1 boundary.");
        }

        var inside = await RunRequiredAsync(
            "inspect the repository root",
            ["rev-parse", "--is-inside-work-tree"],
            LocalOperationTimeout,
            cancellationToken);
        if (!StringComparer.Ordinal.Equals(inside.StandardOutput.Trim(), "true"))
        {
            throw new GitPublicationException(
                GitPublicationFailure.NotRepository,
                "The mounted workspace is not a Git work tree.");
        }

        var topLevel = await RunRequiredAsync(
            "inspect the repository top level",
            ["rev-parse", "--show-toplevel"],
            LocalOperationTimeout,
            cancellationToken);
        var actualRoot = Path.GetFullPath(topLevel.StandardOutput.Trim());
        if (!PlatformPathComparer.Equals(actualRoot, _workspace.Root))
        {
            throw new GitPublicationException(
                GitPublicationFailure.NotRepository,
                "The mounted workspace must be the Git repository top level.");
        }
    }

    private async Task<RepositoryState> ReadRepositoryStateAsync(CancellationToken cancellationToken)
    {
        var branchResult = await _commands.RunAsync(
            GitArguments(["symbolic-ref", "--quiet", "--short", "HEAD"]),
            LocalOperationTimeout,
            cancellationToken);
        var branch = branchResult.ExitCode == 0
            ? RequireSingleLine(branchResult.StandardOutput, "current branch")
            : null;

        var headResult = await _commands.RunAsync(
            GitArguments(["rev-parse", "--verify", "--quiet", "HEAD"]),
            LocalOperationTimeout,
            cancellationToken);
        string? head = null;
        if (headResult.ExitCode == 0)
        {
            head = NormalizeObjectId(headResult.StandardOutput, "HEAD");
        }
        else if (headResult.ExitCode != 1)
        {
            throw CommandFailure("inspect HEAD", headResult);
        }

        if (head is null && branch is null)
        {
            throw new GitPublicationException(
                GitPublicationFailure.NotRepository,
                "Git HEAD is neither a branch nor a commit.");
        }

        string? upstream = null;
        if (head is not null && branch is not null)
        {
            var upstreamResult = await _commands.RunAsync(
                GitArguments(["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"]),
                LocalOperationTimeout,
                cancellationToken);
            if (upstreamResult.ExitCode == 0)
            {
                upstream = RequireSingleLine(upstreamResult.StandardOutput, "upstream branch");
            }
        }

        var remoteResult = await RunRequiredAsync(
            "list configured remotes",
            ["remote"],
            LocalOperationTimeout,
            cancellationToken);
        var remotes = remoteResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var remoteBranches = await ReadRemoteBranchesAsync(remotes, cancellationToken);
        var statusEntries = await ReadStatusEntriesAsync(cancellationToken);
        var changes = statusEntries.Values
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(ToContractWithAllowed)
            .ToArray();
        return new RepositoryState(
            new GitRepositorySnapshot(
                head,
                IsUnborn: head is null,
                IsDetached: head is not null && branch is null,
                branch,
                upstream,
                remotes,
                remoteBranches,
                changes),
            statusEntries);
    }

    private async Task<IReadOnlyList<GitRemoteBranch>> ReadRemoteBranchesAsync(
        IReadOnlyList<string> remotes,
        CancellationToken cancellationToken)
    {
        var result = await RunRequiredAsync(
            "inspect remote-tracking branches",
            ["for-each-ref", "--format=%(refname:short)%00%(objectname)%00%(symref)", "refs/remotes"],
            LocalOperationTimeout,
            cancellationToken);
        var branches = new List<GitRemoteBranch>();
        foreach (var line in result.StandardOutput.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\0');
            if (fields.Length != 3 || fields[2].Length != 0)
            {
                continue;
            }

            var remote = remotes.FirstOrDefault(candidate =>
                fields[0].StartsWith(candidate + "/", StringComparison.Ordinal));
            if (remote is null)
            {
                continue;
            }

            var branch = fields[0][(remote.Length + 1)..];
            if (!IsPortableName(branch, allowSlash: true, maximumLength: 250))
            {
                continue;
            }

            EnsureObjectId(fields[1], "remote-tracking SHA");
            branches.Add(new GitRemoteBranch(
                remote,
                branch,
                fields[1].ToLowerInvariant()));
        }

        return branches
            .OrderBy(branch => branch.Remote, StringComparer.Ordinal)
            .ThenBy(branch => branch.Branch, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, StatusEntry>> ReadStatusEntriesAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunRequiredAsync(
            "inspect repository changes",
            ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--no-renames"],
            LocalOperationTimeout,
            cancellationToken);
        var entries = new Dictionary<string, StatusEntry>(StringComparer.Ordinal);
        foreach (var rawEntry in result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawEntry.Length < 4 || rawEntry[2] != ' ')
            {
                throw new GitPublicationException(
                    GitPublicationFailure.CommandFailed,
                    "Git returned an unsupported repository status record.");
            }

            var path = rawEntry[3..].Replace('\\', '/');
            if (!entries.TryAdd(path, new StatusEntry(path, rawEntry[0], rawEntry[1])))
            {
                throw new GitPublicationException(
                    GitPublicationFailure.CommandFailed,
                    "Git returned the same changed path more than once.");
            }
        }

        return entries;
    }

    private void EnsureCommitReady(RepositoryState state)
    {
        EnsureNoStagedOrUnmergedChanges(state);
        if (state.Snapshot.IsUnborn || state.Snapshot.HeadSha is null)
        {
            throw new GitPublicationException(
                GitPublicationFailure.Conflict,
                "Preparing the first commit in an unborn repository is outside the V1 publication boundary.");
        }

        if (state.Snapshot.IsDetached || state.Snapshot.Branch is null)
        {
            throw new GitPublicationException(
                GitPublicationFailure.Conflict,
                "Preparing a commit from detached HEAD is not supported. No branch was changed.");
        }
    }

    private static void EnsureNoStagedOrUnmergedChanges(RepositoryState state)
    {
        if (state.StatusEntries.Values.Any(IsUnmerged))
        {
            throw new GitPublicationException(
                GitPublicationFailure.Conflict,
                "The repository contains unmerged paths. Resolve them outside Tezuri before publication.");
        }

        if (state.StatusEntries.Values.Any(IsStaged))
        {
            throw new GitPublicationException(
                GitPublicationFailure.StagedChangesPresent,
                "The Git index already contains staged changes. V1 refuses to alter a pre-existing staged plan.");
        }
    }

    private IReadOnlyList<string> ValidateSelectedPaths(
        IReadOnlyList<string>? requestedPaths,
        IReadOnlyDictionary<string, StatusEntry> statusEntries)
    {
        var paths = ValidatePathList(requestedPaths);
        foreach (var path in paths)
        {
            if (!statusEntries.ContainsKey(path))
            {
                throw new GitPublicationException(
                    GitPublicationFailure.PreconditionFailed,
                    "Every selected path must still have an unstaged or untracked change.");
            }
        }

        return paths;
    }

    private IReadOnlyList<string> ValidatePathList(IReadOnlyList<string>? requestedPaths)
    {
        if (requestedPaths is null || requestedPaths.Count == 0 || requestedPaths.Count > MaximumSelectedPaths)
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                $"Select between 1 and {MaximumSelectedPaths} changed paths.");
        }

        var selected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in requestedPaths)
        {
            EnsureSafeSelectedPath(path);
            if (!selected.Add(path))
            {
                throw new GitPublicationException(
                    GitPublicationFailure.InvalidRequest,
                    "Selected Git paths must be unique.");
            }
        }

        return selected.ToArray();
    }

    private void EnsureSafeSelectedPath(string path)
    {
        // This ordering is the containment guarantee, not the allow-list. A path that traverses,
        // escapes, or names .git is refused here regardless of what any pattern permits, so the
        // allow-list only ever narrows an already-safe set.
        if (!IsPortableRepositoryPath(path))
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                "Selected Git paths must be portable repository-relative file paths outside .git.");
        }

        if (!IsAllowed(path))
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                "A selected path is outside the paths this workspace allows Tezuri to commit.");
        }

        try
        {
            _workspace.Resolve(path);
        }
        catch (WorkspacePathException)
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                "A selected path failed workspace containment or symbolic-link validation.");
        }
    }

    private bool IsAllowed(string path) =>
        _allowedPaths.Any(pattern => GitAllowedPathMatcher.IsMatch(pattern, path));

    private static bool IsPortableRepositoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > MaximumPathCharacters ||
            path.StartsWith('/') ||
            path.Contains('\\') ||
            path.Contains("//", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            path.Any(char.IsControl))
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or ".." ||
                segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                segment.Length > 255 ||
                segment[0] == '-' ||
                segment[^1] is '.' or ' ' ||
                segment.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
            {
                return false;
            }

            var stem = segment.Split('.', 2)[0];
            if (ReservedSegments.Contains(stem))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<IReadOnlyList<ContentPlanEntry>> CreateContentPlanEntriesAsync(
        IReadOnlyList<string> selectedPaths,
        IReadOnlyDictionary<string, StatusEntry> statuses,
        bool staged,
        CancellationToken cancellationToken)
    {
        var entries = new List<ContentPlanEntry>(selectedPaths.Count);
        foreach (var path in selectedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!statuses.TryGetValue(path, out var status))
            {
                throw new GitPublicationException(
                    GitPublicationFailure.PreconditionFailed,
                    "A selected path no longer has the expected Git change.");
            }

            var change = GetChange(status, staged);
            if (change == "deleted")
            {
                entries.Add(new ContentPlanEntry(path, change, "-", -1));
                continue;
            }

            string absolutePath;
            try
            {
                absolutePath = _workspace.Resolve(path);
            }
            catch (WorkspacePathException)
            {
                throw new GitPublicationException(
                    GitPublicationFailure.PreconditionFailed,
                    "A selected path no longer passes workspace containment.");
            }

            if (!File.Exists(absolutePath) || Directory.Exists(absolutePath))
            {
                throw new GitPublicationException(
                    GitPublicationFailure.PreconditionFailed,
                    "A selected changed file no longer exists as a regular file.");
            }

            var content = await HashFileAsync(absolutePath, cancellationToken);
            entries.Add(new ContentPlanEntry(path, change, content.Sha256, content.ByteLength));
        }

        return entries;
    }

    private static string GetChange(StatusEntry status, bool staged)
    {
        if (!staged && status.Index == '?' && status.WorkTree == '?')
        {
            return "added";
        }

        var code = staged ? status.Index : status.WorkTree;
        return code switch
        {
            'A' => "added",
            'M' => "modified",
            'D' => "deleted",
            'T' => "type-changed",
            _ => throw new GitPublicationException(
                GitPublicationFailure.PreconditionFailed,
                "A selected path has an unsupported Git change type.")
        };
    }

    private static async Task<FileHash> HashFileAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        long length = 0;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                length = checked(length + read);
            }

            return new FileHash(
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string ComputePlanSha256(
        string expectedHead,
        IReadOnlyList<ContentPlanEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendPlanField(hash, "tezuri.git-content-plan/v1");
        AppendPlanField(hash, expectedHead.ToLowerInvariant());
        foreach (var entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            AppendPlanField(hash, entry.Path);
            AppendPlanField(hash, entry.Change);
            AppendPlanField(hash, entry.ByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendPlanField(hash, entry.ContentSha256);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendPlanField(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static void EnsureExactStagedPlan(
        RepositoryState state,
        IReadOnlyList<string> selectedPaths)
    {
        var stagedPaths = state.StatusEntries.Values
            .Where(IsStaged)
            .Select(entry => entry.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!stagedPaths.SequenceEqual(selectedPaths, StringComparer.Ordinal))
        {
            throw new GitPublicationException(
                GitPublicationFailure.PreconditionFailed,
                "The Git index does not contain exactly the selected publication paths.");
        }

        foreach (var path in selectedPaths)
        {
            var status = state.StatusEntries[path];
            if (status.WorkTree != ' ')
            {
                throw new GitPublicationException(
                    GitPublicationFailure.PreconditionFailed,
                    "A selected file changed again after it was staged.");
            }
        }
    }

    private async Task TryRestoreServiceStagingAsync(
        string expectedHead,
        string expectedPlan,
        IReadOnlyList<string> selectedPaths,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await ReadRepositoryStateAsync(cancellationToken);
            if (!StringComparer.OrdinalIgnoreCase.Equals(state.Snapshot.HeadSha, expectedHead))
            {
                return;
            }

            var stagedPaths = state.StatusEntries.Values
                .Where(IsStaged)
                .Select(entry => entry.Path)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!stagedPaths.SequenceEqual(selectedPaths, StringComparer.Ordinal) ||
                selectedPaths.Any(path => state.StatusEntries[path].WorkTree != ' '))
            {
                return;
            }

            var entries = await CreateContentPlanEntriesAsync(
                selectedPaths,
                state.StatusEntries,
                staged: true,
                cancellationToken);
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    ComputePlanSha256(expectedHead, entries),
                    expectedPlan))
            {
                return;
            }

            var restoreArguments = new List<string>
            {
                "restore",
                "--staged",
                $"--source={expectedHead}",
                "--"
            };
            restoreArguments.AddRange(selectedPaths);
            var result = await _commands.RunAsync(
                GitArguments(restoreArguments),
                LocalOperationTimeout,
                cancellationToken);
            _ = result.ExitCode;
        }
        catch
        {
            // Cleanup is best effort. Never run broader index/history repair when the
            // exact service-added staged plan cannot be proven.
        }
    }

    private async Task<GitCommitReceipt?> TryCreateIdempotentReceiptAsync(
        PrepareGitCommitRequest request,
        IReadOnlyList<string> selectedPaths,
        RepositoryState current,
        CancellationToken cancellationToken)
    {
        if (current.Snapshot.HeadSha is null ||
            current.Snapshot.Branch is null ||
            current.Snapshot.IsDetached ||
            current.Snapshot.IsUnborn ||
            selectedPaths.Any(current.StatusEntries.ContainsKey))
        {
            return null;
        }

        try
        {
            await EnsureCommitMatchesAsync(
                current.Snapshot.HeadSha,
                request.ExpectedHeadSha,
                request.ExpectedPlanSha256,
                request.Message,
                selectedPaths,
                current,
                cancellationToken);
        }
        catch (GitPublicationException)
        {
            return null;
        }

        return CreateCommitReceipt(
            request.ExpectedHeadSha.ToLowerInvariant(),
            current.Snapshot.HeadSha,
            current.Snapshot.Branch,
            request.ExpectedPlanSha256.ToLowerInvariant(),
            selectedPaths,
            created: false);
    }

    private async Task EnsureCommitMatchesAsync(
        string commitSha,
        string expectedParent,
        string expectedPlan,
        string expectedMessage,
        IReadOnlyList<string> selectedPaths,
        RepositoryState current,
        CancellationToken cancellationToken)
    {
        var parentsResult = await RunRequiredAsync(
            "inspect commit ancestry",
            ["rev-list", "--parents", "-n", "1", commitSha],
            LocalOperationTimeout,
            cancellationToken);
        var ancestry = parentsResult.StandardOutput
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ancestry.Length != 2 ||
            !StringComparer.OrdinalIgnoreCase.Equals(ancestry[0], commitSha) ||
            !StringComparer.OrdinalIgnoreCase.Equals(ancestry[1], expectedParent))
        {
            throw new GitPublicationException(
                GitPublicationFailure.PreconditionFailed,
                "Current HEAD is not the single reviewed commit over the expected parent.");
        }

        var messageResult = await RunRequiredAsync(
            "inspect commit message",
            ["log", "-1", "--format=%B", commitSha],
            LocalOperationTimeout,
            cancellationToken);
        var actualMessage = messageResult.StandardOutput.TrimEnd('\r', '\n');
        if (!StringComparer.Ordinal.Equals(actualMessage, expectedMessage))
        {
            throw new GitPublicationException(
                GitPublicationFailure.PreconditionFailed,
                "Current HEAD does not have the reviewed commit message.");
        }

        var changes = await ReadCommitChangesAsync(commitSha, cancellationToken);
        var actualPaths = changes.Select(change => change.Path).Order(StringComparer.Ordinal).ToArray();
        if (!actualPaths.SequenceEqual(selectedPaths, StringComparer.Ordinal))
        {
            throw new GitPublicationException(
                GitPublicationFailure.PreconditionFailed,
                "Current HEAD does not contain exactly the reviewed publication paths.");
        }

        if (selectedPaths.Any(current.StatusEntries.ContainsKey))
        {
            throw new GitPublicationException(
                GitPublicationFailure.PreconditionFailed,
                "A reviewed path changed again after the commit was created.");
        }

        var commitStatuses = changes.ToDictionary(
            change => change.Path,
            change => new StatusEntry(change.Path, change.Status, ' '),
            StringComparer.Ordinal);
        var entries = await CreateContentPlanEntriesAsync(
            selectedPaths,
            commitStatuses,
            staged: true,
            cancellationToken);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                ComputePlanSha256(expectedParent, entries),
                expectedPlan))
        {
            throw new GitPublicationException(
                GitPublicationFailure.PreconditionFailed,
                "Current HEAD content does not match the reviewed commit plan.");
        }
    }

    private async Task<IReadOnlyList<CommitChange>> ReadCommitChangesAsync(
        string commitSha,
        CancellationToken cancellationToken)
    {
        var result = await RunRequiredAsync(
            "inspect committed paths",
            ["diff-tree", "--root", "--no-commit-id", "--name-status", "-r", "--no-renames", "-z", commitSha],
            LocalOperationTimeout,
            cancellationToken);
        var fields = result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<CommitChange>();
        for (var index = 0; index < fields.Length;)
        {
            string statusField;
            string path;
            if (fields[index].Length == 1 && IsSupportedChangeCode(fields[index][0]))
            {
                statusField = fields[index++];
                if (index >= fields.Length)
                {
                    throw UnsupportedCommitDiff();
                }

                path = fields[index++];
            }
            else if (fields[index].Length > 2 && fields[index][1] == '\t')
            {
                statusField = fields[index][..1];
                path = fields[index][2..];
                index++;
            }
            else
            {
                throw UnsupportedCommitDiff();
            }

            if (!IsSupportedChangeCode(statusField[0]) || !IsPortableRepositoryPath(path))
            {
                throw UnsupportedCommitDiff();
            }

            changes.Add(new CommitChange(path, statusField[0]));
        }

        return changes;
    }

    private static GitPublicationException UnsupportedCommitDiff() => new(
        GitPublicationFailure.CommandFailed,
        "Git returned an unsupported committed-path record.");

    private static bool IsSupportedChangeCode(char status) => status is 'A' or 'M' or 'D' or 'T';

    private static void EnsurePushState(RepositoryState state, GitPushRequest request)
    {
        if (state.Snapshot.IsUnborn || state.Snapshot.HeadSha is null)
        {
            throw new GitPublicationException(
                GitPublicationFailure.Conflict,
                "Pushing an unborn repository is outside the V1 publication boundary.");
        }

        if (state.Snapshot.IsDetached || state.Snapshot.Branch is null)
        {
            throw new GitPublicationException(
                GitPublicationFailure.Conflict,
                "Detached HEAD cannot be pushed through Tezuri V1.");
        }

        if (!StringComparer.Ordinal.Equals(state.Snapshot.Branch, request.Branch))
        {
            throw new GitPublicationException(
                GitPublicationFailure.PreconditionFailed,
                "The checked-out branch differs from the reviewed push branch.");
        }

        EnsureHeadEquals(state, request.ExpectedHeadSha, "HEAD changed after the push was reviewed.");
    }

    private static void EnsureHeadEquals(
        RepositoryState state,
        string expectedHead,
        string message)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(state.Snapshot.HeadSha, expectedHead))
        {
            throw new GitPublicationException(GitPublicationFailure.PreconditionFailed, message);
        }
    }

    private async Task<GitCommandResult> RunRequiredAsync(
        string operation,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await _commands.RunAsync(
            GitArguments(arguments),
            timeout,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw CommandFailure(operation, result);
        }

        return result;
    }

    private IReadOnlyList<string> GitArguments(IReadOnlyList<string> arguments)
    {
        var result = new List<string>(arguments.Count + 2)
        {
            "-c",
            $"safe.directory={_workspace.Root}"
        };
        result.AddRange(arguments);
        return result;
    }

    private IReadOnlyList<string> TransportSafetyArguments() =>
    [
        "-c", $"safe.directory={_workspace.Root}",
        "-c", "protocol.ext.allow=never",
        "-c", "protocol.file.allow=always"
    ];

    private static GitPublicationException CommandFailure(
        string operation,
        GitCommandResult result)
    {
        var detail = result.StandardError.Trim();
        if (detail.Length == 0)
        {
            detail = result.StandardOutput.Trim();
        }

        return new GitPublicationException(
            GitPublicationFailure.CommandFailed,
            detail.Length == 0
                ? $"Git could not {operation}."
                : $"Git could not {operation}: {GitCommandRunner.RedactOutput(detail)}");
    }

    private GitChangedPath ToContractWithAllowed(StatusEntry entry) => new(
        entry.Path,
        DescribeStatus(entry.Index, entry.Index == '?' && entry.WorkTree == '?'),
        DescribeStatus(entry.WorkTree, entry.Index == '?' && entry.WorkTree == '?'),
        IsPortableRepositoryPath(entry.Path) && IsAllowed(entry.Path));

    private static string DescribeStatus(char status, bool untracked)
    {
        if (untracked)
        {
            return "untracked";
        }

        return status switch
        {
            ' ' => "none",
            'A' => "added",
            'M' => "modified",
            'D' => "deleted",
            'R' => "renamed",
            'C' => "copied",
            'T' => "type-changed",
            'U' => "unmerged",
            _ => "unknown"
        };
    }

    private static bool IsStaged(StatusEntry entry) => entry.Index is not ' ' and not '?';

    private static bool IsUnmerged(StatusEntry entry) =>
        entry.Index == 'U' ||
        entry.WorkTree == 'U' ||
        (entry.Index, entry.WorkTree) is ('A', 'A') or ('D', 'D');

    private static void EnsureObjectId(string value, string name)
    {
        if ((value.Length is not 40 and not 64) || !value.All(Uri.IsHexDigit))
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                $"The {name} must be a full Git object id.");
        }
    }

    private static void EnsureSha256(string value, string name)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                $"The {name} must be a full SHA-256 value.");
        }
    }

    private static void EnsureCommitMessage(string message)
    {
        // A conventional commit is a subject plus an optional body, so newlines are ordinary
        // content here. Every other control character is still refused.
        if (string.IsNullOrWhiteSpace(message) ||
            message.Length > MaximumCommitMessageCharacters ||
            !StringComparer.Ordinal.Equals(message, message.Trim()) ||
            message.Any(character => char.IsControl(character) && character is not '\n'))
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                $"The commit message must be plain text of at most {MaximumCommitMessageCharacters} characters.");
        }
    }

    private static void EnsureRemoteName(string remote)
    {
        if (!IsPortableName(remote, allowSlash: false, maximumLength: 100))
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                "The remote must be one configured portable Git remote name.");
        }
    }

    private static void EnsureBranchName(string branch)
    {
        if (!IsPortableName(branch, allowSlash: true, maximumLength: 250) ||
            branch.Contains("..", StringComparison.Ordinal) ||
            branch.Contains("@{", StringComparison.Ordinal) ||
            branch.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                "The branch must be a portable full branch name without ref-control syntax.");
        }
    }

    private static bool IsPortableName(string value, bool allowSlash, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value[0] is '-' or '.' or '/' ||
            value[^1] is '.' or '/' ||
            value.Contains("//", StringComparison.Ordinal) ||
            value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '.' or '_' or '-' ||
                  (allowSlash && character == '/'))))
        {
            return false;
        }

        return value.Split('/').All(segment =>
            segment.Length > 0 && segment is not "." and not ".." && segment[0] != '.');
    }

    private static string NormalizeObjectId(string output, string name)
    {
        var value = RequireSingleLine(output, name);
        EnsureObjectId(value, name);
        return value.ToLowerInvariant();
    }

    private static string NormalizeLsRemoteSha(string output)
    {
        var line = RequireSingleLine(output, "remote tip");
        var separator = line.IndexOfAny(['\t', ' ']);
        if (separator <= 0)
        {
            throw new GitPublicationException(
                GitPublicationFailure.CommandFailed,
                "Git returned an unsupported remote-tip record.");
        }

        var sha = line[..separator];
        EnsureObjectId(sha, "remote tip");
        return sha.ToLowerInvariant();
    }

    private static string RequireSingleLine(string output, string name)
    {
        var value = output.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => character is '\r' or '\n' or '\0'))
        {
            throw new GitPublicationException(
                GitPublicationFailure.CommandFailed,
                $"Git returned an unsupported {name} value.");
        }

        return value;
    }

    private static GitCommitReceipt CreateCommitReceipt(
        string beforeSha,
        string afterSha,
        string branch,
        string planSha256,
        IReadOnlyList<string> selectedPaths,
        bool created) => new(
        beforeSha.ToLowerInvariant(),
        afterSha.ToLowerInvariant(),
        branch,
        planSha256.ToLowerInvariant(),
        selectedPaths.ToArray(),
        created);

    private static GitPushReceipt CreatePushReceipt(
        GitPushRequest request,
        string localSha,
        string remoteBeforeSha,
        string remoteAfterSha,
        bool pushed) => new(
        request.Remote,
        request.Branch,
        localSha,
        remoteBeforeSha,
        remoteAfterSha,
        pushed);

    private static StringComparer PlatformPathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record RepositoryState(
        GitRepositorySnapshot Snapshot,
        IReadOnlyDictionary<string, StatusEntry> StatusEntries);

    private sealed record StatusEntry(string Path, char Index, char WorkTree);

    private sealed record ContentPlanEntry(
        string Path,
        string Change,
        string ContentSha256,
        long ByteLength);

    private sealed record FileHash(string Sha256, long ByteLength);

    private sealed record CommitChange(string Path, char Status);
}

[ApiController]
[Route("api/v1/git")]
public sealed class GitPublicationController(GitPublicationService publication) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<GitRepositorySnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitRepositorySnapshot>> Inspect(
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => publication.InspectAsync(cancellationToken));

    [HttpPost("commit-plans")]
    [ProducesResponseType<GitCommitPlan>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitCommitPlan>> PlanCommit(
        [FromBody] GitCommitPlanRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            () => publication.PlanCommitAsync(request, cancellationToken));

    [HttpPost("commits")]
    [ProducesResponseType<GitCommitReceipt>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitCommitReceipt>> PrepareCommit(
        [FromBody] PrepareGitCommitRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            () => publication.PrepareCommitAsync(request, cancellationToken));

    [HttpPost("pushes")]
    [ProducesResponseType<GitPushReceipt>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GitPushReceipt>> Push(
        [FromBody] GitPushRequest request,
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
