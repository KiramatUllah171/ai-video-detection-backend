using System.Security.Cryptography;
using System.Text;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Infrastructure.Common;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Security;

public class ApplicationEncryptionServiceTests
{
    private static readonly string TestMasterKey = Convert.ToBase64String(
        SHA256.HashData(Encoding.UTF8.GetBytes("test-only-application-encryption-key")));

    [Fact]
    public void ProtectStringEncryptsAndRestoresValue()
    {
        var service = CreateService();
        const string plaintext = """{"score":95.6,"message":"private model payload"}""";

        var protectedValue = service.ProtectString(plaintext);
        var restored = service.UnprotectString(protectedValue);

        Assert.NotNull(protectedValue);
        Assert.StartsWith("enc:v1:", protectedValue);
        Assert.NotEqual(plaintext, protectedValue);
        Assert.Equal(plaintext, restored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void ProtectStringLeavesSafePlaceholdersReadable(string placeholder)
    {
        var service = CreateService();

        var protectedValue = service.ProtectString(placeholder);

        Assert.Equal(placeholder, protectedValue);
    }

    [Fact]
    public async Task ProtectStreamEncryptsAndRestoresContent()
    {
        var service = CreateService();
        var plaintextBytes = RandomNumberGenerator.GetBytes(4096);
        await using var plaintext = new MemoryStream(plaintextBytes);
        await using var encrypted = new MemoryStream();
        await using var restored = new MemoryStream();

        await service.ProtectStreamAsync(plaintext, encrypted);
        encrypted.Position = 0;
        await service.UnprotectStreamAsync(encrypted, restored);

        Assert.NotEqual(plaintextBytes, encrypted.ToArray());
        Assert.Equal(plaintextBytes, restored.ToArray());
    }

    [Fact]
    public async Task UnprotectStreamReadsExistingPlaintextWhenFallbackIsEnabled()
    {
        var service = CreateService();
        var plaintextBytes = Encoding.UTF8.GetBytes("existing unencrypted object");
        await using var plaintext = new MemoryStream(plaintextBytes);
        await using var restored = new MemoryStream();

        await service.UnprotectStreamAsync(plaintext, restored);

        Assert.Equal(plaintextBytes, restored.ToArray());
    }

    private static AesApplicationEncryptionService CreateService(bool allowPlaintextFallback = true)
    {
        return new AesApplicationEncryptionService(Options.Create(new ApplicationEncryptionOptions
        {
            Enabled = true,
            EncryptStorageObjects = true,
            EncryptDatabaseFields = true,
            AllowPlaintextFallback = allowPlaintextFallback,
            MasterKeyBase64 = TestMasterKey
        }));
    }
}
