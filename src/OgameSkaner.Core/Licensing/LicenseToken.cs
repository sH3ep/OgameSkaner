using System.Security.Cryptography;
using System.Text.Json;

namespace OgameSkaner.Core.Licensing;

/// <summary>
/// The token format: <c>OSL1.&lt;payload&gt;.&lt;signature&gt;</c>, both parts base64url. The payload is the UTF-8 JSON of a
/// <see cref="LicensePayload"/> (camelCase), the signature ECDSA P-256 with SHA-256 over the payload bytes, in the
/// IEEE P1363 format (r‖s, 64 bytes).
/// </summary>
public static class LicenseToken
{
    public const string Prefix = "OSL1.";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Signs <paramref name="payload"/>; only the license server (and the tests) hold a private key.</summary>
    public static string Sign(LicensePayload payload, ECDsa privateKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var signature = privateKey.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Prefix + Base64Url(bytes) + "." + Base64Url(signature);
    }

    /// <summary>Splits a token into its payload bytes and signature; false when it isn't one (nothing is verified here).</summary>
    public static bool TrySplit(string? token, out byte[] payload, out byte[] signature)
    {
        payload = signature = [];
        token = token?.Trim();
        if (token is null || !token.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = token[Prefix.Length..].Split('.');
        return parts.Length == 2 && TryFromBase64Url(parts[0], out payload) && TryFromBase64Url(parts[1], out signature);
    }

    /// <summary>Reads the payload bytes; null when they aren't a payload. Verify the signature first.</summary>
    public static LicensePayload? ReadPayload(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize<LicensePayload>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The public key as the app embeds it (SubjectPublicKeyInfo, base64).</summary>
    public static string ExportPublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryFromBase64Url(string text, out byte[] bytes)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
