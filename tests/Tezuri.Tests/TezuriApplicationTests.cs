using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tezuri.Workspace;
using Tezuri.Security;

namespace Tezuri.App.Tests;

[Collection(TezuriHostCollection.Name)]
public sealed class TezuriApplicationTests
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
    public async Task ListsArticlesFromTheMountedRepository()
    {
        using var response = await _client.GetAsync(
            "/api/v1/articles",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
    }

    [Fact]
    public async Task CreateRequiresTheNonceAndThenWritesEntityAndMarkdown()
    {
        using var unauthorized = new HttpRequestMessage(HttpMethod.Post, "/api/v1/articles")
        {
            Content = JsonContent.Create(new { title = "Refused" })
        };
        using var refused = await _client.SendAsync(
            unauthorized,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/articles")
        {
            Content = JsonContent.Create(new { title = $"Craft {Guid.NewGuid():N}" })
        };
        request.Headers.Add(BootstrapNonce.HeaderName, _factory.Services.GetRequiredService<BootstrapNonce>().Value);
        using var created = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var article = await created.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: TestContext.Current.CancellationToken);
        var id = article.GetProperty("id").GetString();
        Assert.NotNull(id);
        Assert.StartsWith("craft-", id, StringComparison.Ordinal);
        var title = article.GetProperty("title").GetString();

        // The entity is the canonical document; index.md is generated beside it for the site build.
        var folder = Path.Combine(_factory.WorkspaceRoot, "src", "writing", id!);
        Assert.True(File.Exists(Path.Combine(folder, "article.json")));
        var markdown = File.ReadAllText(Path.Combine(folder, "index.md"));
        Assert.Contains($"title: {title}", markdown, StringComparison.Ordinal);
        Assert.Contains("draft: true", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveRefusesAStaleRevision()
    {
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/articles")
        {
            Content = JsonContent.Create(new { title = "Revision Guard" })
        };
        create.Headers.Add(BootstrapNonce.HeaderName, _factory.Services.GetRequiredService<BootstrapNonce>().Value);
        using var created = await _client.SendAsync(create, TestContext.Current.CancellationToken);
        var article = await created.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: TestContext.Current.CancellationToken);
        var id = article.GetProperty("id").GetString();

        using var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/articles/{id}")
        {
            Content = JsonContent.Create(new
            {
                title = "Revision Guard",
                subtitle = (string?)null,
                body = "Written by a second session.",
                draft = true,
                date = (string?)null,
                tags = Array.Empty<string>(),
                revision = "a-revision-this-session-never-read"
            })
        };
        stale.Headers.Add(BootstrapNonce.HeaderName, _factory.Services.GetRequiredService<BootstrapNonce>().Value);
        using var conflict = await _client.SendAsync(stale, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
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
