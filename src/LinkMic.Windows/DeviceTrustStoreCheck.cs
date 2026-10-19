using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LinkMic.Core;

namespace LinkMic.Windows;
internal static class DeviceTrustStoreCheck
{
    public static void Run(string directory)
    {
        string? previous = Environment.GetEnvironmentVariable("LINKMIC_STATE_DIR");
        string path = Path.Combine(Path.GetFullPath(directory), "trust-store-check", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("LINKMIC_STATE_DIR", path);
        try
        {
            var store = DeviceTrustStore.Open(); string id = store.ServerId, phone = DevicePairing.NewId(); var token = RandomNumberGenerator.GetBytes(32);
            store.Remember(phone, "SyntheticPhone", token);
            var restored = DeviceTrustStore.Open();
            if (restored.ServerId != id || DeviceTrustStore.Count != 1 || !CryptographicOperations.FixedTimeEquals(token, restored.FindToken(phone)!)) throw new Exception("DPAPI device trust did not survive reload.");
            var bytes = File.ReadAllBytes(Path.Combine(path, "paired-devices.dat"));
            if (Encoding.UTF8.GetString(bytes).Contains(Convert.ToBase64String(token), StringComparison.Ordinal)) throw new Exception("Credential saved without protection.");
            DeviceTrustStore.ForgetAll();
            if (DeviceTrustStore.Count != 0 || DeviceTrustStore.Open().ServerId != id) throw new Exception("Explicit revocation failed or unexpectedly changed computer identity.");
            var file = Path.Combine(path, "paired-devices.dat"); bytes = File.ReadAllBytes(file); bytes[^1] ^= 1; File.WriteAllBytes(file, bytes);
            bool rejected = false; try { DeviceTrustStore.Open(); } catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Corrupted credential file was accepted.");
            DeviceTrustStore.ForgetAll();
            if (DeviceTrustStore.Count != 0) throw new Exception("Explicit recovery from a damaged record failed.");
            File.WriteAllText(Path.Combine(directory, "device-trust-store-results.json"), JsonSerializer.Serialize(new { passed = true, dpapiRoundTrip = true, restartPreservesIdentity = true, encryptedAtRest = true, explicitRevocation = true, corruptedRecordRejected = true, explicitRecovery = true, actualUserProfileModified = false }, new JsonSerializerOptions { WriteIndented = true }));
            CryptographicOperations.ZeroMemory(token);
        }
        finally { Environment.SetEnvironmentVariable("LINKMIC_STATE_DIR", previous); }
    }
}
