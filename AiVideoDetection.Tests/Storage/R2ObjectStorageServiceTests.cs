using System.Security.Cryptography;
using System.Text;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Infrastructure.Common;
using AiVideoDetection.Infrastructure.Storage;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Storage;

public sealed class R2ObjectStorageServiceTests
{
    private static readonly string TestMasterKey = Convert.ToBase64String(
        SHA256.HashData(Encoding.UTF8.GetBytes("test-only-r2-storage-encryption-key")));

    [Fact]
    public async Task DownloadToAsyncDecryptsNonSeekableObjectStoreStream()
    {
        var plaintextBytes = RandomNumberGenerator.GetBytes(4096);
        var encryptionService = CreateEncryptionService();
        await using var plaintext = new MemoryStream(plaintextBytes);
        await using var encrypted = new MemoryStream();
        await encryptionService.ProtectStreamAsync(plaintext, encrypted);

        var service = new R2ObjectStorageService(
            new FakeAmazonS3Client(encrypted.ToArray()),
            Options.Create(new R2Options { BucketName = "test-bucket" }),
            encryptionService);

        var outputDirectory = Path.Combine(Path.GetTempPath(), $"r2-storage-test-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(outputDirectory, "video.mp4");
        try
        {
            await service.DownloadToAsync("uploads/video.mp4", outputPath);

            Assert.Equal(plaintextBytes, await File.ReadAllBytesAsync(outputPath));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static AesApplicationEncryptionService CreateEncryptionService()
    {
        return new AesApplicationEncryptionService(Options.Create(new ApplicationEncryptionOptions
        {
            Enabled = true,
            EncryptStorageObjects = true,
            EncryptDatabaseFields = true,
            AllowPlaintextFallback = false,
            MasterKeyBase64 = TestMasterKey
        }));
    }

    private sealed class FakeAmazonS3Client(byte[] objectBytes) : AmazonS3Client(
        "test-access-key",
        "test-secret-key",
        new AmazonS3Config
        {
            ServiceURL = "http://localhost",
            ForcePathStyle = true
        })
    {
        public override Task<GetObjectResponse> GetObjectAsync(
            string bucketName,
            string key,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GetObjectResponse
            {
                BucketName = bucketName,
                Key = key,
                ResponseStream = new NonSeekableReadStream(new MemoryStream(objectBytes, writable: false))
            });
        }
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
