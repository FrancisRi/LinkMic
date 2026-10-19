param([string]$KitRoot=(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.tools\driver-kit'),[string]$Version='0.2.0-test.2')
$ErrorActionPreference='Stop'
$DriverRoot=Split-Path $PSScriptRoot -Parent
$ProjectRoot=Split-Path $DriverRoot -Parent
$output=Join-Path $DriverRoot 'out\test-signed'
$private=Join-Path $DriverRoot '.test-signing'
New-Item -ItemType Directory -Force -Path $output,$private | Out-Null
$pfx=Join-Path $private 'LinkMic-Development.pfx'
$secretFile=Join-Path $private 'local-password.txt'
if(!(Test-Path -LiteralPath $pfx)){
    $rsa=[Security.Cryptography.RSA]::Create(3072)
    $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=LinkMic Development Test Only',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,$true))
    $oids=[Security.Cryptography.OidCollection]::new();$null=$oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids,$false))
    $certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1),[DateTimeOffset]::UtcNow.AddDays(90))
    $password=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    [IO.File]::WriteAllBytes($pfx,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password))
    [IO.File]::WriteAllText($secretFile,$password)
    $certificate.Dispose();$rsa.Dispose()
}
$password=[IO.File]::ReadAllText($secretFile)
$certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx,$password,[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
if($certificate.NotAfter -lt (Get-Date).AddDays(1)){throw 'Development certificate is expiring; rotate deliberately, do not overwrite a certificate trusted by a test machine.'}
[IO.File]::WriteAllBytes((Join-Path $output 'LinkMic-Test.cer'),$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
$original=Join-Path $DriverRoot 'out\x64'
Copy-Item -LiteralPath "$original\LinkMicCapture.sys","$original\LinkMicCapture.inf" -Destination $output -Force
Copy-Item -LiteralPath "$DriverRoot\out\test-tools\LinkMicDriverTools.exe" -Destination $output -Force
Copy-Item -Path "$DriverRoot\testkit\*.ps1" -Destination $output -Force
Copy-Item -LiteralPath "$DriverRoot\testkit\README-TEST.md","$DriverRoot\LICENSE" -Destination $output -Force
$source=Join-Path $output 'source'
New-Item -ItemType Directory -Force -Path $source | Out-Null
Copy-Item -LiteralPath "$DriverRoot\testkit\DriverTools.cpp","$DriverRoot\include\LinkMicAbi.h","$DriverRoot\scripts\build-test-tools.ps1","$DriverRoot\scripts\make-test-kit.ps1" -Destination $source -Force
$signTool="$KitRoot\microsoft.windows.sdk.cpp\c\bin\10.0.26100.0\x64\signtool.exe"
foreach($file in @('LinkMicCapture.sys','LinkMicDriverTools.exe')){
    & $signTool sign /fd SHA256 /f $pfx /p $password (Join-Path $output $file)
    if($LASTEXITCODE -ne 0){throw "Test signing failed: $file"}
}
# Catalog must be generated AFTER embedding the driver's test signature.
& "$KitRoot\microsoft.windows.wdk.x64\c\bin\10.0.26100.0\x86\Inf2Cat.exe" "/driver:$output" /os:10_CO_X64,10_NI_X64,10_GE_X64
if($LASTEXITCODE -ne 0){throw 'Test-signed catalog generation failed'}
& $signTool sign /fd SHA256 /f $pfx /p $password "$output\LinkMicCapture.cat"
if($LASTEXITCODE -ne 0){throw 'Catalog test signing failed'}
& "$DriverRoot\testkit\Verify-Signatures.ps1" -Directory $output -CertificateThumbprint $certificate.Thumbprint | Set-Content -LiteralPath "$output\cryptographic-verification.json" -Encoding UTF8
$signatures=foreach($name in @('LinkMicCapture.sys','LinkMicCapture.cat','LinkMicDriverTools.exe')){
    $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $output $name)
    if(!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or $signature.Status -eq 'HashMismatch' -or $signature.Status -eq 'NotSigned'){throw "Signature identity/integrity check failed: $name ($($signature.Status))"}
    [ordered]@{file=$name;status=$signature.Status.ToString();thumbprint=$signature.SignerCertificate.Thumbprint}
}
$rootPackageFiles=@(Get-ChildItem -LiteralPath $output -File | Where-Object {$_.Name -notin @('manifest.json','signing-verification.json','test-state.json')})
$packageFiles=$rootPackageFiles+@(Get-ChildItem -LiteralPath $source -File)
$files=foreach($file in $packageFiles){
    [ordered]@{name=$file.FullName.Substring($output.Length+1);sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
[ordered]@{version=$Version;certificateThumbprint=$certificate.Thumbprint;certificateExpires=$certificate.NotAfter.ToUniversalTime().ToString('o');testSigned=$true;microsoftProductionSigned=$false;installed=$false;hardwareVerified=$false;unsignedSysSha256=(Get-FileHash -LiteralPath "$original\LinkMicCapture.sys" -Algorithm SHA256).Hash.ToLowerInvariant();files=@($files)} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$output\manifest.json" -Encoding UTF8
[ordered]@{signatures=@($signatures);trustedLocally=$false;note='Development certificate not imported into system trust. Untrusted chain is expected; test signatures are not Microsoft production signatures.'}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath "$output\signing-verification.json" -Encoding UTF8
$zip=Join-Path $ProjectRoot "artifacts\LinkMic-Driver-TestKit-$Version-win11-x64.zip"
$archivePaths=@($rootPackageFiles.FullName)+@($source,"$output\manifest.json","$output\signing-verification.json")
Compress-Archive -LiteralPath $archivePaths -DestinationPath $zip -Force
$certificate.Dispose()
Get-Item -LiteralPath $zip | Select-Object Name,Length
Get-FileHash -LiteralPath $zip -Algorithm SHA256
