namespace AiVideoDetection.Application.Common;

public class ApiResponse<T>
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public T? Data { get; init; }

    public List<string> Errors { get; init; } = [];

    public string? CorrelationId { get; init; }

    public static ApiResponse<T> SuccessResponse(
        T data,
        string message = "Request completed successfully.",
        string? correlationId = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            CorrelationId = correlationId
        };
    }

    public static ApiResponse<T> ErrorResponse(
        string message,
        IEnumerable<string>? errors = null,
        string? correlationId = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Errors = errors?.ToList() ?? [],
            CorrelationId = correlationId
        };
    }
}
