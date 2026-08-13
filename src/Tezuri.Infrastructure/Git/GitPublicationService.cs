using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Tezuri.Domain.Git;
using Tezuri.Infrastructure.Configuration;
using Tezuri.Infrastructure.Workspace;

namespace Tezuri.Infrastructure.Git;

public sealed class GitPublicationService : IDisposable
{
    private const int MaximumSelectedPaths = 256;
    private const int MaximumPathCharacters = 1_024;
    private const int MaximumCommitMessageCharacters = 300;
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
    private readonly WorkspaceConfigurationV1 _configuration;
    private readonly WorkspaceConfigurationValidator _validator;
    private readonly GitCommandRunner _commands;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public GitPublicationService(
        WorkspacePathGuard workspace,
        WorkspaceConfigurationV1 configuration,
        WorkspaceConfigurationValidator validator,
        GitCommandRunner commands)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public async Task<GitRepositorySnapshotV1> InspectAsync(
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

    public async Task<GitCommitPlanV1> PlanCommitAsync(
        GitCommitPlanRequestV1 request,
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
            return new GitCommitPlanV1(
                GitPublicationProtocolV1.CommitPlan,
                GitPublicationProtocolV1.Version,
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

    public async Task<GitCommitReceiptV1> PrepareCommitAsync(
        PrepareGitCommitRequestV1 request,
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

    public async Task<GitPushReceiptV1> PushAsync(
        GitPushRequestV1 request,
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
        _validator.EnsureValid(_configuration);
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
            new GitRepositorySnapshotV1(
                GitPublicationProtocolV1.RepositorySnapshot,
                GitPublicationProtocolV1.Version,
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

    private async Task<IReadOnlyList<GitRemoteBranchV1>> ReadRemoteBranchesAsync(
        IReadOnlyList<string> remotes,
        CancellationToken cancellationToken)
    {
        var result = await RunRequiredAsync(
            "inspect remote-tracking branches",
            ["for-each-ref", "--format=%(refname:short)%00%(objectname)%00%(symref)", "refs/remotes"],
            LocalOperationTimeout,
            cancellationToken);
        var branches = new List<GitRemoteBranchV1>();
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
            branches.Add(new GitRemoteBranchV1(
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
                "A selected path is outside git.allowedPaths in tezuri.yaml.");
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
        _configuration.Git.AllowedPaths.Any(pattern => GitAllowedPathMatcher.IsMatch(pattern, path));

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

    private async Task<GitCommitReceiptV1?> TryCreateIdempotentReceiptAsync(
        PrepareGitCommitRequestV1 request,
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

    private static void EnsurePushState(RepositoryState state, GitPushRequestV1 request)
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

    private GitChangedPathV1 ToContractWithAllowed(StatusEntry entry) => new(
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
        if (string.IsNullOrWhiteSpace(message) ||
            message.Length > MaximumCommitMessageCharacters ||
            !StringComparer.Ordinal.Equals(message, message.Trim()) ||
            message.Any(character => char.IsControl(character)))
        {
            throw new GitPublicationException(
                GitPublicationFailure.InvalidRequest,
                $"The commit message must be one plain line of at most {MaximumCommitMessageCharacters} characters.");
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

    private static GitCommitReceiptV1 CreateCommitReceipt(
        string beforeSha,
        string afterSha,
        string branch,
        string planSha256,
        IReadOnlyList<string> selectedPaths,
        bool created) => new(
        GitPublicationProtocolV1.CommitReceipt,
        GitPublicationProtocolV1.Version,
        beforeSha.ToLowerInvariant(),
        afterSha.ToLowerInvariant(),
        branch,
        planSha256.ToLowerInvariant(),
        selectedPaths.ToArray(),
        created);

    private static GitPushReceiptV1 CreatePushReceipt(
        GitPushRequestV1 request,
        string localSha,
        string remoteBeforeSha,
        string remoteAfterSha,
        bool pushed) => new(
        GitPublicationProtocolV1.PushReceipt,
        GitPublicationProtocolV1.Version,
        request.Remote,
        request.Branch,
        localSha,
        remoteBeforeSha,
        remoteAfterSha,
        pushed);

    private static StringComparer PlatformPathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record RepositoryState(
        GitRepositorySnapshotV1 Snapshot,
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
