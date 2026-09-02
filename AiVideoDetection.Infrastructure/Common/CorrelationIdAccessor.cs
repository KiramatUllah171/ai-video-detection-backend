using AiVideoDetection.Application.Common;

namespace AiVideoDetection.Infrastructure.Common;

public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    public string? CorrelationId { get; set; }
}
