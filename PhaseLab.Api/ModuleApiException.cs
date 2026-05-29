namespace PhaseLab.Api;

public sealed class ModuleApiException : Exception
{
    public ModuleApiException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
