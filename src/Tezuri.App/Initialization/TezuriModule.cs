using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Web.Hosting;
using Koan.Web.Options;
using Tezuri.Domain.Workspace;
using Tezuri.Infrastructure.Configuration;
using Tezuri.Infrastructure.Documents;
using Tezuri.Infrastructure.Git;
using Tezuri.Infrastructure.Import;
using Tezuri.Infrastructure.Media;
using Tezuri.Infrastructure.Proof;
using Tezuri.Infrastructure.Workspace;
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
            sp.GetRequiredService<IConfiguration>()["TEZURI_WORKSPACE"] ?? "/workspace"));
        services.AddSingleton<WorkspaceConfigurationParser>();
        services.AddSingleton<WorkspaceConfigurationValidator>();
        services.AddSingleton<WorkspaceConfigurationLoader>();
        services.AddSingleton(sp => sp
            .GetRequiredService<WorkspaceConfigurationLoader>()
            .LoadAsync(sp.GetRequiredService<WorkspacePathGuard>())
            .GetAwaiter()
            .GetResult());
        services.AddSingleton(sp => sp.GetRequiredService<WorkspaceConfigurationV1>().ToWorkspaceContract());
        services.AddSingleton<ArticleDocumentCodec>();
        services.AddSingleton<AtomicFileWriter>();
        services.AddSingleton<FileArticleWorkspace>();
        services.AddSingleton<ArticleMediaStore>();
        services.AddSingleton(sp => new SubstackImporter(
            sp.GetRequiredService<WorkspacePathGuard>(),
            sp.GetRequiredService<WorkspaceContract>(),
            sp.GetRequiredService<WorkspaceConfigurationV1>(),
            sp.GetRequiredService<AtomicFileWriter>(),
            TimeProvider.System));
        services.AddSingleton<GitCommandRunner>();
        services.AddSingleton<GitPublicationService>();
        services.AddSingleton(sp => new SiteProofRunner(
            sp.GetRequiredService<WorkspacePathGuard>(),
            sp.GetRequiredService<WorkspaceConfigurationV1>(),
            sp.GetRequiredService<WorkspaceConfigurationValidator>()));
    }

    public override void Report(
        ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version, "Repository-native article workspace");
        module.AddSetting("Workspace", configuration["TEZURI_WORKSPACE"] ?? "/workspace");
        module.AddNote("Article source remains ordinary UTF-8 files in the mounted repository.");
    }
}
