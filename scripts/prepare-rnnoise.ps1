. "$PSScriptRoot\env.ps1"
$vendor = Join-Path $ProjectRoot 'third-party\rnnoise'
$provenance = Get-Content -LiteralPath "$vendor\provenance.json" -Raw | ConvertFrom-Json
$archives = @(
    @{ Name='rnnoise-v0.2.zip'; Url=$provenance.sourceUrl; Hash=$provenance.sourceArchiveSha256 },
    @{ Name="rnnoise_data-$($provenance.modelVersion).tar.gz"; Url=$provenance.modelUrl; Hash=$provenance.modelArchiveSha256 }
)
foreach ($archive in $archives) {
    $path = Join-Path $vendor $archive.Name
    if (!(Test-Path -LiteralPath $path)) { Invoke-WebRequest -Uri $archive.Url -OutFile $path }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archive.Hash) { throw "RNNoise checksum mismatch: $($archive.Name)" }
}
$sourceRoot = Join-Path $vendor 'source'
$source = Join-Path $sourceRoot "rnnoise-$($provenance.commit)"
if (!(Test-Path -LiteralPath "$source\src\denoise.c")) {
    Expand-Archive -LiteralPath "$vendor\rnnoise-v0.2.zip" -DestinationPath $sourceRoot -Force
}
if (!(Test-Path -LiteralPath "$source\src\rnnoise_data.c")) {
    & tar -xzf (Join-Path $vendor $archives[1].Name) -C $source src/rnnoise_data.c src/rnnoise_data.h
    if ($LASTEXITCODE -ne 0) { throw 'RNNoise model extraction failed' }
}
Write-Output "RNNoise pinned source and model ready: $source"
