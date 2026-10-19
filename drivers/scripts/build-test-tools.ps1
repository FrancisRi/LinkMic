param([string]$KitRoot=(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.tools\driver-kit'))
$ErrorActionPreference='Stop'
$DriverRoot=Split-Path $PSScriptRoot -Parent
$Vc="$KitRoot\msvc\Contents\VC\Tools\MSVC\14.44.35207"
$Sdk="$KitRoot\microsoft.windows.sdk.cpp\c"
$SdkLib="$KitRoot\microsoft.windows.sdk.cpp.x64\c"
$output="$DriverRoot\out\test-tools"
New-Item -ItemType Directory -Force -Path $output | Out-Null
& "$Vc\bin\Hostx64\x64\cl.exe" /nologo /W4 /WX /std:c++17 /EHsc /MT /O2 /utf-8 "/I$DriverRoot\include" "/I$Vc\include" "/I$Sdk\Include\10.0.26100.0\ucrt" "/I$Sdk\Include\10.0.26100.0\shared" "/I$Sdk\Include\10.0.26100.0\um" "/I$Sdk\Include\10.0.26100.0\winrt" "/Fo$output\DriverTools.obj" "/Fe$output\LinkMicDriverTools.exe" "$DriverRoot\testkit\DriverTools.cpp" /link /NODEFAULTLIB:OLDNAMES.lib "/LIBPATH:$Vc\lib\x64" "/LIBPATH:$SdkLib\ucrt\x64" "/LIBPATH:$SdkLib\um\x64" ole32.lib uuid.lib setupapi.lib newdev.lib cfgmgr32.lib propsys.lib
if($LASTEXITCODE -ne 0){throw 'Test helper compilation failed'}
& "$output\LinkMicDriverTools.exe" selftest
if($LASTEXITCODE -ne 0){throw 'Offline test helper selftest failed'}
