using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LinkMic.Core;

internal static class RememberedPairingTests
{
    public static async Task RunAsync(string directory)
    {
        var store = new MemoryTrust();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("JAVA_HOME")!, "bin", "java.exe")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in new[] { "-cp", directory, "PairingClientFixture", ((IPEndPoint)listener.LocalEndpoint).Port.ToString(), directory }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new Exception("Java pairing fixture failed to start.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        bool[] accepted = [true, true, true, false, false, false, false, false, false, true, false];
        try
        {
            for (int index = 0; index < accepted.Length; index++)
            {
                if (index == 8) store.Phones.Clear(); // Explicit desktop revocation.
                using var client = await listener.AcceptTcpClientAsync(timeout.Token); using var stream = client.GetStream();
                bool success;
                try
                {
                    var paired = await Pairing.AcceptAsync(stream, "123456", store, timeout.Token);
                    var frame = await AudioProtocol.ReadAsync(stream, paired.Session, timeout.Token);
                    if (frame.Sequence != 77) throw new Exception("Post-auth audio protocol failed.");
                    success = true;
                }
                catch (Exception error) when (error is InvalidDataException or EndOfStreamException) { success = false; }
                if (success != accepted[index]) throw new Exception($"Remembered pairing scenario {index} had unexpected outcome.");
                if (store.Phones.Count != (index == 8 ? 0 : 1)) throw new Exception($"Failed pairing modified the trust store at {index}.");
                if (index == 1) { store = store.Restart(); store.ServerName = "办公电脑"; } // Process restart and OS rename.
            }
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new Exception(await errors);
            Console.Write(await output);
            await CheckV2CompatibilityAsync(timeout.Token);
            File.WriteAllText(Path.Combine(directory, "remembered-pairing-results.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = true, realJavaClient = true, actualDotnetServer = true, transport = "loopback TCP", handshakeScenarios = accepted.Length, initialPairing = true, reconnectWithoutPin = true, processStateReload = true, revokeAndRepair = true, replayRejected = true, serverImpersonationRejected = true, encryptedEnrollmentTamperingRejected = true, credentialBoundToPhone = true, authenticatedUnicodeComputerName = true, computerRenameOnReconnect = true, olderV2EnrollmentAndReconnect = true, hardwareBluetoothTested = false, androidKeystorePhysicallyTested = false }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        var key = RandomNumberGenerator.GetBytes(32); string encrypted = DevicePairing.Encrypt(key, "PAIR 123456", "first transcript");
        try { DevicePairing.Decrypt(key, encrypted, "changed transcript"); throw new Exception("Changed transcript accepted."); }
        catch (InvalidDataException) { }
    }
    private static async Task CheckV2CompatibilityAsync(CancellationToken token)
    {
        var store = new MemoryTrust(); string phone = DevicePairing.NewId(); byte[]? credential = null;
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var accepting = Task.Run(async () =>
            {
                using var incoming = await listener.AcceptTcpClientAsync(token);
                return await Pairing.AcceptAsync(incoming.GetStream(), "123456", store, token);
            }, token);
            using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, token);
            using var stream = client.GetStream(); using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            string hello = $"LMIC/2 {phone} {Convert.ToBase64String(ephemeral.ExportSubjectPublicKeyInfo())} {Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))} OldAndroid";
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(hello + "\n"), token);
            string[] challenge = (await Pairing.ReadLineAsync(stream, token, 1024)).Split(' ');
            string transcript = $"{hello}|{challenge[1]}|{challenge[2]}|{challenge[3]}|{challenge[4]}";
            using var peer = ECDiffieHellman.Create(); peer.ImportSubjectPublicKeyInfo(Convert.FromBase64String(challenge[2]), out _);
            byte[] shared = ephemeral.DeriveRawSecretAgreement(peer.PublicKey), key = DevicePairing.HandshakeKey(shared, transcript);
            try
            {
                string login = credential is null ? "PAIR 123456" : "AUTH " + DevicePairing.Mac(credential, "CLIENT|" + transcript);
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("PROOF " + DevicePairing.Encrypt(key, login, transcript + "|CLIENT") + "\n"), token);
                string ack = await Pairing.ReadLineAsync(stream, token, 1024);
                string[] fields = DevicePairing.Decrypt(key, ack[4..], transcript + "|SERVER").Split(' ');
                if (fields.Length != 3 || fields[0] != "OK") throw new Exception("Old Android v2 acknowledgment changed.");
                credential = Convert.FromBase64String(fields[2]); await accepting;
            }
            finally { CryptographicOperations.ZeroMemory(shared); CryptographicOperations.ZeroMemory(key); }
        }
        if (credential is not null) CryptographicOperations.ZeroMemory(credential);
        Console.WriteLine("PASS older v2 enrollment and PIN-free reconnect");
    }
    private sealed class MemoryTrust : IDeviceTrustStore
    {
        public string ServerId { get; private init; } = DevicePairing.NewId();
        public string ServerName { get; set; } = "书房电脑";
        public Dictionary<string, byte[]> Phones { get; } = [];
        public byte[]? FindToken(string id) => Phones.TryGetValue(id, out var token) ? (byte[])token.Clone() : null;
        public void Remember(string id, string name, byte[] token) => Phones[id] = (byte[])token.Clone();
        public MemoryTrust Restart() { var next = new MemoryTrust { ServerId = ServerId }; foreach (var (id, token) in Phones) next.Phones[id] = (byte[])token.Clone(); return next; }
    }
}
