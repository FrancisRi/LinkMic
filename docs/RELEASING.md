# Build and publish

Build with `scripts/build.ps1` and run the relevant checks in the README. Keep the root MIT license, third-party notices and RNNoise license/provenance in the Windows ZIP. Android embeds LinkMic's MIT license and Apache dependency notices in its assets.

`scripts/package.ps1` puts the full self-contained WinUI 3 publish output from `artifacts/windows-winui/` into the Windows ZIP and includes the Android APK in the checksum list when available. The Windows App SDK runtime files and notices must remain alongside the app. The script can optionally include the vendor's original, unmodified VB-CABLE ZIP at `third-party/vb-cable/VBCABLE_Driver_Pack45.zip`, together with its original license and origin/donationware notice. Users install VB-CABLE separately. The experimental MS-PL driver is optional.

Tag the tested source commit and upload the Windows ZIP, Android APK and SHA256SUMS.txt as GitHub Release assets. Mark development builds as prereleases. A regular release must have `draft=false` and `prerelease=false`; select it as Latest after checking its tag, attachments and SHA-256 digests. An existing tested prerelease can be promoted without replacing its tag or binaries. Keep generated binaries out of Git. Never upload signing keys, caches, captured audio, real pairing records, reverse-engineering materials, or machine environment reports.

The first regular GitHub Release is v0.3.3 (Windows 0.3.3 / Android 0.2.3), promoted on 2026-10-19. Its Android APK keeps the original development certificate for update compatibility; GitHub release status does not change signing identity. A future production signing identity is a separate decision and must account for existing installations.

The Gradle wrapper is Apache-2.0; its distribution checksum is pinned in `android/gradle/wrapper/gradle-wrapper.properties`.
