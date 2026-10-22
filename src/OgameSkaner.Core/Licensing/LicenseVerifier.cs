using System.Security.Cryptography;

namespace OgameSkaner.Core.Licensing;

/// <summary>Verifies a token against the public key and the facts it must match (the reference for every check).</summary>
public static class LicenseVerifier
{
    /// <summary>How far the PC clock may be behind the server's before it counts as turned back.</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(10);

    public static LicenseStatus Verify(LicenseEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.Token))
            return new LicenseStatus(LicenseState.Missing);
        if (!LicenseToken.TrySplit(evidence.Token, out var bytes, out var signature) || !SignatureMatches(bytes, signature, evidence.PublicKey)
            || LicenseToken.ReadPayload(bytes) is not { } payload)
            return new LicenseStatus(LicenseState.Invalid);

        var state = payload switch
        {
            _ when !string.Equals(payload.Machine, evidence.Machine, StringComparison.Ordinal) => LicenseState.WrongMachine,
            _ when evidence.Now < payload.IssuedAt - ClockTolerance => LicenseState.ClockTurnedBack,
            _ when evidence.Now >= payload.ExpiresAt => LicenseState.Expired,
            _ when IsOutdated(payload.MinAppVersion, evidence.AppVersion) => LicenseState.Outdated,
            _ => LicenseState.Valid
        };
        return new LicenseStatus(state, payload);
    }

    internal static bool IsOutdated(string? minimum, Version appVersion) =>
        minimum is not null && (!Version.TryParse(minimum, out var required) || appVersion < required);

    private static bool SignatureMatches(byte[] payload, byte[] signature, byte[] publicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            return key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
