using System.Text.Json;
using Json.Schema;
using Tezuri.Domain.Documents;
using Tezuri.Domain.Git;
using Tezuri.Domain.Import;
using Tezuri.Domain.Media;
using Tezuri.Domain.Proof;
using Tezuri.Domain.Workspace;

namespace Tezuri.Contracts.Tests;

public sealed class JsonSchemaContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions DiagnosticJson = new()
    {
        WriteIndented = true
    };

    [Fact]
    public void PublicSchemasBuildAndHaveUniqueImmutableIds()
    {
        var suite = ContractSuite.Load();

        Assert.Equal(suite.Schemas.Count, suite.SchemaIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(suite.SchemaIds, id => Assert.StartsWith("urn:sylin:tezuri:", id, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryPublicDocumentSchemaHasPassingAndFailingGoldenEvidence()
    {
        var suite = ContractSuite.Load();
        var catalog = suite.ReadFixtureCatalog();
        var coveredSchemas = catalog.Cases
            .Select(item => item.Schema)
            .ToHashSet(StringComparer.Ordinal);
        var expectedSchemas = suite.Schemas.Keys
            .Where(name => !StringComparer.Ordinal.Equals(name, "tezuri-common-v1.schema.json"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expectedSchemas.Order(StringComparer.Ordinal), coveredSchemas.Order(StringComparer.Ordinal));

        foreach (var item in catalog.Cases)
        {
            Assert.True(
                suite.Evaluate(item.Schema, item.Valid).IsValid,
                $"Expected {item.Valid} to satisfy {item.Schema}.");
            Assert.False(
                suite.Evaluate(item.Schema, item.Invalid).IsValid,
                $"Expected {item.Invalid} to be rejected by {item.Schema}.");
        }
    }

    [Fact]
    public void ExistingDomainWireRecordsSerializeAgainstTheirPublicSchemas()
    {
        var suite = ContractSuite.Load();
        const string sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string objectId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var instant = new DateTimeOffset(2026, 8, 13, 15, 30, 0, TimeSpan.Zero);

        AssertWireValid(
            suite,
            "tezuri-article-source-v1.schema.json",
            new ArticleListEnvelopeV1(
                ArticleSourceProtocolV1.ArticleList,
                ArticleSourceProtocolV1.Version,
                [new ArticleEntry(
                    "article:patina",
                    "patina",
                    "Patina as memory",
                    "src/writing/patina/index.md",
                    "published",
                    sha256,
                    instant,
                    512)]));

        AssertWireValid(
            suite,
            "tezuri-media-asset-v1.schema.json",
            new MediaAssetReceiptV1(
                MediaAssetProtocolV1.Receipt,
                MediaAssetProtocolV1.Version,
                "article:patina",
                "patina.png",
                $"{sha256}.png",
                $"src/writing/patina/media/{sha256}.png",
                "image/png",
                sha256,
                2048,
                false));

        AssertWireValid(
            suite,
            "tezuri-git-publication-v1.schema.json",
            new GitCommitPlanV1(
                GitPublicationProtocolV1.CommitPlan,
                GitPublicationProtocolV1.Version,
                objectId,
                "main",
                sha256,
                ["src/writing/patina/index.md"],
                [new GitChangedPathV1(
                    "src/writing/patina/index.md",
                    "none",
                    "modified",
                    true)]));

        AssertWireValid(
            suite,
            "tezuri-git-publication-v1.schema.json",
            new GitRepositorySnapshotV1(
                GitPublicationProtocolV1.RepositorySnapshot,
                GitPublicationProtocolV1.Version,
                objectId,
                false,
                false,
                "main",
                "origin/main",
                ["origin"],
                [new GitRemoteBranchV1("origin", "main", objectId)],
                []));

        AssertWireValid(
            suite,
            "tezuri-site-proof-v1.schema.json",
            new SiteProofRunReceiptV1(
                SiteProofProtocolV1.RunReceipt,
                SiteProofProtocolV1.Version,
                "proof:synthetic-1",
                SiteProofProtocolV1.Passed,
                instant,
                instant.AddSeconds(1),
                new SiteProofProgressV1(SiteProofProtocolV1.Passed, 1, 1, null),
                new SiteProofResultV1(
                    true,
                    [new SiteProofCommandResultV1(
                        "site-test",
                        "npm",
                        ["test"],
                        SiteProofProtocolV1.Passed,
                        0,
                        false,
                        1000,
                        "Synthetic proof passed.",
                        string.Empty,
                        false,
                        false,
                        "dist",
                        true)])));

        var sourceMetadata = JsonSerializer.SerializeToElement(new { tags = new[] { "architecture" } });
        var resultMetadata = JsonSerializer.SerializeToElement(new { status = "current" });
        AssertWireValid(
            suite,
            "tezuri-import-manifest-v1.schema.json",
            new ImportManifestV1(
                ImportManifestProtocolV1.Schema,
                "import:synthetic-1",
                new ImportSourceV1(
                    "substack-feed-archive",
                    "https://example.test/feed",
                    "https://example.test/archive",
                    null,
                    "2026-08-13T15:00:00Z"),
                ImportManifestProtocolV1.Succeeded,
                "2026-08-13T15:00:00Z",
                "2026-08-13T15:30:00Z",
                new ImportSummaryV1(1, 1, 0, 0, 0),
                [new ImportArticleV1(
                    new ImportSourceArticleV1(
                        "post-1",
                        "https://example.test/p/patina",
                        "Patina as memory",
                        "2024-04-05T12:00:00Z",
                        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                        sourceMetadata),
                    ImportManifestProtocolV1.Imported,
                    null,
                    "src/writing/patina/index.md",
                    "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                    resultMetadata,
                    [new ImportTransformationV1(
                        "remove-subscription-chrome",
                        "Removed platform subscription controls.",
                        null,
                        null)],
                    [],
                    [new ImportFidelityV1("body", "preserved", "Reviewed against the synthetic source.")],
                    [new ImportAssetV1(
                        "https://example.test/images/patina.png",
                        null,
                        ImportManifestProtocolV1.Imported,
                        null,
                        "src/writing/patina/media/patina.png",
                        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        [],
                        [])])],
                []));
    }

    private static void AssertWireValid<T>(ContractSuite suite, string schemaName, T value)
    {
        var instance = JsonSerializer.SerializeToElement(value, WebJson);
        var result = suite.Evaluate(schemaName, instance);

        Assert.True(
            result.IsValid,
            $"{typeof(T).Name} does not satisfy {schemaName}.\n" +
            $"Instance:\n{instance.GetRawText()}\n" +
            $"Evaluation:\n{JsonSerializer.Serialize(result, DiagnosticJson)}");
    }

    private sealed class ContractSuite(
        IReadOnlyDictionary<string, JsonSchema> schemas,
        IReadOnlyList<string> schemaIds,
        string examplesDirectory)
    {
        private static readonly EvaluationOptions EvaluationOptions = new()
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        };

        public IReadOnlyDictionary<string, JsonSchema> Schemas { get; } = schemas;

        public IReadOnlyList<string> SchemaIds { get; } = schemaIds;

        public static ContractSuite Load()
        {
            var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "Schemas");
            var examplesDirectory = Path.Combine(AppContext.BaseDirectory, "Examples");
            var registry = new SchemaRegistry();
            var buildOptions = new BuildOptions
            {
                Dialect = Dialect.Draft202012,
                SchemaRegistry = registry
            };
            var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
            var ids = new List<string>();

            foreach (var path in Directory.GetFiles(schemaDirectory, "*.schema.json")
                         .Order(StringComparer.Ordinal))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var id = document.RootElement.GetProperty("$id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(id));
                ids.Add(id!);
                schemas.Add(
                    Path.GetFileName(path),
                    JsonSchema.Build(document.RootElement.Clone(), buildOptions));
            }

            return new ContractSuite(schemas, ids, examplesDirectory);
        }

        public FixtureCatalog ReadFixtureCatalog()
        {
            var source = File.ReadAllText(Path.Combine(examplesDirectory, "catalog.json"));
            return JsonSerializer.Deserialize<FixtureCatalog>(source, WebJson) ??
                   throw new InvalidOperationException("The contract fixture catalog is empty.");
        }

        public EvaluationResults Evaluate(string schemaName, string exampleName)
        {
            using var instance = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(examplesDirectory, exampleName)));
            return Evaluate(schemaName, instance.RootElement);
        }

        public EvaluationResults Evaluate(string schemaName, JsonElement instance) =>
            Schemas[schemaName].Evaluate(instance, EvaluationOptions);
    }

    private sealed record FixtureCatalog(IReadOnlyList<FixtureCase> Cases);

    private sealed record FixtureCase(string Schema, string Valid, string Invalid);
}
