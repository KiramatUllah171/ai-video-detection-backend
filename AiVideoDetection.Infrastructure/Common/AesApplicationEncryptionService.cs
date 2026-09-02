using System.Security.Cryptography;
using System.Text;
using AiVideoDetection.Application.Common;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Common;

public sealed class AesApplicationEncryptionService : IApplicationEncryptionService
{
    private const string TextPrefix = "enc:v1:";
    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    private const int FileIvSize = 16;
    private const int FileTagSize = 32;
    private static readonly byte[] FileMagic = "SachAIEnc1"u8.ToArray();
    private readonly byte[] _aesKey;
    private readonly byte[] _hmacKey;
    private readonly ApplicationEncryptionOptions _options;

    public AesApplicationEncryptionService(IOptions<ApplicationEncryptionOptions> options)
    {
        _options = options.Value;
        if (!_options.Enabled)
        {
            _aesKey = [];
            _hmacKey = [];
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.MasterKeyBase64))
        {
            throw new InvalidOperationException("Encryption:MasterKeyBase64 is required when encryption is enabled.");
        }

        byte[] masterKey;
        try
        {
            masterKey = Convert.FromBase64String(_options.MasterKeyBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Encryption:MasterKeyBase64 must be a valid base64 value.", exception);
        }

        if (masterKey.Length != 32)
        {
            throw new InvalidOperationException("Encryption:MasterKeyBase64 must decode to exactly 32 bytes.");
        }

        using var keyDeriver = new HMACSHA512(masterKey);
        var derived = keyDeriver.ComputeHash(Encoding.UTF8.GetBytes("SachAI application encryption v1"));
        _aesKey = derived[..32];
        _hmacKey = derived[32..64];
        CryptographicOperations.ZeroMemory(masterKey);
        CryptographicOperations.ZeroMemory(derived);
    }

    public bool EncryptStorageObjects => _options.Enabled && _options.EncryptStorageObjects;

    public bool EncryptDatabaseFields => _options.Enabled && _options.EncryptDatabaseFields;

    public string? ProtectString(string? plaintext)
    {
        if (!EncryptDatabaseFields || plaintext is null)
        {
            return plaintext;
        }

        if (plaintext.StartsWith(TextPrefix, StringComparison.Ordinal) || IsSafePlaceholder(plaintext))
        {
            return plaintext;
        }

        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[GcmTagSize];

        using var aes = new AesGcm(_aesKey, GcmTagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[1 + nonce.Length + tag.Length + ciphertext.Length];
        payload[0] = 1;
        Buffer.BlockCopy(nonce, 0, payload, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, 1 + nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, 1 + nonce.Length + tag.Length, ciphertext.Length);

        CryptographicOperations.ZeroMemory(plaintextBytes);
        return TextPrefix + Convert.ToBase64String(payload);
    }

    public string? UnprotectString(string? protectedValue)
    {
        if (protectedValue is null || !protectedValue.StartsWith(TextPrefix, StringComparison.Ordinal))
        {
            if (EncryptDatabaseFields && !_options.AllowPlaintextFallback && protectedValue is not null)
            {
                throw new CryptographicException("Database field is not encrypted.");
            }

            return protectedValue;
        }

        var payload = Convert.FromBase64String(protectedValue[TextPrefix.Length..]);
        if (payload.Length < 1 + GcmNonceSize + GcmTagSize || payload[0] != 1)
        {
            throw new CryptographicException("Encrypted database field has an invalid format.");
        }

        var nonce = payload.AsSpan(1, GcmNonceSize);
        var tag = payload.AsSpan(1 + GcmNonceSize, GcmTagSize);
        var ciphertext = payload.AsSpan(1 + GcmNonceSize + GcmTagSize);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_aesKey, GcmTagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        var value = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        return value;
    }

    public async Task ProtectStreamAsync(Stream plaintext, Stream destination, CancellationToken cancellationToken = default)
    {
        if (!EncryptStorageObjects)
        {
            await plaintext.CopyToAsync(destination, cancellationToken);
            return;
        }

        var iv = RandomNumberGenerator.GetBytes(FileIvSize);
        await destination.WriteAsync(FileMagic, cancellationToken);
        await destination.WriteAsync(iv, cancellationToken);

        using var hmac = new HMACSHA256(_hmacKey);
        hmac.TransformBlock(FileMagic, 0, FileMagic.Length, null, 0);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);

        using var aes = Aes.Create();
        aes.Key = _aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        await using (var hmacStream = new HmacWriteStream(destination, hmac, leaveOpen: true))
        await using (var cryptoStream = new CryptoStream(hmacStream, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: false))
        {
            await plaintext.CopyToAsync(cryptoStream, cancellationToken);
            cryptoStream.FlushFinalBlock();
        }

        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        await destination.WriteAsync(hmac.Hash!, cancellationToken);
    }

