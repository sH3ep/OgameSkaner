using System.Text.Json.Serialization;

namespace OgameSkaner.Core.Licensing;

/// <summary>A game account a license covers: the OgameX player id (the GUID the game pages show) on one universe.</summary>
public sealed record LicensedAccount(string Universe, string PlayerId)
{
    public bool Matches(string universe, string playerId) =>
        string.Equals(Universe, universe, StringComparison.OrdinalIgnoreCase)
        && string.Equals(PlayerId, playerId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a license token says; the license server signs it, the app only reads it.</summary>
/// <param name="LicenseId">The license (subscription) the token belongs to.</param>
/// <param name="Machine">The <see cref="IMachineFingerprint"/> of the PC the token was issued for.</param>
/// <param name="Accounts">The game accounts bound to the license so far.</param>
/// <param name="MaxAccounts">How many accounts the license may bind; 0 = any account (no binding).</param>
/// <param name="IssuedAt">Server time of issue; a PC clock earlier than this means the clock was turned back.</param>
/// <param name="ExpiresAt">A few days after issue, so a short server outage doesn't lock anyone out.</param>
/// <param name="MinAppVersion">Older builds are refused (after a crack, ship an update); null = any.</param>
public sealed record LicensePayload(
    string LicenseId,
    string Machine,
    IReadOnlyList<LicensedAccount> Accounts,
    int MaxAccounts,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string? MinAppVersion = null,
    string? Customer = null,
    string? Plan = null)
{
    public bool Covers(string universe, string playerId) => MaxAccounts == 0 || Accounts.Any(a => a.Matches(universe, playerId));

    /// <summary>Whether another account can still be bound (by the server).</summary>
    [JsonIgnore]
    public bool HasFreeAccount => MaxAccounts > 0 && Accounts.Count < MaxAccounts;
}
