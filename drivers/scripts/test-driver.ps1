param([string]$KitRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.tools\driver-kit'))
$ErrorActionPreference='Stop'
$DriverRoot=Split-Path $PSScriptRoot -Parent
$Vc="$KitRoot\msvc\Contents\VC\Tools\MSVC\14.44.35207"
$Sdk="$KitRoot\microsoft.windows.sdk.cpp\c"
$SdkLib="$KitRoot\microsoft.windows.sdk.cpp.x64\c"
$Output="$DriverRoot\out\tests"
New-Item -ItemType Directory -Force $Output | Out-Null
& "$Vc\bin\Hostx64\x64\cl.exe" /nologo /W4 /WX /std:c++17 /EHsc /MT /O2 "/I$DriverRoot\include" "/I$DriverRoot\src" "/I$Vc\include" "/I$Sdk\Include\10.0.26100.0\ucrt" "/Fo$Output\ring-tests.obj" "/Fe$Output\ring-tests.exe" "$DriverRoot\tests\ring-tests.cpp" /link /NODEFAULTLIB:OLDNAMES.lib "/LIBPATH:$Vc\lib\x64" "/LIBPATH:$SdkLib\ucrt\x64" "/LIBPATH:$SdkLib\um\x64"
if($LASTEXITCODE -ne 0) {throw 'Ring test build failed'}
& "$Output\ring-tests.exe"
if($LASTEXITCODE -ne 0) {throw 'Ring test failed'}
& "$Vc\bin\Hostx64\x64\cl.exe" /nologo /W4 /WX /std:c++17 /EHsc /MT /O2 /DLINKMIC_CONTROL_HOST_TEST "/I$DriverRoot\include" "/I$DriverRoot\src" "/I$DriverRoot\tests" "/I$Vc\include" "/I$Sdk\Include\10.0.26100.0\ucrt" "/Fo$Output\\" "/Fe$Output\control-tests.exe" "$DriverRoot\tests\control-tests.cpp" "$DriverRoot\src\LinkMicControl.cpp" /link /NODEFAULTLIB:OLDNAMES.lib "/LIBPATH:$Vc\lib\x64" "/LIBPATH:$SdkLib\ucrt\x64" "/LIBPATH:$SdkLib\um\x64"
if($LASTEXITCODE -ne 0) {throw 'Control dispatch test build failed'}
& "$Output\control-tests.exe"
if($LASTEXITCODE -ne 0) {throw 'Control dispatch test failed'}
