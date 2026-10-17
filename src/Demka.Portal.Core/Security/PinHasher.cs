using System.Security.Cryptography;

namespace Demka.Portal.Core.Security;

/// <summary>
/// Hashes PINs with PBKDF2-SHA256 and a random salt. Stored as "v1.{iterations}.{salt}.{hash}" (Base64).
/// A PIN is never stored or logged in plain text.
/// </summary>
public static class PinHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    public const int DefaultIterations = 210_000;

    public static string Hash(string pin, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"v1.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>A random 6-digit PIN for the admin to hand to a user (who must change it at first sign-in).</summary>
    public static string GenerateTemporaryPin()
    {
        string pin;
        do pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        while (PinPolicy.Check(pin) is not null);
        return pin;
    }
}

/// <summary>Rules for PINs users choose.</summary>
public static class PinPolicy
{
    public const int MinLength = 4;
    public const int MaxLength = 6;

    /// <summary>Returns why the PIN is not allowed, or null when it is fine.</summary>
    public static string? Check(string? pin)
    {
        if (string.IsNullOrEmpty(pin) || pin.Length < MinLength || pin.Length > MaxLength || !pin.All(char.IsAsciiDigit))
            return $"Use {MinLength} to {MaxLength} digits.";
        if (pin.Distinct().Count() == 1)
            return "Don't use the same digit for every number.";
        if (IsRun(pin, +1) || IsRun(pin, -1))
            return "Don't use a sequence like 1234 or 4321.";
        return null;
    }

    private static bool IsRun(string pin, int step)
    {
        for (var i = 1; i < pin.Length; i++)
            if (pin[i] - pin[i - 1] != step) return false;
        return true;
    }
}

/// <summary>Kenyan mobile numbers, stored as +254 followed by 9 digits.</summary>
public static class PhoneNumber
{
    /// <summary>
    /// Accepts 0712 345 678, 712345678, 254712345678, +254 712 345 678 and the 01xx ranges.
    /// Returns null when the input is not a Kenyan mobile number.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("254")) digits = digits[3..];
        else if (digits.StartsWith('0')) digits = digits[1..];
        if (digits.Length != 9 || (digits[0] != '7' && digits[0] != '1')) return null;
        return "+254" + digits;
    }

    /// <summary>Shows only the last three digits, e.g. "07•• ••• 678".</summary>
    public static string Mask(string? normalized) =>
        normalized is { Length: 13 } ? $"0{normalized[4]}•• ••• {normalized[^3..]}" : "not set";
}
