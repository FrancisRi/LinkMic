. "$PSScriptRoot\env.ps1"
& "$PSScriptRoot\prepare-rnnoise.ps1"
$kit=Join-Path $ToolsRoot 'driver-kit'
$vc="$kit\msvc\Contents\VC\Tools\MSVC\14.44.35207"
$sdk="$kit\microsoft.windows.sdk.cpp\c"
$libs="$kit\microsoft.windows.sdk.cpp.x64\c"
$vendor=Join-Path $ProjectRoot 'third-party\rnnoise'
$source="$vendor\source\rnnoise-904a876dce1f9ab8860c0a5000ed151f9f6eef58"
$output=Join-Path $ProjectRoot 'native\win-x64'
$objects=Join-Path $output 'obj'
New-Item -ItemType Directory -Force -Path $objects | Out-Null
$expected=@{
    'rnnoise-v0.2.zip'='61339f4f2eab6d2dcce1ec6ff7afe7d63215b4f51acf0a0b6d51615c75db1e15'
    'rnnoise_data-0b50c45.tar.gz'='4ac81c5c0884ec4bd5907026aaae16209b7b76cd9d7f71af582094a2f98f4b43'
}
foreach($item in $expected.GetEnumerator()){
    if((Get-FileHash -LiteralPath (Join-Path $vendor $item.Key)).Hash.ToLowerInvariant() -ne $item.Value){throw "RNNoise archive checksum mismatch: $($item.Key)"}
}
$sources=@('denoise','rnn','pitch','kiss_fft','celt_lpc','nnet','nnet_default','parse_lpcnet_weights','rnnoise_data','rnnoise_tables')
# SSE2 is part of the Windows x64 baseline; MSVC does not define GCC's __SSE2__.
# Upstream deliberately uses double constants for float DSP/model tables.
# Isolate those two conversion warnings; keep all other native warnings fatal.
$compiler=Join-Path $vc 'bin\Hostx64\x64\cl.exe'
$linker=Join-Path $vc 'bin\Hostx64\x64\link.exe'
$includeArguments=@()
$libraryArguments=@()
if(Test-Path -LiteralPath $compiler){
    $includeArguments=@("/I$vc\include","/I$sdk\Include\10.0.26100.0\ucrt")
    $libraryArguments=@("/LIBPATH:$vc\lib\x64","/LIBPATH:$libs\ucrt\x64","/LIBPATH:$libs\um\x64")
}else{
    $compiler=(Get-Command cl.exe -ErrorAction Stop).Source
    $linker=(Get-Command link.exe -ErrorAction Stop).Source
    if(!$env:INCLUDE -or !$env:LIB){throw 'Open an x64 Visual Studio Developer PowerShell with C++ Build Tools and Windows SDK installed.'}
}
$arguments=@('/nologo','/c','/O2','/MT','/std:c11','/fp:precise','/W3','/WX','/wd4244','/wd4305','/utf-8','/D__SSE2__=1','/DWIN32','/DRNNOISE_BUILD','/DDLL_EXPORT','/D_CRT_SECURE_NO_WARNINGS',"/I$source\include","/I$source\src")+$includeArguments
foreach($name in $sources){
    & $compiler @arguments "/Fo$objects\$name.obj" "$source\src\$name.c"
    if($LASTEXITCODE -ne 0){throw "RNNoise compilation failed: $name"}
}
$built=@($sources | ForEach-Object {"$objects\$_.obj"})
& $linker /nologo /DLL /MACHINE:X64 /OPT:REF /OPT:ICF /DYNAMICBASE /NXCOMPAT /INCREMENTAL:NO /NODEFAULTLIB:OLDNAMES.lib "/OUT:$output\rnnoise.dll" @libraryArguments @built
if($LASTEXITCODE -ne 0){throw 'RNNoise linking failed'}
Copy-Item -LiteralPath "$source\COPYING" -Destination "$vendor\LICENSE.txt"
if(Test-Path -LiteralPath "$vendor\provenance.json"){
    $provenance=Get-Content -LiteralPath "$vendor\provenance.json" -Raw | ConvertFrom-Json
    $provenance.dllSha256=(Get-FileHash -LiteralPath "$output\rnnoise.dll").Hash.ToLowerInvariant()
    $provenance | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$vendor\provenance.json"
}
Get-Item -LiteralPath "$output\rnnoise.dll" | Select-Object Name,Length
