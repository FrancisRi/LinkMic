# LinkMic

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

把 Android 手机变成 Windows 电脑的麦克风，支持 **蓝牙和 Wi-Fi**。电脑端采用 Fluent 2 与原生 Windows 窗口，安卓端使用 Material 3。

Use your Android phone as a Windows microphone over **Bluetooth or Wi-Fi**. The desktop app uses Fluent 2/native Windows windows; Android uses Material 3 and follows the first system language: Chinese for Chinese, English otherwise.

## 下载与使用

从 [GitHub Releases](https://github.com/FrancisRi/LinkMic/releases) 下载 Windows **0.3.3** 和 Android **0.2.3**。目前为开发预览，Android APK 使用开发签名。

1. Windows：完整解压 ZIP，运行 `LinkMic.exe`，保留旁边的 `rnnoise.dll`。
2. Android：安装 APK，允许麦克风及所用连接方式的权限；从旧版升级请覆盖安装以保留配对。
3. 蓝牙：先在系统设置中配对手机和电脑。手机选择蓝牙并开始连接，电脑刷新、选中该手机并开始接收。
4. Wi-Fi：两端处于同一可信局域网。电脑选择 Wi-Fi 并开始接收，手机填写电脑的 IPv4 地址后连接。使用 TCP 42791。
5. 首次连接输入电脑显示的六位配对码；已记住的电脑下次免码。安卓右上角可查看电脑名称并选择遗忘。

连接后显示本次连接时长。电脑端支持输入增益、静音以及默认关闭、可随时切换的 **RNNoise 背景降噪**。两端适配系统深浅色；手机切换语言后页面和录音通知同步翻译。

普通扬声器输出用于监听。要让会议/录音软件使用手机声音，请另外安装 [VB-CABLE](https://vb-audio.com/Cable/)，LinkMic 选择 **CABLE Input**，会议软件的麦克风选择 **CABLE Output**。VB-CABLE 是独立许可的 donationware，欢迎向原作者[捐赠或购买许可](https://shop.vb-audio.com/)。无需修改 Windows 默认播放设备。

首次配对请在可信网络中完成。凭据由 Windows DPAPI / Android Keystore 加密保存；Wi-Fi 音频流目前没有加密。正常使用不需要 REA、Ghidra、ADB、USB 调试或自行编译驱动。

## Build from source

Clone this repository, then install the tools for your target:

| Target | Requirements |
| --- | --- |
| Windows | Windows x64, .NET SDK 10.0.402 (or compatible 10.0.4xx patch), Visual Studio C++ Build Tools and Windows SDK for RNNoise |
| Android | JDK 21 (Java 17 language level), Android SDK platform 35 and build-tools 35.0.0; Gradle 8.13 wrapper is included |

Windows PowerShell commands from the repository root:

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

Or open `android/` in Android Studio and `LinkMic.slnx` in a .NET 10-capable IDE. On Linux/macOS use `cd android && ./gradlew testDebugUnitTest lintDebug assembleDebug` after configuring the Android SDK. The first build needs internet for dependencies and RNNoise's pinned source/model.

`scripts/env.ps1` respects existing `JAVA_HOME`, `ANDROID_HOME`/`ANDROID_SDK_ROOT`, `DOTNET_ROOT` and cache settings. Optional portable tools can be selected with `LINKMIC_TOOLS_ROOT`; installed tools also work. No particular checkout drive or REA workspace is required. RNNoise downloads are verified against `third-party/rnnoise/provenance.json`. The normal application does not need the WDK.

Outputs are under `artifacts/`. See [release instructions](docs/RELEASING.md). Android debug signing uses your local development key. A build on another machine cannot overwrite an installation signed with a different key without uninstalling it first. Never commit signing keys.

## Source layout

| Path | Purpose |
| --- | --- |
| `src/LinkMic.Core` | Pairing, framing, IMA ADPCM and playout buffering |
| `src/LinkMic.Windows` | WPF receiver, Fluent UI, Bluetooth/Wi-Fi, output and optional RNNoise |
| `android/app` | Java microphone service, Material 3 UI and remembered devices |
| `tests`, `scripts` | Cross-language checks, builds and verification |
| `drivers` | Optional experimental WaveRT driver, separately licensed under MS-PL |
| `third-party` | License texts and pinned dependency provenance |

Audio path: AudioRecord 48 kHz mono PCM16 → independent 20 ms IMA ADPCM frames → Bluetooth RFCOMM or TCP → Windows session checks → bounded playout → optional RNNoise → gain/mute → selected playback or virtual-cable device.

See the [protocol](docs/PROTOCOL.md), [RNNoise](docs/RNNOISE.md), [remembered computers](docs/PAIRED-DEVICES.md), [Android update](docs/ANDROID-0.2.3.md) and [verification status](docs/STATUS.md). There is currently no automatic reconnect, discovery, encrypted Wi-Fi audio, adaptive clock resampling or WebRTC AEC3.

## License

LinkMic's original application code and documentation use the **[MIT License](LICENSE)**, copyright © 2026 FrancisRi.

Third-party components retain their licenses: RNNoise uses its upstream BSD-style license; Android dependencies use Apache-2.0; the experimental Microsoft-sample-based driver uses [MS-PL](drivers/LICENSE). VB-CABLE is proprietary donationware and is not covered by MIT. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). No WO Mic binary or decompiled code is included.
