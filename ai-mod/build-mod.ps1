param([string]$Gradle = '', [string]$JavaHome = $env:JAVA_HOME)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-Location -LiteralPath $PSScriptRoot
if (-not $JavaHome) {
    $cachedJdk = Get-ChildItem -LiteralPath '.tools\jdk' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cachedJdk) { $JavaHome = $cachedJdk.FullName }
}
if (-not $JavaHome -or -not (Test-Path -LiteralPath (Join-Path $JavaHome 'bin\javac.exe'))) { throw 'Set JAVA_HOME or -JavaHome to a JDK 21 installation' }
$env:JAVA_HOME = $JavaHome
# ASCII TEMP avoids Windows AF_UNIX failures under non-ASCII profile directories.
$publicDirectory = $env:PUBLIC
if (-not $publicDirectory) { $publicDirectory = Split-Path -Parent ([Environment]::GetFolderPath('CommonDocuments')) }
$buildTemp = Join-Path $publicDirectory 'SuyunCraft-build-tmp'
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
$env:TEMP = $buildTemp; $env:TMP = $buildTemp
if (-not $Gradle) { $Gradle = Join-Path $PSScriptRoot 'gradlew.bat' }
& $Gradle --no-daemon build
if ($LASTEXITCODE -ne 0) { throw 'Fabric mod build failed' }
New-Item -ItemType Directory -Force -Path dist | Out-Null
Copy-Item -LiteralPath 'build\libs\suyuncraft-ai-0.5.0.jar' -Destination dist
$apiName = 'fabric-api-0.106.0+1.21.1.jar'
$apiUrl = 'https://maven.fabricmc.net/net/fabricmc/fabric-api/fabric-api/0.106.0+1.21.1/' + $apiName
$hashContent = (Invoke-WebRequest -UseBasicParsing ($apiUrl + '.sha256')).Content
$hash = $(if ($hashContent -is [byte[]]) { [Text.Encoding]::UTF8.GetString($hashContent) } else { [string]$hashContent }).Trim()
if ($hash -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid Fabric API checksum' }
$apiPath = Join-Path dist $apiName
if (-not (Test-Path -LiteralPath $apiPath) -or (Get-FileHash $apiPath -Algorithm SHA256).Hash -ne $hash) {
    Invoke-WebRequest -UseBasicParsing $apiUrl -OutFile (Join-Path dist ($apiName + '.tmp'))
    if ((Get-FileHash (Join-Path dist ($apiName + '.tmp')) -Algorithm SHA256).Hash -ne $hash) { throw 'Fabric API checksum mismatch' }
    Move-Item -LiteralPath (Join-Path dist ($apiName + '.tmp')) -Destination $apiPath -Force
}
# Support the standalone Desktop launcher without embedding keys or auto-starting a service.
$runtime = Join-Path $env:APPDATA 'SuyunCraft\ai-mod'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
Copy-Item -LiteralPath dist,bridge,start-bridge.ps1,README.md -Destination $runtime -Recurse -Force
Write-Host "Mod built and launcher bundle staged: $runtime"
