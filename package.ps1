$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$project = [System.IO.Path]::GetFullPath($PSScriptRoot)
$destination = Join-Path (Split-Path -Parent $project) 'SuyunCraft-v1.5-source-and-app.zip'
$files = @()
$files += @(Get-ChildItem -LiteralPath $project -File | Where-Object {
    $_.Extension -in @('.exe', '.ps1', '.md', '.manifest', '.html', '.ico', '.png')
})
$files += @(Get-ChildItem -LiteralPath (Join-Path $project 'src') -File -Filter '*.cs')
$files += @(Get-ChildItem -LiteralPath (Join-Path $project 'tests') -File | Where-Object {
    $_.Extension -in @('.cs', '.txt', '.png')
})
$files += @(Get-ChildItem -LiteralPath (Join-Path $project 'ai-mod') -File -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/](\.tools|\.gradle|build|run|__pycache__)[\\/]' -and
    $_.Name -notlike '*.local.json' -and $_.Extension -in @('.java','.json','.py','.ps1','.gradle','.properties','.bat','.jar','.md')
})
$stream = [System.IO.File]::Open($destination, [System.IO.FileMode]::Create)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($project.Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, 'SuyunCraft/' + $relative,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
Write-Host "[package] $destination"
