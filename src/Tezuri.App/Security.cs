using System.Net;
using System.Security.Cryptography;
using Koan.Web.Hosting;
using Microsoft.Extensions.Primitives;

namespace Tezuri;

/// <summary>
/// The local boundary: a nonce that proves a request came from the window Tezuri opened, a refusal
/// of anything that did not, and the response headers that keep a browser from doing more.
/// </summary>
public sealed record BootstrapNonce(string Value)
{
    public const string HeaderName = "X-Tezuri-Nonce";

    public static BootstrapNonce Create() => new(
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_'));
}

public sealed class LocalRequestSecurityMiddleware(
    RequestDelegate next,
    BootstrapNonce nonce,
    ILogger<LocalRequestSecurityMiddleware> logger)
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsAllowedHost(context.Request.Host.Host))
        {
            await RejectAsync(context, StatusCodes.Status400BadRequest, "Host is not a loopback address.");
            return;
        }

        if (!HasAllowedOrigin(context.Request))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "Origin does not match this Tezuri process.");
            return;
        }

        if (!SafeMethods.Contains(context.Request.Method) && !HasNonce(context.Request.Headers))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "The launch nonce is missing or invalid.");
            return;
        }

        await next(context);
    }

    private bool HasNonce(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(BootstrapNonce.HeaderName, out var supplied) ||
            supplied.Count != 1)
        {
            return false;
        }

        return StringComparer.Ordinal.Equals(supplied[0], nonce.Value);
    }

    private static bool HasAllowedOrigin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out StringValues origins) || origins.Count == 0)
        {
            return true;
        }

        if (origins.Count != 1 || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin))
        {
            return false;
        }

        var defaultPort = origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? 443
            : 80;
        var originPort = origin.IsDefaultPort ? defaultPort : origin.Port;
        var requestPort = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        return origin.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase) &&
               IsAllowedHost(origin.Host) &&
               originPort == requestPort;
    }

    private static bool IsAllowedHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    private async Task RejectAsync(HttpContext context, int statusCode, string detail)
    {
        logger.LogWarning(
            "Rejected {Method} {Path}: {Detail}",
            context.Request.Method,
            context.Request.Path,
            detail);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tezuri.local/problems/local-request-boundary",
            title = "Request rejected by the local security boundary",
            status = statusCode,
            detail
        });
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] =
            "default-src 'self'; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; " +
            "form-action 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; " +
            "font-src 'self'; connect-src 'self'; worker-src 'self' blob:";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
        headers.CacheControl = "no-store";
        await next(context);
    }
}

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
