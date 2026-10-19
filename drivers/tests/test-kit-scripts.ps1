$ErrorActionPreference='Stop'
# Load the real functions without running Common.ps1's package initialization.
$common=Join-Path (Split-Path $PSScriptRoot -Parent) 'testkit\Common.ps1'
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($common,[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw ($parseErrors | Out-String)}
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
    . ([scriptblock]::Create($definition.Extent.Text))
}
$passed=0
function Assert-Case([bool]$Condition,[string]$Name){
    if(!$Condition){throw "FAIL: $Name"}
    $script:passed++
}

$inventory=Convert-DeviceInventoryJson -Json '[]'
Assert-Case ($inventory.Count -eq 0) 'empty inventory permits first installation'
$report=[pscustomobject]@{linkMicDevices=$inventory}
$serialized=$report|ConvertTo-Json -Depth 5 -Compress
Assert-Case ($serialized -match '"linkMicDevices":\[\]') 'report contains a real empty JSON array'
$roundTrip=ConvertFrom-Json -InputObject $serialized
Assert-Case ($roundTrip.linkMicDevices.Count -eq 0) 'remote report empty array round trip'
$inventory=Convert-DeviceInventoryJson -Json '[{"instanceId":"ROOT\\MEDIA\\0000","inf":"oem1.inf","started":true,"problem":0}]'
Assert-Case ($inventory.Count -eq 1 -and $inventory[0].instanceId -eq 'ROOT\MEDIA\0000') 'one device still prevents untracked overwrite'
$inventory=Convert-DeviceInventoryJson -Json '[{"instanceId":"first"},{"instanceId":"second"}]'
Assert-Case ($inventory.Count -eq 2) 'multiple devices preserved'
foreach($json in @('null','{}','[null]','[{}]','[1]','[')){
    $rejected=$false
    try{$null=Convert-DeviceInventoryJson -Json $json}catch{$rejected=$true}
    Assert-Case $rejected "invalid inventory rejected: $json"
}

# Mock only the OS query boundary. These cases never touch firmware or encryption.
function Get-FirmwareKind {
    if($script:firmware -eq 'Error'){throw 'firmware probe failed'}
    return $script:firmware
}
function Confirm-SecureBootUEFI {
    [CmdletBinding()]param()
    if($script:secureBoot -is [string]){throw 'secure boot access denied'}
    return $script:secureBoot
}
$firmware='UEFI';$secureBoot=$true;$state=Get-SecureBootState
Assert-Case ($state.enabled -eq $true -and $state.supported -eq $true) 'enabled Secure Boot preserved'
$secureBoot=$false;$state=Get-SecureBootState
Assert-Case ($state.enabled -eq $false -and $state.errors.Count -eq 0) 'disabled Secure Boot requires successful query'
$secureBoot='Error';$state=Get-SecureBootState
Assert-Case ($null -eq $state.enabled -and $state.errors[0] -match 'access denied') 'failed Secure Boot query remains unknown with error'
$firmware='BIOS';$state=Get-SecureBootState
Assert-Case ($state.enabled -eq $false -and $state.supported -eq $false) 'legacy BIOS distinguished from failed UEFI query'
$firmware='Error';$secureBoot=$true;$state=Get-SecureBootState
Assert-Case ($state.enabled -eq $true -and $state.errors[0] -match 'firmware probe failed') 'successful Secure Boot query survives firmware query error'
$secureBoot='Error';$state=Get-SecureBootState
Assert-Case ($null -eq $state.enabled -and $state.errors.Count -eq 2) 'both query failures remain unknown'

function Get-BitLockerVolume {
    [CmdletBinding()]param($MountPoint)
    if($script:bitLockerPrimaryFails){throw 'BitLocker module unavailable'}
    [pscustomobject]@{ProtectionStatus='Off';VolumeStatus='FullyEncrypted'}
}
function Get-CimInstance {
    [CmdletBinding()]param($Namespace,$ClassName,$Filter)
    if($script:bitLockerWmiFails){throw 'BitLocker WMI access denied'}
    [pscustomobject]@{DriveLetter='C:'}
}
function Invoke-CimMethod {
    [CmdletBinding()]param($InputObject,$MethodName)
    [pscustomobject]@{ReturnValue=$script:wmiReturn;ProtectionStatus=$script:wmiProtection}
}
$bitLockerPrimaryFails=$false;$bitLockerWmiFails=$false;$wmiReturn=0;$wmiProtection=1
$state=Get-BitLockerState
Assert-Case ($state.protection -eq 'Off' -and $state.encryption -eq 'FullyEncrypted') 'protection Off does not erase encryption status'
$bitLockerPrimaryFails=$true;$state=Get-BitLockerState
Assert-Case ($state.protection -eq 'On' -and $state.source -match 'Win32_EncryptableVolume') 'WMI fallback detects protection On'
$wmiProtection=0;$state=Get-BitLockerState
Assert-Case ($state.protection -eq 'Off' -and $state.encryption -eq 'Unknown') 'WMI Off does not claim unencrypted'
$wmiProtection=2;$state=Get-BitLockerState
Assert-Case ($state.protection -eq 'Unknown') 'WMI unknown preserved'
$wmiProtection=$null;$state=Get-BitLockerState
Assert-Case ($state.protection -eq 'Unknown' -and $state.errors.Count -eq 2) 'missing WMI status never becomes Off'
$wmiProtection=1;$wmiReturn=5;$state=Get-BitLockerState
Assert-Case ($state.protection -eq 'Unknown' -and $state.errors[1] -match 'returned 5') 'WMI failure return value preserved'
$bitLockerWmiFails=$true;$state=Get-BitLockerState
Assert-Case ($state.protection -eq 'Unknown' -and $state.errors[1] -match 'access denied') 'all BitLocker query failures remain unknown'
[pscustomobject]@{passed=$passed;powerShellVersion=$PSVersionTable.PSVersion.ToString();systemChangesMade=$false}|ConvertTo-Json
