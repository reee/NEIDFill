# Remove build outputs only: bin/obj directories under src and tests, plus dist at repo root.
# Never deletes anything else (repo root, libs, source folders stay untouched).
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'NEIDFill.sln'))) {
    throw "Solution not found: $root\NEIDFill.sln (scripts must stay in the repository scripts directory)"
}

foreach ($scope in @('src', 'tests')) {
    $scopeDir = Join-Path $root $scope
    if (-not (Test-Path -LiteralPath $scopeDir)) { continue }
    $targets = @(Get-ChildItem -LiteralPath $scopeDir -Recurse -Directory |
        Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' })
    foreach ($t in $targets) {
        if (-not (Test-Path -LiteralPath $t.FullName)) { continue }
        Write-Host "Removing $($t.FullName)"
        Remove-Item -LiteralPath $t.FullName -Recurse -Force
    }
}

$dist = Join-Path $root 'dist'
if (Test-Path -LiteralPath $dist) {
    Write-Host "Removing $dist"
    Remove-Item -LiteralPath $dist -Recurse -Force
}
exit 0
