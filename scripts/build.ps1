param([ValidateSet('All','Windows','Android')][string]$Target='All')
. "$PSScriptRoot\env.ps1"
$artifacts=Join-Path $ProjectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if ($Target -in 'All','Windows') {
    if(!(Test-Path -LiteralPath "$ProjectRoot\native\win-x64\rnnoise.dll")){ & "$PSScriptRoot\build-rnnoise.ps1" }
    & $Dotnet publish "$ProjectRoot\src\LinkMic.Windows\LinkMic.Windows.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$artifacts\windows"
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
}
if ($Target -in 'All','Android') {
    $sdkPath=$env:ANDROID_HOME.Replace('\','/').Replace(':','\:')
    "sdk.dir=$sdkPath" | Set-Content -LiteralPath "$ProjectRoot\android\local.properties" -Encoding ascii
    Push-Location "$ProjectRoot\android"
    try { & $Gradle --no-daemon lintDebug assembleDebug; if ($LASTEXITCODE -ne 0) { throw 'Android build or lint failed' } } finally { Pop-Location }
    Copy-Item -LiteralPath "$ProjectRoot\android\app\build\outputs\apk\debug\app-debug.apk" -Destination "$artifacts\LinkMic-Android-0.2.3-debug.apk"
}
