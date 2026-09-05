namespace AiVideoDetection.Application.Payments.Options;

public class PaymentOptions
{
    public const string SectionName = "Payments";

    public bool MockEnabled { get; set; }

    public int PendingPaymentExpiryMinutes { get; set; } = 30;

    public string OrderPrefix { get; set; } = "SACHAI";
}
