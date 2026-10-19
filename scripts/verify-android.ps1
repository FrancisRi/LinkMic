param([switch]$Offline)
. "$PSScriptRoot\env.ps1"
New-Item -ItemType Directory -Force -Path (Join-Path $ProjectRoot 'artifacts\tests') | Out-Null
Push-Location (Join-Path $ProjectRoot 'android')
try {
    $arguments=@('--no-daemon','testDebugUnitTest','lintDebug','assembleDebug')
    if($Offline){$arguments+='--offline'}
    & $Gradle @arguments
    if($LASTEXITCODE -ne 0){throw 'Android UI tests, lint or APK build failed'}
} finally { Pop-Location }
$apk=Join-Path $ProjectRoot 'artifacts\LinkMic-Android-0.2.3-debug.apk'
Copy-Item -LiteralPath "$ProjectRoot\android\app\build\outputs\apk\debug\app-debug.apk" -Destination $apk
& "$env:ANDROID_HOME\build-tools\35.0.0\apksigner.bat" verify --verbose --print-certs $apk
if($LASTEXITCODE -ne 0){throw 'APK signature verification failed'}
Get-Item -LiteralPath $apk | Select-Object Name,Length
