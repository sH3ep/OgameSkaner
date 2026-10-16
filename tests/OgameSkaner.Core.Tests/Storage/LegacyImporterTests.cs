using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Storage.Legacy;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Storage;

public class LegacyImporterTests
{
    [Fact]
    public void Decrypts_value_written_by_the_old_app()
    {
        // Placeholder token "token" as stored by the old app's default configuration.
        Assert.Equal("token", LegacyTokenDecryptor.TryDecrypt("xdYSHbC6r0nBMwWHwumQUg=="));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    public void Decrypt_returns_null_for_invalid_input(string? cipherText)
    {
        Assert.Null(LegacyTokenDecryptor.TryDecrypt(cipherText));
    }

    [Fact]
    public async Task Imports_only_ogamex_universes_with_session_and_planets()
    {
        using var legacy = new TempDirectory();
        using var data = new TempDirectory();
        WriteLegacyFiles(legacy.Path);

        var paths = new AppPaths(data.Path);
        var profiles = new JsonProfileStore(paths);
        var sessions = new JsonSessionStore(paths, new FakeSecretProtector());
        var planets = new JsonPlanetRepository(paths);
        var importer = new LegacyImporter(profiles, sessions, planets, NullLogger<LegacyImporter>.Instance);

        var result = await importer.ImportAsync(legacy.Path);

        Assert.Equal(["tron", "orion"], result.ImportedUniverses);
        Assert.Equal(["tron", "orion"], profiles.GetAll().Select(p => p.Name).OrderDescending());
        Assert.Equal(new OgamexSession("real-token", "session-1"), sessions.Get("tron"));
        Assert.Null(sessions.Get("orion")); // placeholder token is not imported

        var tron = (await planets.GetAsync("tron")).Snapshot();
        Assert.Equal(2, result.ImportedPlanets);
        Assert.Equal(
        [
            new PlayerPlanet("Admin", new Coordinates(1, 1, 1), false, DateTimeOffset.Parse("2023-10-05T19:15:06+02:00")),
            new PlayerPlanet("Bartolo", new Coordinates(1, 1, 2), true, DateTimeOffset.Parse("2023-10-05T19:15:06+02:00"))
        ], tron);
    }

    [Fact]
    public async Task Second_import_skips_existing_universes()
    {
        using var legacy = new TempDirectory();
        using var data = new TempDirectory();
        WriteLegacyFiles(legacy.Path);

        var paths = new AppPaths(data.Path);
        var importer = new LegacyImporter(
            new JsonProfileStore(paths),
            new JsonSessionStore(paths, new FakeSecretProtector()),
            new JsonPlanetRepository(paths),
            NullLogger<LegacyImporter>.Instance);

        await importer.ImportAsync(legacy.Path);
        var second = await importer.ImportAsync(legacy.Path);

        Assert.Empty(second.ImportedUniverses);
        Assert.Equal(["tron", "orion"], second.SkippedUniverses);
    }

    private static void WriteLegacyFiles(string directory)
    {
        File.WriteAllText(Path.Combine(directory, LegacyImporter.ConfigFileName), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfGameConfigurationModel xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <GameConfigurationModel>
                <GameType>Sgame</GameType>
                <Universum>2</Universum>
                <EncryptedToken>{LegacyEncrypt("sgame-token")}</EncryptedToken>
              </GameConfigurationModel>
              <GameConfigurationModel>
                <GameType>OgameX</GameType>
                <Universum>tron</Universum>
                <EncryptedToken>{LegacyEncrypt("real-token")}</EncryptedToken>
                <SessionId>session-1</SessionId>
              </GameConfigurationModel>
              <GameConfigurationModel>
                <GameType>OgameX</GameType>
                <Universum>orion</Universum>
                <EncryptedToken>{LegacyEncrypt("token")}</EncryptedToken>
              </GameConfigurationModel>
            </ArrayOfGameConfigurationModel>
            """);

        File.WriteAllText(Path.Combine(directory, "DatabaseOgameXtron.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfUserPlanet xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <UserPlanet>
                <PlanetId>0</PlanetId>
                <HasMoon>false</HasMoon>
                <CreationDate>2023-10-05T19:15:06+02:00</CreationDate>
                <Galaxy>1</Galaxy>
                <SolarSystem>1</SolarSystem>
                <Position>1</Position>
                <UserName>Admin</UserName>
                <ToDelete>false</ToDelete>
                <Localization>1:1:1</Localization>
              </UserPlanet>
              <UserPlanet>
                <HasMoon>true</HasMoon>
                <CreationDate>2023-10-05T19:15:06+02:00</CreationDate>
                <Galaxy>1</Galaxy>
                <SolarSystem>1</SolarSystem>
                <Position>2</Position>
                <UserName>Bartolo</UserName>
              </UserPlanet>
              <UserPlanet>
                <Galaxy>1</Galaxy>
                <SolarSystem>1</SolarSystem>
                <Position>0</Position>
                <UserName>InvalidPosition</UserName>
              </UserPlanet>
            </ArrayOfUserPlanet>
            """);
    }

    /// <summary>The encryption used by the old app (EncryptionHelper.Encrypt).</summary>
    private static string LegacyEncrypt(string clearText)
    {
        var key = Rfc2898DeriveBytes.Pbkdf2("abc123", new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 }, 1000, HashAlgorithmName.SHA1, 48);
        using var aes = Aes.Create();
        aes.Key = key[..32];
        return Convert.ToBase64String(aes.EncryptCbc(Encoding.Unicode.GetBytes(clearText), key[32..]));
    }
}
