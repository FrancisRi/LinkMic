$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$KitDirectory=$PSScriptRoot
$Manifest=Get-Content -LiteralPath (Join-Path $KitDirectory 'manifest.json') -Raw | ConvertFrom-Json
function Test-Administrator {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    $principal=New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Assert-Administrator {
    if(!(Test-Administrator)){throw 'Open PowerShell as Administrator on the TEST computer.'}
}
function Convert-DeviceInventoryJson {
    param([Parameter(Mandatory=$true)][string]$Json)
    if(!$Json.TrimStart().StartsWith('[')){throw 'Device inventory must be a JSON array'}
    # PS 5.1 emits an empty JSON array as one pipeline object. @(... | ConvertFrom-Json)
    # then has Count=1. Parse as a property to also preserve null array entries,
    # and preserve the array as one function result.
    [object[]]$items=(ConvertFrom-Json -InputObject ('{"devices":'+$Json+'}')).devices
    if($null -eq $items){$items=@()}
    foreach($item in $items){
        if($null -eq $item -or !($item.instanceId -is [string]) -or [string]::IsNullOrWhiteSpace($item.instanceId)){
            throw 'Invalid device inventory entry'
        }
    }
    return ,$items
}
function Get-FirmwareKind {
    if(-not ('LinkMicTestKit.Firmware' -as [type])){
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace LinkMicTestKit {
 public static class Firmware {
  [DllImport("kernel32.dll", SetLastError=true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static extern bool GetFirmwareType(out uint kind);
  public static uint Read() { uint kind; if(!GetFirmwareType(out kind))throw new Win32Exception(Marshal.GetLastWin32Error());return kind; }
 }
}
'@
    }
    switch([LinkMicTestKit.Firmware]::Read()){
        1 {return 'BIOS'}
        2 {return 'UEFI'}
        default {throw 'Windows returned an unknown firmware type'}
    }
}
function Get-SecureBootState {
    $firmware='Unknown';$enabled=$null;$supported=$null;$source='Unknown';$errors=@()
    try{$firmware=Get-FirmwareKind}catch{$errors+="GetFirmwareType: $($_.Exception.Message)"}
    if($firmware -eq 'BIOS'){
        $enabled=$false;$supported=$false;$source='GetFirmwareType (legacy BIOS)'
    }else{
        try{
            $result=Confirm-SecureBootUEFI -ErrorAction Stop
            if(!($result -is [bool])){throw 'Unexpected Secure Boot query result'}
            $enabled=$result;$supported=$true;$source='Confirm-SecureBootUEFI'
        }catch{$errors+="Confirm-SecureBootUEFI: $($_.Exception.Message)"}
    }
    [pscustomobject]@{enabled=$enabled;supported=$supported;firmware=$firmware;source=$source;errors=$errors}
}
function Get-BitLockerState {
    $protection='Unknown';$encryption='Unknown';$source='Unknown';$errors=@()
    try{
        $volume=Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop
        if($null -eq $volume -or $null -eq $volume.ProtectionStatus){throw 'No system volume protection status returned'}
        $protection=$volume.ProtectionStatus.ToString();$encryption=$volume.VolumeStatus.ToString();$source='Get-BitLockerVolume'
    }catch{
        $errors+="Get-BitLockerVolume: $($_.Exception.Message)"
        try{
            $volume=Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='$env:SystemDrive'" -ErrorAction Stop
            if($null -eq $volume){throw 'System volume was not returned by BitLocker WMI'}
            $status=Invoke-CimMethod -InputObject $volume -MethodName GetProtectionStatus -ErrorAction Stop
            if($status.ReturnValue -ne 0){throw "GetProtectionStatus returned $($status.ReturnValue)"}
            if($null -eq $status.ProtectionStatus){throw 'BitLocker WMI returned no protection status'}
            switch([int]$status.ProtectionStatus){0 {$protection='Off'} 1 {$protection='On'} 2 {$protection='Unknown'} default {throw 'Invalid BitLocker protection status'}}
            $source='Win32_EncryptableVolume.GetProtectionStatus'
        }catch{$errors+="BitLocker WMI: $($_.Exception.Message)"}
    }
    # Off is protection status, not evidence that the volume is unencrypted.
    [pscustomobject]@{protection=$protection;encryption=$encryption;source=$source;errors=$errors}
}
function Assert-Package {
    foreach($file in $Manifest.files){
        $path=Join-Path $KitDirectory $file.name
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw "Package file changed: $($file.name)"}
    }
    $cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $KitDirectory 'LinkMic-Test.cer'))
    if($cert.Thumbprint -ne $Manifest.certificateThumbprint){throw 'Certificate identity mismatch'}
    if($cert.NotAfter -lt (Get-Date)){throw 'Development certificate expired'}
}
function Get-TestMode {
    # NtQuerySystemInformation avoids localized parsing of BCDEdit output.
    if(-not ('LinkMicTestKit.CodeIntegrity' -as [type])){
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace LinkMicTestKit {
 public static class CodeIntegrity {
  [StructLayout(LayoutKind.Sequential)] public struct Info { public uint Length, Options; }
  [DllImport("ntdll.dll")] public static extern int NtQuerySystemInformation(uint kind, ref Info data, uint size, out uint returned);
  public static uint Read() { var data=new Info { Length=8 }; uint returned; int status=NtQuerySystemInformation(103,ref data,8,out returned); if(status!=0)throw new Exception("CodeIntegrity NTSTATUS="+status);return data.Options; }
 }
}
'@
    }
    $options=[LinkMicTestKit.CodeIntegrity]::Read()
    return (($options -band 2) -ne 0)
}
