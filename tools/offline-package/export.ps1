param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$AppImage,
    [switch]$SkipDocker
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

Push-Location $repositoryRoot
try {
    dotnet restore DualNewsSearch.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Locked restore failed." }

    $nugetArchive = Join-Path $output "nuget-global-packages.zip"
    Compress-Archive -Path ".nuget/packages/*" -DestinationPath $nugetArchive -Force

    if (-not $SkipDocker) {
        docker pull $AppImage
        docker save $AppImage -o (Join-Path $output "dual-news-search-image.tar")
        docker pull "vespaengine/vespa:8.721.11"
        docker save "vespaengine/vespa:8.721.11" -o (Join-Path $output "vespa-8.721.11-linux-amd64.tar")
    }

    Get-ChildItem -LiteralPath $output -File | ForEach-Object {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
        "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
    } | Set-Content -Encoding ASCII (Join-Path $output "SHA256SUMS")
}
finally {
    Pop-Location
}

