// Original fixed-format parser; caller supplies actual available KSDATAFORMAT bytes.
#pragma once
inline unsigned short LinkMicU16(const unsigned char* p) {
    return static_cast<unsigned short>(p[0] | (p[1] << 8));
}
inline unsigned int LinkMicU32(const unsigned char* p) {
    return static_cast<unsigned int>(p[0]) | (static_cast<unsigned int>(p[1]) << 8) |
        (static_cast<unsigned int>(p[2]) << 16) | (static_cast<unsigned int>(p[3]) << 24);
}
inline bool LinkMicValidateFormat(const unsigned char* data, unsigned int available) {
    if (!data || available < 82) return false; // KSDATAFORMAT64 + WAVEFORMATEX18.
    unsigned int declared = LinkMicU32(data);
    if (declared < 82 || declared > available || declared > 104) return false;
    const unsigned char* wave = data + 64;
    unsigned short tag = LinkMicU16(wave);
    if (LinkMicU16(wave + 2) != 1 || LinkMicU32(wave + 4) != 48000 ||
        LinkMicU32(wave + 8) != 96000 || LinkMicU16(wave + 12) != 2 ||
        LinkMicU16(wave + 14) != 16) return false;
    if (tag == 1) return LinkMicU16(wave + 16) == 0 && declared <= 88;
    if (tag != 0xfffe || LinkMicU16(wave + 16) != 22 || declared != 104) return false;
    if (LinkMicU16(wave + 18) != 16 || LinkMicU32(wave + 20) != 4) return false;
    static const unsigned char pcmGuid[16] = {1,0,0,0,0,0,0x10,0,0x80,0,0,0xaa,0,0x38,0x9b,0x71};
    for (unsigned int i = 0; i < 16; ++i) if (wave[24+i] != pcmGuid[i]) return false;
    return true;
}
