using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage;

/// <summary>Encrypts and decrypts secrets at rest (DPAPI on Windows).</summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}

/// <summary>Session cookies per universe.</summary>
public interface ISessionStore
{
    OgamexSession? Get(string universe);

    void Save(string universe, OgamexSession session);

    void Delete(string universe);
}

public sealed class JsonSessionStore(IAppPaths paths, ISecretProtector protector) : ISessionStore
{
    private readonly object _sync = new();
    private Dictionary<string, StoredSession>? _sessions;

    public OgamexSession? Get(string universe)
    {
        lock (_sync)
        {
            if (!Load().TryGetValue(universe, out var stored))
                return null;

            try
            {
                return new OgamexSession(protector.Unprotect(stored.AuthToken), protector.Unprotect(stored.SessionId));
            }
            catch (Exception)
            {
                // Data protected by another Windows user/machine cannot be read; treat as no session.
                return null;
            }
        }
    }

    public void Save(string universe, OgamexSession session)
    {
        lock (_sync)
        {
            Load()[universe] = new StoredSession(protector.Protect(session.AuthToken), protector.Protect(session.SessionId));
            JsonFile.Write(paths.SessionsFile, _sessions);
        }
    }

    public void Delete(string universe)
    {
        lock (_sync)
        {
            if (Load().Remove(universe))
                JsonFile.Write(paths.SessionsFile, _sessions);
        }
    }

    private Dictionary<string, StoredSession> Load() =>
        _sessions ??= JsonFile.Read<Dictionary<string, StoredSession>>(paths.SessionsFile) ?? [];

    private sealed record StoredSession(string AuthToken, string SessionId);
}
