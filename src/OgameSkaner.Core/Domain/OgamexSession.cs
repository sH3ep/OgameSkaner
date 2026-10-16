namespace OgameSkaner.Core.Domain;

/// <summary>Browser session cookies used to authenticate against OgameX.</summary>
public sealed record OgamexSession(string AuthToken, string SessionId)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(AuthToken) && !string.IsNullOrWhiteSpace(SessionId);
}
