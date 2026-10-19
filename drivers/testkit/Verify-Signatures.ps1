param([string]$Directory=$PSScriptRoot,[string]$CertificateThumbprint)
$ErrorActionPreference='Stop'
if(!$CertificateThumbprint){$CertificateThumbprint=(Get-Content -LiteralPath "$Directory\manifest.json" -Raw|ConvertFrom-Json).certificateThumbprint}
try{Add-Type -AssemblyName System.Security.Cryptography.Pkcs}catch{Add-Type -AssemblyName System.Security}
function Read-Element([byte[]]$Data,[ref]$Position){
    $start=$Position.Value
    if($start+2 -gt $Data.Length){throw 'Truncated ASN.1'}
    $tag=$Data[$Position.Value++];[int]$length=$Data[$Position.Value++]
    if($length -ge 128){$count=$length-band127;if($count -lt 1 -or $count -gt 4){throw 'Unsupported ASN.1 length'};$length=0
        for($i=0;$i -lt $count;$i++){if($Position.Value -ge $Data.Length){throw 'Truncated ASN.1 length'};$length=($length-shl8)-bor$Data[$Position.Value++]}}
    if($length -lt 0 -or $Position.Value+$length -gt $Data.Length){throw 'Invalid ASN.1 bounds'}
    $content=$Position.Value;$Position.Value+=$length
    return [pscustomobject]@{Tag=$tag;Offset=$content;Length=$length}
}
function Verify-Pe([string]$Path){
    [byte[]]$data=[IO.File]::ReadAllBytes($Path)
    $pe=[BitConverter]::ToInt32($data,0x3c);$optional=$pe+24
    if([BitConverter]::ToUInt16($data,$optional) -ne 0x20b){throw 'Expected PE32+ x64 file'}
    $checksum=$optional+64;$security=$optional+112+32
    $certificateOffset=[BitConverter]::ToInt32($data,$security);$certificateSize=[BitConverter]::ToInt32($data,$security+4)
    if($certificateOffset -le $security+8 -or $certificateOffset+$certificateSize -ne $data.Length){throw 'Unsupported PE certificate layout'}
    $headers=[BitConverter]::ToInt32($data,$optional+60);$sectionCount=[BitConverter]::ToUInt16($data,$pe+6)
    $sectionTable=$optional+[BitConverter]::ToUInt16($data,$pe+20);$sections=@()
    for($i=0;$i -lt $sectionCount;$i++){$entry=$sectionTable+$i*40;$size=[BitConverter]::ToInt32($data,$entry+16);$offset=[BitConverter]::ToInt32($data,$entry+20);if($size){$sections+=[pscustomobject]@{Offset=$offset;Size=$size}}}
    $expected=$headers;foreach($section in $sections|Sort-Object Offset){if($section.Offset -ne $expected){throw 'PE sections are not contiguous; refusing simplified Authenticode hash'};$expected+=$section.Size}
    if($expected -ne $certificateOffset){throw 'Unexpected PE overlay before certificate'}
    $length=[BitConverter]::ToInt32($data,$certificateOffset)
    if($length -lt 8 -or $length -gt $certificateSize){throw 'Invalid WIN_CERTIFICATE'}
    [byte[]]$encoded=New-Object byte[] ($length-8);[Array]::Copy($data,$certificateOffset+8,$encoded,0,$encoded.Length)
    $cms=[Security.Cryptography.Pkcs.SignedCms]::new();$cms.Decode($encoded);$cms.CheckSignature($true)
    if($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $CertificateThumbprint){throw 'Unexpected PE signer'}
    [byte[]]$content=$cms.ContentInfo.Content;[int]$position=0;$root=Read-Element $content ([ref]$position)
    $position=$root.Offset;$null=Read-Element $content ([ref]$position);$digestInfo=Read-Element $content ([ref]$position)
    $position=$digestInfo.Offset;$algorithm=Read-Element $content ([ref]$position);$digest=Read-Element $content ([ref]$position)
    [int]$algorithmPosition=$algorithm.Offset;$oid=Read-Element $content ([ref]$algorithmPosition)
    $sha256Oid='608648016503040201';$actualOid=([BitConverter]::ToString($content,$oid.Offset,$oid.Length)).Replace('-','').ToLowerInvariant()
    if($actualOid -ne $sha256Oid -or $digest.Tag -ne 4 -or $digest.Length -ne 32){throw 'Expected SHA256 Authenticode digest'}
    $hash=[Security.Cryptography.SHA256]::Create()
    $null=$hash.TransformBlock($data,0,$checksum,$data,0)
    $null=$hash.TransformBlock($data,$checksum+4,$security-$checksum-4,$data,$checksum+4)
    $null=$hash.TransformBlock($data,$security+8,$certificateOffset-$security-8,$data,$security+8)
    $null=$hash.TransformFinalBlock([byte[]]@(),0,0)
    $actual=([BitConverter]::ToString($hash.Hash)).Replace('-','')
    $signed=([BitConverter]::ToString($content,$digest.Offset,$digest.Length)).Replace('-','')
    $hash.Dispose();if($actual -ne $signed){throw "Authenticode digest mismatch: $Path"}
    return [ordered]@{file=[IO.Path]::GetFileName($Path);cmsSignatureValid=$true;authenticodeDigestValid=$true;thumbprint=$CertificateThumbprint}
}
$results=@(Verify-Pe "$Directory\LinkMicCapture.sys";Verify-Pe "$Directory\LinkMicDriverTools.exe")
$catalog=[Security.Cryptography.Pkcs.SignedCms]::new();$catalog.Decode([IO.File]::ReadAllBytes("$Directory\LinkMicCapture.cat"));$catalog.CheckSignature($true)
if($catalog.SignerInfos.Count -ne 1 -or $catalog.SignerInfos[0].Certificate.Thumbprint -ne $CertificateThumbprint){throw 'Unexpected catalog signer'}
[ordered]@{passed=$true;files=$results;catalogCmsSignatureValid=$true;systemTrustModified=$false;microsoftProductionSigned=$false}|ConvertTo-Json -Depth 6
