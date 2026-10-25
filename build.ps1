# Build all source modules with the Windows .NET Framework compiler.
param([switch]$Tests)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw '.NET Framework compiler not found' }
$refs = @('System.dll','System.Core.dll','System.Windows.Forms.dll','System.Drawing.dll',
          'System.Web.Extensions.dll','Microsoft.VisualBasic.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')
$opt = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', '/warn:4', '/out:SuyunCraft.exe')
$opt += '/win32manifest:app.manifest'
if (Test-Path -LiteralPath 'app.config') { $opt += '/appconfig:app.config' }
if (Test-Path -LiteralPath 'app.ico') { $opt += '/win32icon:app.ico' }
foreach ($ref in $refs) { $opt += "/reference:$ref" }
$opt += @(Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | ForEach-Object { 'src\' + $_.Name })
& $csc $opt
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host '[build] OK -> SuyunCraft.exe'
if ($Tests) {
    $testOpt = @('/nologo', '/target:exe', '/main:SuyunCraft.RegressionTests', '/codepage:65001',
                 '/warn:4', '/win32manifest:app.manifest', '/out:tests\RegressionTests.exe')
    if (Test-Path -LiteralPath 'app.config') { $testOpt += '/appconfig:app.config' }
    foreach ($ref in $refs) { $testOpt += "/reference:$ref" }
    $testOpt += @(Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | ForEach-Object { 'src\' + $_.Name })
    $testOpt += @(Get-ChildItem -LiteralPath 'tests' -Filter '*.cs' | ForEach-Object { 'tests\' + $_.Name })
    & $csc $testOpt
    if ($LASTEXITCODE -ne 0) { throw "Test build failed ($LASTEXITCODE)" }
    & '.\tests\RegressionTests.exe'
    if ($LASTEXITCODE -ne 0) { throw "Regression tests failed ($LASTEXITCODE)" }
}
