param([switch]$Apply)
. "$PSScriptRoot\Common.ps1"
Assert-Package
if(!$Apply){Write-Host 'PLAN: trust only the supplied development certificate in LocalMachine Root/TrustedPublisher, create/update ROOT\LinkMicCapture and install the test-signed package. No boot or firmware settings are changed. Run with -Apply only after checking environment.json.';return}
Assert-Administrator
$version=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
if([int]$version.CurrentBuildNumber -lt 22621 -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64'){throw 'Requires Windows 11 build 22621+ x64'}
if(!(Get-TestMode)){throw 'TESTSIGNING is not active. No certificate or device changes were made. Read README-TEST.md.'}
$secureBoot=Get-SecureBootState
if($null -eq $secureBoot.enabled){throw 'Cannot determine Secure Boot; run Check-Environment.ps1 as administrator and review secureBootDetails.'}
if($secureBoot.enabled){throw 'Secure Boot is enabled; no automatic firmware or boot policy change will be attempted.'}
$thumb=$Manifest.certificateThumbprint
$statePath=Join-Path $PSScriptRoot 'test-state.json'
if(Test-Path -LiteralPath $statePath){$state=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json}
else{
    $existing=& "$PSScriptRoot\LinkMicDriverTools.exe" inspect
    if($LASTEXITCODE -ne 0){throw 'Device inventory failed'}
    $inventory=Convert-DeviceInventoryJson -Json ($existing -join [Environment]::NewLine)
    if($inventory.Count -ne 0){throw 'An existing LinkMic device was found. Remove it deliberately before first test-kit installation; this kit will not overwrite an untracked installation.'}
    $state=[pscustomobject]@{certificateThumbprint=$thumb;rootPreexisted=(Test-Path "Cert:\LocalMachine\Root\$thumb");publisherPreexisted=(Test-Path "Cert:\LocalMachine\TrustedPublisher\$thumb");installationAttempted=$false}
    $state|ConvertTo-Json|Set-Content -LiteralPath $statePath -Encoding UTF8
}
if($state.certificateThumbprint -ne $thumb){throw 'State belongs to another test certificate'}
if(!(Test-Path "Cert:\LocalMachine\Root\$thumb")){Import-Certificate -FilePath "$PSScriptRoot\LinkMic-Test.cer" -CertStoreLocation Cert:\LocalMachine\Root | Out-Null}
if(!(Test-Path "Cert:\LocalMachine\TrustedPublisher\$thumb")){Import-Certificate -FilePath "$PSScriptRoot\LinkMic-Test.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null}
$state.installationAttempted=$true;$state|ConvertTo-Json|Set-Content -LiteralPath $statePath -Encoding UTF8
& "$PSScriptRoot\LinkMicDriverTools.exe" install "$PSScriptRoot\LinkMicCapture.inf"
if($LASTEXITCODE -ne 0){throw 'Installation failed. Keep test-state.json and collect results; use Remove-TestDriver.ps1 -Apply to remove kit trust.'}
& "$PSScriptRoot\Check-Environment.ps1"
Write-Host 'If rebootRequired was true, restart manually before audio tests. No restart was initiated.'
