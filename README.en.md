# LinkMic

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[简体中文](README.md) | English

Use your Android phone as a Windows microphone over **Bluetooth or Wi-Fi**. The desktop app uses Fluent 2 with the native Windows window frame; the Android app uses Material 3.

## Download and setup

Download Windows **0.3.3** and Android **0.2.3** from the [latest release](https://github.com/FrancisRi/LinkMic/releases/latest). The Android APK retains the existing development signing certificate and can update an earlier installation signed with the same certificate. See the [changelog](CHANGELOG.md).

1. **Windows:** Extract the entire ZIP and run `LinkMic.exe`. Keep `rnnoise.dll` in the same directory.
2. **Android:** Install the APK and grant microphone access and the permissions requested for your connection method. When upgrading, install over the existing app to retain paired computers.
3. **Bluetooth:** Pair the phone and computer in their system Bluetooth settings first. On the phone, select Bluetooth and tap **Connect**. On the computer, refresh the paired-device list, select the phone, and start receiving.
4. **Wi-Fi:** Connect both devices to the same trusted local network. Select Wi-Fi on the computer and start receiving, then enter the computer's IPv4 address in the phone app and connect. LinkMic uses TCP port **42791**.
5. **Pairing:** For the first connection, enter the six-digit code displayed by the computer. Remembered computers reconnect without a code. The menu at the top right of the Android app lists paired computer names and lets you select which ones to forget.

The app displays the elapsed time of the current connection. On Windows, you can adjust input gain, mute audio, and toggle **RNNoise background noise reduction** before or during a connection. RNNoise is off by default. Both apps support system light and dark modes. Android follows the phone's first system language: Chinese for Chinese, English for every other language. The interface and recording notification update when the system language changes.

## Use LinkMic in a meeting or recording app

An ordinary speaker output lets you monitor the received audio. To use the phone's audio as a microphone in another app, install [VB-CABLE](https://vb-audio.com/Cable/) separately:

1. Select **CABLE Input** as LinkMic's output device.
2. Select **CABLE Output** as the microphone in your meeting or recording app.

You do not need to change the default Windows playback device. VB-CABLE is separately licensed donationware; please consider [donating or purchasing a license](https://shop.vb-audio.com/) from its author.

Perform first-time pairing on a trusted network. Device credentials are encrypted at rest using Windows DPAPI and Android Keystore; the Wi-Fi audio stream is currently **not encrypted**. Normal use does not require ADB, USB debugging, or compiling a driver.

## Build from source

Clone this repository, then install the tools for your target:

| Target | Requirements |
| --- | --- |
| Windows | Windows x64, .NET SDK 10.0.402 (or a compatible 10.0.4xx patch), Visual Studio C++ Build Tools and Windows SDK for RNNoise |
| Android | JDK 21 (Java 17 language level), Android SDK platform 35 and build-tools 35.0.0; the Gradle 8.13 wrapper is included |

Run these PowerShell commands from the repository root on Windows:

```powershell
# With JAVA_HOME and ANDROID_HOME set:
./scripts/build.ps1 -Target Android

# Run in an x64 Visual Studio Developer PowerShell:
./scripts/build.ps1 -Target Windows

# Relevant checks (no ADB or driver installation):
./scripts/test.ps1
./scripts/verify-android.ps1
./scripts/verify-windows.ps1
```

You can also open `android/` in Android Studio and `LinkMic.slnx` in an IDE that supports .NET 10. On Linux or macOS, configure the Android SDK and run:

```sh
cd android
./gradlew testDebugUnitTest lintDebug assembleDebug
```

The first build requires internet access to restore dependencies and download RNNoise's pinned source and model.

`scripts/env.ps1` respects existing `JAVA_HOME`, `ANDROID_HOME`/`ANDROID_SDK_ROOT`, `DOTNET_ROOT`, and cache settings. Set `LINKMIC_TOOLS_ROOT` to use an optional portable tool directory; installed tools also work. RNNoise downloads are verified against the hashes in `third-party/rnnoise/provenance.json`. Building the normal application does not require the WDK.

Build outputs are written to `artifacts/`. See the [release instructions](docs/RELEASING.md) for packaging and publishing. Android debug builds use your local development key. If a build is signed with a different key, you must uninstall the existing app before installing it. Never commit signing keys.

## Source layout

| Path | Purpose |
| --- | --- |
| `src/LinkMic.Core` | Pairing, framing, IMA ADPCM, and playout buffering |
| `src/LinkMic.Windows` | WPF receiver, Fluent UI, Bluetooth/Wi-Fi, audio output, and optional RNNoise |
| `android/app` | Java microphone service, Material 3 UI, and remembered computers |
| `tests`, `scripts` | Cross-language checks, builds, and verification |
| `drivers` | Optional experimental WaveRT driver, separately licensed under MS-PL |
| `third-party` | License texts and pinned dependency provenance |

Audio path: AudioRecord at 48 kHz, mono PCM16 → independent 20 ms IMA ADPCM frames → Bluetooth RFCOMM or TCP → Windows session checks → bounded playout → optional RNNoise → gain/mute → selected playback or virtual-cable device.

See the [protocol](docs/PROTOCOL.md), [RNNoise documentation](docs/RNNOISE.md), [remembered computers](docs/PAIRED-DEVICES.md), [Android update notes](docs/ANDROID-0.2.3.md), and [verification status](docs/STATUS.md). Some detailed documents are currently available in Chinese. LinkMic currently has no automatic reconnection, device discovery, encrypted Wi-Fi audio, adaptive clock resampling, or WebRTC AEC3.

## License

LinkMic's original application code and documentation are released under the **[MIT License](LICENSE)**, copyright © 2026 FrancisRi.

Third-party components retain their own licenses:

- RNNoise uses its upstream BSD-style license.
- Android dependencies use Apache-2.0.
- The experimental driver based on Microsoft's sample uses [MS-PL](drivers/LICENSE).
- VB-CABLE is proprietary donationware and is not covered by MIT.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). No WO Mic binary or decompiled code is included.
