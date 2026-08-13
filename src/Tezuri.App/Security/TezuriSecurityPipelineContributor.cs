using Koan.Web.Hosting;

namespace Tezuri.Security;

public sealed class TezuriSecurityPipelineContributor : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.BeforeRouting;

    public int Order => int.MinValue;

    public void Configure(IApplicationBuilder app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<LocalRequestSecurityMiddleware>();
    }
}
