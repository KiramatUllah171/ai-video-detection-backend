using AiVideoDetection.Application.Payments;
using AiVideoDetection.Application.Payments.DTOs;
using AiVideoDetection.Application.Payments.Interfaces;
using AiVideoDetection.Application.Payments.Options;
using AiVideoDetection.Domain.Constants;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AiVideoDetection.Infrastructure.Payments;

public class EasypaisaPaymentGateway(IOptions<EasypaisaOptions> options) : IPaymentGateway
{
    private readonly EasypaisaOptions _options = options.Value;

    public string Provider => PaymentProviders.Easypaisa;

    public Task<PaymentGatewayInitiationResult> InitiatePaymentAsync(
        PaymentGatewayInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.CheckoutBaseUrl))
        {
            return Task.FromResult(PaymentGatewayInitiationResult.Failed(
                PaymentErrorCodes.PaymentGatewayUnavailable,
                "Easypaisa production checkout is not configured. Provide official merchant integration settings before enabling live payments."));
        }

        return Task.FromResult(PaymentGatewayInitiationResult.Failed(
            PaymentErrorCodes.PaymentGatewayUnavailable,
            "Easypaisa live initiation requires official merchant API field mapping before production use."));
    }

    public Task<PaymentGatewayVerificationResult> VerifyCallbackAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.CallbackSecret))
        {
            return Task.FromResult(PaymentGatewayVerificationResult.Failed(
                request.OrderId,
                PaymentErrorCodes.PaymentGatewayUnavailable,
                "Easypaisa callback verification is not configured."));
        }

        if (!string.Equals(request.Status, "Verified", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PaymentGatewayVerificationResult.Failed(
                request.OrderId,
                PaymentErrorCodes.PaymentVerificationFailed,
                "Easypaisa callback did not report a successful payment."));
        }

        if (!IsValidSignature(request))
        {
            return Task.FromResult(PaymentGatewayVerificationResult.Failed(
                request.OrderId,
                PaymentErrorCodes.PaymentVerificationFailed,
                "Easypaisa callback signature verification failed."));
        }

        return Task.FromResult(PaymentGatewayVerificationResult.Succeeded(
            request.OrderId,
            request.ProviderTransactionId,
            request.Amount,
            request.Currency));
    }

    private bool IsValidSignature(PaymentCallbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Signature) || string.IsNullOrWhiteSpace(_options.CallbackSecret))
        {
            return false;
        }

        var canonical = string.Join(
            "|",
            request.OrderId.Trim(),
            request.ProviderTransactionId?.Trim() ?? string.Empty,
            request.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            request.Currency.Trim().ToUpperInvariant(),
            request.Status.Trim());
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.CallbackSecret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(request.Signature.Trim().ToLowerInvariant()));
    }
}
