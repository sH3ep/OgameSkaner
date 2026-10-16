namespace OgameSkaner.Core.Ogamex;

/// <summary>A request to OgameX failed or returned something unexpected.</summary>
public class OgamexException : Exception
{
    public OgamexException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

/// <summary>The session cookies are missing or no longer accepted by the server.</summary>
public sealed class SessionExpiredException : OgamexException
{
    public SessionExpiredException(string message) : base(message)
    {
    }
}
