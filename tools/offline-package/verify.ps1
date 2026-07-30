param([Parameter(Mandatory = $true)][string]$PackageDirectory)

$ErrorActionPreference = "Stop"
$directory = [System.IO.Path]::GetFullPath($PackageDirectory)
$manifest = Join-Path $directory "SHA256SUMS"
if (-not (Test-Path -LiteralPath $manifest)) { throw "SHA256SUMS not found." }

foreach ($line in Get-Content -LiteralPath $manifest) {
    $parts = $line -split "\s+", 2
    $path = Join-Path $directory $parts[1]
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
    if ($actual -ne $parts[0]) { throw "Checksum mismatch: $($parts[1])" }
}
Write-Output "All package checksums passed."

