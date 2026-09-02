namespace AiVideoDetection.Application.Common;

public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; set; }
}
