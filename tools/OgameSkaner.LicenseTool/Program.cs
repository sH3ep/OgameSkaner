using System.Security.Cryptography;
using System.Text.Json;
using OgameSkaner.Core.Licensing;

// ogs-license: signing license tokens by hand and managing the license server (see docs/licensing-plan.md).
//   keygen  --out <file.pem>                     new P-256 key pair; prints the public key for LicenseKeys.Production
//   pubkey  --key <file.pem>                     prints the public key of a private key
//   issue   --key <file.pem> --license <id> --machine <id> [--account <universe>:<playerId>]… [--max-accounts N]
//           [--days 3] [--min-version 1.2.0] [--customer <name>] [--plan <name>]
//   inspect <token> [--key <file.pem>]           prints the payload (verified when a key is given)
//   admin <command> …                            manages licenses on the license server (Admin.cs)

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0)
        return Usage();

    try
    {
        if (args[0] == "admin")
            return Admin.Run(args[1..]);

        var options = Options.Parse(args[1..]);
        switch (args[0])
        {
            case "keygen":
            {
                var path = options.Required("out");
                if (File.Exists(path))
                    throw new ArgumentException($"{path} exists; refusing to overwrite a signing key.");
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
                Console.WriteLine($"Private key written to {Path.GetFullPath(path)}. Back it up and keep it out of the repo.");
                Console.WriteLine("Public key (LicenseKeys.Production):");
                Console.WriteLine(LicenseToken.ExportPublicKey(key));
                return 0;
            }
            case "pubkey":
            {
                using var key = LoadKey(options.Required("key"));
                Console.WriteLine(LicenseToken.ExportPublicKey(key));
                return 0;
            }
            case "issue":
            {
                using var key = LoadKey(options.Required("key"));
                var now = DateTimeOffset.UtcNow;
                var accounts = options.All("account").Select(ParseAccount).ToList();
                var payload = new LicensePayload(
                    options.Required("license"),
                    options.Required("machine"),
                    accounts,
                    int.Parse(options.Optional("max-accounts") ?? accounts.Count.ToString()),
                    now,
                    now.AddDays(double.Parse(options.Optional("days") ?? "3", System.Globalization.CultureInfo.InvariantCulture)),
                    options.Optional("min-version"),
                    options.Optional("customer"),
                    options.Optional("plan"));
                Console.WriteLine(LicenseToken.Sign(payload, key));
                return 0;
            }
            case "inspect":
            {
                if (!LicenseToken.TrySplit(args.ElementAtOrDefault(1), out var payload, out var signature))
                    throw new ArgumentException("Not a license token.");
                if (options.Optional("key") is { } keyPath)
                {
                    using var key = LoadKey(keyPath);
                    var valid = key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                    Console.WriteLine(valid ? "Signature: valid" : "Signature: NOT valid for this key");
                }

                Console.WriteLine(JsonSerializer.Serialize(LicenseToken.ReadPayload(payload), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
                return 0;
            }
            default:
                return Usage();
        }
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or CryptographicException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static ECDsa LoadKey(string path)
{
    var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(path));
    return key;
}

static LicensedAccount ParseAccount(string value) =>
    value.Split(':', 2) is [var universe, var player] && universe.Length > 0 && player.Length > 0
        ? new LicensedAccount(universe, player)
        : throw new ArgumentException($"--account {value}: expected <universe>:<playerId>.");

static int Usage()
{
    Console.Error.WriteLine(
        """
        ogs-license keygen  --out <file.pem>
        ogs-license pubkey  --key <file.pem>
        ogs-license issue   --key <file.pem> --license <id> --machine <id> [--account <universe>:<playerId>]... [--max-accounts N]
                            [--days 3] [--min-version 1.2.0] [--customer <name>] [--plan <name>]
                            (no --account and no --max-accounts: any game account)
        ogs-license inspect <token> [--key <file.pem>]

        """ + Admin.Usage);
    return 2;
}

/// <summary><c>--name value</c> pairs; a name may repeat.</summary>
internal sealed class Options
{
    private readonly List<(string Name, string Value)> _values = [];

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue; // the token of "inspect"
            if (i + 1 >= args.Length)
                throw new ArgumentException($"{args[i]} needs a value.");
            options._values.Add((args[i][2..], args[++i]));
        }

        return options;
    }

    public string? Optional(string name) => _values.LastOrDefault(v => v.Name == name).Value;

    public string Required(string name) => Optional(name) ?? throw new ArgumentException($"--{name} is required.");

    public IEnumerable<string> All(string name) => _values.Where(v => v.Name == name).Select(v => v.Value);
}
