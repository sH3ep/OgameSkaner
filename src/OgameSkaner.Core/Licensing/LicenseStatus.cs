namespace OgameSkaner.Core.Licensing;

public enum LicenseState
{
    /// <summary>No token stored: the app isn't activated.</summary>
    Missing,

    Valid,

    /// <summary>Not a token, or its signature doesn't match the app's public key.</summary>
    Invalid,

    /// <summary>Issued for another PC.</summary>
    WrongMachine,

    /// <summary>The PC clock is earlier than the token's issue time.</summary>
    ClockTurnedBack,

    Expired,

    /// <summary>The license server requires a newer build.</summary>
    Outdated
}

/// <summary>The license as the app sees it now.</summary>
/// <param name="Payload">The token's content once its signature is verified; null for <see cref="LicenseState.Missing"/> and <see cref="LicenseState.Invalid"/>.</param>
public sealed record LicenseStatus(LicenseState State, LicensePayload? Payload = null)
{
    public bool IsValid => State == LicenseState.Valid;

    /// <summary>For the sidebar and the activation window.</summary>
    public string Text => State switch
    {
        LicenseState.Missing => "Not activated",
        LicenseState.Valid => $"Licensed until {Payload!.ExpiresAt.ToLocalTime():g}",
        LicenseState.Invalid => "The license token is not valid",
        LicenseState.WrongMachine => "The license was issued for another computer",
        LicenseState.ClockTurnedBack => "The computer's clock is behind the license server's; correct the clock",
        LicenseState.Expired => $"The license expired on {Payload!.ExpiresAt.ToLocalTime():g}",
        LicenseState.Outdated => $"This version is too old for the license; update to {Payload!.MinAppVersion} or newer",
        _ => State.ToString()
    };
}

/// <summary>An action was refused because the license doesn't allow it (missing, expired, another account, …).</summary>
public sealed class LicenseException(string message) : Exception(message)
{
    public static LicenseException For(LicenseStatus status) =>
        new(status.State == LicenseState.Missing ? "OgameSkaner is not activated. Activate it with your license key." : status.Text + ".");
}

/// <summary>What every license check verifies on its own: the stored token and the facts it must match.</summary>
/// <param name="PublicKey">The license server's public key (SubjectPublicKeyInfo).</param>
public sealed record LicenseEvidence(string? Token, string Machine, byte[] PublicKey, Version AppVersion, DateTimeOffset Now);
