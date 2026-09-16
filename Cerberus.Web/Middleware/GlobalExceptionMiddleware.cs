using Cerberus.Application.Exceptions;
using Cerberus.Domain.Exceptions;
using Cerberus.Web.Extensions;
using Cerberus.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Cerberus.Web.Middleware;

/// <summary>
/// Maps domain and application exceptions to HTTP semantics. JSON callers receive RFC 7807 ProblemDetails without
/// internal details outside Development; browser navigations get a toast and are redirected. Unexpected errors are
/// rethrown to the standard exception handler.
/// </summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITempDataDictionaryFactory tempDataFactory)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (Map(ex) is { } mapped && !context.Response.HasStarted)
        {
            logger.LogWarning("{ExceptionType} handled as {StatusCode}: {Message}", ex.GetType().Name, mapped.Status, ex.Message);

            if (context.Request.IsAjaxRequest() || context.Request.AcceptsJsonOnly())
            {
                context.Response.Clear();
                context.Response.StatusCode = mapped.Status;
                var problem = new ProblemDetails
                {
                    Status = mapped.Status,
                    Title = mapped.Title,
                    Detail = ex.Message
                };
                if (ex is DomainValidationException validation)
                {
                    problem.Extensions["field"] = validation.FieldName;
                }

                await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
                return;
            }

            if (mapped.Status == StatusCodes.Status403Forbidden)
            {
                context.Response.Redirect("/Account/AccessDenied");
                return;
            }

            var tempData = tempDataFactory.GetTempData(context);
            tempData.Put(ToastMessage.TempDataKey, new ToastMessage(ToastType.Error, ex.Message));
            tempData.Save();
            var referer = context.Request.Headers.Referer.ToString();
            context.Response.Redirect(Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == context.Request.Host.Host
                ? uri.PathAndQuery
                : "/");
        }
    }

    private static (int Status, string Title)? Map(Exception ex) => ex switch
    {
        ResourceNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
        InsufficientRightsException => (StatusCodes.Status403Forbidden, "Forbidden"),
        DuplicateEntityException => (StatusCodes.Status409Conflict, "Conflict"),
        DomainException => (StatusCodes.Status422UnprocessableEntity, "Business rule violation"),
        _ => null
    };
}
