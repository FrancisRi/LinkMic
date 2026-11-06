# Current release and verification

Windows **0.3.4**, Android **0.2.4**. First public source publication: 2026-10-19.

The tested v0.3.3 release was promoted from prerelease to a regular GitHub Release on 2026-10-19 and selected as Latest. It retains the same tagged application source, Windows ZIP, Android APK, signing certificate and checksums. Promotion changes release metadata and documentation, not the application binaries. Android still uses the existing development signing certificate.

Windows provides Bluetooth/Wi-Fi reception, Fluent 2/system theme support and optional RNNoise. Android uses Material 3, per-second duration, persistent named-computer pairing and Chinese/English selected by the first system language. Both apps now distinguish an explicit disconnect from an unexpected link loss.

Android tests cover desktop-initiated disconnect, phone-initiated disconnect, legacy closure and unexpected EOF, along with the existing UI checks. Windows regression checks exercise the real buttons and receiver over loopback, with an audio sink fixture. Java/.NET loopback checks also cover encoded audio, v3 authentication and older v2 clients. See `scripts/verify-android.ps1` and `scripts/verify-windows.ps1`.

Publication verification caught a window-close race: stopping reception can complete synchronously inside WPF's Closing event, so calling Close again on the same stack throws InvalidOperationException. Windows 0.3.3 queues the final close through the Dispatcher. The regression repeats the actual close-during-session scenario ten times, alongside stop, peer-disconnect, output routing and theme checks.

These automated checks do not certify physical Bluetooth, microphone, installed driver, or end-to-end acoustic performance. Run the checks in your environment. `artifacts/tests` is generated locally, not versioned. The optional `drivers/` tree has separate MS-PL licensing and is not the recommended installation path.
