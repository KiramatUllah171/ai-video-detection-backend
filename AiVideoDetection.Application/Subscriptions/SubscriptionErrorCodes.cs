namespace AiVideoDetection.Application.Subscriptions;

public static class SubscriptionErrorCodes
{
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string SubscriptionRequired = "SUBSCRIPTION_REQUIRED";
    public const string DetailedScanNotAllowed = "DETAILED_SCAN_NOT_ALLOWED";
    public const string VideoSizeLimitExceeded = "VIDEO_SIZE_LIMIT_EXCEEDED";
    public const string ScanQuotaExhausted = "SCAN_QUOTA_EXHAUSTED";
    public const string FreeTrialExhausted = "FREE_TRIAL_EXHAUSTED";
    public const string DeviceIdentityRequired = "DEVICE_IDENTITY_REQUIRED";
    public const string ClientIpRequired = "CLIENT_IP_REQUIRED";
    public const string ReservationNotFound = "RESERVATION_NOT_FOUND";
    public const string ReservationAlreadyReleased = "RESERVATION_ALREADY_RELEASED";
    public const string ReservationAlreadyConsumed = "RESERVATION_ALREADY_CONSUMED";
    public const string ReservationCannotBeReleased = "RESERVATION_CANNOT_BE_RELEASED";
    public const string SubscriptionSecurityNotConfigured = "SUBSCRIPTION_SECURITY_NOT_CONFIGURED";
}
