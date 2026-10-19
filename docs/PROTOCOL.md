# LinkMic pairing v3 (v2/v1 compatible desktop) / audio frames v1

This is a new protocol shared by this project's Java and C# implementations. It does not speak the original WO Mic phone/client protocol.

## Named remembered-device pairing (Android 0.2.2 / Windows 0.3.1)

The current phone sends `LMIC/3` with the same five hello fields below. The authenticated encrypted acknowledgment is `OK session credentialBase64 computerNameBase64`, where the fourth field contains the Windows machine name encoded as UTF-8 then Base64 (bounded to 80 UTF-16 code units). The name is display metadata, never an authentication key; the random persistent ID identifies the computer. On every successful reconnect, the phone preserves the credential and refreshes the stored name. Old token-only records are enriched without new PIN enrollment.

Windows 0.3.1 accepts both `LMIC/3` and `LMIC/2`. Only a v3 hello receives the fourth acknowledgment field; v2 responses retain exactly three fields for older Android clients. Transcript construction includes the actual hello version; HKDF keeps the existing `LinkMic handshake v2` domain label and all other cryptographic operations remain unchanged. New Android requires Windows 0.3.1; there is no automatic protocol downgrade.

## Remembered-device pairing v2 (Android 0.2.0–0.2.1 / Windows 0.3.0+)

The persistent identity is independent of Bluetooth addresses and Wi-Fi IP addresses. Each installation has a random 16-byte ID encoded as 32 lowercase hex characters; each enrolled phone/computer pair has a random 32-byte credential. Neither side saves the six-digit PIN.

Handshake lines are UTF-8/ASCII, at most 1024 bytes with an 8-second desktop deadline and 10-second phone deadline. Names are sanitized to 1–30 ASCII letters/digits/`.`/`_`/`-`.

1. Phone sends `LMIC/2 phoneId clientEphemeralSPKI clientNonce phoneName\n`. Keys and 32-byte nonces are Base64. The key is ephemeral P-256 ECDH.
2. Desktop replies `CHALLENGE serverId serverEphemeralSPKI serverNonce PAIR|AUTH serverProof\n`. The transcript is the exact hello line without newline followed by `|serverId|serverEphemeralSPKI|serverNonce|PAIR-or-AUTH`.
3. An AUTH challenge contains hex HMAC-SHA256 of `SERVER|transcript` under the saved pair credential. The phone verifies it before authenticating. Unknown or revoked identities require an explicit PIN. For first PAIR, the proof is `-`.
4. Derive a handshake key with HKDF-SHA256: salt = SHA256(UTF8 transcript), IKM = raw 32-byte P-256 shared secret, info = `LinkMic handshake v2`, length = 32. The single expansion block ends in byte `01`.
5. Phone sends `PROOF envelope\n`. AES-256-GCM cleartext is `PAIR six-digit-PIN` or `AUTH HMAC-SHA256(credential, CLIENT|transcript)` in lowercase hex. AAD is `transcript|CLIENT`.
6. After validation, desktop sends `OK2 envelope\n`, containing `OK nonzero-uint32-session credentialBase64`, with AAD `transcript|SERVER`. The credential is generated and persisted only for accepted PIN enrollment; a reconnect reuses the existing credential. The phone commits a new credential only after validating this response.

Each envelope is Base64 of `random-12-byte-nonce || ciphertext || 16-byte-tag`, bounded to 512 decoded bytes. Nonces and ephemeral keys change every handshake; proof and ciphertext replay against a new transcript are rejected. Revocation requires a fresh PIN and rotates the credential. Credentials are protected at rest by current-user Windows DPAPI and an Android Keystore AES-GCM key; writes are atomic.

This is remembered-device authentication, not encrypted audio transport. First-use enrollment assumes a trusted LAN or OS-paired Bluetooth; it is not a full PAKE defense against an active first-use MITM. Audio frames remain version 1 and Wi-Fi media remains plaintext. There is no automatic downgrade from the new Android client.

## Legacy pairing (Android 0.1.0 compatibility)

Android sends ASCII `LMIC/1 <six-digit-code> <sanitized-phone-name>\n`. Windows bounds the line to 128 bytes, applies an 8-second deadline and compares the six-digit code in constant time. Success returns `OK <nonzero-uint32-session>\n`; rejection returns `NO\n` and closes/rejects the peer. The phone has a 10-second handshake deadline.

The random session identifier prevents stale-frame mixups; it is not an encryption key or cryptographic authenticator. RFCOMM uses OS pairing and encryption. Wi-Fi TCP is plaintext in this preview.

## Frame header

All multibyte values are little endian; header size is 32 bytes.

| Offset | Bytes | Field |
| --- | --- | --- |
| 0 | 4 | ASCII `LMIC` |
| 4 | 1 | Version = 1 |
| 5 | 1 | Codec: 0 PCM16, 1 independent IMA ADPCM |
| 6 | 2 | Sample count = 960 |
| 8 | 4 | Sequence uint32, wrapping |
| 12 | 8 | Capture timestamp in microseconds from session capture start |
| 20 | 4 | Sample rate = 48000 |
| 24 | 4 | Payload length: PCM 1920, ADPCM 484 |
| 28 | 4 | Session uint32 from pairing |

Each frame represents 20 ms of mono audio. The receiver validates version, codec, sample count, rate, exact length and session before payload allocation. Partial stream reads are supported; a stalled frame has a 5-second deadline.

ADPCM payload: initial PCM sample int16, initial step index uint8 (0–88), reserved zero uint8, then 959 four-bit IMA codes packed low nibble first. The last byte's unused high nibble is zero. Each frame resets decoder state so dropped frames do not poison subsequent frames. Total packet size is 516 bytes, 25,800 bytes/s (206.4 kbit/s) before transport overhead. Bluetooth bandwidth and latency still require physical measurement.

## Playout

Windows buffers at most five frames, starts after two frames and renders one frame every 20 ms. Overflow drops the oldest whole frame; underflow emits silence and re-enters prebuffering. Sequence comparisons handle uint32 wrap and reject duplicates/older frames. Gain is 0–2 with sample saturation; mute produces zero PCM.

The phone uses a three-frame bounded queue between capture and transmission. Audio is not written to disk in normal operation. Test fixtures are generated synthetic tones, never recordings of the user's microphone.
