using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LinkMic.Windows;

// ABI v1 is defined by our driver, independent of the WO Mic control protocol.
public sealed class VirtualMicOutput : IAudioOutput
{
    public const string DevicePath = @"\\.\LinkMicCapture";
    public const uint QueryIoctl = 0x00226000, ResetIoctl = 0x0022a004;
    private readonly SafeFileHandle handle;
    private int disposed;
    public static bool Exists()
    {
        // Presence detection must not open the exclusive writer or reset its ring.
        return QueryDosDevice("LinkMicCapture", new char[512], 512) != 0;
    }
    public VirtualMicOutput()
    {
        handle = Open(0xc0000000);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            throw new Win32Exception(error, "无法打开 LinkMic 虚拟麦克风。请确认自有驱动已安装，且没有其他 LinkMic 正在使用。");
        }
        try
        {
            var info = new byte[32];
            if (!DeviceIoControl(handle, QueryIoctl, null, 0, info, (uint)info.Length, out uint returned, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "查询 LinkMic 驱动版本失败。");
            ValidateDriverInfo(info, returned);
            if (!DeviceIoControl(handle, ResetIoctl, null, 0, null, 0, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "重置 LinkMic 音频队列失败。");
        }
        catch { handle.Dispose(); throw; }
    }
    internal static void ValidateDriverInfo(byte[] info, uint returned)
    {
        if (returned != 32 || info.Length != 32) throw new InvalidDataException("LinkMic 驱动版本响应不完整。");
        uint Read(int index) => BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(index * 4));
        if (Read(0) != 32 || Read(1) != 1 || Read(2) != 48000 || Read(3) != 1 || Read(4) != 16 || Read(5) != 1920 || Read(6) < 1920 || Read(6) > 96000 || Read(6) % 1920 != 0 || (Read(7) & 1) == 0)
            throw new NotSupportedException("LinkMic 驱动接口或音频格式不匹配，请安装对应版本驱动。");
    }
    public void Write(byte[] pcm)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (pcm.Length != 1920) throw new ArgumentException("Expected one 20 ms mono PCM16 frame.", nameof(pcm));
        if (!WriteFile(handle, pcm, (uint)pcm.Length, out uint written, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "写入 LinkMic 虚拟麦克风失败。");
        if (written != pcm.Length) throw new IOException("LinkMic 驱动违反完整帧写入约定。");
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        handle.Dispose(); // Driver cleanup clears the queue; no extra IOCTL on a stale handle.
    }
    private static SafeFileHandle Open(uint access) => CreateFile(DevicePath, access, 0, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint QueryDosDevice(string deviceName, [Out] char[] targetPath, uint maximum);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[]? input, uint inputSize, byte[]? output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WriteFile(SafeFileHandle file, byte[] bytes, uint length, out uint written, IntPtr overlapped);
}
