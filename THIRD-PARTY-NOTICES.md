# Third-party software

LinkMic's original application code is MIT-licensed; see the root `LICENSE`. Third-party components and the experimental driver keep the licenses described below. The Gradle wrapper uses Apache-2.0, copyright the Gradle authors; the complete license is in `docs/licenses/Apache-2.0.txt`, source: https://github.com/gradle/gradle/tree/v8.13.0/gradle/wrapper .

LinkMic's Windows self-contained build includes the .NET 10.0.13 runtime and Windows Desktop/WPF runtime. Their original license and notice files are copied into the distribution's `licenses` directory. The SDK, JDK, Gradle, Android SDK and REA are development tools and are not included in the product archive.

NuGet dependencies bundled in the Windows executable:

| Component | Version | License / copyright |
| --- | --- | --- |
| InTheHand.Net.Bluetooth / 32feet.NET | 4.2.5 | MIT; Copyright (c) 2004–2026 In The Hand Ltd |
| Linux.Bluetooth | 5.67.1 | Apache-2.0; Copyright 2024 Xeno Innovations, Inc. |
| Tmds.DBus | 0.95.1 | MIT; Tom Deseyn; Alp Toker |

The generic .NET Bluetooth package carries Windows and Linux providers. Its Linux dependencies are included by NuGet even though this product targets Windows. Their licenses are retained.

## MIT license — InTheHand.Net.Bluetooth and Tmds.DBus

Copyright (c) 2004–2026 In The Hand Ltd

Copyright (c) Tom Deseyn; Alp Toker

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Apache-2.0 — Linux.Bluetooth

The full Apache License 2.0 is included in `licenses/Apache-2.0.txt`. Project: https://github.com/SuessLabs/Linux.Bluetooth . Package metadata identifies copyright 2024 Xeno Innovations, Inc.

## Android UI dependencies

The Android APK includes Material Components for Android 1.13.0 (Google / The Android Open Source Project), AndroidX libraries and the Kotlin standard library (JetBrains and contributors). These components use the Apache License, Version 2.0. The APK includes attribution and the full license in `assets/THIRD-PARTY-NOTICES.txt` and `assets/Apache-2.0.txt`. Sources: https://github.com/material-components/material-components-android , https://developer.android.com/jetpack/androidx , https://github.com/JetBrains/kotlin . The resolved runtime inventory is recorded in `artifacts/tests/android-runtime-dependencies.txt`. Robolectric and JUnit are test-only dependencies and are not distributed in the APK.

## RNNoise v0.2 (Windows 0.3.2+)

The Windows package includes `rnnoise.dll`, built from the official Xiph RNNoise v0.2 source at commit `904a876dce1f9ab8860c0a5000ed151f9f6eef58`, with the upstream default model `0b50c45` embedded. Source: https://github.com/xiph/rnnoise/tree/v0.2 ; model: https://media.xiph.org/rnnoise/models/rnnoise_data-0b50c45.tar.gz .

RNNoise uses a BSD-style license. Copyright holders include Jean-Marc Valin, Amazon, Mozilla, Xiph.Org Foundation and Mark Borgerding. The complete upstream copyright/license is included in `licenses/RNNoise-LICENSE.txt`; pinned source/model hashes and build details are in `licenses/RNNoise-provenance.json`. The DLL uses static CRT linkage and the Windows x64 SSE2 baseline, and does not require an AVX2-capable CPU or a separate VC runtime installation.

## External drivers

No WO Mic executable, APK or driver is bundled. The application uses ordinary Windows audio playback APIs to route phone audio into an installed virtual audio cable, or explicitly selected monitor speakers. An installed experimental LinkMic capture driver remains optional.

Windows 0.2.1 includes the **unmodified original** `VBCABLE_Driver_Pack45.zip` from VB-Audio / Vincent Burel under `drivers/vb-cable/`. VB-CABLE is proprietary donationware, not covered by LinkMic's source licenses. Origin: https://vb-audio.com/Cable/ and https://www.vb-cable.com/ . **VB-CABLE is donationware; donations and license purchases are welcome at https://shop.vb-audio.com/ .** Its original `readme.txt` license is preserved inside the original ZIP and also copied alongside it. The end user separately extracts and runs the vendor's setup; LinkMic does not silently install it, rename its endpoints or modify the signed files. Commercial/organizational distribution must follow the vendor's applicable licensing terms: https://vb-audio.com/Services/licensing.htm .

The Windows 10/11 x64 driver version is 3.3.1.7. Its Microsoft-signed catalog, driver/INF catalog membership and installer Authenticode signatures were verified; records and hashes accompany the ZIP. This does not claim an installed-device, HVCI or hardware audio test. No MicYou GPL source or VirtualDrivers driver binary is included.

Windows 0.2.0 removes the WO Mic compatibility adapter. The separate experimental LinkMic driver derives from Microsoft's Windows-driver-samples SimpleAudioSample at the commit recorded in `drivers/vendor/provenance.json`. That sample uses the Microsoft Public License (MS-PL); its original license and notices accompany the driver sources and binary package. It is separate from the MIT-licensed Bluetooth dependencies above.
