$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$WorkspaceRoot = Split-Path $ProjectRoot -Parent
# Explicit settings win. A sibling portable tool directory is optional.
$ToolsRoot = if ($env:LINKMIC_TOOLS_ROOT) { $env:LINKMIC_TOOLS_ROOT } else { Join-Path $WorkspaceRoot 'tools' }
function Find-LinkMicTool([string]$candidate, [string]$name) {
    if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $name
}
if (!$env:JAVA_HOME) {
    $portableJava = Join-Path $ToolsRoot 'java\jdk-21.0.12.1+1'
    if (Test-Path -LiteralPath $portableJava) { $env:JAVA_HOME = $portableJava }
    else { $java = Get-Command java.exe -ErrorAction SilentlyContinue; if ($java) { $env:JAVA_HOME = Split-Path (Split-Path $java.Source -Parent) -Parent } }
}
if (!$env:ANDROID_HOME) {
    if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_HOME = $env:ANDROID_SDK_ROOT }
    elseif (Test-Path -LiteralPath (Join-Path $ToolsRoot 'android-sdk')) { $env:ANDROID_HOME = Join-Path $ToolsRoot 'android-sdk' }
    else { $env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
}
if (!$env:DOTNET_ROOT -and (Test-Path -LiteralPath (Join-Path $ToolsRoot 'dotnet'))) { $env:DOTNET_ROOT = Join-Path $ToolsRoot 'dotnet' }
if (!$env:ANDROID_USER_HOME) { $env:ANDROID_USER_HOME = Join-Path $ProjectRoot '.android' }
if (!$env:GRADLE_USER_HOME) { $env:GRADLE_USER_HOME = Join-Path $ProjectRoot '.gradle' }
if (!$env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $ProjectRoot '.dotnet-home' }
if (!$env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $ProjectRoot '.nuget' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$Dotnet = Find-LinkMicTool $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }) 'dotnet.exe'
if ($env:LINKMIC_GRADLE) { $Gradle = $env:LINKMIC_GRADLE }
elseif (Test-Path -LiteralPath (Join-Path $ToolsRoot 'gradle\gradle-8.13\bin\gradle.bat')) { $Gradle = Join-Path $ToolsRoot 'gradle\gradle-8.13\bin\gradle.bat' }
else { $Gradle = Join-Path $ProjectRoot 'android\gradlew.bat' }
