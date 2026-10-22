using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OgameSkaner.Core.Licensing;

/// <summary>Identifies this PC for the license (the App hashes the Windows machine id); stable across restarts.</summary>
public interface IMachineFingerprint
{
    string Id { get; }
}

/// <summary>
/// The app's license: activation with a key (the server returns a signed token bound to this PC), renewal, offline
/// import of a token, the terms, and the game accounts the license covers. Checks elsewhere verify
/// <see cref="Evidence"/> on their own (<see cref="LicenseChecks"/>); <see cref="Status"/> is for display.
/// </summary>
public interface ILicenseService
{
    /// <summary>The license now (verified on every read, so an expiry shows without an event).</summary>
    LicenseStatus Status { get; }

    LicenseEvidence Evidence { get; }

    /// <summary>The fingerprint the token must name; shown in the activation window for an offline token.</summary>
    string MachineId { get; }

    /// <summary>Whether a license server is configured (activation with a key, renewal, binding accounts).</summary>
    bool CanActivateOnline { get; }

    bool TermsAccepted { get; }

    /// <summary>Raised (on any thread) when the stored token or the terms changed.</summary>
    event EventHandler? Changed;

    void AcceptTerms();

    /// <exception cref="LicenseException">Refused by the server, unreachable, or the token doesn't fit this PC/version.</exception>
    Task<LicenseStatus> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="token"/> when it's valid here (pasted by hand, without a server).</summary>
    LicenseStatus Import(string token);

    /// <summary>Asks the server for a fresh token; without a server or key the status stays as it is.</summary>
    /// <exception cref="LicenseException">Refused or unreachable.</exception>
    Task<LicenseStatus> RenewAsync(CancellationToken cancellationToken = default);

    /// <summary>Renews now and every <see cref="LicenseOptions.RenewEvery"/> until cancelled; failures are logged.</summary>
    Task KeepRenewedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Passes when the license covers the game account (binding it on the server while the license has a free one).
    /// </summary>
    /// <exception cref="LicenseException">Not covered, or the license isn't valid.</exception>
    Task ConfirmAccountAsync(string universe, string playerId, CancellationToken cancellationToken = default);
}

public sealed class LicenseService : ILicenseService
{
    private readonly ILicenseStore _store;
    private readonly ILicenseServer _server;
    private readonly IMachineFingerprint _machine;
    private readonly LicenseOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly byte[] _publicKey;
    private readonly Version _appVersion;
    private readonly object _sync = new();
    private StoredLicense _stored;

