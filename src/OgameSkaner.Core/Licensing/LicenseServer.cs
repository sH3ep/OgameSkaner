using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace OgameSkaner.Core.Licensing;

/// <summary>The <c>License</c> section of appsettings.json.</summary>
public sealed class LicenseOptions
{
    public const string SectionName = "License";

    /// <summary>Base address of the license server; null = no server yet, tokens are only imported by hand.</summary>
    public Uri? ServerUrl { get; set; }

    /// <summary>How often a running app asks for a fresh token (it is valid for a few days).</summary>
    public TimeSpan RenewEvery { get; set; } = TimeSpan.FromHours(4);

    /// <summary>The next try after a failed renewal.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>One request for a token: activation, renewal and binding another game account are all this.</summary>
/// <param name="Account">A game account to bind to the license; null = keep the bound ones.</param>
public sealed record LicenseTokenRequest(string LicenseKey, string Machine, string AppVersion, LicensedAccount? Account = null);

/// <summary>The license server refused (unknown or cancelled key, too many PCs or accounts, …) or couldn't be reached.</summary>
public sealed class LicenseServerException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>The license server, behind which the payment provider's webhooks keep the subscriptions.</summary>
public interface ILicenseServer
{
    bool IsConfigured { get; }

    /// <returns>A signed token (see <see cref="LicenseToken"/>).</returns>
    /// <exception cref="LicenseServerException">Refused or unreachable.</exception>
    Task<string> RequestTokenAsync(LicenseTokenRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>POST {ServerUrl}/v1/token</c> with the <see cref="LicenseTokenRequest"/> as JSON; the answer is
/// <c>{ "token": "OSL1…" }</c>, or an error status with <c>{ "error": "…" }</c> shown to the player as it is.
/// </summary>
public sealed class HttpLicenseServer(HttpClient http, IOptions<LicenseOptions> options) : ILicenseServer
{
    public HttpLicenseServer(IOptions<LicenseOptions> options)
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, options)
    {
    }

    public bool IsConfigured => options.Value.ServerUrl is not null;

    public async Task<string> RequestTokenAsync(LicenseTokenRequest request, CancellationToken cancellationToken = default)
    {
        var server = options.Value.ServerUrl ?? throw new LicenseServerException("No license server is configured.");
        if (!server.AbsoluteUri.EndsWith('/'))
            server = new Uri(server.AbsoluteUri + "/");
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(new Uri(server, "v1/token"), request, LicenseToken.JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LicenseServerException($"The license server can't be reached: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LicenseServerException("The license server didn't answer in time.", ex);
        }

        using (response)
        {
            TokenResponse? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync<TokenResponse>(LicenseToken.JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (System.Text.Json.JsonException)
            {
            }

            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body?.Token))
                return body.Token;
            throw new LicenseServerException(body?.Error ?? $"The license server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private sealed record TokenResponse(string? Token, string? Error);
}
