namespace LinkMic.Core;

// A bounded 20 ms playout queue. Prefer fresh audio when the clocks diverge.
public sealed class PlayoutBuffer
{
    private readonly Queue<AudioFrame> queue = new();
    private readonly object gate = new();
    private uint? lastSequence;
    private bool playing;
    public long Dropped { get; private set; }
    public long Underruns { get; private set; }
    public int Count { get { lock (gate) return queue.Count; } }
    public void Push(AudioFrame frame)
    {
        lock (gate)
        {
            if (lastSequence.HasValue && unchecked((int)(frame.Sequence - lastSequence.Value)) <= 0) { Dropped++; return; }
            lastSequence = frame.Sequence;
            while (queue.Count >= 5) { queue.Dequeue(); Dropped++; }
            queue.Enqueue(frame);
        }
    }
    public short[] Take()
    {
        lock (gate)
        {
            if (!playing && queue.Count >= 2) playing = true;
            if (playing && queue.Count > 0) return queue.Dequeue().Samples;
            if (playing) { Underruns++; playing = false; }
            return new short[AudioProtocol.SamplesPerFrame];
        }
    }
}
