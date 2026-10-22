using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Licensing;
using OgameSkaner.Core.Ogamex.Parsing;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Licensing;

public class LicenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly LicensedAccount Account = new("genesis", "22222222-2222-2222-2222-222222222222");

    private static LicenseService At(string? token, DateTimeOffset? now = null, FakeLicenseServer? server = null, string? licenseKey = null, FakeLicenseStore? store = null) =>
        TestLicense.Service(token, server, new InstantTimeProvider(now ?? Now), store, licenseKey);

    /// <summary>Every check of the app, so a token one of them takes but another refuses shows up.</summary>
    private static readonly Action<ILicenseService>[] Checks =
    [
        LicenseChecks.DemandA,
        LicenseChecks.DemandB,
        license => LicenseChecks.DemandC(license)
    ];

    private static void AssertAllChecksPass(ILicenseService license)
    {
        foreach (var check in Checks)
            check(license);
    }

    private static void AssertAllChecksRefuse(ILicenseService license)
    {
        foreach (var check in Checks)
            Assert.Throws<LicenseException>(() => check(license));
    }

    [Fact]
    public void A_signed_token_for_this_machine_is_valid()
    {
        var license = At(TestLicense.Token(TestLicense.Payload(Now.AddHours(-1), TimeSpan.FromDays(3))));

        Assert.Equal(LicenseState.Valid, license.Status.State);
        Assert.Equal("LIC-1", license.Status.Payload!.LicenseId);
        AssertAllChecksPass(license);
    }

    [Fact]
    public void Without_a_token_the_app_is_not_activated()
    {
        var license = At(null);

        Assert.Equal(LicenseState.Missing, license.Status.State);
        AssertAllChecksRefuse(license);
        Assert.Contains("not activated", Assert.Throws<LicenseException>(() => LicenseChecks.DemandA(license)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_payload_breaks_the_signature()
    {
        var token = TestLicense.Token(TestLicense.Payload(Now.AddHours(-1), maxAccounts: 1));
        LicenseToken.TrySplit(token, out var payload, out var signature);
        var forged = System.Text.Encoding.UTF8.GetString(payload).Replace("\"maxAccounts\":1", "\"maxAccounts\":0", StringComparison.Ordinal);
        var license = At(LicenseToken.Prefix + B64(System.Text.Encoding.UTF8.GetBytes(forged)) + "." + B64(signature));

        Assert.Equal(LicenseState.Invalid, license.Status.State);
        AssertAllChecksRefuse(license);
    }

    [Fact]
    public void A_token_signed_with_another_key_is_invalid()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var license = At(TestLicense.Token(TestLicense.Payload(Now.AddHours(-1)), other));

        Assert.Equal(LicenseState.Invalid, license.Status.State);
        AssertAllChecksRefuse(license);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("OSL1.")]
    [InlineData("OSL1.abc.def")]
    [InlineData("OSL1.!!!.???")]
    public void Garbage_is_invalid(string token)
    {
        var license = At(token);

        Assert.Equal(LicenseState.Invalid, license.Status.State);
        AssertAllChecksRefuse(license);
    }

    [Fact]
    public void A_token_of_another_machine_is_refused()
    {
        var license = At(TestLicense.Token(TestLicense.Payload(Now.AddHours(-1), machine: "OTHER-PC")));

        Assert.Equal(LicenseState.WrongMachine, license.Status.State);
        AssertAllChecksRefuse(license);
    }

    [Fact]
    public void An_expired_token_is_refused()
    {
        var license = At(TestLicense.Token(TestLicense.Payload(Now.AddDays(-4), TimeSpan.FromDays(3))));

        Assert.Equal(LicenseState.Expired, license.Status.State);
        AssertAllChecksRefuse(license);
    }

    [Fact]
    public void A_clock_turned_back_before_the_issue_time_is_refused_but_a_little_skew_is_not()
    {
        var token = TestLicense.Token(TestLicense.Payload(Now, TimeSpan.FromDays(3)));

        var skewed = At(token, Now.AddMinutes(-5));
        Assert.Equal(LicenseState.Valid, skewed.Status.State);
        AssertAllChecksPass(skewed);

        var turnedBack = At(token, Now.AddDays(-1));
        Assert.Equal(LicenseState.ClockTurnedBack, turnedBack.Status.State);
        AssertAllChecksRefuse(turnedBack);
    }

    [Fact]
    public void An_older_app_than_the_minimum_version_is_refused()
    {
        var outdated = At(TestLicense.Token(TestLicense.Payload(Now.AddHours(-1), minAppVersion: "1.3")));
        Assert.Equal(LicenseState.Outdated, outdated.Status.State);
        AssertAllChecksRefuse(outdated);

        var current = At(TestLicense.Token(TestLicense.Payload(Now.AddHours(-1), minAppVersion: "1.2.0")));
        AssertAllChecksPass(current);
    }

    [Fact]
    public async Task The_action_queue_refuses_groups_without_a_license()
    {
        var queue = new ActionQueue(new FakeOgamexClient(new UniverseProfile("genesis")), ActionQueueSettings.Immediate, At(null), TimeProvider.System,
            NullLogger.Instance);
        var ran = false;

        await Assert.ThrowsAsync<LicenseException>(() => queue.RunAsync("Raids", "spy", _ => Task.FromResult(ran = true)));
        await Assert.ThrowsAsync<LicenseException>(() => queue.RunUrgentAsync("Defense", "escape", _ => Task.FromResult(ran = true)));
        Assert.False(ran);
    }

    [Fact]
    public async Task Activation_stores_the_key_and_the_servers_token()
    {
        var server = new FakeLicenseServer { Respond = _ => TestLicense.Token(TestLicense.Payload(Now.AddMinutes(-1), TimeSpan.FromDays(3))) };
        var store = new FakeLicenseStore();
        var license = At(null, server: server, store: store);

        var status = await license.ActivateAsync("  KEY-123 ");

        Assert.True(status.IsValid);
        Assert.Equal(new LicenseTokenRequest("KEY-123", TestLicense.Machine, "1.2.0"), Assert.Single(server.Requests));
        Assert.Equal("KEY-123", store.Stored.LicenseKey);
        Assert.True(license.Status.IsValid);
    }

    [Fact]
    public async Task Activation_with_a_token_that_does_not_fit_stores_nothing()
    {
        var server = new FakeLicenseServer { Respond = _ => TestLicense.Token(TestLicense.Payload(Now.AddMinutes(-1), machine: "OTHER-PC")) };
        var store = new FakeLicenseStore();
        var license = At(null, server: server, store: store);

        await Assert.ThrowsAsync<LicenseException>(() => license.ActivateAsync("KEY-123"));
        Assert.Equal(0, store.Saves);
        Assert.Equal(LicenseState.Missing, license.Status.State);
    }

    [Fact]
    public async Task A_refusal_of_the_server_comes_back_as_its_message()
    {
        var server = new FakeLicenseServer { Respond = _ => throw new LicenseServerException("The subscription was cancelled.") };
        var license = At(null, server: server);

        var error = await Assert.ThrowsAsync<LicenseException>(() => license.ActivateAsync("KEY-123"));
        Assert.Equal("The subscription was cancelled.", error.Message);
    }

    [Fact]
    public void Import_keeps_only_a_valid_token()
    {
        var store = new FakeLicenseStore();
        var license = At(null, store: store);

        Assert.Equal(LicenseState.WrongMachine, license.Import(TestLicense.Token(TestLicense.Payload(Now.AddMinutes(-1), machine: "OTHER-PC"))).State);
        Assert.Equal(0, store.Saves);

        Assert.True(license.Import(TestLicense.Token(TestLicense.Payload(Now.AddMinutes(-1)))).IsValid);
        Assert.True(license.Status.IsValid);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Renewal_replaces_the_token_with_the_servers_newer_one()
    {
        var old = TestLicense.Token(TestLicense.Payload(Now.AddDays(-2), TimeSpan.FromDays(3)));
        var server = new FakeLicenseServer { Respond = _ => TestLicense.Token(TestLicense.Payload(Now.AddMinutes(-1), TimeSpan.FromDays(3))) };
        var license = At(old, server: server, licenseKey: "KEY-123");

        var status = await license.RenewAsync();

        Assert.Equal(Now.AddMinutes(-1).AddDays(3), status.Payload!.ExpiresAt);
        Assert.Equal("KEY-123", Assert.Single(server.Requests).LicenseKey);
    }

    [Fact]
    public async Task Without_a_server_renewal_keeps_the_token()
    {
        var token = TestLicense.Token(TestLicense.Payload(Now.AddHours(-1)));
        var license = At(token, licenseKey: "KEY-123");

        Assert.True((await license.RenewAsync()).IsValid);
        Assert.Equal(token, license.Evidence.Token);
    }

    [Fact]
    public async Task A_bound_account_passes_and_another_one_is_refused_when_the_license_is_full()
    {
        var license = At(TestLicense.Token(TestLicense.Payload(Now.AddHours(-1), maxAccounts: 1, accounts: [Account])));

        await license.ConfirmAccountAsync("GENESIS", Account.PlayerId.ToUpperInvariant());
        var error = await Assert.ThrowsAsync<LicenseException>(() => license.ConfirmAccountAsync("genesis", "33333333-3333-3333-3333-333333333333"));
        Assert.Contains("all 1 of its accounts are taken", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_account_is_bound_on_the_server_while_the_license_has_a_free_one()
    {
        var server = new FakeLicenseServer
        {
            Respond = request => TestLicense.Token(TestLicense.Payload(Now.AddMinutes(-1), maxAccounts: 2, accounts: [Account, request.Account!]))
        };
        var license = At(TestLicense.Token(TestLicense.Payload(Now.AddHours(-1), maxAccounts: 2, accounts: [Account])), server: server, licenseKey: "KEY-123");
        var other = new LicensedAccount("genesis", "33333333-3333-3333-3333-333333333333");

        await license.ConfirmAccountAsync(other.Universe, other.PlayerId);

        Assert.Equal(other, Assert.Single(server.Requests).Account);
        Assert.Equal(2, license.Status.Payload!.Accounts.Count);
    }

    [Fact]
    public async Task Any_account_passes_a_license_without_binding()
    {
        var license = At(TestLicense.Token(TestLicense.Payload(Now.AddHours(-1))));

        await license.ConfirmAccountAsync("tron", "whoever");
    }

    [Fact]
    public async Task Accounts_are_not_confirmed_with_an_invalid_license()
    {
        var license = At(TestLicense.Token(TestLicense.Payload(Now.AddDays(-4), TimeSpan.FromDays(3))));

        await Assert.ThrowsAsync<LicenseException>(() => license.ConfirmAccountAsync("tron", "whoever"));
    }

    [Fact]
    public void The_terms_are_accepted_once_per_version()
    {
        var store = new FakeLicenseStore();
        var license = At(null, store: store);
        Assert.False(license.TermsAccepted);

        license.AcceptTerms();

        Assert.True(license.TermsAccepted);
        Assert.Equal(LicenseTerms.Version, store.Stored.TermsVersion);
        Assert.Equal(Now, store.Stored.TermsAcceptedAt);
    }

    [Fact]
    public void The_logged_in_player_is_read_from_a_game_page()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "building_resource.html"));

        Assert.Equal("22222222-2222-2222-2222-222222222222", PageIdentityParser.CurrentPlayerId(html));
        Assert.Null(PageIdentityParser.CurrentPlayerId("{\"isSuccess\":true}"));
        Assert.Null(PageIdentityParser.CurrentPlayerId(null));
    }

    [Fact]
    public void The_embedded_public_key_is_a_P256_key()
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(LicenseKeys.Production, out _);

        Assert.Equal(256, key.KeySize);
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
