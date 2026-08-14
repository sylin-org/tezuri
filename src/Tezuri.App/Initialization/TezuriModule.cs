using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Web.Hosting;
using Koan.Web.Options;
using Tezuri.Workspace;
using Tezuri.Articles;
using Tezuri.Publishing;
using Tezuri.Import;
using Tezuri.Media;
using Tezuri.Proof;
using Tezuri.Security;

namespace Tezuri.Initialization;

public sealed class TezuriModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.Configure<KoanWebOptions>(options =>
        {
            // Tezuri serves static files after Koan's controller endpoint block so the
            // same local-request boundary can cover APIs, the SPA, and its assets.
            options.EnableStaticFiles = false;
            options.HealthPath = string.Empty;
        });
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IKoanWebPipelineContributor, TezuriSecurityPipelineContributor>());
        services.AddSingleton(BootstrapNonce.Create());
        services.AddSingleton(sp => new WorkspacePathGuard(
            sp.GetRequiredService<SelectedWorkspace>().Root));

        // Layout is convention (WorkspaceLayout); only the media policy, the proof command, and the
        // committable paths are choices, and each ships with a working default.
        services.AddOptions<WorkspaceSettings>().BindConfiguration("Tezuri");
        services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<WorkspaceSettings>>().Value);

        services.AddSingleton<AtomicFileWriter>();
        services.AddSingleton<ArticleMarkdownWriter>();
        services.AddSingleton<ArticleMediaStore>();
        services.AddSingleton<SubstackImporter>();
        services.AddSingleton<GitCommandRunner>();
        services.AddSingleton<GitPublicationService>();
        services.AddSingleton(sp => new SiteProofRunner(
            sp.GetRequiredService<WorkspacePathGuard>(),
            sp.GetRequiredService<WorkspaceSettings>()));
    }

    public override void Report(
        ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version, "Repository-native article workspace");
        module.AddSetting("Workspace", configuration["TEZURI_WORKSPACE"] ?? "/workspace");
        module.AddNote("Articles are JSON entities in the mounted repository; index.md is generated.");
    }
}
