using System.Security.Cryptography;
using System.Text.Json;

namespace OgameSkaner.Core.Licensing;

/// <summary>
/// The checks spread over the app (client factory, account binding, action queue, optimizer, raids). Each verifies the
/// token's signature and limits itself, each with its own code, so patching one check (or <see cref="LicenseVerifier"/>)
/// doesn't disable the others. They throw <see cref="LicenseException"/> with the same honest message as the sidebar.
/// </summary>
/// <remarks>Keep the variants different; don't "deduplicate" them into one helper.</remarks>
internal static class LicenseChecks
{
    /// <summary>Signature over the payload bytes, limits read from a <see cref="JsonDocument"/>.</summary>
    public static void DemandA(ILicenseService license)
    {
        var evidence = license.Evidence;
        if (LicenseToken.TrySplit(evidence.Token, out var payload, out var signature))
        {
            using var key = ECDsa.Create();
            try
            {
                key.ImportSubjectPublicKeyInfo(evidence.PublicKey, out _);
                if (key.VerifyData(payload.AsSpan(), signature.AsSpan(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                {
                    using var json = JsonDocument.Parse(payload);
                    var root = json.RootElement;
                    if (root.GetProperty("machine").GetString() == evidence.Machine
                        && root.GetProperty("expiresAt").GetDateTimeOffset() > evidence.Now
                        && root.GetProperty("issuedAt").GetDateTimeOffset() - LicenseVerifier.ClockTolerance <= evidence.Now
                        && !LicenseVerifier.IsOutdated(root.TryGetProperty("minAppVersion", out var min) ? min.GetString() : null, evidence.AppVersion))
                        return;
                }
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
            }
        }

        throw LicenseException.For(LicenseVerifier.Verify(evidence));
    }

    /// <summary>The SHA-256 hash computed here and verified with <see cref="ECDsa.VerifyHash(byte[], byte[])"/>, limits from the record.</summary>
    public static void DemandB(ILicenseService license)
    {
        var evidence = license.Evidence;
        var ok = false;
        if (LicenseToken.TrySplit(evidence.Token, out var payload, out var signature) && signature.Length == 64)
        {
            var hash = SHA256.HashData(payload);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            try
            {
                key.ImportSubjectPublicKeyInfo(evidence.PublicKey, out _);
                ok = key.VerifyHash(hash, signature)
                     && LicenseToken.ReadPayload(payload) is { } read
                     && evidence.Now < read.ExpiresAt
                     && evidence.Now >= read.IssuedAt.Subtract(LicenseVerifier.ClockTolerance)
                     && string.Equals(read.Machine, evidence.Machine, StringComparison.Ordinal)
                     && !LicenseVerifier.IsOutdated(read.MinAppVersion, evidence.AppVersion);
            }
            catch (CryptographicException)
            {
                ok = false;
            }
        }

        if (!ok)
            throw LicenseException.For(LicenseVerifier.Verify(evidence));
    }

    /// <summary>The key rebuilt from its curve point, the payload streamed; returns it for the account check.</summary>
    public static LicensePayload DemandC(ILicenseService license)
    {
        var evidence = license.Evidence;
        LicensePayload? accepted = null;
        if (LicenseToken.TrySplit(evidence.Token, out var payload, out var signature))
        {
            try
            {
                using var imported = ECDsa.Create();
                imported.ImportSubjectPublicKeyInfo(evidence.PublicKey, out _);
                using var key = ECDsa.Create(imported.ExportParameters(includePrivateParameters: false));
                using var stream = new MemoryStream(payload, writable: false);
                if (key.VerifyData(stream, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                    && LicenseToken.ReadPayload(payload) is { } read
                    && read.Machine == evidence.Machine
                    && read.ExpiresAt.UtcTicks > evidence.Now.UtcTicks
                    && read.IssuedAt.UtcTicks - LicenseVerifier.ClockTolerance.Ticks <= evidence.Now.UtcTicks
                    && !LicenseVerifier.IsOutdated(read.MinAppVersion, evidence.AppVersion))
                    accepted = read;
            }
            catch (CryptographicException)
            {
            }
        }

        return accepted ?? throw LicenseException.For(LicenseVerifier.Verify(evidence));
    }
}
