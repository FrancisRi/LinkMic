param([ValidateSet('Tone','Phone')][string]$Mode='Tone',[string]$OutputDirectory=(Join-Path $PSScriptRoot 'results'))
. "$PSScriptRoot\Common.ps1"
Assert-Package
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$modeArgument=if($Mode -eq 'Tone'){'tone'}else{'capture'}
Write-Host $(if($Mode -eq 'Tone'){'Close the LinkMic desktop application. Injecting a synthetic 440Hz tone into the virtual microphone only; no speaker playback.'}else{'Connect the phone in LinkMic and select LinkMic Virtual Microphone, then speak for 8 seconds. This explicitly records that virtual endpoint into a WAV.'})
$output=Join-Path $OutputDirectory "$modeArgument.wav"
$result=& "$PSScriptRoot\LinkMicDriverTools.exe" $modeArgument $output 2> (Join-Path $OutputDirectory "$modeArgument-error.txt")
$code=$LASTEXITCODE
if($result){$result|Set-Content -LiteralPath (Join-Path $OutputDirectory "$modeArgument-result.json") -Encoding UTF8;$result}
if($code -ne 0){throw "Audio test failed ($code); inspect $OutputDirectory. No default microphone fallback was used."}
