namespace AiVideoDetection.Domain.Constants;

public static class SubscriptionPlanCodes
{
    public const string Free = "FREE";
    public const string Plus = "PLUS";
    public const string Pro = "PRO";
}

public static class SubscriptionStatuses
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";
}

public static class PaymentProviders
{
    public const string Easypaisa = "Easypaisa";
}

public static class PaymentTransactionStatuses
{
    public const string Pending = "Pending";
    public const string Verified = "Verified";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";
}

public static class ScanReservationKinds
{
    public const string FreeTrial = "FreeTrial";
    public const string PaidSubscription = "PaidSubscription";
    public const string InternalUnlimited = "InternalUnlimited";
}

public static class ScanReservationStatuses
{
    public const string Reserved = "Reserved";
    public const string Consumed = "Consumed";
    public const string Released = "Released";
}
