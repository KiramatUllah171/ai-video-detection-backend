namespace AiVideoDetection.Application.Payments.Options;

public class EasypaisaOptions
{
    public const string SectionName = "Easypaisa";

    public bool Enabled { get; set; }

    public string? CheckoutBaseUrl { get; set; }

    public string? MerchantId { get; set; }

    public string? StoreId { get; set; }

    public string? CallbackSecret { get; set; }
}
