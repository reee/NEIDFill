# Publish NEIDFill release binaries into dist\ (ASCII-only, Windows PowerShell 5.1).
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'NEIDFill.sln'))) {
    throw "Solution not found: $root\NEIDFill.sln (scripts must stay in the repository scripts directory)"
}

Push-Location $root
try {
    dotnet build NEIDFill.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    $outDir = Join-Path $root 'src\NEIDFill\bin\Release\net48'
    $whitelist = @(
        'NEIDFill.exe',
        'NEIDFill.exe.config',
        'NEIDFill.Core.dll',
        'NEIDFill.Devices.dll',
        'Sdtapi.dll',
        'Dewlt.dll',
        'SavePhoto.dll',
        'WltRS.dll',
        'JpgDll.dll'
    )
    $missing = @($whitelist | Where-Object { -not (Test-Path -LiteralPath (Join-Path $outDir $_)) })
    if ($missing.Count -gt 0) {
        throw "Missing deployment files in $outDir : $(($missing | ForEach-Object { $_ }) -join ', ')"
    }

    $fileVersion = (Get-Item -LiteralPath (Join-Path $outDir 'NEIDFill.exe')).VersionInfo.FileVersion
    if ([string]::IsNullOrWhiteSpace($fileVersion)) { $fileVersion = '0.0.0.0' }

    $stageDir = Join-Path $root "dist\NEIDFill-$fileVersion"
    $zipPath  = Join-Path $root "dist\NEIDFill-$fileVersion.zip"
    if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
    if (Test-Path -LiteralPath $zipPath)  { Remove-Item -LiteralPath $zipPath -Force }
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

    foreach ($f in $whitelist) {
        Copy-Item -LiteralPath (Join-Path $outDir $f) -Destination $stageDir
    }
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stageDir

    Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -Force

    Write-Host "Staged files in $stageDir :"
    Get-ChildItem -LiteralPath $stageDir -File | ForEach-Object {
        Write-Host ("  {0}  {1}" -f $_.Name, $_.Length)
    }
    Write-Host "Zip: $zipPath"
    exit 0
}
finally {
    Pop-Location
}
