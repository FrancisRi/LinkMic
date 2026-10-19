# Build and publish

Build with `scripts/build.ps1` and run the relevant checks in the README. Keep the root MIT license, third-party notices and RNNoise license/provenance in the Windows ZIP. Android embeds LinkMic's MIT license and Apache dependency notices in its assets.

`scripts/package.ps1` packages a Windows build and, when present, the Android APK. It can optionally include the vendor's original, unmodified VB-CABLE ZIP at `third-party/vb-cable/VBCABLE_Driver_Pack45.zip`, together with its original license and origin/donationware notice. This remains separately licensed and must not become an automatic installation procedure. Otherwise users download VB-CABLE from its author. The experimental MS-PL driver is not required or included in a normal release.

Tag the tested source commit and upload the Windows ZIP, Android APK and SHA256SUMS.txt as GitHub Release assets. Mark development builds as prereleases. Keep generated binaries out of Git. Never upload signing keys, caches, captured audio, real pairing records, reverse-engineering materials, or machine environment reports.

The first public preview uses Windows 0.3.3 / Android 0.2.3. The Gradle wrapper is Apache-2.0; its distribution checksum is pinned in `android/gradle/wrapper/gradle-wrapper.properties`.
