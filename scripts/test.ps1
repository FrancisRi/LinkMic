. "$PSScriptRoot\env.ps1"
$artifacts = Join-Path $ProjectRoot 'artifacts\tests'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
& "$env:JAVA_HOME\bin\javac.exe" -encoding UTF-8 -d $artifacts "$ProjectRoot\android\app\src\main\java\dev\linkmic\app\AppError.java" "$ProjectRoot\android\app\src\main\java\dev\linkmic\app\Wire.java" "$ProjectRoot\android\app\src\main\java\dev\linkmic\app\DevicePairing.java" "$ProjectRoot\android\app\src\main\java\dev\linkmic\app\ConnectionTime.java" "$ProjectRoot\tests\InteropFixture.java" "$ProjectRoot\tests\PairingClientFixture.java"
if ($LASTEXITCODE -ne 0) { throw 'Java fixture compilation failed' }
& "$env:JAVA_HOME\bin\java.exe" -cp $artifacts InteropFixture $artifacts
if ($LASTEXITCODE -ne 0) { throw 'Java fixture failed' }
& $Dotnet run --project "$ProjectRoot\tests\LinkMic.Tests\LinkMic.Tests.csproj" -c Release -- $artifacts
if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed' }
