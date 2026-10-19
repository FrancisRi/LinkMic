. "$PSScriptRoot\env.ps1"
if(!(Test-Path -LiteralPath "$ProjectRoot\native\win-x64\rnnoise.dll")){ & "$PSScriptRoot\build-rnnoise.ps1" }
$env:LINKMIC_LOG_DIR = Join-Path $ProjectRoot 'artifacts\tests\logs'
$env:LINKMIC_STATE_DIR = Join-Path $ProjectRoot 'artifacts\tests\device-state'
$tests = Join-Path $ProjectRoot 'artifacts\tests'
if (!(Test-Path "$tests\android-frames.bin")) { & "$PSScriptRoot\test.ps1" }
& $Dotnet build "$ProjectRoot\src\LinkMic.Windows\LinkMic.Windows.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
& $Dotnet "$ProjectRoot\src\LinkMic.Windows\bin\Release\net10.0-windows\LinkMic.dll" --rnnoise-check $tests
if ($LASTEXITCODE -ne 0) { throw "RNNoise native/UI regression failed; inspect $tests\rnnoise-error.txt" }
& $Dotnet "$ProjectRoot\src\LinkMic.Windows\bin\Release\net10.0-windows\LinkMic.dll" --pairing-store-check $tests
if ($LASTEXITCODE -ne 0) { throw 'Protected device trust store regression failed' }
& $Dotnet "$ProjectRoot\src\LinkMic.Windows\bin\Release\net10.0-windows\LinkMic.dll" --output-check $tests
if ($LASTEXITCODE -ne 0) { throw 'Output routing regression failed' }
& $Dotnet "$ProjectRoot\src\LinkMic.Windows\bin\Release\net10.0-windows\LinkMic.dll" --theme-check $tests
if ($LASTEXITCODE -ne 0) { throw "Windows theme/receiver check failed; inspect $tests\theme-error.txt" }
& $Dotnet "$ProjectRoot\src\LinkMic.Windows\bin\Release\net10.0-windows\LinkMic.dll" --disconnect-check $tests
if ($LASTEXITCODE -ne 0) { throw "Windows disconnect check failed; inspect $tests\disconnect-error.txt" }
& $Dotnet run --project "$ProjectRoot\tests\DisconnectProbe\DisconnectProbe.csproj" -c Release -- "$tests\disconnect-after.json"
if ($LASTEXITCODE -ne 0) { throw 'Bluetooth native disposal regression failed' }
Get-Content "$tests\theme-results.json"
