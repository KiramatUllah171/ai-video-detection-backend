namespace AiVideoDetection.Application.Payments.DTOs;

public class PaymentCallbackRequest
{
    public string OrderId { get; set; } = string.Empty;

    public string? ProviderTransactionId { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "PKR";

    public string? Signature { get; set; }

    public string? RawPayloadJson { get; set; }
}
