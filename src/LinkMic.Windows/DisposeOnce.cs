using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DisconnectProbe")]

namespace LinkMic.Windows;

// Some native Bluetooth providers throw when disposed twice. All cancellation,
// timeout and final-cleanup paths must share this one ownership gate.
internal sealed class DisposeOnce<T>(T resource) : IDisposable where T : class, IDisposable
{
    private T? owned = resource;
    public T Value { get; } = resource;
    public void Dispose() => Interlocked.Exchange(ref owned, null)?.Dispose();
}
