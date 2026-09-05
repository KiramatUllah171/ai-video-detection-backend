namespace AiVideoDetection.Application.Payments;

public static class PaymentErrorCodes
{
    public const string PlanNotFound = "PLAN_NOT_FOUND";
    public const string PaymentNotRequired = "PAYMENT_NOT_REQUIRED";
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";
    public const string PaymentExpired = "PAYMENT_EXPIRED";
    public const string PaymentAlreadyFinalized = "PAYMENT_ALREADY_FINALIZED";
    public const string PaymentGatewayUnavailable = "PAYMENT_GATEWAY_UNAVAILABLE";
    public const string PaymentVerificationFailed = "PAYMENT_VERIFICATION_FAILED";
    public const string PaymentAmountMismatch = "PAYMENT_AMOUNT_MISMATCH";
    public const string MockPaymentUnavailable = "MOCK_PAYMENT_UNAVAILABLE";
}
