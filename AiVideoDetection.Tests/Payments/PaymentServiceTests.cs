using AiVideoDetection.Application.Payments;
using AiVideoDetection.Application.Payments.DTOs;
using AiVideoDetection.Application.Payments.Interfaces;
using AiVideoDetection.Application.Payments.Options;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Constants;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Payments;

public class PaymentServiceTests
{
    [Fact]
    public async Task InitiatePaymentAsyncCreatesMockPendingTransactionUsingDatabasePlanPrice()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(dbContext, environmentName: Environments.Development, mockEnabled: true);

        var response = await service.InitiatePaymentAsync(1, new InitiatePaymentRequest { PlanCode = "plus" });

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(SubscriptionPlanCodes.Plus, response.Data.PlanCode);
        Assert.Equal(499m, response.Data.Amount);
        Assert.Equal(PaymentTransactionStatuses.Pending, response.Data.Status);
        Assert.True(response.Data.IsMock);
        Assert.Contains("/api/payments/mock/", response.Data.PaymentUrl);
        var payment = await dbContext.PaymentTransactions.SingleAsync();
        Assert.Equal(499m, payment.Amount);
        Assert.Equal("PKR", payment.Currency);
    }

    [Fact]
    public async Task InitiatePaymentAsyncRejectsFreePlan()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(dbContext, environmentName: Environments.Development, mockEnabled: true);

        var response = await service.InitiatePaymentAsync(1, new InitiatePaymentRequest { PlanCode = SubscriptionPlanCodes.Free });

        Assert.False(response.Success);
        Assert.Equal(PaymentErrorCodes.PaymentNotRequired, response.ErrorCode);
        Assert.Empty(dbContext.PaymentTransactions);
    }

    [Fact]
    public async Task CompleteMockPaymentAsyncActivatesSubscriptionIdempotently()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(dbContext, environmentName: Environments.Development, mockEnabled: true);
        var checkout = await service.InitiatePaymentAsync(1, new InitiatePaymentRequest { PlanCode = SubscriptionPlanCodes.Pro });

        var first = await service.CompleteMockPaymentAsync(
            1,
            checkout.Data!.OrderId,
            new MockPaymentCompletionRequest { Succeed = true });
        var second = await service.CompleteMockPaymentAsync(
            1,
            checkout.Data.OrderId,
            new MockPaymentCompletionRequest { Succeed = true });

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(PaymentTransactionStatuses.Verified, first.Data!.Status);
        Assert.Equal(1, await dbContext.UserSubscriptions.CountAsync());
        var subscription = await dbContext.UserSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatuses.Active, subscription.Status);
        Assert.Equal(3, subscription.SubscriptionPlanId);
        Assert.True(subscription.ExpiresAt > subscription.StartsAt);
    }

    [Fact]
    public async Task CompleteMockPaymentAsyncRejectsWrongAmountAndDoesNotActivate()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(dbContext, environmentName: Environments.Development, mockEnabled: true);
        var checkout = await service.InitiatePaymentAsync(1, new InitiatePaymentRequest { PlanCode = SubscriptionPlanCodes.Plus });

        var response = await service.CompleteMockPaymentAsync(
            1,
            checkout.Data!.OrderId,
            new MockPaymentCompletionRequest { Succeed = true, Amount = 1m });

        Assert.False(response.Success);
        Assert.Equal(PaymentErrorCodes.PaymentAmountMismatch, response.ErrorCode);
        Assert.Empty(dbContext.UserSubscriptions);
        Assert.Equal(PaymentTransactionStatuses.Failed, (await dbContext.PaymentTransactions.SingleAsync()).Status);
    }

    [Fact]
    public async Task CompleteMockPaymentAsyncCannotActivateAnotherUsersOrder()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        dbContext.Users.Add(CreateUser(2));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, environmentName: Environments.Development, mockEnabled: true);
        var checkout = await service.InitiatePaymentAsync(1, new InitiatePaymentRequest { PlanCode = SubscriptionPlanCodes.Plus });

        var response = await service.CompleteMockPaymentAsync(
            2,
            checkout.Data!.OrderId,
            new MockPaymentCompletionRequest { Succeed = true });

        Assert.False(response.Success);
        Assert.Equal(PaymentErrorCodes.PaymentNotFound, response.ErrorCode);
        Assert.Empty(dbContext.UserSubscriptions);
    }

    [Fact]
    public async Task CompleteMockPaymentAsyncIsUnavailableOutsideDevelopment()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(dbContext, environmentName: Environments.Production, mockEnabled: true);

        var response = await service.CompleteMockPaymentAsync(
            1,
            "SACHAI-ORDER",
            new MockPaymentCompletionRequest { Succeed = true });

        Assert.False(response.Success);
        Assert.Equal(PaymentErrorCodes.MockPaymentUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task HandleProviderCallbackAsyncDoesNotActivateWhenGatewayVerificationFails()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(
            dbContext,
            environmentName: Environments.Production,
            mockEnabled: false,
            gateway: new FakePaymentGateway(PaymentGatewayVerificationResult.Failed(
                "SACHAI-MISSING",
                PaymentErrorCodes.PaymentVerificationFailed,
                "Signature failed.")));

        var response = await service.HandleProviderCallbackAsync(new PaymentCallbackRequest
        {
            OrderId = "SACHAI-MISSING",
            Amount = 499m,
            Currency = "PKR",
            Status = "Success"
        });

        Assert.False(response.Success);
        Assert.Equal(PaymentErrorCodes.PaymentVerificationFailed, response.ErrorCode);
        Assert.Empty(dbContext.UserSubscriptions);
    }

    [Fact]
    public async Task HandleProviderCallbackAsyncRejectsUnknownVerifiedOrder()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(
            dbContext,
            environmentName: Environments.Production,
            mockEnabled: false,
            gateway: new FakePaymentGateway(PaymentGatewayVerificationResult.Succeeded(
                "unknown-order",
                "provider-1",
                499m,
                "PKR")));

        var response = await service.HandleProviderCallbackAsync(new PaymentCallbackRequest
        {
            OrderId = "unknown-order",
            Amount = 499m,
            Currency = "PKR",
            Status = "Success"
        });

        Assert.False(response.Success);
        Assert.Equal(PaymentErrorCodes.PaymentNotFound, response.ErrorCode);
        Assert.Empty(dbContext.UserSubscriptions);
    }

    [Fact]
    public async Task HandleProviderCallbackAsyncRejectsMalformedOrderIdBeforeActivation()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(dbContext, environmentName: Environments.Production, mockEnabled: false);

        var response = await service.HandleProviderCallbackAsync(new PaymentCallbackRequest
        {
            OrderId = "../bad-order",
            ProviderTransactionId = "provider-1",
            Amount = 499m,
            Currency = "PKR",
            Status = "Success"
        });

        Assert.False(response.Success);
        Assert.Equal(PaymentErrorCodes.PaymentNotFound, response.ErrorCode);
        Assert.Empty(dbContext.UserSubscriptions);
    }

    [Fact]
    public async Task HandleProviderCallbackAsyncRequiresProviderTransactionReference()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(dbContext, environmentName: Environments.Production, mockEnabled: false);
        var checkout = await service.InitiatePaymentAsync(1, new InitiatePaymentRequest { PlanCode = SubscriptionPlanCodes.Plus });

        var response = await service.HandleProviderCallbackAsync(new PaymentCallbackRequest
        {
            OrderId = checkout.Data!.OrderId,
            Amount = 499m,
            Currency = "PKR",
            Status = "Success"
        });

        Assert.False(response.Success);
        Assert.Equal(PaymentErrorCodes.PaymentVerificationFailed, response.ErrorCode);
        Assert.Empty(dbContext.UserSubscriptions);
    }

    [Fact]
    public async Task HandleProviderCallbackAsyncRejectsDuplicateProviderTransactionReference()
    {
        await using var dbContext = CreateDbContext();
        await SeedUserAndPlansAsync(dbContext);
        var service = CreateService(dbContext, environmentName: Environments.Production, mockEnabled: false);
        var firstCheckout = await service.InitiatePaymentAsync(1, new InitiatePaymentRequest { PlanCode = SubscriptionPlanCodes.Plus });
        var secondCheckout = await service.InitiatePaymentAsync(1, new InitiatePaymentRequest { PlanCode = SubscriptionPlanCodes.Plus });

        var first = await service.HandleProviderCallbackAsync(new PaymentCallbackRequest
        {
            OrderId = firstCheckout.Data!.OrderId,
            ProviderTransactionId = "provider-duplicate",
            Amount = 499m,
            Currency = "PKR",
            Status = "Success"
        });
        var second = await service.HandleProviderCallbackAsync(new PaymentCallbackRequest
        {
            OrderId = secondCheckout.Data!.OrderId,
            ProviderTransactionId = "provider-duplicate",
            Amount = 499m,
            Currency = "PKR",
            Status = "Success"
        });

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(PaymentErrorCodes.PaymentVerificationFailed, second.ErrorCode);
        Assert.Equal(1, await dbContext.UserSubscriptions.CountAsync());
    }

    private static PaymentService CreateService(
        AppDbContext dbContext,
        string environmentName,
        bool mockEnabled,
        IPaymentGateway? gateway = null)
    {
        return new PaymentService(
            dbContext,
            gateway ?? new FakePaymentGateway(),
            Options.Create(new PaymentOptions
            {
                MockEnabled = mockEnabled,
                PendingPaymentExpiryMinutes = 30,
                OrderPrefix = "SACHAI"
            }),
            new FakeHostEnvironment(environmentName));
    }

    private static async Task SeedUserAndPlansAsync(AppDbContext dbContext)
    {
        dbContext.Users.Add(CreateUser(1));
        dbContext.SubscriptionPlans.AddRange(
            new SubscriptionPlan
            {
                Id = 1,
                Code = SubscriptionPlanCodes.Free,
                Name = "Free",
                PriceAmount = 0,
                Currency = "PKR",
                ScanLimit = 2,
                MaxVideoSizeBytes = VideoUploadSizeLimits.FreeMaxVideoSizeBytes,
                AllowsSmartScan = true,
                AllowsDetailedScan = false,
                IsActive = true,
                SortOrder = 1
            },
            new SubscriptionPlan
            {
                Id = 2,
                Code = SubscriptionPlanCodes.Plus,
                Name = "Plus",
                PriceAmount = 499,
                Currency = "PKR",
                ScanLimit = 10,
                MaxVideoSizeBytes = VideoUploadSizeLimits.PlusMaxVideoSizeBytes,
                AllowsSmartScan = true,
                AllowsDetailedScan = false,
                ValidityDays = 30,
                IsActive = true,
                SortOrder = 2
            },
            new SubscriptionPlan
            {
                Id = 3,
                Code = SubscriptionPlanCodes.Pro,
                Name = "Pro",
                PriceAmount = 999,
                Currency = "PKR",
                ScanLimit = 25,
                MaxVideoSizeBytes = VideoUploadSizeLimits.ProMaxVideoSizeBytes,
                AllowsSmartScan = true,
                AllowsDetailedScan = true,
                ValidityDays = 30,
                IsActive = true,
                SortOrder = 3
            });
        await dbContext.SaveChangesAsync();
    }

    private static User CreateUser(long id)
    {
        return new User
        {
            Id = id,
            Name = $"User {id}",
            UserName = $"user{id}@example.com",
            Email = $"user{id}@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            IsActive = true,
            EmailConfirmed = true
        };
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private sealed class FakePaymentGateway(PaymentGatewayVerificationResult? verificationResult = null) : IPaymentGateway
    {
        public string Provider => PaymentProviders.Easypaisa;

        public Task<PaymentGatewayInitiationResult> InitiatePaymentAsync(
            PaymentGatewayInitiationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PaymentGatewayInitiationResult.Succeeded(null));
        }

        public Task<PaymentGatewayVerificationResult> VerifyCallbackAsync(
            PaymentCallbackRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(verificationResult ?? PaymentGatewayVerificationResult.Succeeded(
                request.OrderId,
                request.ProviderTransactionId,
                request.Amount,
                request.Currency));
        }
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
