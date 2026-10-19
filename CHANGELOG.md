# Changelog

## v0.3.3 — 2026-10-19

First regular GitHub Release, promoted from the verified public preview. Windows **0.3.3**, Android **0.2.3**. Original application code is MIT-licensed; third-party licenses remain unchanged.

- Bluetooth RFCOMM and Wi-Fi microphone streaming from Android to Windows.
- Fluent 2 desktop UI, system light/dark themes, gain, mute and optional RNNoise background noise reduction.
- Material 3 Android UI, per-second connection duration, named remembered computers and selective forgetting.
- Chinese UI when Chinese is the phone's first system language; English for other first languages, including recording notifications.
- Fix vertical alignment of Android Bluetooth/Wi-Fi buttons.
- Fix Windows close-event reentrancy when cancellation completes synchronously. The regression exercises actual close-during-session ten times.

Validation: 27 Android tests, Lint, signature continuity, Java/.NET audio and pairing loopback, Windows audio-processing/theme/trust-store checks and 17 session/disconnect cases. Release attachments and license notices were verified. These checks do not replace a physical Bluetooth/audio endurance test of this build.

The promotion retains the existing release files, download URLs and Android development certificate. Android's APK remains named `LinkMic-Android-0.2.3-debug.apk` so its build/signing status stays explicit.
