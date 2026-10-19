# Native audio processing

`scripts/build-rnnoise.ps1` restores the pinned RNNoise source/model from their original URLs, checks both SHA-256 hashes and builds `win-x64/rnnoise.dll`. Generated sources, models, object files and DLLs are excluded from Git. See `third-party/rnnoise/provenance.json` and `LICENSE.txt` there for the upstream license and source identities.

Use an x64 Visual Studio Developer PowerShell with the C++ Build Tools and Windows SDK, or set `LINKMIC_TOOLS_ROOT` to a portable tool directory containing the layout used by the build script. The normal application does not require the WDK.
