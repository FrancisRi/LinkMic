// LinkMic original control ABI. Version 1; integers use Windows little endian.
#pragma once
#define LINKMIC_PROTOCOL_VERSION 1u
#define LINKMIC_SAMPLE_RATE 48000u
#define LINKMIC_CHANNELS 1u
#define LINKMIC_BITS_PER_SAMPLE 16u
#define LINKMIC_FRAME_BYTES 1920u
#define LINKMIC_RING_BYTES (LINKMIC_FRAME_BYTES * 5u)
#define LINKMIC_IOCTL_QUERY 0x00226000u
#define LINKMIC_IOCTL_RESET 0x0022A004u
#define LINKMIC_DEVICE_PATH L"\\\\.\\LinkMicCapture"
typedef struct LINKMIC_INFO {
    unsigned int Size;
    unsigned int Version;
    unsigned int SampleRate;
    unsigned int Channels;
    unsigned int BitsPerSample;
    unsigned int FrameBytes;
    unsigned int RingBytes;
    unsigned int Capabilities; // Bit 0: complete writes; oldest bytes dropped on overflow.
} LINKMIC_INFO;
