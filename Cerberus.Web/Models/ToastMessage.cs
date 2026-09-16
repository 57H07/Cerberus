namespace Cerberus.Web.Models;

public enum ToastType
{
    Success,
    Error
}

public sealed record ToastMessage(ToastType Type, string Message)
{
    public const string TempDataKey = "Toast";
}

public sealed record ErrorViewModel(string? RequestId, string? Error = null, string? ErrorDescription = null);
