// Original bounded ring shared by the kernel adapter and host-side tests.
// Callers serialize all operations. Storage is embedded in nonpaged driver data.
#pragma once
#include "LinkMicAbi.h"
struct LinkMicRing {
    unsigned char Bytes[LINKMIC_RING_BYTES];
    unsigned int ReadOffset;
    unsigned int Count;
    void Reset() { ReadOffset = 0; Count = 0; }
    void PushFrame(const unsigned char* source) {
        unsigned int excess = Count + LINKMIC_FRAME_BYTES > LINKMIC_RING_BYTES
            ? Count + LINKMIC_FRAME_BYTES - LINKMIC_RING_BYTES : 0;
        ReadOffset = (ReadOffset + excess) % LINKMIC_RING_BYTES;
        Count -= excess;
        unsigned int write = (ReadOffset + Count) % LINKMIC_RING_BYTES;
        for (unsigned int i = 0; i < LINKMIC_FRAME_BYTES; ++i)
            Bytes[(write + i) % LINKMIC_RING_BYTES] = source[i];
        Count += LINKMIC_FRAME_BYTES;
    }
    void Read(unsigned char* destination, unsigned int length) {
        unsigned int take = Count < length ? Count : length;
        for (unsigned int i = 0; i < take; ++i)
            destination[i] = Bytes[(ReadOffset + i) % LINKMIC_RING_BYTES];
        for (unsigned int i = take; i < length; ++i) destination[i] = 0;
        ReadOffset = (ReadOffset + take) % LINKMIC_RING_BYTES;
        Count -= take;
    }
};
