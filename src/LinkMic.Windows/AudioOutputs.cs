using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LinkMic.Windows;

public interface IAudioOutput : IDisposable { void Write(byte[] pcm); }
public sealed record OutputDevice(string Name, int Id, bool Driver = false, bool Cable = false, string? DeviceName = null)
{
    public string Hint => Cable ? "VB-CABLE：请在会议或录音软件中选择 CABLE Output。"
        : Driver ? "实验驱动：请在会议软件中选择 LinkMic Virtual Microphone。"
        : "虚拟音频线缆请选择对应录音端；普通扬声器仅用于监听。";
}

public sealed class WaveOutput : IAudioOutput
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Caps { public ushort Mid, Pid; public uint Version; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public uint Formats; public ushort Channels, Reserved; public uint Support; }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct Format { public ushort Tag, Channels; public uint Rate, BytesPerSec; public ushort Align, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public IntPtr Data; public uint Length, Recorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }
    private sealed record Slot(IntPtr Header, IntPtr Data) { public bool Used; }
    private readonly List<Slot> slots = []; private readonly IntPtr device; private int next, disposed;
    public static List<OutputDevice> Devices()
    {
        var playback = new List<OutputDevice>();
        for (uint i = 0; i < waveOutGetNumDevs(); i++)
            if (waveOutGetDevCaps((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<Caps>()) == 0)
                playback.Add(DescribeDevice((int)i, caps.Name));
        return OrderDevices(playback, VirtualMicOutput.Exists());
    }
    internal static OutputDevice DescribeDevice(int id, string name)
    {
        // WinMM truncates names to 31 characters. Match the standard base cable,
        // including its vendor prefix; the separate 16-channel endpoint is not the default.
        bool cable = name.StartsWith("CABLE Input (VB-Audio Virtual", StringComparison.OrdinalIgnoreCase);
        return new(cable ? "CABLE Input · VB-CABLE 虚拟麦克风" : name, id, Cable: cable, DeviceName: name);
    }
    internal static List<OutputDevice> OrderDevices(IEnumerable<OutputDevice> playback, bool experimentalInstalled)
    {
        var devices = playback.ToList();
        var result = devices.Where(output => output.Cable).ToList();
        result.Add(new("默认扬声器 · 监听测试", -1));
        result.AddRange(devices.Where(output => !output.Cable));
        if (experimentalInstalled) result.Add(new("LinkMic Virtual Microphone · 实验驱动", 0, true));
        return result;
    }
    private static int ResolveOutputId(OutputDevice output)
    {
        if (output.Id == -1 && !output.Cable) return -1;
        if (output.Id < 0 || waveOutGetDevCaps((UIntPtr)(uint)output.Id, out var caps, (uint)Marshal.SizeOf<Caps>()) != 0
            || output.DeviceName is not null && caps.Name != output.DeviceName
            || output.Cable && !DescribeDevice(output.Id, caps.Name).Cable)
            throw new IOException("所选音频设备已变化，请刷新设备后重新选择。VB-CABLE 需要安装后重启电脑。");
        return output.Id;
    }
    public WaveOutput(OutputDevice output) : this(ResolveOutputId(output)) { }
    private WaveOutput(int id)
    {
        var format = new Format { Tag = 1, Channels = 1, Rate = 48000, BytesPerSec = 96000, Align = 2, Bits = 16 };
        Check(waveOutOpen(out device, unchecked((uint)id), ref format, IntPtr.Zero, IntPtr.Zero, 0));
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var slot = new Slot(Marshal.AllocHGlobal(Marshal.SizeOf<Header>()), Marshal.AllocHGlobal(1920)); slots.Add(slot);
                Marshal.StructureToPtr(new Header { Data = slot.Data, Length = 1920 }, slot.Header, false);
                Check(waveOutPrepareHeader(device, slot.Header, (uint)Marshal.SizeOf<Header>()));
            }
        }
        catch { Dispose(); throw; }
    }
    public void Write(byte[] pcm)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (pcm.Length != 1920) throw new ArgumentException("Expected one 20 ms PCM frame.", nameof(pcm));
        var slot = slots[next]; var header = Marshal.PtrToStructure<Header>(slot.Header);
        if (slot.Used && (header.Flags & 1) == 0) return; // bounded device queue; never accumulate stale audio
        Marshal.Copy(pcm, 0, slot.Data, pcm.Length); Check(waveOutWrite(device, slot.Header, (uint)Marshal.SizeOf<Header>())); slot.Used = true; next = (next + 1) % slots.Count;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        waveOutReset(device);
        foreach (var slot in slots) { waveOutUnprepareHeader(device, slot.Header, (uint)Marshal.SizeOf<Header>()); Marshal.FreeHGlobal(slot.Data); Marshal.FreeHGlobal(slot.Header); }
        slots.Clear(); waveOutClose(device);
    }
    private static void Check(uint result) { if (result != 0) throw new IOException($"音频设备错误 {result}。"); }
    [DllImport("winmm.dll")] private static extern uint waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint waveOutGetDevCaps(UIntPtr id, out Caps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr device, uint id, ref Format format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr device);
}

