namespace Tezuri.Security;

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
