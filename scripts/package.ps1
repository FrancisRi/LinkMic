. "$PSScriptRoot\env.ps1"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$artifacts=Join-Path $ProjectRoot 'artifacts'
$windows=Join-Path $artifacts 'windows'
if(!(Test-Path -LiteralPath "$windows\LinkMic.exe")){throw 'Build Windows before packaging'}
$licenses=Join-Path $windows 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
$runtime=Join-Path $env:NUGET_PACKAGES 'microsoft.netcore.app.runtime.win-x64\10.0.13'
$desktop=Join-Path $env:NUGET_PACKAGES 'microsoft.windowsdesktop.app.runtime.win-x64\10.0.13'
Copy-Item -LiteralPath "$runtime\LICENSE.TXT" -Destination "$licenses\dotnet-runtime-LICENSE.txt"
Copy-Item -LiteralPath "$runtime\THIRD-PARTY-NOTICES.TXT" -Destination "$licenses\dotnet-runtime-NOTICES.txt"
Copy-Item -LiteralPath "$desktop\LICENSE" -Destination "$licenses\wpf-LICENSE.txt"
Copy-Item -LiteralPath "$ProjectRoot\docs\licenses\Apache-2.0.txt" -Destination $licenses
Copy-Item -LiteralPath "$ProjectRoot\third-party\rnnoise\LICENSE.txt" -Destination "$licenses\RNNoise-LICENSE.txt"
Copy-Item -LiteralPath "$ProjectRoot\third-party\rnnoise\provenance.json" -Destination "$licenses\RNNoise-provenance.json"
Copy-Item -LiteralPath "$ProjectRoot\LICENSE" -Destination "$windows\LICENSE.txt"
Copy-Item -LiteralPath "$ProjectRoot\README.md","$ProjectRoot\THIRD-PARTY-NOTICES.md" -Destination $windows
$docs=Join-Path $windows 'docs'
New-Item -ItemType Directory -Force -Path $docs | Out-Null
Copy-Item -LiteralPath "$ProjectRoot\docs\PROTOCOL.md","$ProjectRoot\docs\SIGNED-AUDIO-DRIVER.md","$ProjectRoot\docs\RNNOISE.md","$ProjectRoot\docs\PAIRED-DEVICES.md","$ProjectRoot\docs\ANDROID-0.2.3.md" -Destination $docs
$files=@("$windows\LinkMic.exe","$windows\rnnoise.dll","$windows\LICENSE.txt","$windows\README.md","$windows\THIRD-PARTY-NOTICES.md",$licenses,$docs)
$vendorZip=Join-Path $ProjectRoot 'third-party\vb-cable\VBCABLE_Driver_Pack45.zip'
if(Test-Path -LiteralPath $vendorZip){
    $provenance=Get-Content "$ProjectRoot\third-party\vb-cable\provenance.json" -Raw | ConvertFrom-Json
    if((Get-FileHash -LiteralPath $vendorZip).Hash.ToLowerInvariant() -ne $provenance.archiveSha256){throw 'VB-CABLE archive is not the pinned original'}
    $cable=Join-Path $windows 'drivers\vb-cable'
    New-Item -ItemType Directory -Force -Path $cable | Out-Null
    Copy-Item -LiteralPath $vendorZip,"$ProjectRoot\third-party\vb-cable\provenance.json" -Destination $cable
    Copy-Item -LiteralPath "$ProjectRoot\third-party\vb-cable\README.md" -Destination "$cable\INSTALL.md"
    $archive=[IO.Compression.ZipFile]::OpenRead($vendorZip)
    try{
        $notice=$archive.GetEntry('readme.txt')
        if(!$notice){throw 'Missing vendor license'}
        [IO.Compression.ZipFileExtensions]::ExtractToFile($notice,"$cable\VB-CABLE-original-readme.txt",$true)
    }finally{$archive.Dispose()}
    $files+=Join-Path $windows 'drivers'
}
$zip=Join-Path $artifacts 'LinkMic-Windows-0.3.3-win-x64.zip'
Compress-Archive -LiteralPath $files -DestinationPath $zip -Force
$checksumFiles=@($zip)
$apk=Join-Path $artifacts 'LinkMic-Android-0.2.3-debug.apk'
if(Test-Path -LiteralPath $apk){$checksumFiles+=$apk}
$lines=foreach($path in $checksumFiles){
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($path))"
}
$lines | Set-Content -LiteralPath "$artifacts\SHA256SUMS.txt" -Encoding ascii
Get-Item -LiteralPath $checksumFiles | Select-Object Name,Length
