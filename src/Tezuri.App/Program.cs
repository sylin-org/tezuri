using Koan.Core;
using Tezuri.Security;

var builder = WebApplication.CreateBuilder(args);

// Sylin.Koan.App includes the JSON provider as its zero-configuration data floor.
// Tezuri does not persist articles through it, so keep its incidental health-probe
// directory in disposable process temp instead of the app or mounted repository.
const string koanJsonDirectoryKey = "Koan:Data:Json:DirectoryPath";
if (string.IsNullOrWhiteSpace(builder.Configuration[koanJsonDirectoryKey]))
{
    builder.Configuration[koanJsonDirectoryKey] = Path.Combine(
        Path.GetTempPath(),
        "tezuri",
        "koan-data");
}

builder.Services.AddKoan();

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
