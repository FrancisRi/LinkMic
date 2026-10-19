param([switch]$Apply)
. "$PSScriptRoot\Common.ps1"
Assert-Package
if(!$Apply){Write-Host 'PLAN: remove only ROOT\LinkMicCapture device(s) created by this kit, then remove its exact public certificate if it was not present before installation. Leaves boot configuration and Driver Store unchanged.';return}
Assert-Administrator
$statePath=Join-Path $PSScriptRoot 'test-state.json'
if(!(Test-Path -LiteralPath $statePath)){throw 'No kit installation state; refusing to remove an untracked device or certificate'}
$state=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
if($state.certificateThumbprint -ne $Manifest.certificateThumbprint){throw 'Certificate state mismatch'}
if($state.installationAttempted){& "$PSScriptRoot\LinkMicDriverTools.exe" remove;if($LASTEXITCODE -ne 0){throw 'Device removal failed; certificate trust retained'}}
$thumb=$Manifest.certificateThumbprint
if(!$state.rootPreexisted -and (Test-Path "Cert:\LocalMachine\Root\$thumb")){Remove-Item -LiteralPath "Cert:\LocalMachine\Root\$thumb"}
if(!$state.publisherPreexisted -and (Test-Path "Cert:\LocalMachine\TrustedPublisher\$thumb")){Remove-Item -LiteralPath "Cert:\LocalMachine\TrustedPublisher\$thumb"}
Write-Host 'Kit device and newly added certificate trust removed. Driver Store, TESTSIGNING, firmware and BitLocker were not modified. See README-TEST.md for deliberate final cleanup.'
