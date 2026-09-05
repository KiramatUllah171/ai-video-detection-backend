namespace AiVideoDetection.Application.Payments.DTOs;

public class MockPaymentCompletionRequest
{
    public bool Succeed { get; set; } = true;

    public decimal? Amount { get; set; }

    public string? ProviderTransactionId { get; set; }

    public string? FailureReason { get; set; }
}
