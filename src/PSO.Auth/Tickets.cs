// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PSO.Auth;

public sealed class Tickets
{
    private const byte Version = 1;
    private const int GuidLength = 16;
    private const int IssuedAtLength = sizeof(long);
    private const int TtlLength = sizeof(int);
    private const int NonceLength = 16;
    private const int SignatureLength = 32;
    private static readonly TimeSpan DefaultTicketTtl = TimeSpan.FromSeconds(60);

    private readonly byte[] _secret;
    private readonly TimeSpan _defaultTtl;
    private readonly Func<DateTimeOffset> _clock;

    public Tickets(byte[] secret, TimeSpan? defaultTtl = null, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length < 32)
        {
            throw new ArgumentException("Ticket secret must be at least 32 bytes", nameof(secret));
        }

        _secret = secret.ToArray();
        _defaultTtl = defaultTtl ?? DefaultTicketTtl;
        if (_defaultTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultTtl), "Ticket TTL must be positive.");
        }

        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static Tickets FromEnvironment(TimeSpan? defaultTtl = null, Func<DateTimeOffset>? clock = null)
    {
        var secretValue = Environment.GetEnvironmentVariable("PCORE_TICKET_SECRET");
        if (string.IsNullOrWhiteSpace(secretValue))
        {
            throw new InvalidOperationException("PCORE_TICKET_SECRET is not configured.");
        }

        var secretBytes = DecodeSecret(secretValue);
        if (secretBytes.Length < 32)
        {
            throw new InvalidOperationException("PCORE_TICKET_SECRET must decode to at least 32 bytes.");
        }

        return new Tickets(secretBytes, defaultTtl, clock);
    }

    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid accountId, TimeSpan? ttl = null)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account id cannot be empty", nameof(accountId));
        }

        var ttlValue = ttl ?? _defaultTtl;
        if (ttlValue <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Ticket TTL must be positive.");
        }

        var ttlSecondsDouble = ttlValue.TotalSeconds;
        if (ttlSecondsDouble > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Ticket TTL too large.");
        }

        var ttlSeconds = (int)Math.Max(1, Math.Ceiling(ttlSecondsDouble));
        var issuedAt = _clock();
        var issuedAtSeconds = issuedAt.ToUnixTimeSeconds();
        var issuedAtNormalized = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);

        Span<byte> payload = stackalloc byte[1 + GuidLength + IssuedAtLength + TtlLength + NonceLength + SignatureLength];
        payload[0] = Version;
        var span = payload.Slice(1);
        if (!accountId.TryWriteBytes(span.Slice(0, GuidLength)))
        {
            throw new InvalidOperationException("Failed to encode account id");
        }

        BinaryPrimitives.WriteInt64BigEndian(span.Slice(GuidLength, IssuedAtLength), issuedAtSeconds);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(GuidLength + IssuedAtLength, TtlLength), ttlSeconds);

        RandomNumberGenerator.Fill(span.Slice(GuidLength + IssuedAtLength + TtlLength, NonceLength));

        using var hmac = new HMACSHA256(_secret);
        Span<byte> signature = stackalloc byte[SignatureLength];
        if (!hmac.TryComputeHash(payload.Slice(1, GuidLength + IssuedAtLength + TtlLength + NonceLength), signature, out var bytesWritten) || bytesWritten != SignatureLength)
        {
            throw new InvalidOperationException("Failed to compute ticket signature");
        }

        signature.CopyTo(span.Slice(GuidLength + IssuedAtLength + TtlLength + NonceLength, SignatureLength));

        var token = Base64UrlEncode(payload);
        var expiresAt = issuedAtNormalized.AddSeconds(ttlSeconds);
        return (token, expiresAt);
    }

    public bool TryValidate(string token, out Guid accountId)
    {
        accountId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (!TryBase64UrlDecode(token, out var buffer))
        {
            return false;
        }

        if (buffer.Length != 1 + GuidLength + IssuedAtLength + TtlLength + NonceLength + SignatureLength)
        {
            return false;
        }

        if (buffer[0] != Version)
        {
            return false;
        }

        var payload = buffer.AsSpan();
        var span = payload.Slice(1);
        var dataLength = GuidLength + IssuedAtLength + TtlLength + NonceLength;
        var data = span.Slice(0, dataLength);
        var signature = span.Slice(dataLength, SignatureLength);

        using var hmac = new HMACSHA256(_secret);
        Span<byte> expectedSignature = stackalloc byte[SignatureLength];
        if (!hmac.TryComputeHash(data, expectedSignature, out _))
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
        {
            return false;
        }

        var issuedAtSeconds = BinaryPrimitives.ReadInt64BigEndian(span.Slice(GuidLength, IssuedAtLength));
        var ttlSeconds = BinaryPrimitives.ReadInt32BigEndian(span.Slice(GuidLength + IssuedAtLength, TtlLength));
        if (ttlSeconds <= 0)
        {
            return false;
        }

        DateTimeOffset issuedAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var expiresAt = issuedAt.AddSeconds(ttlSeconds);
        if (_clock() > expiresAt)
        {
            return false;
        }

        accountId = new Guid(span.Slice(0, GuidLength));
        return true;
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        var base64 = Convert.ToBase64String(data);
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static bool TryBase64UrlDecode(string token, out byte[] buffer)
    {
        var paddedLength = token.Length % 4;
        var builder = new StringBuilder(token.Length + (paddedLength == 0 ? 0 : 4 - paddedLength));
        builder.Append(token.Replace('-', '+').Replace('_', '/'));

        if (paddedLength != 0)
        {
            builder.Append('=', 4 - paddedLength);
        }

        try
        {
            buffer = Convert.FromBase64String(builder.ToString());
            return true;
        }
        catch (FormatException)
        {
            buffer = Array.Empty<byte>();
            return false;
        }
    }

    private static byte[] DecodeSecret(string secretValue)
    {
        Span<byte> buffer = stackalloc byte[(secretValue.Length * 3) / 4 + 4];
        if (Convert.TryFromBase64String(secretValue, buffer, out var bytesWritten))
        {
            return buffer.Slice(0, bytesWritten).ToArray();
        }

        return Encoding.UTF8.GetBytes(secretValue);
    }
}
