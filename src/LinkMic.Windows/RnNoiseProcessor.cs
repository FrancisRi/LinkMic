using System.IO;
using System.Runtime.InteropServices;

namespace LinkMic.Windows;

// This object is owned by the receiver's audio worker. The UI only changes a flag.
internal sealed class RnNoiseProcessor : IDisposable
{
    private const int BlockSize = 480;
    private readonly float[] block = new float[BlockSize];
    private readonly short[] processed = new short[BlockSize * 2];
    private DenoiseHandle? state;
    private bool disposed;
    private static readonly Lazy<NativeApi> Native = new(() => new NativeApi());

    public static bool IsAvailable(out string reason)
    {
        try { _ = Native.Value; reason = ""; return true; }
        catch (Exception error) when (IsLibraryError(error)) { reason = error.Message; return false; }
    }
    internal static bool IsLibraryError(Exception error) => error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidDataException or InvalidOperationException;

    public void Process(short[] samples, bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!enabled) { Reset(); return; } // Exact bypass; never convert or delay disabled audio.
        if (samples.Length != BlockSize * 2) throw new ArgumentException("Expected 960 mono samples.", nameof(samples));
        var api = Native.Value;
        state ??= new DenoiseHandle(api);
        for (int offset = 0; offset < samples.Length; offset += BlockSize)
        {
            // RNNoise's float API uses int16-scale PCM, not normalized [-1, 1].
            for (int i = 0; i < BlockSize; i++) block[i] = samples[offset + i];
            float probability = api.Process(state.DangerousGetHandle(), block, block);
            if (!float.IsFinite(probability)) throw new InvalidDataException("RNNoise returned invalid audio.");
            for (int i = 0; i < BlockSize; i++)
            {
                if (!float.IsFinite(block[i])) throw new InvalidDataException("RNNoise returned invalid audio.");
                processed[offset + i] = (short)Math.Clamp(MathF.Round(block[i]), short.MinValue, short.MaxValue);
            }
        }
        // Commit only after both 10 ms blocks succeed, preserving dry audio on errors.
        processed.CopyTo(samples, 0);
    }
    public void Reset() { state?.Dispose(); state = null; }
    public void Dispose() { if (disposed) return; Reset(); disposed = true; }

    private sealed class DenoiseHandle : SafeHandle
    {
        private readonly NativeApi api;
        public DenoiseHandle(NativeApi api) : base(IntPtr.Zero, true)
        {
            this.api = api; SetHandle(api.Create(IntPtr.Zero));
            if (IsInvalid) throw new InvalidOperationException("RNNoise initialization failed.");
        }
        public override bool IsInvalid => handle == IntPtr.Zero;
        protected override bool ReleaseHandle() { api.Destroy(handle); return true; }
    }
    private sealed class NativeApi
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int FrameSize();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr CreateState(IntPtr model);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DestroyState(IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate float ProcessFrame(IntPtr state, [In, Out] float[] output, [In] float[] input);
        public readonly CreateState Create;
        public readonly DestroyState Destroy;
        public readonly ProcessFrame Process;
        public NativeApi()
        {
            // Load only the packaged library, never a DLL from the current directory/PATH.
            IntPtr library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "rnnoise.dll"));
            try
            {
                var frameSize = Marshal.GetDelegateForFunctionPointer<FrameSize>(NativeLibrary.GetExport(library, "rnnoise_get_frame_size"));
                if (frameSize() != BlockSize) throw new InvalidDataException("Unsupported RNNoise frame size.");
                Create = Marshal.GetDelegateForFunctionPointer<CreateState>(NativeLibrary.GetExport(library, "rnnoise_create"));
                Destroy = Marshal.GetDelegateForFunctionPointer<DestroyState>(NativeLibrary.GetExport(library, "rnnoise_destroy"));
                Process = Marshal.GetDelegateForFunctionPointer<ProcessFrame>(NativeLibrary.GetExport(library, "rnnoise_process_frame"));
            }
            catch { NativeLibrary.Free(library); throw; }
            // One module handle stays loaded for the application lifetime.
        }
    }
}
