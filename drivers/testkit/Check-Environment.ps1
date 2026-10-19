param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'results'))
. "$PSScriptRoot\Common.ps1"
Assert-Package
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$secureBoot=Get-SecureBootState
$bitlocker=Get-BitLockerState
$testMode=$null;$hvci=$null;$queryErrors=@()
try{$testMode=Get-TestMode}catch{$queryErrors+="CodeIntegrity: $($_.Exception.Message)"}
try{$guard=Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop;$hvci=($guard.SecurityServicesRunning -contains 2)}catch{$queryErrors+="DeviceGuard: $($_.Exception.Message)"}
$version=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$devices=& "$PSScriptRoot\LinkMicDriverTools.exe" inspect
if($LASTEXITCODE -ne 0){throw 'Device inventory failed'}
$inventory=Convert-DeviceInventoryJson -Json ($devices -join [Environment]::NewLine)
$report=[ordered]@{
    testKitVersion=$Manifest.version;powerShellVersion=$PSVersionTable.PSVersion.ToString();isAdministrator=(Test-Administrator);
    capturedAt=[DateTimeOffset]::Now.ToString('o');build=[int]$version.CurrentBuildNumber;revision=$version.UBR;
    architecture=$env:PROCESSOR_ARCHITECTURE;secureBoot=$secureBoot.enabled;testMode=$testMode;memoryIntegrityRunning=$hvci;
    bitLockerProtection=$bitlocker.protection;secureBootDetails=$secureBoot;bitLockerDetails=$bitlocker;queryErrors=$queryErrors;
    testCertificateThumbprint=$Manifest.certificateThumbprint;
    certificateTrustedRoot=(Test-Path "Cert:\LocalMachine\Root\$($Manifest.certificateThumbprint)");
    certificateTrustedPublisher=(Test-Path "Cert:\LocalMachine\TrustedPublisher\$($Manifest.certificateThumbprint)");
    linkMicDevices=$inventory;packageIntegrityVerified=$true;changesMade=$false
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'environment.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 8
Write-Host "Read-only report: $OutputDirectory\environment.json"
