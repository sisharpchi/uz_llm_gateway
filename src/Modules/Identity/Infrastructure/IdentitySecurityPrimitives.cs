using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using UZLLM.Modules.Identity.Contracts;

namespace UZLLM.Modules.Identity.Infrastructure;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int IterationCount = 600_000;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public string Hash(string password)
    {
        PasswordPolicy.Validate(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, IterationCount, HashAlgorithmName.SHA512, HashLength);
        return string.Join('$', "uzllm-pbkdf2-sha512", IterationCount, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        var fields = passwordHash.Split('$', StringSplitOptions.None);
        if (fields.Length != 4
            || !string.Equals(fields[0], "uzllm-pbkdf2-sha512", StringComparison.Ordinal)
            || !int.TryParse(fields[1], out var iterations)
            || iterations < IterationCount)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(fields[2]);
            var expected = Convert.FromBase64String(fields[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or CryptographicException)
        {
            return false;
        }
    }
}

public sealed class DataProtectionIdentitySecretProtector(IDataProtectionProvider dataProtectionProvider) : IIdentitySecretProtector
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("UZLLM.Identity.OperatorMfa.v1");

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return protector.Protect(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        return protector.Unprotect(protectedValue);
    }
}

public sealed class TotpAuthenticator : ITotpAuthenticator
{
    private const int SecretLength = 20;
    private const int TimeStepSeconds = 30;

    public string CreateSharedSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretLength));

    public string CreateCode(string sharedSecret, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedSecret);
        var counter = (ulong)(timestamp.ToUnixTimeSeconds() / TimeStepSeconds);
        Span<byte> counterBytes = stackalloc byte[sizeof(ulong)];
        for (var index = counterBytes.Length - 1; index >= 0; index--)
        {
            counterBytes[index] = (byte)(counter & 0xff);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(Base32.Decode(sharedSecret));
        var digest = hmac.ComputeHash(counterBytes.ToArray());
        var offset = digest[^1] & 0x0f;
        var value = ((digest[offset] & 0x7f) << 24)
            | (digest[offset + 1] << 16)
            | (digest[offset + 2] << 8)
            | digest[offset + 3];
        return (value % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool VerifyCode(string sharedSecret, string code, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || code.Any(character => !char.IsAsciiDigit(character)))
        {
            return false;
        }

        for (var offset = -1; offset <= 1; offset++)
        {
            var candidate = CreateCode(sharedSecret, timestamp.AddSeconds(offset * TimeStepSeconds));
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(code)))
            {
                return true;
            }
        }

        return false;
    }
}

internal static class PasswordPolicy
{
    public static void Validate(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length is < 12 or > 128)
        {
            throw new ArgumentException("Password must be between 12 and 128 characters.", nameof(password));
        }
    }
}

internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bitsLeft - 5)) & 0x1f]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1f]);
        }

        return builder.ToString();
    }

    public static byte[] Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var buffer = 0;
        var bitsLeft = 0;
        var bytes = new List<byte>();
        foreach (var character in value.Trim().ToUpperInvariant())
        {
            var index = Alphabet.IndexOf(character, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new FormatException("The shared secret is not valid Base32.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return [.. bytes];
    }
}
