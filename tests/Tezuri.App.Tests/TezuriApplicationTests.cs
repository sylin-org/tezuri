using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tezuri.Domain.Documents;
using Tezuri.Domain.Workspace;
using Tezuri.Security;

namespace Tezuri.App.Tests;

public sealed class TezuriApplicationTests : IClassFixture<TezuriApplicationFactory>
{
    private readonly TezuriApplicationFactory _factory;
    private readonly HttpClient _client;

    public TezuriApplicationTests(TezuriApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task AddKoanHostDiscoversTezuriModuleAndArticleController()
    {
        using var response = await _client.GetAsync(
            "/api/v1/articles",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ArticleListEnvelopeV1>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal(ArticleSourceProtocolV1.ArticleList, payload.Protocol);
        var article = Assert.Single(payload.Articles);
        Assert.Equal("patina", article.Id);
        Assert.Equal("src/writing/patina/index.md", article.RelativePath);
        Assert.NotEqual(default, article.UpdatedAt);
    }

    [Fact]
    public async Task SourceRoundTripRequiresNonceAndReturnsAppliedEnvelope()
    {
        var source = await _client.GetFromJsonAsync<ArticleSourceEnvelopeV1>(
            "/api/v1/articles/patina/source",
            TestContext.Current.CancellationToken);
        Assert.NotNull(source);

        var patchSet = new SourcePatchSetV1(
            ArticleSourceProtocolV1.SourcePatchSet,
            ArticleSourceProtocolV1.Version,
            source.Article.Id,
            source.Article.RelativePath,
            source.Base.Sha256,
            []);

        using var rejected = await _client.PostAsJsonAsync(
            "/api/v1/articles/patina/source-patches",
            patchSet,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/articles/patina/source-patches")
        {
            Content = JsonContent.Create(patchSet)
        };
        request.Headers.Add(
            BootstrapNonce.HeaderName,
            _factory.Services.GetRequiredService<BootstrapNonce>().Value);
        using var accepted = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var applied = await accepted.Content.ReadFromJsonAsync<AppliedSourcePatchV1>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(applied);
        Assert.Equal(source.Base.Sha256, applied.Current.Base.Sha256);
    }

    [Fact]
    public async Task RejectsDnsRebindingAndCrossOriginMutation()
    {
        using var badHost = new HttpRequestMessage(HttpMethod.Get, "/api/v1/articles");
        badHost.Headers.Host = "attacker.example";
        using var badHostResponse = await _client.SendAsync(
            badHost,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badHostResponse.StatusCode);

        using var crossOrigin = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/articles/patina/source-patches")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        crossOrigin.Headers.Add("Origin", "http://attacker.example");
        crossOrigin.Headers.Add(
            BootstrapNonce.HeaderName,
            _factory.Services.GetRequiredService<BootstrapNonce>().Value);
        using var crossOriginResponse = await _client.SendAsync(
            crossOrigin,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, crossOriginResponse.StatusCode);
    }

    [Fact]
    public async Task BrowserResponsesCarryRestrictiveSecurityHeaders()
    {
        using var response = await _client.GetAsync(
            "/api/v1/articles",
            TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }
}

public sealed class TezuriApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _safeParent = Path.Combine(Path.GetTempPath(), "tezuri-app-tests");

    public TezuriApplicationFactory()
    {
        WorkspaceRoot = Path.Combine(_safeParent, Guid.NewGuid().ToString("N"));
        var articleDirectory = Path.Combine(WorkspaceRoot, "src", "writing", "patina");
        Directory.CreateDirectory(articleDirectory);
        File.WriteAllText(
            Path.Combine(articleDirectory, "index.md"),
            "---\ntitle: Patina\n---\n\nA kept paragraph.\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(WorkspaceRoot, "tezuri.yaml"),
            """
            schema: tezuri.workspace/v1
            site:
              url: https://example.test
            articles:
              root: src/writing
              fileName: index.md
              mediaDirectory: media
              metadataSchema: schemas/article-v1.schema.json
            media:
              requireOwnedAssets: true
              maximumAssetBytes: 26214400
              allowedExtensions:
                - .png
            proof:
              workingDirectory: .
              commands:
                - id: site-test
                  executable: npm
                  arguments:
                    - test
                  timeoutSeconds: 300
                  outputDirectory: dist
            git:
              allowedPaths:
                - src/writing/**
            """,
            new UTF8Encoding(false));
    }

    public string WorkspaceRoot { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebHostDefaults.StaticWebAssetsKey, string.Empty);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TEZURI_WORKSPACE"] = WorkspaceRoot
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        var resolved = Path.GetFullPath(WorkspaceRoot);
        var expectedParent = Path.GetFullPath(_safeParent) + Path.DirectorySeparatorChar;
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
