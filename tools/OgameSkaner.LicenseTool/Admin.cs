using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// <c>ogs-license admin …</c>: the license server's /admin API (server/license-worker). The server and token come from
/// the environment variables <c>OGS_LICENSE_SERVER</c> (e.g. https://ogameskaner-license.example.workers.dev) and
/// <c>OGS_LICENSE_ADMIN_TOKEN</c>.
/// </summary>
internal static class Admin
{
    public const string Usage =
        """
        ogs-license admin public-key
        ogs-license admin list       [--q <text>] [--status active|past_due|cancelled|revoked]
        ogs-license admin show       <license id>
        ogs-license admin create     [--email <address>] [--customer <name>] [--plan <name>] [--max-machines N] [--max-accounts N]
                                     [--days N | --valid-until <date>] [--notes <text>] [--mail true]
        ogs-license admin set        <license id> [--status <status>] [--plan <name>] [--max-machines N] [--max-accounts N]
                                     [--days N | --valid-until <date>|none] [--email …] [--customer …] [--notes …]
        ogs-license admin rotate-key <license id> [--mail true]
        ogs-license admin release    <license id> <machine id>
        ogs-license admin unbind     <license id> <universe> <player id>
                                     (environment: OGS_LICENSE_SERVER, OGS_LICENSE_ADMIN_TOKEN)
        """;

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException(Usage);

        var options = Options.Parse(args[1..]);
        var positional = Positional(args[1..]);
        string Arg(int index, string name) =>
            positional.ElementAtOrDefault(index) ?? throw new ArgumentException($"{name} is required.\n{Usage}");

        var request = args[0] switch
        {
            "public-key" => new HttpRequestMessage(HttpMethod.Get, "admin/public-key"),
            "list" => new HttpRequestMessage(HttpMethod.Get, "admin/licenses" + Query(("q", options.Optional("q")), ("status", options.Optional("status")))),
            "show" => new HttpRequestMessage(HttpMethod.Get, $"admin/licenses/{Escape(Arg(0, "<license id>"))}"),
            "create" => Json(HttpMethod.Post, "admin/licenses", Body(options, withMail: true)),
            "set" => Json(HttpMethod.Patch, $"admin/licenses/{Escape(Arg(0, "<license id>"))}", Body(options, withMail: false)),
            "rotate-key" => Json(HttpMethod.Post, $"admin/licenses/{Escape(Arg(0, "<license id>"))}/key", MailOnly(options)),
            "release" => new HttpRequestMessage(HttpMethod.Delete,
                $"admin/licenses/{Escape(Arg(0, "<license id>"))}/machines/{Escape(Arg(1, "<machine id>"))}"),
            "unbind" => new HttpRequestMessage(HttpMethod.Delete,
                $"admin/licenses/{Escape(Arg(0, "<license id>"))}/accounts/{Escape(Arg(1, "<universe>"))}/{Escape(Arg(2, "<player id>"))}"),
            _ => throw new ArgumentException(Usage)
        };

        return Send(request);
    }

    private static int Send(HttpRequestMessage request)
    {
        var server = Environment.GetEnvironmentVariable("OGS_LICENSE_SERVER")
                     ?? throw new ArgumentException("Set OGS_LICENSE_SERVER to the license server's address.");
        var token = Environment.GetEnvironmentVariable("OGS_LICENSE_ADMIN_TOKEN")
                    ?? throw new ArgumentException("Set OGS_LICENSE_ADMIN_TOKEN to the server's ADMIN_TOKEN.");

        using var http = new HttpClient { BaseAddress = new Uri(server.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response;
        try
        {
            response = http.Send(request);
        }
        catch (HttpRequestException ex)
        {
            throw new IOException($"The license server can't be reached: {ex.Message}", ex);
        }

        using (response)
        {
            var text = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();
            if (text.Length > 0)
            {
                try
                {
                    text = JsonNode.Parse(text)!.ToJsonString(Pretty);
                }
                catch (JsonException)
                {
                }
            }

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine(text.Length > 0 ? text : $"{(int)response.StatusCode} {response.ReasonPhrase}");
                return 0;
            }

            Console.Error.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}: {text}");
            return 1;
        }
    }

    /// <summary>The JSON body of create/set from the options (only the ones given).</summary>
    private static JsonObject Body(Options options, bool withMail)
    {
        var body = new JsonObject();
        void Text(string option, string field)
        {
            if (options.Optional(option) is { } value)
                body[field] = value;
        }

        void Number(string option, string field)
        {
            if (options.Optional(option) is { } value)
                body[field] = int.Parse(value);
        }

        Text("email", "email");
        Text("customer", "customer");
        Text("plan", "plan");
        Text("status", "status");
        Text("notes", "notes");
        Number("max-machines", "maxMachines");
        Number("max-accounts", "maxAccounts");
        Number("days", "days");
        if (options.Optional("valid-until") is { } until)
            body["validUntil"] = until is "none" or "null" ? null : until;
        if (withMail && IsTrue(options.Optional("mail")))
            body["mail"] = true;
        return body;
    }

    private static JsonObject MailOnly(Options options) =>
        IsTrue(options.Optional("mail")) ? new JsonObject { ["mail"] = true } : new JsonObject();

    private static bool IsTrue(string? value) => value is "true" or "yes" or "1";

    private static HttpRequestMessage Json(HttpMethod method, string path, JsonObject body) =>
        new(method, path) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string Query(params (string Name, string? Value)[] values)
    {
        var given = values.Where(v => !string.IsNullOrEmpty(v.Value)).Select(v => $"{v.Name}={Escape(v.Value!)}").ToList();
        return given.Count == 0 ? "" : "?" + string.Join("&", given);
    }

    /// <summary>The arguments that aren't an option or an option's value.</summary>
    private static List<string> Positional(string[] args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                i++;
            else
                result.Add(args[i]);
        }

        return result;
    }
}
