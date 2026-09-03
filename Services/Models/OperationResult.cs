namespace FridgeManager.Services.Models;

public record OperationResult(bool Success, string? Error)
{
    public static OperationResult Ok() => new(true, null);
    public static OperationResult Fail(string e) => new(false, e);
}

public record OperationResult<T>(bool Success, T? Value, string? Error)
{
    public static OperationResult<T> Ok(T v) => new(true, v, null);
    public static OperationResult<T> Fail(string e) => new(false, default, e);
}
