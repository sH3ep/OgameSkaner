using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Licensing;

namespace OgameSkaner.Core.Tests.Fakes;

/// <summary>Licenses signed with a key made for the test run, so the checks pass (or fail) like with the real one.</summary>
public static class TestLicense
{
    public const string Machine = "TEST-MACHINE";

    public static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static readonly Version AppVersion = new(1, 2, 0);

    /// <summary>Valid for any game account for a year.</summary>
    public static ILicenseService Valid { get; } = Service(Token(Payload()));

    public static LicensePayload Payload(
        DateTimeOffset? issuedAt = null, TimeSpan? lifetime = null, string machine = Machine, int maxAccounts = 0,
        IReadOnlyList<LicensedAccount>? accounts = null, string? minAppVersion = null)
    {
        var issued = issuedAt ?? DateTimeOffset.UtcNow.AddMinutes(-1);
        return new LicensePayload("LIC-1", machine, accounts ?? [], maxAccounts, issued, issued + (lifetime ?? TimeSpan.FromDays(365)), minAppVersion);
    }

    public static string Token(LicensePayload payload, ECDsa? key = null) => LicenseToken.Sign(payload, key ?? Key);

    public static LicenseService Service(
        string? token = null, ILicenseServer? server = null, TimeProvider? clock = null, FakeLicenseStore? store = null, string? licenseKey = null) =>
        new(store ?? new FakeLicenseStore(new StoredLicense(licenseKey, token)), server ?? new FakeLicenseServer(), new FakeMachine(Machine),
            new LicenseOptions(), clock ?? TimeProvider.System, NullLogger.Instance, Key.ExportSubjectPublicKeyInfo(), AppVersion);
}

public sealed class FakeLicenseStore(StoredLicense? initial = null) : ILicenseStore
{
    public StoredLicense Stored { get; private set; } = initial ?? new StoredLicense();

    public int Saves { get; private set; }

    public StoredLicense Load() => Stored;

    public void Save(StoredLicense license)
    {
        Stored = license;
        Saves++;
    }
}

/// <summary>Answers each request with <see cref="Respond"/>; not configured while it's null.</summary>
public sealed class FakeLicenseServer : ILicenseServer
{
    public Func<LicenseTokenRequest, string>? Respond { get; set; }

    public List<LicenseTokenRequest> Requests { get; } = [];

    public bool IsConfigured => Respond is not null;

    public Task<string> RequestTokenAsync(LicenseTokenRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult((Respond ?? throw new LicenseServerException("No license server is configured."))(request));
    }
}

public sealed record FakeMachine(string Id) : IMachineFingerprint;
