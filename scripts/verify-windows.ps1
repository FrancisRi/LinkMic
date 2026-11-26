. "$PSScriptRoot\env.ps1"
$env:LINKMIC_LOG_DIR = Join-Path $ProjectRoot 'artifacts\tests\logs'
$env:LINKMIC_STATE_DIR = Join-Path $ProjectRoot 'artifacts\tests\device-state'
$tests = Join-Path $ProjectRoot 'artifacts\tests'
if (!(Test-Path "$tests\android-frames.bin")) { & "$PSScriptRoot\test.ps1" }
& "$PSScriptRoot\build.ps1" -Target Windows
$exe = Join-Path $ProjectRoot 'artifacts\windows-winui\LinkMic.exe'

function Invoke-LinkMicCheck([string]$Command, [string]$Report) {
    $started = [DateTime]::UtcNow
    $process = Start-Process -FilePath $exe -ArgumentList $Command, ('"{0}"' -f $tests) -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "$Command failed (exit $($process.ExitCode)). See $tests" }
    $result = Get-Item -LiteralPath (Join-Path $tests $Report) -ErrorAction SilentlyContinue
    if (!$result -or $result.LastWriteTimeUtc -lt $started -or !(Get-Content -LiteralPath $result.FullName -Raw | ConvertFrom-Json).passed) {
        throw "$Command did not produce a passing $Report"
    }
}

Invoke-LinkMicCheck '--rnnoise-check' 'rnnoise-results.json'
Invoke-LinkMicCheck '--pairing-store-check' 'device-trust-store-results.json'
Invoke-LinkMicCheck '--output-check' 'output-routing-results.json'
Invoke-LinkMicCheck '--theme-check' 'theme-results.json'
Invoke-LinkMicCheck '--disconnect-check' 'ui-disconnect-results.json'
& $Dotnet run --project "$ProjectRoot\tests\DisconnectProbe\DisconnectProbe.csproj" -c Release -- "$tests\disconnect-after.json"
if ($LASTEXITCODE -ne 0) { throw 'Bluetooth native disposal regression failed' }
Get-Content "$tests\theme-results.json"
