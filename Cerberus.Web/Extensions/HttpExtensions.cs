using System.Text.Json;
using Cerberus.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Cerberus.Web.Extensions;

public static class HttpExtensions
{
    public static bool IsAjaxRequest(this HttpRequest request)
        => string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    public static bool AcceptsJsonOnly(this HttpRequest request)
    {
        var accept = request.Headers.Accept.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    public static void Put<T>(this ITempDataDictionary tempData, string key, T value)
        => tempData[key] = JsonSerializer.Serialize(value);

    public static T? Get<T>(this ITempDataDictionary tempData, string key)
        => tempData.TryGetValue(key, out var value) && value is string json ? JsonSerializer.Deserialize<T>(json) : default;

    public static void NotifySuccess(this Controller controller, string message)
        => controller.TempData.Put(ToastMessage.TempDataKey, new ToastMessage(ToastType.Success, message));

    public static void NotifyError(this Controller controller, string message)
        => controller.TempData.Put(ToastMessage.TempDataKey, new ToastMessage(ToastType.Error, message));

    public static void AddErrors(this ModelStateDictionary modelState, IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            modelState.AddModelError(string.Empty, error);
        }
    }

    public static IReadOnlyList<string> SplitLines(this string? value)
        => (value ?? string.Empty).Split(['\r', '\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
