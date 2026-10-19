# Current release and verification

Windows **0.3.3**, Android **0.2.3**. First public source publication: 2026-10-19.

The tested v0.3.3 release was promoted from prerelease to a regular GitHub Release on 2026-10-19 and selected as Latest. It retains the same tagged application source, Windows ZIP, Android APK, signing certificate and checksums. Promotion changes release metadata and documentation, not the application binaries. Android still uses the existing development signing certificate.

Windows provides Bluetooth/Wi-Fi reception, Fluent 2/system theme support and optional RNNoise. Android uses Material 3, per-second duration, persistent named-computer pairing and Chinese/English selected by the first system language. The latest Android change fixes segmented-button vertical alignment.

Android 0.2.3 passed 27 Robolectric tests, Lint (zero errors) and signature verification. Java/.NET loopback checks cover encoded audio, v3 authentication and older v2 clients. Windows has RNNoise, output routing, DPAPI trust-store, theme and disconnect checks in `scripts/verify-windows.ps1`.

Publication verification caught a window-close race: stopping reception can complete synchronously inside WPF's Closing event, so calling Close again on the same stack throws InvalidOperationException. Windows 0.3.3 queues the final close through the Dispatcher. The regression repeats the actual close-during-session scenario ten times, alongside stop, peer-disconnect, output routing and theme checks.

These automated checks do not certify physical Bluetooth, microphone, installed driver, or end-to-end acoustic performance. Run the checks in your environment. `artifacts/tests` is generated locally, not versioned. The optional `drivers/` tree has separate MS-PL licensing and is not the recommended installation path.
