using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using LinkMic.Core;

namespace LinkMic.Windows;
internal sealed class DeviceTrustStore : IDeviceTrustStore
{
    private sealed record Document(string ServerId, List<TrustedPhone> Phones);
    private static readonly object Gate = new();
    private static string StatePath => Path.Combine(Environment.GetEnvironmentVariable("LINKMIC_STATE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkMic"), "paired-devices.dat");
    private readonly Document document;
    private DeviceTrustStore(Document document) => this.document = document;
    public string ServerId => document.ServerId;
    public static DeviceTrustStore Open()
    {
        lock (Gate)
        {
            if (File.Exists(StatePath)) return new(Read());
            var data = new Document(DevicePairing.NewId(), []); Write(data); return new(data);
        }
    }
    public static int Count { get { lock (Gate) return File.Exists(StatePath) ? Read().Phones.Count : 0; } }
    public static void ForgetAll()
    {
        lock (Gate)
        {
            if (!File.Exists(StatePath)) return;
            try { var data = Read(); data.Phones.Clear(); Write(data); }
            catch (Exception error) when (error is Win32Exception or CryptographicException or JsonException or InvalidDataException or FormatException)
            { File.Delete(StatePath); }
        }
    }
    public byte[]? FindToken(string phoneId)
    {
        lock (Gate) { var phone = document.Phones.SingleOrDefault(peer => peer.Id == phoneId); return phone is null ? null : Convert.FromBase64String(phone.Token); }
    }
    public void Remember(string phoneId, string name, byte[] token)
    {
        if (!DevicePairing.ValidId(phoneId) || token.Length != 32) throw new InvalidDataException("无效设备凭据。");
        lock (Gate)
        {
            var previous = document.Phones.FirstOrDefault(peer => peer.Id == phoneId);
            if (previous is not null) document.Phones.Remove(previous);
            if (document.Phones.Count >= 32) throw new InvalidDataException("已配对设备过多，请先在设置中忘记设备。");
            document.Phones.Add(new(phoneId, name, Convert.ToBase64String(token))); Write(document);
        }
    }
    private static Document Read()
    {
        if (new FileInfo(StatePath).Length > 65536) throw new InvalidDataException("配对记录过大。");
        var clear = Protect(File.ReadAllBytes(StatePath), false);
        try
        {
            var data = JsonSerializer.Deserialize<Document>(clear) ?? throw new InvalidDataException("配对记录损坏。");
            if (!DevicePairing.ValidId(data.ServerId) || data.Phones is null || data.Phones.Count > 32
                || data.Phones.Any(peer => peer is null || !DevicePairing.ValidId(peer.Id) || peer.Token is null || Convert.FromBase64String(peer.Token).Length != 32)
                || data.Phones.Select(peer => peer.Id).Distinct().Count() != data.Phones.Count) throw new InvalidDataException("配对记录损坏。");
            return data;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static void Write(Document data)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(data);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            string temporary = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, Protect(clear, true)); File.Move(temporary, StatePath, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref Blob data, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob result);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob result);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool success = encrypt ? CryptProtectData(ref input, "LinkMic paired devices", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取或保存配对设备记录。");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length); Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { if (!encrypt) Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); LocalFree(output.Data); }
        }
    }
}
