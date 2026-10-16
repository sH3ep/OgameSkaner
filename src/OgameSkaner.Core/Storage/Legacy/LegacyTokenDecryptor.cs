using System.Security.Cryptography;
using System.Text;

namespace OgameSkaner.Core.Storage.Legacy;

/// <summary>
/// Decrypts tokens written by the pre-2026 versions of the app (AES with a key derived from a
/// hard-coded password). Only used to migrate old data; new data is protected with DPAPI.
/// </summary>
internal static class LegacyTokenDecryptor
{
    private const string Password = "abc123";
    private static readonly byte[] Salt = [0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76];

    public static string? TryDecrypt(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return null;

        try
        {
            // Rfc2898DeriveBytes(password, salt) defaults: 1000 iterations, SHA1; key = first 32 bytes, IV = next 16.
            var keyMaterial = Rfc2898DeriveBytes.Pbkdf2(Password, Salt, 1000, HashAlgorithmName.SHA1, 48);
            using var aes = Aes.Create();
            aes.Key = keyMaterial[..32];
            aes.IV = keyMaterial[32..];

            var cipherBytes = Convert.FromBase64String(cipherText.Replace(' ', '+'));
            var plainBytes = aes.DecryptCbc(cipherBytes, aes.IV);
            return Encoding.Unicode.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
