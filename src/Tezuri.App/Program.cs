using Koan.Core;
using Microsoft.Extensions.Options;
using Tezuri.Security;
using Tezuri.Workspace;

var builder = WebApplication.CreateBuilder(args);

// The JSON store keeps one document per article folder so a commit can select a single article and
// its media travel with it (ADR 0015). The directory is resolved from the selected workspace at the
// moment it is first needed — never at builder time, because the workspace is chosen at runtime and
// can be switched.
builder.Configuration["Koan:Data:Sources:Default:Adapter"] = "json";
builder.Configuration["Koan:Data:Sources:Default:json:Layout"] = "IndividualFiles";
builder.Configuration["Koan:Data:Sources:Default:json:IndividualFilePath"] = "{id}/article.json";

builder.Services.AddSingleton<SelectedWorkspace>();
builder.Services.AddKoan();
builder.Services.AddSingleton<IPostConfigureOptions<Koan.Data.Connector.Json.JsonDataOptions>,
    WorkspaceJsonDirectory>();

var app = builder.Build();

// Koan contributes the web pipeline; Tezuri owns its bundled single-page shell.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

var nonce = app.Services.GetRequiredService<BootstrapNonce>();
app.Logger.LogInformation(
    "Open Tezuri at http://127.0.0.1:{Port}/?nonce={Nonce}",
    builder.Configuration["ASPNETCORE_HTTP_PORTS"] ?? "8080",
    nonce.Value);

await app.RunAsync();

public partial class Program;
