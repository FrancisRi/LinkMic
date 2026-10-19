param([string]$KitRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.tools\driver-kit'))
$ErrorActionPreference = 'Stop'
$DriverRoot = Split-Path $PSScriptRoot -Parent
$Source = Join-Path $DriverRoot 'src\wavert'
$Output = Join-Path $DriverRoot 'out\x64'
$Objects = Join-Path $Output 'obj'
New-Item -ItemType Directory -Force $Objects | Out-Null
$Vc = Join-Path $KitRoot 'msvc\Contents\VC\Tools\MSVC\14.44.35207'
$Wdk = Join-Path $KitRoot 'microsoft.windows.wdk.x64\c'
$Sdk = Join-Path $KitRoot 'microsoft.windows.sdk.cpp\c'
$Compiler = Join-Path $Vc 'bin\Hostx64\x64\cl.exe'
$Linker = Join-Path $Vc 'bin\Hostx64\x64\link.exe'
if (!(Test-Path -LiteralPath $Compiler)) { throw "Missing portable MSVC: $Compiler" }
$IncludePaths = @("$DriverRoot\include", "$DriverRoot\src", "$Source\Inc", "$Source\Main", "$Source\Filters", "$Source\Utilities",
    "$Wdk\Include\10.0.26100.0\km", "$Wdk\Include\10.0.26100.0\km\crt", "$Wdk\Include\wdf\kmdf\1.33",
    "$Sdk\Include\10.0.26100.0\shared", "$Sdk\Include\10.0.26100.0\um", "$Vc\include")
$Defines = @('_AMD64_', 'AMD64', '_WIN64', '_WIN32_WINNT=0x0A00', 'NTDDI_VERSION=0x0A00000B', 'NT_PROCESSOR_GROUPS',
    '_USE_WAVERT_', '_USE_IPortClsRuntimePower', '_NEW_DELETE_OPERATORS_', 'PC_IMPLEMENTATION', 'UNICODE', '_UNICODE', 'DBG=0',
    'DEBUG_LEVEL=DEBUGLVL_TERSE', 'KMDF_VERSION_MAJOR=1', 'KMDF_VERSION_MINOR=33')
$Arguments = @('/nologo', '/c', '/kernel', '/Zl', '/GR-', '/EHs-c-', '/GS', '/O2', '/W4', '/WX', '/Zi', '/std:c++17', '/utf-8', '/FC', '/wd4201', '/wd4324')
$Arguments += $IncludePaths | ForEach-Object { "/I$_" }
$Arguments += $Defines | ForEach-Object { "/D$_" }
$Files = @(Get-ChildItem -LiteralPath "$Source\Main" -Filter '*.cpp') +
    @(Get-ChildItem -LiteralPath "$Source\Filters" -Filter '*.cpp') +
    @(Get-ChildItem -LiteralPath "$Source\Utilities" -Filter '*.cpp') +
    @(Get-Item -LiteralPath "$DriverRoot\src\LinkMicControl.cpp")
$BuiltObjects = @()
foreach ($File in $Files) {
    $Object = Join-Path $Objects ($File.BaseName + '.obj')
    & $Compiler @Arguments "/Fo$Object" "/Fd$Output\LinkMicCapture.pdb" $File.FullName
    if ($LASTEXITCODE -ne 0) { throw "C++ compile failed: $($File.Name)" }
    $BuiltObjects += $Object
}
$KernelLib = "$Wdk\Lib\10.0.26100.0\km\x64"
$WdfLib = "$Wdk\Lib\wdf\kmdf\x64\1.33"
$Libraries = @('ntoskrnl.lib', 'hal.lib', 'portcls.lib', 'stdunk.lib', 'libcntpr.lib', 'ksguid.lib', 'wdmsec.lib', 'BufferOverflowFastFailK.lib', 'wmilib.lib', 'ntstrsafe.lib')
& $Linker /nologo /DRIVER /SUBSYSTEM:NATIVE,10.00 /MACHINE:X64 /ENTRY:FxDriverEntry /NODEFAULTLIB /INCREMENTAL:NO /OPT:REF /OPT:ICF /DYNAMICBASE /NXCOMPAT /DEBUG:FULL /INTEGRITYCHECK "/OUT:$Output\LinkMicCapture.sys" "/PDB:$Output\LinkMicCapture.pdb" "/MAP:$Output\LinkMicCapture.map" "/LIBPATH:$KernelLib" "/LIBPATH:$WdfLib" @BuiltObjects @Libraries wdfdriverentry.lib wdfldr.lib
if ($LASTEXITCODE -ne 0) { throw 'Kernel link failed' }
Copy-Item -LiteralPath "$DriverRoot\LinkMicCapture.inf" -Destination "$Output\LinkMicCapture.inf" -Force
& "$Wdk\tools\10.0.26100.0\x64\InfVerif.exe" /w "$Output\LinkMicCapture.inf"
if ($LASTEXITCODE -ne 0) { throw 'INF verification failed' }
& "$Wdk\bin\10.0.26100.0\x86\Inf2Cat.exe" "/driver:$Output" /os:10_CO_X64,10_NI_X64,10_GE_X64
if ($LASTEXITCODE -ne 0) { throw 'Catalog signability checks failed' }
Get-FileHash "$Output\LinkMicCapture.sys", "$Output\LinkMicCapture.inf", "$Output\LinkMicCapture.cat" -Algorithm SHA256 |
    Select-Object Path,Hash | ConvertTo-Json | Set-Content "$Output\sha256.json"
Write-Host "Built unsigned driver: $Output\LinkMicCapture.sys. It has NOT been installed or loaded."
