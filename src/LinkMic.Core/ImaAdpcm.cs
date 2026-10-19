using System.Buffers.Binary;

namespace LinkMic.Core;

// Independently framed IMA ADPCM: LE16 predictor, index, zero, then low nibble first.
// Each frame restarts its predictor so dropping a whole frame cannot poison the next.
public static class ImaAdpcm
{
    private static readonly int[] Indices = [-1,-1,-1,-1,2,4,6,8];
    private static readonly int[] Steps = [7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767];

    public static byte[] Encode(ReadOnlySpan<short> samples)
    {
        if (samples.Length == 0) throw new ArgumentException("Empty frame.");
        var output = new byte[4 + samples.Length / 2];
        BinaryPrimitives.WriteInt16LittleEndian(output, samples[0]);
        int predictor = samples[0], index = 0;
        // Pick a starting step for this block rather than warming up from silence.
        int initialDelta = samples.Length > 1 ? Math.Abs(samples[1] - predictor) : 0;
        while (index < 88 && Steps[index] < initialDelta) index++;
        output[2] = (byte)index;
        for (int i = 1; i < samples.Length; i++)
        {
            int step = Steps[index], difference = samples[i] - predictor, code = difference < 0 ? 8 : 0;
            int magnitude = Math.Abs(difference);
            if (magnitude >= step) { code |= 4; magnitude -= step; }
            if (magnitude >= (step >> 1)) { code |= 2; magnitude -= step >> 1; }
            if (magnitude >= (step >> 2)) code |= 1;
            Update(code, ref predictor, ref index);
            output[4 + (i - 1) / 2] |= (byte)(code << (((i - 1) % 2) * 4));
        }
        return output;
    }

    public static short[] Decode(ReadOnlySpan<byte> payload, int count)
    {
        if (count < 1 || payload.Length != 4 + count / 2 || payload[2] > 88 || payload[3] != 0)
            throw new InvalidDataException("Invalid ADPCM block.");
        int predictor = BinaryPrimitives.ReadInt16LittleEndian(payload), index = payload[2];
        var samples = new short[count]; samples[0] = (short)predictor;
        for (int i = 1; i < count; i++)
        {
            int code = payload[4 + (i - 1) / 2] >> (((i - 1) % 2) * 4) & 15;
            Update(code, ref predictor, ref index); samples[i] = (short)predictor;
        }
        return samples;
    }

    private static void Update(int code, ref int predictor, ref int index)
    {
        int step = Steps[index];
        int delta = (step >> 3) + ((code & 4) != 0 ? step : 0) + ((code & 2) != 0 ? step >> 1 : 0) + ((code & 1) != 0 ? step >> 2 : 0);
        predictor = Math.Clamp(predictor + ((code & 8) != 0 ? -delta : delta), short.MinValue, short.MaxValue);
        index = Math.Clamp(index + Indices[code & 7], 0, 88);
    }
}
