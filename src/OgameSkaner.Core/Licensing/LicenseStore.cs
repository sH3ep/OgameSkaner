using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Licensing;

/// <summary>What the app keeps about its license.</summary>
/// <param name="LicenseKey">The key the player bought; the app renews the token with it. Null after an offline import.</param>
/// <param name="TermsVersion">The <see cref="LicenseTerms.Version"/> the player accepted; null = not yet.</param>
public sealed record StoredLicense(string? LicenseKey = null, string? Token = null, int? TermsVersion = null, DateTimeOffset? TermsAcceptedAt = null);

public interface ILicenseStore
{
    StoredLicense Load();

    void Save(StoredLicense license);
}

/// <summary><c>license.json</c> in the app folder; the key and the token are protected like the sessions (DPAPI).</summary>
public sealed class JsonLicenseStore(IAppPaths paths, ISecretProtector protector) : ILicenseStore
{
    private readonly object _sync = new();
    private string FilePath => Path.Combine(paths.Root, "license.json");

    public StoredLicense Load()
    {
        lock (_sync)
        {
            if (JsonFile.Read<StoredLicense>(FilePath) is not { } stored)
                return new StoredLicense();

            return stored with { LicenseKey = Unprotect(stored.LicenseKey), Token = Unprotect(stored.Token) };
        }
    }

    public void Save(StoredLicense license)
    {
        lock (_sync)
            JsonFile.Write(FilePath, license with { LicenseKey = Protect(license.LicenseKey), Token = Protect(license.Token) });
    }

    private string? Protect(string? value) => value is null ? null : protector.Protect(value);

    private string? Unprotect(string? value)
    {
        if (value is null)
            return null;
        try
        {
            return protector.Unprotect(value);
        }
        catch (Exception)
        {
            // Copied from another Windows user or PC: the license has to be activated again there.
            return null;
        }
    }
}
