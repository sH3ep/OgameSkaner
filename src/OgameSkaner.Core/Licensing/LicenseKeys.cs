namespace OgameSkaner.Core.Licensing;

/// <summary>
/// The license server's public key; only tokens signed with its private key are accepted. The private key never goes
/// into the repo or the app (made with <c>ogs-license keygen</c>, see tools/OgameSkaner.LicenseTool).
/// </summary>
internal static class LicenseKeys
{
    public static byte[] Production => Convert.FromBase64String(
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEAKnTyHRB0O5AKNgZVQipa9+hnbb35Rn5N+NU97i/lz1MiCSVvx5v+nA/p6bLHqjKQBzgi5VmavW7x+5RrdSlqQ==");
}
