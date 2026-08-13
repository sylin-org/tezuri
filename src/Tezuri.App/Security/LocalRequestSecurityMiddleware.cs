using System.Net;
using Microsoft.Extensions.Primitives;

namespace Tezuri.Security;

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
