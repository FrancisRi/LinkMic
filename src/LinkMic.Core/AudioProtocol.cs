using System.Buffers.Binary;

namespace LinkMic.Core;

public sealed record AudioFrame(uint Sequence, ulong TimestampMicros, uint Session, short[] Samples);

public static class AudioProtocol
{
    public const int SampleRate = 48000, SamplesPerFrame = 960, HeaderSize = 32;
    public static readonly Guid BluetoothService = new("e7509170-7e2e-4820-b9c6-d21d21e1e531");
    public const int Port = 42791;

    public static byte[] Encode(AudioFrame frame, bool compressed = true)
    {
        if (frame.Samples.Length != SamplesPerFrame) throw new InvalidDataException("Expected a 20 ms frame.");
        byte[] payload;
        if (compressed) payload = ImaAdpcm.Encode(frame.Samples);
        else
        {
            payload = new byte[SamplesPerFrame * 2];
            for (int i = 0; i < SamplesPerFrame; i++) BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(i * 2), frame.Samples[i]);
        }
        var bytes = new byte[HeaderSize + payload.Length];
        "LMIC"u8.CopyTo(bytes);
        bytes[4] = 1; bytes[5] = (byte)(compressed ? 1 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), SamplesPerFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), frame.Sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12), frame.TimestampMicros);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), frame.Session);
        payload.CopyTo(bytes, HeaderSize);
        return bytes;
    }

    public static int ValidateHeader(ReadOnlySpan<byte> header, uint session)
    {
        if (header.Length != HeaderSize || !header[..4].SequenceEqual("LMIC"u8) || header[4] != 1)
            throw new InvalidDataException("Unsupported LinkMic packet.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != SamplesPerFrame ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[20..]) != SampleRate ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[28..]) != session)
            throw new InvalidDataException("Audio format or session mismatch.");
        int expected = header[5] switch { 0 => SamplesPerFrame * 2, 1 => 4 + SamplesPerFrame / 2, _ => throw new InvalidDataException("Unknown codec.") };
        if (BinaryPrimitives.ReadUInt32LittleEndian(header[24..]) != expected) throw new InvalidDataException("Invalid payload length.");
        return expected;
    }

    public static async Task<AudioFrame> ReadAsync(Stream stream, uint session, CancellationToken token)
    {
        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, token);
        int length = ValidateHeader(header, session);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token);
        short[] samples;
        if (header[5] == 1) samples = ImaAdpcm.Decode(payload, SamplesPerFrame);
        else
        {
            samples = new short[SamplesPerFrame];
            for (int i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(i * 2));
        }
        return new(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)), BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(12)), session, samples);
    }

    public static byte[] ToPcm(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        return bytes;
    }
}