    public LicenseService(
        ILicenseStore store,
        ILicenseServer server,
        IMachineFingerprint machine,
        IOptions<LicenseOptions> options,
        TimeProvider timeProvider,
        ILogger<LicenseService> logger)
        : this(store, server, machine, options.Value, timeProvider, logger, LicenseKeys.Production,
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0))
    {
    }

    /// <summary>For tests: tokens signed with a test key, a fixed app version.</summary>
    internal LicenseService(
        ILicenseStore store,
        ILicenseServer server,
        IMachineFingerprint machine,
        LicenseOptions options,
        TimeProvider timeProvider,
        ILogger logger,
        byte[] publicKey,
        Version appVersion)
    {
        _store = store;
        _server = server;
        _machine = machine;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _publicKey = publicKey;
        _appVersion = appVersion;
        _stored = store.Load();
    }

    public event EventHandler? Changed;

    public LicenseStatus Status => LicenseVerifier.Verify(Evidence);

    public LicenseEvidence Evidence
    {
        get
        {
            lock (_sync)
                return EvidenceFor(_stored.Token);
        }
    }

    public string MachineId => _machine.Id;

    public bool CanActivateOnline => _server.IsConfigured;

    public bool TermsAccepted
    {
        get
        {
            lock (_sync)
                return _stored.TermsVersion >= LicenseTerms.Version;
        }
    }

    public void AcceptTerms() => Update(s => s with { TermsVersion = LicenseTerms.Version, TermsAcceptedAt = _timeProvider.GetUtcNow() });

    public async Task<LicenseStatus> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        var key = licenseKey.Trim();
        if (key.Length == 0)
            throw new LicenseException("Enter the license key.");

        var token = await RequestAsync(new LicenseTokenRequest(key, MachineId, _appVersion.ToString()), cancellationToken).ConfigureAwait(false);
        var status = LicenseVerifier.Verify(EvidenceFor(token));
        if (!status.IsValid)
            throw LicenseException.For(status);

        Update(s => s with { LicenseKey = key, Token = token });
        _logger.LogInformation("License {License} activated until {Expires}", status.Payload!.LicenseId, status.Payload.ExpiresAt);
        return status;
    }

    public LicenseStatus Import(string token)
    {
        token = token.Trim();
        var status = LicenseVerifier.Verify(EvidenceFor(token));
        if (!status.IsValid)
            return status;

        // The key renews only its own license.
        var sameLicense = LicenseVerifier.Verify(Evidence).Payload?.LicenseId == status.Payload!.LicenseId;
        Update(s => s with { Token = token, LicenseKey = sameLicense ? s.LicenseKey : null });
        _logger.LogInformation("License token {License} imported, valid until {Expires}", status.Payload.LicenseId, status.Payload.ExpiresAt);
        return status;
    }

    public async Task<LicenseStatus> RenewAsync(CancellationToken cancellationToken = default)
    {
        string? key;
        lock (_sync)
            key = _stored.LicenseKey;
        if (key is null || !_server.IsConfigured)
            return Status;

        var token = await RequestAsync(new LicenseTokenRequest(key, MachineId, _appVersion.ToString()), cancellationToken).ConfigureAwait(false);
        var status = LicenseVerifier.Verify(EvidenceFor(token));
        if (status.IsValid)
            Update(s => s with { Token = token });
        else
            _logger.LogWarning("The license server sent a token that isn't valid here: {State}", status.State);
        return Status;
    }

    public async Task KeepRenewedAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var wait = _options.RenewEvery;
            try
            {
                await RenewAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (LicenseException ex)
            {
                _logger.LogWarning("License renewal failed: {Error}", ex.Message);
                wait = _options.RetryAfter;
            }

            try
            {
                await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task ConfirmAccountAsync(string universe, string playerId, CancellationToken cancellationToken = default)
    {
        var payload = LicenseChecks.DemandC(this);
        if (payload.Covers(universe, playerId))
            return;

        string? key;
        lock (_sync)
            key = _stored.LicenseKey;
        if (payload.HasFreeAccount && key is not null && _server.IsConfigured)
        {
            var account = new LicensedAccount(universe, playerId);
            var token = await RequestAsync(new LicenseTokenRequest(key, MachineId, _appVersion.ToString(), account), cancellationToken).ConfigureAwait(false);
            var status = LicenseVerifier.Verify(EvidenceFor(token));
            if (status.IsValid)
            {
                Update(s => s with { Token = token });
                if (status.Payload!.Covers(universe, playerId))
                {
                    _logger.LogInformation("Game account {Player} on {Universe} bound to license {License}", playerId, universe, payload.LicenseId);
                    return;
                }
            }
        }

        throw new LicenseException(
            $"This license doesn't cover the game account {playerId} on {universe}"
            + (payload.HasFreeAccount ? "." : $": all {payload.MaxAccounts} of its accounts are taken."));
    }

    private LicenseEvidence EvidenceFor(string? token) => new(token, _machine.Id, _publicKey, _appVersion, _timeProvider.GetUtcNow());

    private async Task<string> RequestAsync(LicenseTokenRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _server.RequestTokenAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (LicenseServerException ex)
        {
            throw new LicenseException(ex.Message);
        }
    }

    private void Update(Func<StoredLicense, StoredLicense> change)
    {
        lock (_sync)
        {
            _stored = change(_stored);
            _store.Save(_stored);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
