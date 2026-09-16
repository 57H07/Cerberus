namespace Cerberus.Web.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        // form-action is not restricted: the authorization flow legitimately posts/redirects to registered client URIs.
        // The hash allows only the auto-submit script of the OpenIddict form_post response ("document.form.submit();").
        headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self'; "
            + "script-src 'self' 'sha256-j7OoGArf6XW6YY4cAyS3riSSvrJRqpSi1fOF9vQ5SrI='; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
        return next(context);
    }
}
