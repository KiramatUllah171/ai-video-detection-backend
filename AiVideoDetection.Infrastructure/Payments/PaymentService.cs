using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Payments;
using AiVideoDetection.Application.Payments.DTOs;
using AiVideoDetection.Application.Payments.Interfaces;
using AiVideoDetection.Application.Payments.Options;
using AiVideoDetection.Domain.Constants;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Payments;

public class PaymentService(
    AppDbContext dbContext,
    IPaymentGateway paymentGateway,
    IOptions<PaymentOptions> options,
    IHostEnvironment environment) : IPaymentService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly PaymentOptions _options = options.Value;

    public async Task<ApiResponse<PaymentInitiationResponse>> InitiatePaymentAsync(
        long userId,
        InitiatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var planCode = NormalizePlanCode(request.PlanCode);
        if (string.IsNullOrWhiteSpace(planCode))
        {
            return ApiResponse<PaymentInitiationResponse>.ErrorResponse(
                "Subscription plan was not found.",
                errorCode: PaymentErrorCodes.PlanNotFound);
        }

        var plan = await dbContext.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Code == planCode && candidate.IsActive,
                cancellationToken);
        if (plan is null)
        {
            return ApiResponse<PaymentInitiationResponse>.ErrorResponse(
                "Subscription plan was not found.",
                errorCode: PaymentErrorCodes.PlanNotFound);
        }

        if (plan.PriceAmount <= 0 || plan.ValidityDays is null)
        {
            return ApiResponse<PaymentInitiationResponse>.ErrorResponse(
                "This plan does not require payment.",
                errorCode: PaymentErrorCodes.PaymentNotRequired);
        }

        var now = DateTimeOffset.UtcNow;
        var payment = new PaymentTransaction
        {
            UserId = userId,
            SubscriptionPlanId = plan.Id,
            Provider = PaymentProviders.Easypaisa,
            OrderId = GenerateOrderId(),
            Status = PaymentTransactionStatuses.Pending,
            Amount = plan.PriceAmount,
            Currency = plan.Currency,
            InitiatedAt = now,
            ExpiresAt = now.AddMinutes(Math.Clamp(_options.PendingPaymentExpiryMinutes, 5, 1440))
        };

        var gatewayRequest = new PaymentGatewayInitiationRequest
        {
            OrderId = payment.OrderId,
            UserId = userId,
            PlanCode = plan.Code,
            Amount = payment.Amount,
            Currency = payment.Currency,
            ExpiresAt = payment.ExpiresAt!.Value
        };
        payment.GatewayRequestJson = JsonSerializer.Serialize(gatewayRequest, SerializerOptions);

        if (IsMockEnabled())
        {
            payment.GatewayResponseJson = JsonSerializer.Serialize(new
            {
                mode = "mock",
                paymentUrl = $"/api/payments/mock/{payment.OrderId}/complete"
            }, SerializerOptions);
            dbContext.PaymentTransactions.Add(payment);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ApiResponse<PaymentInitiationResponse>.SuccessResponse(
                ToInitiationResponse(payment, plan, $"/api/payments/mock/{payment.OrderId}/complete", isMock: true),
                "Mock payment created.");
        }

        var gatewayResult = await paymentGateway.InitiatePaymentAsync(gatewayRequest, cancellationToken);
        payment.ProviderTransactionId = gatewayResult.ProviderTransactionId;
        payment.GatewayResponseJson = JsonSerializer.Serialize(gatewayResult, SerializerOptions);

        if (!gatewayResult.Success)
        {
            payment.Status = PaymentTransactionStatuses.Failed;
            payment.FailedAt = DateTimeOffset.UtcNow;
            payment.FailureReason = gatewayResult.ErrorMessage;
            dbContext.PaymentTransactions.Add(payment);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ApiResponse<PaymentInitiationResponse>.ErrorResponse(
                gatewayResult.ErrorMessage ?? "Payment gateway is unavailable.",
                errorCode: gatewayResult.ErrorCode ?? PaymentErrorCodes.PaymentGatewayUnavailable);
        }

        dbContext.PaymentTransactions.Add(payment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse<PaymentInitiationResponse>.SuccessResponse(
            ToInitiationResponse(payment, plan, gatewayResult.PaymentUrl, isMock: false),
            "Payment created.");
    }

    public async Task<ApiResponse<PaymentStatusResponse>> GetPaymentStatusAsync(
        long userId,
        string orderId,
        CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentForUserAsync(userId, orderId, cancellationToken);
        if (payment is null)
        {
            return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                "Payment was not found.",
                errorCode: PaymentErrorCodes.PaymentNotFound);
        }

        await ExpireIfNeededAsync(payment, cancellationToken);
        return ApiResponse<PaymentStatusResponse>.SuccessResponse(ToStatusResponse(payment));
    }

    public async Task<ApiResponse<PaymentStatusResponse>> CompleteMockPaymentAsync(
        long userId,
        string orderId,
        MockPaymentCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsMockEnabled())
        {
            return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                "Mock payments are available only in Development when explicitly enabled.",
                errorCode: PaymentErrorCodes.MockPaymentUnavailable);
        }

        if (!IsPlausibleOrderId(orderId))
        {
            return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                "Payment was not found.",
                errorCode: PaymentErrorCodes.PaymentNotFound);
        }

        var payment = await LoadPaymentForUserAsync(userId, orderId, cancellationToken);
        if (payment is null)
        {
            return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                "Payment was not found.",
                errorCode: PaymentErrorCodes.PaymentNotFound);
        }

        if (!request.Succeed)
        {
            return await MarkFailedAsync(
                payment,
                request.FailureReason ?? "Mock payment failed.",
                cancellationToken);
        }

        return await ActivateVerifiedPaymentAsync(
            payment.OrderId,
            request.ProviderTransactionId ?? $"mock-{payment.OrderId}",
            request.Amount ?? payment.Amount,
            payment.Currency,
            JsonSerializer.Serialize(request, SerializerOptions),
            cancellationToken);
    }

    public async Task<ApiResponse<PaymentStatusResponse>> HandleProviderCallbackAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsPlausibleOrderId(request.OrderId))
        {
            return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                "Payment was not found.",
                errorCode: PaymentErrorCodes.PaymentNotFound);
        }

        var verification = await paymentGateway.VerifyCallbackAsync(request, cancellationToken);
        if (!verification.Success)
        {
            return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                verification.ErrorMessage ?? "Payment verification failed.",
                errorCode: verification.ErrorCode ?? PaymentErrorCodes.PaymentVerificationFailed);
        }

        if (!string.Equals(verification.OrderId.Trim(), request.OrderId.Trim(), StringComparison.Ordinal))
        {
            return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                "Payment verification order did not match the callback order.",
                errorCode: PaymentErrorCodes.PaymentVerificationFailed);
        }

        return await ActivateVerifiedPaymentAsync(
            verification.OrderId,
            verification.ProviderTransactionId,
            verification.Amount,
            verification.Currency,
            request.RawPayloadJson ?? JsonSerializer.Serialize(request, SerializerOptions),
            cancellationToken);
    }

    private async Task<ApiResponse<PaymentStatusResponse>> ActivateVerifiedPaymentAsync(
        string orderId,
        string? providerTransactionId,
        decimal verifiedAmount,
        string verifiedCurrency,
        string? callbackPayloadJson,
        CancellationToken cancellationToken)
    {
        var transaction = await BeginSerializableTransactionIfSupportedAsync(cancellationToken);
        await using (transaction)
        {
            if (!IsPlausibleOrderId(orderId))
            {
                return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                    "Payment was not found.",
                    errorCode: PaymentErrorCodes.PaymentNotFound);
            }

            if (!IsPlausibleProviderTransactionId(providerTransactionId))
            {
                return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                    "Payment verification did not include a valid provider transaction reference.",
                    errorCode: PaymentErrorCodes.PaymentVerificationFailed);
            }

            providerTransactionId = providerTransactionId!.Trim();
            orderId = orderId.Trim();

            var payment = await dbContext.PaymentTransactions
                .Include(candidate => candidate.SubscriptionPlan)
                .Include(candidate => candidate.UserSubscription)
                .FirstOrDefaultAsync(candidate => candidate.OrderId == orderId, cancellationToken);

            if (payment is null)
            {
                return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                    "Payment was not found.",
                    errorCode: PaymentErrorCodes.PaymentNotFound);
            }

            if (payment.Status == PaymentTransactionStatuses.Verified)
            {
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return ApiResponse<PaymentStatusResponse>.SuccessResponse(
                    ToStatusResponse(payment),
                    "Payment was already verified.");
            }

            if (payment.Status is PaymentTransactionStatuses.Failed or PaymentTransactionStatuses.Cancelled)
            {
                return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                    "Payment is already finalized.",
                    errorCode: PaymentErrorCodes.PaymentAlreadyFinalized);
            }

            var providerTransactionAlreadyUsed = await dbContext.PaymentTransactions
                .AnyAsync(candidate =>
                    candidate.OrderId != orderId &&
                    candidate.Provider == payment.Provider &&
                    candidate.ProviderTransactionId == providerTransactionId,
                    cancellationToken);
            if (providerTransactionAlreadyUsed)
            {
                return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                    "Payment provider transaction reference was already used.",
                    errorCode: PaymentErrorCodes.PaymentVerificationFailed);
            }

            if (payment.ExpiresAt is not null && payment.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                payment.Status = PaymentTransactionStatuses.Expired;
                payment.FailedAt = DateTimeOffset.UtcNow;
                payment.FailureReason = "Payment expired before verification.";
                await dbContext.SaveChangesAsync(cancellationToken);
                return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                    "Payment expired before verification.",
                    errorCode: PaymentErrorCodes.PaymentExpired);
            }

            if (payment.Amount != verifiedAmount ||
                !string.Equals(payment.Currency, verifiedCurrency, StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = PaymentTransactionStatuses.Failed;
                payment.ProviderTransactionId = providerTransactionId;
                payment.CallbackPayloadJson = callbackPayloadJson;
                payment.FailedAt = DateTimeOffset.UtcNow;
                payment.FailureReason = "Verified payment amount or currency did not match the order.";
                await dbContext.SaveChangesAsync(cancellationToken);
                return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                    "Verified payment amount or currency did not match the order.",
                    errorCode: PaymentErrorCodes.PaymentAmountMismatch);
            }

            var now = DateTimeOffset.UtcNow;
            var subscription = new UserSubscription
            {
                UserId = payment.UserId,
                SubscriptionPlanId = payment.SubscriptionPlanId,
                Status = SubscriptionStatuses.Active,
                StartsAt = now,
                ExpiresAt = now.AddDays(payment.SubscriptionPlan.ValidityDays!.Value),
                ActivatedAt = now
            };

            dbContext.UserSubscriptions.Add(subscription);
            payment.UserSubscription = subscription;
            payment.Status = PaymentTransactionStatuses.Verified;
            payment.ProviderTransactionId = providerTransactionId;
            payment.CallbackPayloadJson = callbackPayloadJson;
            payment.VerifiedAt = now;
            payment.FailedAt = null;
            payment.FailureReason = null;

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ApiResponse<PaymentStatusResponse>.SuccessResponse(
                ToStatusResponse(payment),
                "Payment verified and subscription activated.");
        }
    }

    private async Task<ApiResponse<PaymentStatusResponse>> MarkFailedAsync(
        PaymentTransaction payment,
        string reason,
        CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentTransactionStatuses.Verified)
        {
            return ApiResponse<PaymentStatusResponse>.SuccessResponse(
                ToStatusResponse(payment),
                "Payment was already verified.");
        }

        if (payment.Status is PaymentTransactionStatuses.Failed or PaymentTransactionStatuses.Cancelled or PaymentTransactionStatuses.Expired)
        {
            return ApiResponse<PaymentStatusResponse>.ErrorResponse(
                "Payment is already finalized.",
                errorCode: PaymentErrorCodes.PaymentAlreadyFinalized);
        }

        payment.Status = PaymentTransactionStatuses.Failed;
        payment.FailedAt = DateTimeOffset.UtcNow;
        payment.FailureReason = reason;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse<PaymentStatusResponse>.ErrorResponse(reason, errorCode: PaymentErrorCodes.PaymentVerificationFailed);
    }

    private async Task ExpireIfNeededAsync(PaymentTransaction payment, CancellationToken cancellationToken)
    {
        if (payment.Status != PaymentTransactionStatuses.Pending ||
            payment.ExpiresAt is null ||
            payment.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return;
        }

        payment.Status = PaymentTransactionStatuses.Expired;
        payment.FailedAt = DateTimeOffset.UtcNow;
        payment.FailureReason = "Payment expired before verification.";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<PaymentTransaction?> LoadPaymentForUserAsync(
        long userId,
        string orderId,
        CancellationToken cancellationToken)
    {
        if (!IsPlausibleOrderId(orderId))
        {
            return null;
        }

        orderId = orderId.Trim();
        return await dbContext.PaymentTransactions
            .Include(payment => payment.SubscriptionPlan)
            .Include(payment => payment.UserSubscription)
            .FirstOrDefaultAsync(
                payment => payment.UserId == userId && payment.OrderId == orderId,
                cancellationToken);
    }

    private bool IsMockEnabled()
    {
        return environment.IsDevelopment() && _options.MockEnabled;
    }

    private string GenerateOrderId()
    {
        return $"{_options.OrderPrefix.Trim().ToUpperInvariant()}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{RandomHex(8)}";
    }

    private static string NormalizePlanCode(string? planCode)
    {
        var normalized = planCode?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized.Length <= 32 && IsSafeToken(normalized)
            ? normalized
            : string.Empty;
    }

    private static bool IsPlausibleOrderId(string? orderId)
    {
        var normalized = orderId?.Trim();
        return normalized is { Length: >= 8 and <= 120 } && IsSafeToken(normalized);
    }

    private static bool IsPlausibleProviderTransactionId(string? providerTransactionId)
    {
        var normalized = providerTransactionId?.Trim();
        return normalized is { Length: >= 1 and <= 200 } &&
            normalized.All(character => !char.IsControl(character));
    }

    private static bool IsSafeToken(string value)
    {
        return value.All(character =>
            character is >= 'A' and <= 'Z' ||
            character is >= 'a' and <= 'z' ||
            character is >= '0' and <= '9' ||
            character == '-' ||
            character == '_');
    }

    private static string RandomHex(int byteCount)
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionIfSupportedAsync(CancellationToken cancellationToken)
    {
        return dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
    }

    private static PaymentInitiationResponse ToInitiationResponse(
        PaymentTransaction payment,
        SubscriptionPlan plan,
        string? paymentUrl,
        bool isMock)
    {
        return new PaymentInitiationResponse
        {
            OrderId = payment.OrderId,
            Provider = payment.Provider,
            Status = payment.Status,
            PlanCode = plan.Code,
            PlanName = plan.Name,
            Amount = payment.Amount,
            Currency = payment.Currency,
            ExpiresAt = payment.ExpiresAt,
            PaymentUrl = paymentUrl,
            IsMock = isMock
        };
    }

    private static PaymentStatusResponse ToStatusResponse(PaymentTransaction payment)
    {
        return new PaymentStatusResponse
        {
            OrderId = payment.OrderId,
            Provider = payment.Provider,
            Status = payment.Status,
            PlanCode = payment.SubscriptionPlan.Code,
            PlanName = payment.SubscriptionPlan.Name,
            Amount = payment.Amount,
            Currency = payment.Currency,
            ProviderTransactionId = payment.ProviderTransactionId,
            InitiatedAt = payment.InitiatedAt,
            VerifiedAt = payment.VerifiedAt,
            FailedAt = payment.FailedAt,
            ExpiresAt = payment.ExpiresAt,
            SubscriptionId = payment.UserSubscriptionId,
            SubscriptionStartsAt = payment.UserSubscription?.StartsAt,
            SubscriptionExpiresAt = payment.UserSubscription?.ExpiresAt,
            FailureReason = payment.FailureReason
        };
    }
}
