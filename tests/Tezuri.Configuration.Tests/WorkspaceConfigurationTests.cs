using System.Text.Json;
using Tezuri.Infrastructure.Configuration;

namespace Tezuri.Configuration.Tests;

public sealed class WorkspaceConfigurationTests
{
    private readonly WorkspaceConfigurationParser _parser = new();
    private readonly WorkspaceConfigurationValidator _validator = new();

    [Fact]
    public void ParsesAndValidatesTheFolderNativeExample()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tezuri.yaml"));

        var configuration = _parser.Parse(source);
        var issues = _validator.Validate(configuration);

        Assert.Empty(issues);
        Assert.Equal(WorkspaceConfigurationV1.SchemaName, configuration.Schema);
        Assert.Equal("src/writing", configuration.Articles.Root);
        Assert.Equal("index.md", configuration.Articles.FileName);
        Assert.Equal("media", configuration.Articles.MediaDirectory);
        Assert.Equal("schemas/editor-hints-v1.json", configuration.Articles.EditorHints);
        Assert.True(configuration.Media.RequireOwnedAssets);
        var command = Assert.Single(configuration.Proof.Commands);
        Assert.Equal("npm", command.Executable);
        Assert.Equal(["test"], command.Arguments);
        Assert.Equal("dist", command.OutputDirectory);
    }

    [Fact]
    public void MapsTheConfigurationToTheExistingWorkspaceContract()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tezuri.yaml"));
        var configuration = _parser.Parse(source);

        var contract = configuration.ToWorkspaceContract();

        Assert.Equal(1, contract.Version);
        Assert.Equal(configuration.Articles.Root, contract.ContentRoot);
        Assert.Equal(configuration.Articles.FileName, contract.ArticleFileName);
        Assert.Equal(configuration.Articles.MediaDirectory, contract.MediaDirectoryName);
    }

    [Fact]
    public void RejectsShellInterpretersAsProofExecutables()
    {
        var configuration = ValidConfiguration() with
        {
            Proof = new ProofConfiguration(
                ".",
                [new ProofCommandConfiguration("site-test", "sh", ["-c", "npm test"], 60, "dist")])
        };

        var issues = _validator.Validate(configuration);

        Assert.Contains(
            issues,
            issue => issue.Path == "proof.commands[0].executable" &&
                     issue.Message.Contains("shell interpreter", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsGitPathsThatEscapeOrReachGitInternals()
    {
        var configuration = ValidConfiguration() with
        {
            Git = new GitPublicationConfiguration(["../outside/**", ".git/config"])
        };

        var issues = _validator.Validate(configuration);

        Assert.Contains(issues, issue => issue.Path == "git.allowedPaths[0]");
        Assert.Contains(issues, issue => issue.Path == "git.allowedPaths[1]" && issue.Message.Contains("internals"));
    }

    [Fact]
    public void RejectsUnsafeEditorHintsPath()
    {
        var configuration = ValidConfiguration() with
        {
            Articles = new ArticleLayoutConfiguration(
                "src/writing",
                "index.md",
                "media",
                "schemas/article.json",
                "../editor-hints.json")
        };

        var issues = _validator.Validate(configuration);

        Assert.Contains(issues, issue => issue.Path == "articles.editorHints");
    }

    [Fact]
    public void RejectsUnknownAndNonDeterministicYamlFeatures()
    {
        const string source = """
            schema: tezuri.workspace/v1
            site: &shared
              url: https://example.test
            articles: *shared
            """;

        var error = Assert.Throws<WorkspaceConfigurationException>(() => _parser.Parse(source));

        Assert.Contains("outside the deterministic Tezuri v1 subset", error.Message);
    }

    [Fact]
    public void RejectsUnknownConfigurationKeys()
    {
        const string source = """
            schema: tezuri.workspace/v1
            surprise: true
            site:
              url: https://example.test
            articles:
              root: src/writing
              fileName: index.md
              mediaDirectory: media
              metadataSchema: schemas/article.json
            media:
              requireOwnedAssets: true
              maximumAssetBytes: 1024
              allowedExtensions:
                - .png
            proof:
              workingDirectory: .
              commands:
                - id: site-test
                  executable: npm
                  arguments:
                    - test
                  timeoutSeconds: 60
            git:
              allowedPaths:
                - src/writing/**
            """;

        var error = Assert.Throws<WorkspaceConfigurationException>(() => _parser.Parse(source));

        Assert.Contains("surprise is not supported by v1", error.Message);
    }

    [Fact]
    public void PublishedSchemaRequiresStructuredProofCommands()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "tezuri-workspace-v1.schema.json")));

        var command = document.RootElement
            .GetProperty("$defs")
            .GetProperty("proofCommand");
        var required = command.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();

        Assert.Contains("executable", required);
        Assert.Contains("arguments", required);
        Assert.False(command.GetProperty("additionalProperties").GetBoolean());
        Assert.False(command.GetProperty("properties").TryGetProperty("command", out _));
    }

    private static WorkspaceConfigurationV1 ValidConfiguration() => new(
        WorkspaceConfigurationV1.SchemaName,
        new SiteConfiguration("https://example.test"),
        new ArticleLayoutConfiguration("src/writing", "index.md", "media", "schemas/article.json"),
        new MediaPolicyConfiguration(true, 1_024, [".png"]),
        new ProofConfiguration(
            ".",
            [new ProofCommandConfiguration("site-test", "npm", ["test"], 60, "dist")]),
        new GitPublicationConfiguration(["src/writing/**", "tezuri.yaml"]));
}