    public async Task UnprotectStreamAsync(Stream protectedStream, Stream destination, CancellationToken cancellationToken = default)
    {
        if (!EncryptStorageObjects)
        {
            await protectedStream.CopyToAsync(destination, cancellationToken);
            return;
        }

        if (!protectedStream.CanSeek)
        {
            throw new InvalidOperationException("Encrypted object streams must support seeking.");
        }

        if (!TryReadEncryptedFileHeader(protectedStream, out var iv, out var encryptedContentLength))
        {
            if (_options.AllowPlaintextFallback)
            {
                protectedStream.Position = 0;
                await protectedStream.CopyToAsync(destination, cancellationToken);
                return;
            }

            throw new CryptographicException("Stored object is not encrypted.");
        }

        using var hmac = new HMACSHA256(_hmacKey);
        hmac.TransformBlock(FileMagic, 0, FileMagic.Length, null, 0);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);

        using var aes = Aes.Create();
        aes.Key = _aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        await using (var limitedStream = new HmacLimitedReadStream(protectedStream, encryptedContentLength, hmac))
        await using (var cryptoStream = new CryptoStream(limitedStream, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: false))
        {
            await cryptoStream.CopyToAsync(destination, cancellationToken);
        }

        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var expectedTag = new byte[FileTagSize];
        var tagBytesRead = await protectedStream.ReadAsync(expectedTag, cancellationToken);
        if (tagBytesRead != FileTagSize || !CryptographicOperations.FixedTimeEquals(hmac.Hash!, expectedTag))
        {
            throw new CryptographicException("Stored object authentication failed.");
        }
    }

    private static bool TryReadEncryptedFileHeader(Stream stream, out byte[] iv, out long encryptedContentLength)
    {
        iv = [];
        encryptedContentLength = 0;
        if (stream.Length < FileMagic.Length + FileIvSize + FileTagSize)
        {
            stream.Position = 0;
            return false;
        }

        stream.Position = 0;
        var magic = new byte[FileMagic.Length];
        var read = stream.Read(magic, 0, magic.Length);
        if (read != magic.Length || !CryptographicOperations.FixedTimeEquals(magic, FileMagic))
        {
            stream.Position = 0;
            return false;
        }

        iv = new byte[FileIvSize];
        read = stream.Read(iv, 0, iv.Length);
        if (read != iv.Length)
        {
            throw new CryptographicException("Encrypted object has an invalid header.");
        }

        encryptedContentLength = stream.Length - FileMagic.Length - FileIvSize - FileTagSize;
        return encryptedContentLength >= 0;
    }

    private static bool IsSafePlaceholder(string value)
    {
        return value.Length == 0
            || string.Equals(value, "{}", StringComparison.Ordinal)
            || string.Equals(value, "[]", StringComparison.Ordinal);
    }

    private sealed class HmacWriteStream(Stream inner, HMAC hmac, bool leaveOpen) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            hmac.TransformBlock(buffer, offset, count, null, 0);
            inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytes = buffer.ToArray();
            hmac.TransformBlock(bytes, 0, bytes.Length, null, 0);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!leaveOpen)
            {
                await inner.DisposeAsync();
            }

            await base.DisposeAsync();
        }
    }

    private sealed class HmacLimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _length;
        private readonly HMAC _hmac;
        private long _bytesRemaining;

        public HmacLimitedReadStream(Stream inner, long bytesRemaining, HMAC hmac)
        {
            _inner = inner;
            _length = bytesRemaining;
            _bytesRemaining = bytesRemaining;
            _hmac = hmac;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _bytesRemaining; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_bytesRemaining <= 0)
            {
                return 0;
            }

            var read = _inner.Read(buffer, offset, (int)Math.Min(count, _bytesRemaining));
            if (read > 0)
            {
                _hmac.TransformBlock(buffer, offset, read, null, 0);
                _bytesRemaining -= read;
            }

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_bytesRemaining <= 0)
            {
                return 0;
            }

            var read = await _inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _bytesRemaining)], cancellationToken);
            if (read > 0)
            {
                var bytes = buffer[..read].ToArray();
                _hmac.TransformBlock(bytes, 0, bytes.Length, null, 0);
                _bytesRemaining -= read;
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
