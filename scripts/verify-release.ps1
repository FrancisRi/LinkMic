param([string]$PreviousApk)
. "$PSScriptRoot\env.ps1"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$artifacts=Join-Path $ProjectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path "$artifacts\tests" | Out-Null
function Get-EntryHash($entry){
    if(!$entry){throw 'Missing archive entry'}
    $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
    try{([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose();$stream.Dispose()}
}
$bundle=[IO.Compression.ZipFile]::OpenRead("$artifacts\LinkMic-Windows-0.3.3-win-x64.zip")
$apk=[IO.Compression.ZipFile]::OpenRead("$artifacts\LinkMic-Android-0.2.3-debug.apk")
try{
    $bad=@($bundle.Entries | Where-Object {$_.FullName -match '((^|/)(paired-devices\.dat|paired-computers\.enc|audio-settings\.json)(\.|$)|fixture|\.pfx$|\.keystore$|test-results)'})
    if($bad.Count){throw 'Private/test data in release'}
    foreach($name in @('LinkMic.exe','rnnoise.dll','LICENSE.txt')){
        if((Get-EntryHash $bundle.GetEntry($name)) -ne (Get-FileHash -LiteralPath "$artifacts\windows\$name").Hash.ToLowerInvariant()){throw "Bundled file differs: $name"}
    }
    $provenance=Get-Content "$ProjectRoot\third-party\rnnoise\provenance.json" -Raw | ConvertFrom-Json
    if((Get-EntryHash $bundle.GetEntry('rnnoise.dll')) -ne $provenance.dllSha256){throw 'RNNoise provenance mismatch'}
    foreach($name in @('THIRD-PARTY-NOTICES.md','licenses/RNNoise-LICENSE.txt','licenses/RNNoise-provenance.json','licenses/Apache-2.0.txt')){if(!$bundle.GetEntry($name)){throw "Missing notice: $name"}}
    if((Get-EntryHash $apk.GetEntry('assets/LinkMic-LICENSE.txt')) -ne (Get-FileHash -LiteralPath "$ProjectRoot\LICENSE").Hash.ToLowerInvariant()){throw 'Android MIT notice differs'}
    foreach($name in @('assets/THIRD-PARTY-NOTICES.txt','assets/Apache-2.0.txt')){if(!$apk.GetEntry($name)){throw "Missing Android notice: $name"}}
    $signer="$env:ANDROID_HOME\build-tools\35.0.0\apksigner.bat"
    $new=@(& $signer verify --print-certs "$artifacts\LinkMic-Android-0.2.3-debug.apk")
    if($LASTEXITCODE -ne 0){throw 'APK signature invalid'}
    $sameCertificate=$null
    if($PreviousApk){
        $old=@(& $signer verify --print-certs $PreviousApk)
        if($LASTEXITCODE -ne 0){throw 'Previous APK signature invalid'}
        $oldCert=@($old | Where-Object {$_ -match 'certificate SHA-256 digest:'})
        $newCert=@($new | Where-Object {$_ -match 'certificate SHA-256 digest:'})
        if($oldCert.Count -ne 1 -or $newCert.Count -ne 1 -or $oldCert[0] -ne $newCert[0]){throw 'APK update certificate changed'}
        $sameCertificate=$true
    }
    $checks=@(Get-Content "$artifacts\SHA256SUMS.txt")
    foreach($line in $checks){$hash,$relative=$line -split '  ',2;if((Get-FileHash -LiteralPath (Join-Path $artifacts $relative)).Hash.ToLowerInvariant() -ne $hash){throw "Checksum mismatch: $relative"}}
    $report=[ordered]@{passed=$true;windowsVersion='0.3.3';androidVersion='0.2.3';bundledFilesMatch=$true;androidSignatureVerified=$true;sameAndroidUpdateCertificate=$sameCertificate;licensesIncluded=$true;noPrivatePairingStateBundled=$true;checksumsVerified=$checks.Count;hardwareRetestCompleted=$false}
    $report | ConvertTo-Json | Set-Content "$artifacts\tests\public-release-verification.json"
    $report | ConvertTo-Json
}finally{$bundle.Dispose();$apk.Dispose()}
