using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using OgameSkaner.Core.Licensing;

namespace OgameSkaner.App.Infrastructure;

/// <summary>
/// The license's machine id: a hash of the Windows installation's MachineGuid (survives restarts and app updates,
/// changes with a Windows reinstall), shown as e.g. <c>3F2A-91C0-7D4E-B815-06A2</c>.
/// </summary>
public sealed class WindowsMachineFingerprint : IMachineFingerprint
{
    private readonly Lazy<string> _id = new(Compute);

    public string Id => _id.Value;

    private static string Compute()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("OgameSkaner.Machine:" + (ReadMachineGuid() ?? Environment.MachineName)));
        var hex = Convert.ToHexString(hash, 0, 10);
        return string.Join('-', Enumerable.Range(0, 5).Select(i => hex.Substring(i * 4, 4)));
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
