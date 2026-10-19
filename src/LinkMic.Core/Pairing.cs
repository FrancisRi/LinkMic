using System.Security.Cryptography;
using System.Text;

namespace LinkMic.Core;

public static class Pairing
{
    public static string NewCode() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    public static async Task<string> ReadLineAsync(Stream stream, CancellationToken token, int maximum = 128)
    {
        var bytes = new List<byte>(); var one = new byte[1];
        while (bytes.Count < maximum)
        {
            await stream.ReadExactlyAsync(one, token);
            if (one[0] == 10) return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.Add(one[0]);
        }
        throw new InvalidDataException("Handshake too long.");
    }

    public static Task<(uint Session, string Name)> AcceptAsync(Stream stream, string code, CancellationToken token) => AcceptAsync(stream, code, null, token);
    public static async Task<(uint Session, string Name)> AcceptAsync(Stream stream, string code, IDeviceTrustStore? store, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        string line = await ReadLineAsync(stream, timeout.Token, 1024);
        if ((line.StartsWith("LMIC/2 ", StringComparison.Ordinal) || line.StartsWith("LMIC/3 ", StringComparison.Ordinal)) && store is not null)
        {
            try { return await DevicePairing.AcceptAsync(stream, line, code, store, timeout.Token); }
            catch (InvalidDataException) { await stream.WriteAsync("NO\n"u8.ToArray(), timeout.Token); throw; }
        }
        if (Encoding.UTF8.GetByteCount(line) > 128) throw new InvalidDataException("Handshake too long.");
        var words = line.Split(' ', 3);
        if (words.Length != 3 || words[0] != "LMIC/1" ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(words[1]), Encoding.UTF8.GetBytes(code)))
        {
            await stream.WriteAsync("NO\n"u8.ToArray(), timeout.Token);
            throw new InvalidDataException("配对码不匹配。");
        }
        uint session = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        if (session == 0) session = 1;
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"OK {session}\n"), timeout.Token);
        return (session, words[2]);
    }
}
