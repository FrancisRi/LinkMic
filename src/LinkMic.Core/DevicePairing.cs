using System.Security.Cryptography;
using System.Text;

namespace LinkMic.Core;
public sealed record TrustedPhone(string Id, string Name, string Token);
public interface IDeviceTrustStore
{
    string ServerId { get; }
    string ServerName => Environment.MachineName;
    byte[]? FindToken(string phoneId);
    void Remember(string phoneId, string name, byte[] token);
}
public static class DevicePairing
{
    public static bool ValidId(string? value) => value is { Length: 32 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    private static byte[] Decode(string value, int maximum)
    {
        if (value.Length > maximum * 2) throw new InvalidDataException("配对数据过长。");
        try { var data = Convert.FromBase64String(value); if (data.Length > maximum) throw new InvalidDataException("配对数据过长。"); return data; }
        catch (FormatException) { throw new InvalidDataException("无效的配对数据。"); }
    }
    public static string Mac(byte[] token, string context) => Convert.ToHexStringLower(HMACSHA256.HashData(token, Utf8(context)));
    public static bool EqualText(string a, string b) => CryptographicOperations.FixedTimeEquals(Utf8(a), Utf8(b));
    public static byte[] HandshakeKey(byte[] shared, string transcript)
    {
        // RFC 5869 HKDF-SHA256, with one expansion block.
        var prk = HMACSHA256.HashData(SHA256.HashData(Utf8(transcript)), shared);
        try { return HMACSHA256.HashData(prk, Utf8("LinkMic handshake v2\u0001")); }
        finally { CryptographicOperations.ZeroMemory(prk); }
    }
    public static string Encrypt(byte[] key, string value, string context)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var clear = Utf8(value); var cipher = new byte[clear.Length]; var tag = new byte[16];
        try { using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, clear, cipher, tag, Utf8(context)); return Convert.ToBase64String([.. nonce, .. cipher, .. tag]); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public static string Decrypt(byte[] key, string value, string context)
    {
        var data = Decode(value, 512); if (data.Length < 28) throw new InvalidDataException("无效的配对数据。");
        var clear = new byte[data.Length - 28];
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(12, clear.Length), data.AsSpan(data.Length - 16), clear, Utf8(context)); return Encoding.UTF8.GetString(clear); }
        catch (CryptographicException) { throw new InvalidDataException("配对验证失败。"); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public static async Task<(uint Session, string Name)> AcceptAsync(Stream stream, string hello, string code, IDeviceTrustStore store, CancellationToken token)
    {
        var fields = hello.Split(' ');
        if (fields.Length != 5 || fields[0] is not ("LMIC/2" or "LMIC/3") || !ValidId(fields[1])) throw new InvalidDataException("无效的设备身份。");
        if (Decode(fields[3], 32).Length != 32 || fields[4].Length is < 1 or > 30 || fields[4].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-')) throw new InvalidDataException("无效的设备身份。");
        using var peer = ECDiffieHellman.Create(); var peerBytes = Decode(fields[2], 128);
        try { peer.ImportSubjectPublicKeyInfo(peerBytes, out int read); if (read != peerBytes.Length || peer.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7") throw new CryptographicException(); }
        catch (CryptographicException) { throw new InvalidDataException("不支持的配对密钥。"); }
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Convert.ToBase64String(ephemeral.ExportSubjectPublicKeyInfo()), nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        byte[]? saved = store.FindToken(fields[1]); string mode = saved is null ? "PAIR" : "AUTH";
        string transcript = $"{hello}|{store.ServerId}|{publicKey}|{nonce}|{mode}";
        var shared = ephemeral.DeriveRawSecretAgreement(peer.PublicKey); var key = HandshakeKey(shared, transcript); CryptographicOperations.ZeroMemory(shared);
        byte[]? issued = null;
        try
        {
            string proof = saved is null ? "-" : Mac(saved, "SERVER|" + transcript);
            await stream.WriteAsync(Utf8($"CHALLENGE {store.ServerId} {publicKey} {nonce} {mode} {proof}\n"), token);
            var response = (await Pairing.ReadLineAsync(stream, token, 1024)).Split(' ');
            if (response.Length != 2 || response[0] != "PROOF") throw new InvalidDataException("首次连接需要输入电脑显示的配对码。");
            var login = Decrypt(key, response[1], transcript + "|CLIENT").Split(' ');
            if (login.Length != 2) throw new InvalidDataException("无效的身份验证。");
            byte[] credential;
            if (login[0] == "AUTH" && saved is not null && EqualText(login[1], Mac(saved, "CLIENT|" + transcript))) credential = saved;
            else if (login[0] == "PAIR" && login[1].Length == 6 && login[1].All(char.IsAsciiDigit) && EqualText(login[1], code))
            {
                credential = RandomNumberGenerator.GetBytes(32); issued = credential; store.Remember(fields[1], fields[4], credential);
            }
            else throw new InvalidDataException("配对码或设备凭据不匹配，请重新配对。");
            uint session = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)); if (session == 0) session = 1;
            string computerName = new(store.ServerName.Where(c => !char.IsControl(c)).Take(80).ToArray());
            if (string.IsNullOrWhiteSpace(computerName)) computerName = "Windows";
            // v2 clients require the original three fields. v3 adds authenticated display metadata.
            string metadata = fields[0] == "LMIC/3" ? " " + Convert.ToBase64String(Utf8(computerName)) : "";
            string ack = Encrypt(key, $"OK {session} {Convert.ToBase64String(credential)}{metadata}", transcript + "|SERVER");
            await stream.WriteAsync(Utf8("OK2 " + ack + "\n"), token); return (session, fields[4]);
        }
        finally { CryptographicOperations.ZeroMemory(key); if (saved is not null) CryptographicOperations.ZeroMemory(saved); if (issued is not null) CryptographicOperations.ZeroMemory(issued); }
    }
}
