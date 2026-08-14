using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Tezuri.Tests;

public sealed class GitPublicationServiceTests
{
    [Fact]
    public async Task InspectsBranchHeadRemoteAndAllowedChanges()
    {
        using var repository = new TemporaryGitRepository();
        repository.Write("content/article.md", "changed\n");
        repository.Write("notes.txt", "unrelated\n");
        repository.Git("remote", "add", "origin", repository.CreateBareRemote());
        using var service = CreateService(repository.Root);

        var snapshot = await service.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(repository.InitialSha, snapshot.HeadSha);
        Assert.Equal("main", snapshot.Branch);
        Assert.False(snapshot.IsDetached);
        Assert.False(snapshot.IsUnborn);
        Assert.Contains("origin", snapshot.Remotes);
        Assert.Contains(snapshot.Changes, change =>
            change.Path == "content/article.md" && change.Allowed && change.WorkTreeStatus == "modified");
        Assert.Contains(snapshot.Changes, change =>
            change.Path == "notes.txt" && !change.Allowed);
    }

    [Fact]
    public async Task CommitsOnlyExactSelectionAndPreservesUnrelatedDirtyFiles()
    {
        using var repository = new TemporaryGitRepository();
        repository.Write("content/article.md", "selected change\n");
        repository.Write("content/other.md", "other change\n");
        repository.Write("notes.txt", "unrelated change\n");
        using var service = CreateService(repository.Root);
        var plan = await service.PlanCommitAsync(
            new GitCommitPlanRequest(["content/article.md"]),
            TestContext.Current.CancellationToken);

        var receipt = await service.PrepareCommitAsync(
            Request(plan, "feat: publish one article"),
            TestContext.Current.CancellationToken);

        Assert.True(receipt.Created);
        Assert.Equal(repository.InitialSha, receipt.BeforeSha);
        Assert.NotEqual(receipt.BeforeSha, receipt.AfterSha);
        Assert.Equal(["content/article.md"], receipt.SelectedPaths);
        Assert.Equal("content/article.md", repository.Git("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Trim());
        var status = repository.Git("status", "--porcelain=v1");
        Assert.Contains("content/other.md", status, StringComparison.Ordinal);
        Assert.Contains("notes.txt", status, StringComparison.Ordinal);
        Assert.DoesNotContain("content/article.md", status, StringComparison.Ordinal);
        Assert.Equal(string.Empty, repository.Git("diff", "--cached", "--name-only").Trim());
    }

    [Fact]
    public async Task RefusesPreStagedStateWithoutChangingTheIndex()
    {
        using var repository = new TemporaryGitRepository();
        repository.Write("content/article.md", "staged by owner\n");
        repository.Git("add", "--", "content/article.md");
        var before = repository.Git("diff", "--cached", "--binary");
        using var service = CreateService(repository.Root);

        var error = await Assert.ThrowsAsync<GitPublicationException>(() =>
            service.PlanCommitAsync(
                new GitCommitPlanRequest(["content/article.md"]),
                TestContext.Current.CancellationToken));

        Assert.Equal(GitPublicationFailure.StagedChangesPresent, error.Failure);
        Assert.Equal(before, repository.Git("diff", "--cached", "--binary"));
    }

    [Fact]
    public async Task RefusesChangedContentPlanAndLeavesIndexEmpty()
    {
        using var repository = new TemporaryGitRepository();
        repository.Write("content/article.md", "planned\n");
        using var service = CreateService(repository.Root);
        var plan = await service.PlanCommitAsync(
            new GitCommitPlanRequest(["content/article.md"]),
            TestContext.Current.CancellationToken);
        repository.Write("content/article.md", "changed after review\n");

        var error = await Assert.ThrowsAsync<GitPublicationException>(() =>
            service.PrepareCommitAsync(
                Request(plan, "feat: stale plan"),
                TestContext.Current.CancellationToken));

        Assert.Equal(GitPublicationFailure.PreconditionFailed, error.Failure);
        Assert.Equal(repository.InitialSha, repository.HeadSha());
        Assert.Equal(string.Empty, repository.Git("diff", "--cached", "--name-only").Trim());
        Assert.Equal("changed after review\n", repository.Read("content/article.md"));
    }

    [Fact]
    public async Task RefusesChangedHeadAndLeavesSelectedWorkIntact()
    {
        using var repository = new TemporaryGitRepository();
        repository.Write("content/article.md", "planned\n");
        using var service = CreateService(repository.Root);
        var plan = await service.PlanCommitAsync(
            new GitCommitPlanRequest(["content/article.md"]),
            TestContext.Current.CancellationToken);
        repository.Write("notes.txt", "external commit\n");
        repository.Git("add", "--", "notes.txt");
        repository.Git("commit", "--message", "chore: external commit");

        var error = await Assert.ThrowsAsync<GitPublicationException>(() =>
            service.PrepareCommitAsync(
                Request(plan, "feat: stale head"),
                TestContext.Current.CancellationToken));

        Assert.Equal(GitPublicationFailure.PreconditionFailed, error.Failure);
        Assert.Equal("planned\n", repository.Read("content/article.md"));
        Assert.Contains("content/article.md", repository.Git("status", "--porcelain=v1"), StringComparison.Ordinal);
        Assert.Equal(string.Empty, repository.Git("diff", "--cached", "--name-only").Trim());
    }

    [Fact]
    public async Task RepeatedSamePlanAndMessageReturnsOriginalCommit()
    {
        using var repository = new TemporaryGitRepository();
        repository.Write("content/article.md", "idempotent\n");
        using var service = CreateService(repository.Root);
        var plan = await service.PlanCommitAsync(
            new GitCommitPlanRequest(["content/article.md"]),
            TestContext.Current.CancellationToken);
        var request = Request(plan, "feat: idempotent publication");

        var first = await service.PrepareCommitAsync(request, TestContext.Current.CancellationToken);
        var second = await service.PrepareCommitAsync(request, TestContext.Current.CancellationToken);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.BeforeSha, second.BeforeSha);
        Assert.Equal(first.AfterSha, second.AfterSha);
        Assert.Equal("2", repository.Git("rev-list", "--count", "HEAD").Trim());
    }

    [Fact]
    public async Task PrepareCommitUsesExplicitTezuriIdentityWithoutRepositoryIdentity()
    {
        using var repository = new TemporaryGitRepository();
        repository.Write("content/article.md", "prepared without ambient identity\n");
        using var service = CreateService(repository.Root);
        var plan = await service.PlanCommitAsync(
            new GitCommitPlanRequest(["content/article.md"]),
            TestContext.Current.CancellationToken);
        repository.Git("config", "user.useConfigOnly", "true");
        repository.Git("config", "user.name", string.Empty);
        repository.Git("config", "user.email", string.Empty);

        var receipt = await service.PrepareCommitAsync(
            Request(plan, "feat: prepare without ambient identity"),
            TestContext.Current.CancellationToken);

        Assert.True(receipt.Created);
        Assert.NotEqual(repository.InitialSha, repository.HeadSha());
        Assert.Equal(string.Empty, repository.Git("diff", "--cached", "--name-only").Trim());
        Assert.Equal(
            ["Tezuri", "tezuri@localhost.invalid", "Tezuri", "tezuri@localhost.invalid"],
            repository.Git("show", "-s", "--format=%an%n%ae%n%cn%n%ce", "HEAD")
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task RejectsSelectedPathThatTraversesASymbolicLink()
    {
        using var repository = new TemporaryGitRepository();
        var outside = Path.Combine(repository.Parent, "outside.png");
        File.WriteAllText(outside, "outside", new UTF8Encoding(false));
        var link = Path.Combine(repository.Root, "content", "linked.png");
        try
        {
            File.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        using var service = CreateService(repository.Root);

        var error = await Assert.ThrowsAsync<GitPublicationException>(() =>
            service.PlanCommitAsync(
                new GitCommitPlanRequest(["content/linked.png"]),
                TestContext.Current.CancellationToken));

        Assert.Equal(GitPublicationFailure.InvalidRequest, error.Failure);
        Assert.Equal("outside", File.ReadAllText(outside));
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData(".git/config")]
    [InlineData("notes.txt")]
    public async Task RejectsTraversalGitInternalsAndDisallowedPaths(string selectedPath)
    {
        using var repository = new TemporaryGitRepository();
        repository.Write("content/article.md", "changed\n");
        repository.Write("notes.txt", "changed\n");
        using var service = CreateService(repository.Root);

        var error = await Assert.ThrowsAsync<GitPublicationException>(() =>
            service.PlanCommitAsync(
                new GitCommitPlanRequest([selectedPath]),
                TestContext.Current.CancellationToken));

        Assert.Equal(GitPublicationFailure.InvalidRequest, error.Failure);
        Assert.Equal(repository.InitialSha, repository.HeadSha());
    }

    [Fact]
    public async Task PushesExpectedFastForwardToBareRemoteWithoutForce()
    {
        using var repository = new TemporaryGitRepository();
        var remote = repository.CreateBareRemote();
        repository.Git("remote", "add", "origin", remote);
        repository.Git("push", "--set-upstream", "origin", "main");
        var remoteBefore = repository.InitialSha;
        using var service = CreateService(repository.Root);
        var snapshot = await service.InspectAsync(TestContext.Current.CancellationToken);
        var reviewedRemote = Assert.Single(snapshot.RemoteBranches);
        Assert.Equal("origin", reviewedRemote.Remote);
        Assert.Equal("main", reviewedRemote.Branch);
        Assert.Equal(remoteBefore, reviewedRemote.Sha);
        repository.Write("content/article.md", "ready to push\n");
        var plan = await service.PlanCommitAsync(
            new GitCommitPlanRequest(["content/article.md"]),
            TestContext.Current.CancellationToken);
        var commit = await service.PrepareCommitAsync(
            Request(plan, "feat: prepare push"),
            TestContext.Current.CancellationToken);

        var receipt = await service.PushAsync(
            new GitPushRequest("origin", "main", commit.AfterSha, reviewedRemote.Sha),
            TestContext.Current.CancellationToken);

        Assert.True(receipt.Pushed);
        Assert.Equal(remoteBefore, receipt.RemoteBeforeSha);
        Assert.Equal(commit.AfterSha, receipt.RemoteAfterSha);
        Assert.Equal(commit.AfterSha, TemporaryGitRepository.RunGit(
            repository.Parent,
            "--git-dir", remote,
            "rev-parse", "refs/heads/main").Trim());
    }

    [Fact]
    public async Task FetchRejectsUnexpectedRemoteMovementAndPreservesBothHistories()
    {
        using var repository = new TemporaryGitRepository();
        var remote = repository.CreateBareRemote();
        repository.Git("remote", "add", "origin", remote);
        repository.Git("push", "--set-upstream", "origin", "main");
        var expectedRemote = repository.InitialSha;
        repository.Write("content/article.md", "local publication\n");
        using var service = CreateService(repository.Root);
        var plan = await service.PlanCommitAsync(
            new GitCommitPlanRequest(["content/article.md"]),
            TestContext.Current.CancellationToken);
        var local = await service.PrepareCommitAsync(
            Request(plan, "feat: local publication"),
            TestContext.Current.CancellationToken);

        var competitor = Path.Combine(repository.Parent, "competitor");
        TemporaryGitRepository.RunGit(repository.Parent, "clone", remote, competitor);
        TemporaryGitRepository.ConfigureIdentity(competitor);
        TemporaryGitRepository.WriteFile(competitor, "content/remote.md", "remote movement\n");
        TemporaryGitRepository.RunGit(competitor, "add", "--", "content/remote.md");
        TemporaryGitRepository.RunGit(competitor, "commit", "--message", "feat: remote movement");
        TemporaryGitRepository.RunGit(competitor, "push", "origin", "main");
        var remoteMoved = TemporaryGitRepository.RunGit(
            repository.Parent,
            "--git-dir", remote,
            "rev-parse", "refs/heads/main").Trim();

        var error = await Assert.ThrowsAsync<GitPublicationException>(() => service.PushAsync(
            new GitPushRequest("origin", "main", local.AfterSha, expectedRemote),
            TestContext.Current.CancellationToken));

        Assert.Equal(GitPublicationFailure.Diverged, error.Failure);
        Assert.Equal(local.AfterSha, repository.HeadSha());
        Assert.Equal(remoteMoved, TemporaryGitRepository.RunGit(
            repository.Parent,
            "--git-dir", remote,
            "rev-parse", "refs/heads/main").Trim());
    }

    [Fact]
    public async Task GitCommandErrorsRedactCredentialLikeMaterial()
    {
        using var repository = new TemporaryGitRepository();
        var missing = Path.Combine(repository.Parent, "password=super-secret");
        repository.Git("remote", "add", "origin", missing);
        using var service = CreateService(repository.Root);

        var error = await Assert.ThrowsAsync<GitPublicationException>(() => service.PushAsync(
            new GitPushRequest(
                "origin",
                "main",
                repository.InitialSha,
                repository.InitialSha),
            TestContext.Current.CancellationToken));

        Assert.Equal(GitPublicationFailure.CommandFailed, error.Failure);
        Assert.DoesNotContain("super-secret", error.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationReceiptRoundTripsAsJson()
    {
        var receipt = new GitCommitReceipt(
            new string('a', 40),
            new string('b', 40),
            "main",
            new string('c', 64),
            ["content/article.md"],
            Created: true);

        var json = JsonSerializer.Serialize(receipt);
        var roundTrip = JsonSerializer.Deserialize<GitCommitReceipt>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(receipt.BeforeSha, roundTrip.BeforeSha);
        Assert.Equal(receipt.AfterSha, roundTrip.AfterSha);
        Assert.Equal(receipt.Branch, roundTrip.Branch);
        Assert.Equal(receipt.PlanSha256, roundTrip.PlanSha256);
        Assert.Equal(receipt.SelectedPaths, roundTrip.SelectedPaths);
        Assert.Equal(receipt.Created, roundTrip.Created);
    }

    private static PrepareGitCommitRequest Request(GitCommitPlan plan, string message) => new(
        plan.HeadSha,
        plan.PlanSha256,
        message,
        plan.SelectedPaths);

    private static GitPublicationService CreateService(string root)
    {
        var paths = new WorkspacePathGuard(root);
        return new GitPublicationService(
            paths,
            new WorkspaceSettings { AllowedPaths = ["content/**"] },
            new GitCommandRunner(paths));
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        private readonly string _safeParent;

        public TemporaryGitRepository()
        {
            _safeParent = Path.Combine(Path.GetTempPath(), "tezuri-git-tests");
            Parent = Path.Combine(_safeParent, Guid.NewGuid().ToString("N"));
            Root = Path.Combine(Parent, "workspace");
            Directory.CreateDirectory(Root);
            RunGit(Root, "init", "--initial-branch=main");
            ConfigureIdentity(Root);
            Write("content/article.md", "initial article\n");
            Write("content/other.md", "initial other\n");
            Write("notes.txt", "initial notes\n");
            Git("add", "--", "content/article.md", "content/other.md", "notes.txt");
            Git("commit", "--message", "chore: initial fixture");
            InitialSha = HeadSha();
        }

        public string Parent { get; }

        public string Root { get; }

        public string InitialSha { get; }

        public string CreateBareRemote()
        {
            var remote = Path.Combine(Parent, "remote.git");
            if (!Directory.Exists(remote))
            {
                RunGit(Parent, "init", "--bare", "--initial-branch=main", remote);
            }

            return remote;
        }

        public string Git(params string[] arguments) => RunGit(Root, arguments);

        public string HeadSha() => Git("rev-parse", "HEAD").Trim();

        public void Write(string relativePath, string content) =>
            WriteFile(Root, relativePath, content);

        public string Read(string relativePath) => File.ReadAllText(Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

        public static void ConfigureIdentity(string repository)
        {
            RunGit(repository, "config", "user.name", "Tezuri Test");
            RunGit(repository, "config", "user.email", "tezuri-tests@example.invalid");
            RunGit(repository, "config", "commit.gpgsign", "false");
        }

        public static void WriteFile(string repository, string relativePath, string content)
        {
            var path = Path.Combine(repository, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public static string RunGit(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GCM_INTERACTIVE"] = "Never";
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Git test process did not start.");
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Git test setup timed out.");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git test setup failed ({process.ExitCode}): {stderr}");
            }

            return stdout;
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Parent);
            var expectedParent = Path.GetFullPath(_safeParent) + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(
                    expectedParent,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal) &&
                Directory.Exists(resolved))
            {
                MakeDeletable(new DirectoryInfo(resolved));
                Directory.Delete(resolved, recursive: true);
            }
        }

        private static void MakeDeletable(DirectoryInfo directory)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo child)
                {
                    MakeDeletable(child);
                }

                entry.Attributes = FileAttributes.Normal;
            }

            directory.Attributes = FileAttributes.Normal;
        }
    }
}
