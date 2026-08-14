using System.Text;
using Tezuri.Proof;
using Tezuri.Workspace;

namespace Tezuri.Proof.Tests;

public sealed class SiteProofRunnerTests
{
    private static readonly string FixtureAssembly = Path.Combine(
        AppContext.BaseDirectory,
        "Tezuri.Proof.Fixture.dll");

    [Fact]
    public async Task RunsInCleanCopyAndLeavesMountedSourceUntouched()
    {
        using var temporary = new TemporaryProofWorkspace();
        temporary.Write("source.txt", "original");
        temporary.Write(".git/config", "git-state");
        temporary.Write("node_modules/package/index.js", "dependency");
        temporary.Write("dist/stale.html", "stale");
        temporary.Write("bin/stale.dll", "stale");
        temporary.Write("obj/stale.json", "stale");
        temporary.Write("proof-output/stale.txt", "stale");
        using var runner = CreateRunner(
            temporary,
            [Command("site-proof", [FixtureAssembly, "isolate"], outputDirectory: "proof-output")]);

        var receipt = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SiteProofProtocolV1.RunReceipt, receipt.Protocol);
        Assert.Equal(SiteProofProtocolV1.Version, receipt.Version);
        Assert.Equal(SiteProofProtocolV1.Passed, receipt.Status);
        Assert.True(receipt.Result.Succeeded);
        Assert.Equal(1, receipt.Progress.CompletedCommands);
        var result = Assert.Single(receipt.Result.Commands);
        Assert.Equal(SiteProofProtocolV1.Passed, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("proof-output", result.OutputDirectory);
        Assert.True(result.OutputDirectoryExists);
        Assert.Contains("fixture-result=generated", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("fixture-diagnostic=kept", result.StandardError, StringComparison.Ordinal);

        Assert.Equal("original", temporary.Read("source.txt"));
        Assert.False(File.Exists(Path.Combine(temporary.WorkspaceRoot, "proof-output", "result.txt")));
        Assert.Equal("stale", temporary.Read("proof-output/stale.txt"));
        Assert.Equal("git-state", temporary.Read(".git/config"));
        AssertNoTemporaryRuns(temporary);
    }

    [Fact]
    public async Task TimesOutConfiguredCommandAndCleansItsCopy()
    {
        using var temporary = new TemporaryProofWorkspace();
        temporary.Write("source.txt", "original");
        using var runner = CreateRunner(
            temporary,
            [Command("slow-proof", [FixtureAssembly, "wait", "30000"], timeoutSeconds: 1)]);

        var receipt = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.False(receipt.Result.Succeeded);
        Assert.Equal(SiteProofProtocolV1.Failed, receipt.Status);
        var result = Assert.Single(receipt.Result.Commands);
        Assert.Equal(SiteProofProtocolV1.TimedOut, result.Status);
        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.Equal("original", temporary.Read("source.txt"));
        AssertNoTemporaryRuns(temporary);
    }

    [Fact]
    public async Task RejectsUnsafeConfiguredPathBeforeCreatingACopy()
    {
        using var temporary = new TemporaryProofWorkspace();
        temporary.Write("source.txt", "original");
        var configuration = Configuration(
            [Command("site-proof", [FixtureAssembly, "isolate"])],
            workingDirectory: "../outside");
        using var runner = new SiteProofRunner(
            new WorkspacePathGuard(temporary.WorkspaceRoot),
            configuration,
            new WorkspaceConfigurationValidator(),
            temporary.ProofRoot);

        var error = await Assert.ThrowsAsync<WorkspaceConfigurationValidationException>(() =>
            runner.RunAsync(TestContext.Current.CancellationToken));

        Assert.Contains(error.Issues, issue => issue.Path == "proof.workingDirectory");
        Assert.Equal("original", temporary.Read("source.txt"));
        AssertNoTemporaryRuns(temporary);
    }

    [Fact]
    public async Task CancellationStopsCommandAndCleansItsCopy()
    {
        using var temporary = new TemporaryProofWorkspace();
        temporary.Write("source.txt", "original");
        using var runner = CreateRunner(
            temporary,
            [Command("cancelled-proof", [FixtureAssembly, "wait", "30000"])]);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(cancellation.Token));

        Assert.Equal("original", temporary.Read("source.txt"));
        AssertNoTemporaryRuns(temporary);
    }

    [Fact]
    public async Task BoundsAndRedactsCapturedOutput()
    {
        using var temporary = new TemporaryProofWorkspace();
        temporary.Write("source.txt", "original");
        using var runner = CreateRunner(
            temporary,
            [Command("noisy-proof", [FixtureAssembly, "emit"])]);

        var receipt = await runner.RunAsync(TestContext.Current.CancellationToken);

        var result = Assert.Single(receipt.Result.Commands);
        Assert.True(result.StandardOutputTruncated);
        Assert.True(result.StandardOutput.Length <= SiteProofRunner.MaximumCapturedCharacters);
        Assert.DoesNotContain("super-secret", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.StandardError, StringComparison.Ordinal);
        AssertNoTemporaryRuns(temporary);
    }

    private static SiteProofRunner CreateRunner(
        TemporaryProofWorkspace temporary,
        IReadOnlyList<ProofCommandConfiguration> commands) => new(
        new WorkspacePathGuard(temporary.WorkspaceRoot),
        Configuration(commands),
        new WorkspaceConfigurationValidator(),
        temporary.ProofRoot);

    private static ProofCommandConfiguration Command(
        string id,
        IReadOnlyList<string> arguments,
        int timeoutSeconds = 30,
        string? outputDirectory = null) => new(
        id,
        "dotnet",
        arguments,
        timeoutSeconds,
        outputDirectory);

    private static WorkspaceConfigurationV1 Configuration(
        IReadOnlyList<ProofCommandConfiguration> commands,
        string workingDirectory = ".") => new(
        WorkspaceConfigurationV1.SchemaName,
        new SiteConfiguration("https://example.test"),
        new ArticleLayoutConfiguration("src/writing", "index.md", "media", "schemas/article.json"),
        new MediaPolicyConfiguration(true, 1_024, [".png"]),
        new ProofConfiguration(workingDirectory, commands),
        new GitPublicationConfiguration(["src/writing/**"]));

    private static void AssertNoTemporaryRuns(TemporaryProofWorkspace temporary) =>
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporary.ProofRoot));

    private sealed class TemporaryProofWorkspace : IDisposable
    {
        private readonly string _root;

        public TemporaryProofWorkspace()
        {
            var safeParent = Path.Combine(Path.GetTempPath(), "tezuri-proof-tests");
            _root = Path.Combine(safeParent, Guid.NewGuid().ToString("N"));
            WorkspaceRoot = Path.Combine(_root, "workspace");
            ProofRoot = Path.Combine(_root, "proof-runs");
            Directory.CreateDirectory(WorkspaceRoot);
            Directory.CreateDirectory(ProofRoot);
        }

        public string WorkspaceRoot { get; }

        public string ProofRoot { get; }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(WorkspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public string Read(string relativePath) => File.ReadAllText(
            Path.Combine(WorkspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        public void Dispose()
        {
            var resolved = Path.GetFullPath(_root);
            var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tezuri-proof-tests")) +
                                 Path.DirectorySeparatorChar;
            if (resolved.StartsWith(
                    expectedParent,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal) &&
                Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
