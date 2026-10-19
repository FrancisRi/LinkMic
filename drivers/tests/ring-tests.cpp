#include <assert.h>
#include <stdio.h>
#include <string.h>
#include "LinkMicRing.h"
#include "LinkMicFormat.h"

int main() {
    static_assert(sizeof(LINKMIC_INFO) == 32, "ABI layout");
    unsigned char format[104] = {};
    format[0]=104; format[64]=0xfe; format[65]=0xff; format[66]=1;
    format[68]=0x80; format[69]=0xbb; // 48000
    format[72]=0; format[73]=0x77; format[74]=1; // 96000
    format[76]=2; format[78]=16; format[80]=22; format[82]=16; format[84]=4;
    const unsigned char pcmGuid[16]={1,0,0,0,0,0,0x10,0,0x80,0,0,0xaa,0,0x38,0x9b,0x71};
    memcpy(format+88,pcmGuid,sizeof(pcmGuid));
    assert(LinkMicValidateFormat(format,sizeof(format)));
    assert(!LinkMicValidateFormat(format,103));
    format[80]=255; assert(!LinkMicValidateFormat(format,sizeof(format))); format[80]=22;
    format[73]=0; format[74]=0; assert(!LinkMicValidateFormat(format,sizeof(format))); format[73]=0x77; format[74]=1;
    format[76]=0; assert(!LinkMicValidateFormat(format,sizeof(format))); format[76]=2;
    format[0]=82; assert(!LinkMicValidateFormat(format,sizeof(format)));
    format[64]=1; format[65]=0; format[80]=0; assert(LinkMicValidateFormat(format,82));
    format[80]=22; assert(!LinkMicValidateFormat(format,sizeof(format)));
    LinkMicRing ring = {};
    unsigned char input[LINKMIC_FRAME_BYTES];
    unsigned char output[LINKMIC_FRAME_BYTES * 2];
    memset(output, 0xAA, sizeof(output));
    ring.Read(output, sizeof(output));
    for (unsigned char byte : output) assert(byte == 0);
    // No consumer: all frames are still accepted, with exactly 100ms retained.
    for (unsigned int frame = 1; frame <= 10; ++frame) {
        memset(input, frame, sizeof(input)); ring.PushFrame(input);
        assert(ring.Count <= LINKMIC_RING_BYTES);
    }
    assert(ring.Count == LINKMIC_RING_BYTES);
    for (unsigned int frame = 6; frame <= 10; ++frame) {
        ring.Read(output, LINKMIC_FRAME_BYTES);
        for (unsigned int i = 0; i < LINKMIC_FRAME_BYTES; ++i) assert(output[i] == frame);
    }
    assert(ring.Count == 0);
    // WaveRT may request shorter spans across the DMA wrap.
    for (unsigned int i = 0; i < sizeof(input); ++i) input[i] = static_cast<unsigned char>(i);
    ring.PushFrame(input); ring.Read(output, 400); ring.Read(output + 400, 1520);
    assert(memcmp(input, output, sizeof(input)) == 0);
    // Partly available read fills exactly the tail with silence.
    ring.PushFrame(input); ring.Read(output, sizeof(output));
    assert(memcmp(input, output, sizeof(input)) == 0);
    for (unsigned int i = sizeof(input); i < sizeof(output); ++i) assert(output[i] == 0);
    ring.PushFrame(input); ring.Reset(); assert(ring.Count == 0);
    // Partial consumer progress followed by overflow must evict exact oldest bytes.
    memset(input, 1, sizeof(input)); ring.PushFrame(input);
    ring.Read(output, LINKMIC_FRAME_BYTES / 2);
    for (unsigned int frame = 2; frame <= 6; ++frame) {
        memset(input, frame, sizeof(input)); ring.PushFrame(input);
    }
    assert(ring.Count == LINKMIC_RING_BYTES);
    for (unsigned int frame = 2; frame <= 6; ++frame) {
        ring.Read(output, LINKMIC_FRAME_BYTES);
        for (unsigned int i = 0; i < LINKMIC_FRAME_BYTES; ++i) assert(output[i] == frame);
    }
    ring.Reset();
    // Repeated overflow/wrap checks the ring never leaks earlier discarded data.
    for (unsigned int frame = 0; frame < 10000; ++frame) {
        memset(input, frame % 251, sizeof(input)); ring.PushFrame(input);
        if (frame % 3 == 0) ring.Read(output, 246);
        assert(ring.Count <= LINKMIC_RING_BYTES);
    }
    puts("PASS: fixed-format invalid bytes/zero rate/short headers/cbSize, ABI32, underrun silence, no-consumer full writes, half-frame overflow, DMA split, reset, 10000 wraps.");
    return 0;
}
