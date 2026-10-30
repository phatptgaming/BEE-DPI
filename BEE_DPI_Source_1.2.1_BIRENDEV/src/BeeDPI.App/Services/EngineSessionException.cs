namespace BeeDPI.App.Services;

public sealed class EngineSessionException : Exception
{
    public EngineSessionException(string message, string? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string? Code { get; }
}

public sealed class ElevationCancelledException : Exception
{
    public ElevationCancelledException()
        : base("Người dùng đã từ chối cấp quyền quản trị.")
    {
    }
}
