using System.Security.Cryptography;
using System.Text;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.Infrastructure;

/// <summary>Protects secrets with Windows DPAPI, readable only by the current Windows user.</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "OgameSkaner.Session"u8.ToArray();

    public string Protect(string plainText)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public string Unprotect(string protectedText)
    {
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
