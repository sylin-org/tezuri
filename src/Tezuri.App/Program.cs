using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Data.Connector.Json;
using Koan.Web.Hosting;
using Koan.Web.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Tezuri;

// The content root is where the executable is, not where it happened to be launched from. A
// double-clicked application has no meaningful working directory.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Loopback only, on whatever port the operating system hands out. A fixed port would collide the
// moment a second repository is opened in a second window, which is an ordinary thing to do.
builder.WebHost.UseUrls("http://127.0.0.1:0");

// The JSON store keeps one document per article folder so a commit can select a single article and
// its media travel with it. The directory is resolved from the selected workspace at the moment it
// is first needed — never at builder time, because the workspace is chosen at runtime.
builder.Configuration["Koan:Data:Sources:Default:Adapter"] = "json";
builder.Configuration["Koan:Data:Sources:Default:json:Layout"] = "IndividualFiles";
builder.Configuration["Koan:Data:Sources:Default:json:IndividualFilePath"] =
    $"{{id}}/{WorkspaceLayout.ArticleDocumentFileName}";

builder.Services.AddSingleton<SelectedWorkspace>();
builder.Services.AddKoan();
builder.Services.AddSingleton<IPostConfigureOptions<JsonDataOptions>, WorkspaceJsonDirectory>();

var app = builder.Build();

// Koan contributes the web pipeline; Tezuri owns its bundled single-page shell, which is embedded
// in the assembly rather than sitting in a folder beside it.
var client = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = client });
app.UseStaticFiles(new StaticFileOptions { FileProvider = client });
app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = client });

if (DesktopShell.IsWanted(args, builder.Configuration))
{
    return await DesktopShell.RunAsync(app, args);
}

// Server mode: no window, for a test host or a machine without a desktop. The repository has to be
// named, because there is nobody to ask.
app.Lifetime.ApplicationStarted.Register(() =>
{
    var nonce = app.Services.GetRequiredService<BootstrapNonce>();
    var addresses = app.Services
        .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
        .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?
        .Addresses;
    app.Logger.LogInformation(
        "Open Tezuri at {Origin}/?nonce={Nonce}",
        addresses?.FirstOrDefault()?.TrimEnd('/') ?? "http://127.0.0.1",
        nonce.Value);
});

await app.RunAsync();
return 0;

public partial class Program;

namespace Tezuri
{
    /// <summary>
    /// Everything Tezuri contributes to the host. Koan discovers this and calls it during
    /// <c>AddKoan()</c>, which is why the composition root is a module rather than a pile of
    /// registrations in <c>Program</c>.
    /// </summary>
    public sealed class TezuriModule : KoanModule
    {
        public override void Register(IServiceCollection services)
        {
            services.Configure<KoanWebOptions>(options =>
            {
                // Tezuri serves static files after Koan's controller endpoint block so the same
                // local-request boundary covers APIs, the SPA, and its assets.
                options.EnableStaticFiles = false;
                options.HealthPath = string.Empty;
            });
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IKoanWebPipelineContributor, TezuriSecurityPipelineContributor>());
            services.AddSingleton(BootstrapNonce.Create());
            services.AddSingleton(sp => new WorkspacePathGuard(
                sp.GetRequiredService<SelectedWorkspace>().Root));

            // Layout is convention (WorkspaceLayout); only the media policy, the proof command, and
            // the committable paths are choices, and each ships with a working default.
            services.AddOptions<WorkspaceSettings>().BindConfiguration("Tezuri");
            services.AddSingleton(sp => sp.GetRequiredService<IOptions<WorkspaceSettings>>().Value);

            services.AddSingleton<AtomicFileWriter>();
            services.AddSingleton<ArticleMarkdownWriter>();
            services.AddSingleton<ArticleMediaStore>();
            services.AddSingleton<SubstackImporter>();
            services.AddSingleton<GitCommandRunner>();
            services.AddSingleton<GitPublicationService>();
            services.AddSingleton(sp => new ProofRunner(
                sp.GetRequiredService<WorkspacePathGuard>(),
                sp.GetRequiredService<WorkspaceSettings>()));
        }

        public override void Report(
            ProvenanceModuleWriter module,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            module.Describe(Version, "Repository-native article workspace");
            module.AddSetting("Workspace", configuration["TEZURI_WORKSPACE"] ?? "(chosen at launch)");
            module.AddNote("Articles are JSON entities in the selected repository; index.md is generated.");
        }
    }
}
