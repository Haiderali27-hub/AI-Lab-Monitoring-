namespace Backend_API.Contracts;

public record ApiResponse<T>(bool Success, string Code, string Message, T? Data)
{
    public static ApiResponse<T> Ok(T data, string message = "Success", string code = "SUCCESS") =>
        new(true, code, message, data);

    public static ApiResponse<T> Fail(string code, string message) =>
        new(false, code, message, default);
}