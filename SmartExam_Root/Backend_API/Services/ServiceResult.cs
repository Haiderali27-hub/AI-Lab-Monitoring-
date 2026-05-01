namespace Backend_API.Services;

public class ServiceResult<T>
{
    public bool Success { get; init; }
    public string Code { get; init; } = "SUCCESS";
    public string Message { get; init; } = "Success";
    public T? Data { get; init; }

    public static ServiceResult<T> Ok(T data, string message = "Success", string code = "SUCCESS") =>
        new()
        {
            Success = true,
            Code = code,
            Message = message,
            Data = data
        };

    public static ServiceResult<T> Fail(string code, string message) =>
        new()
        {
            Success = false,
            Code = code,
            Message = message,
            Data = default
        };
}