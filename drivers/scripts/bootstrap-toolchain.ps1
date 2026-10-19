param([string]$KitRoot=(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.tools\driver-kit'), [string]$SevenZip='7z.exe')
$ErrorActionPreference='Stop'
if (!(Test-Path -LiteralPath $SevenZip)) {throw '7z.exe is required to extract the official VSIX/NuGet ZIP packages.'}
$DriverRoot=Split-Path $PSScriptRoot -Parent
$Packages=Get-Content "$DriverRoot\toolchain-lock.json" -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force "$KitRoot\downloads" | Out-Null
foreach($Package in $Packages) {
    $Path=Join-Path "$KitRoot\downloads" $Package.file
    if(!(Test-Path -LiteralPath $Path)) {Invoke-WebRequest $Package.url -OutFile $Path}
    if((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Package.sha256) {
        throw "Download hash mismatch: $($Package.file)"
    }
    & $SevenZip x $Path "-o$KitRoot\$($Package.extract)" -y
    if($LASTEXITCODE -ne 0) {throw "Extraction failed: $($Package.file)"}
}
Write-Host 'Portable toolchain restored under workspace. No global VS/WDK installation was changed.'
