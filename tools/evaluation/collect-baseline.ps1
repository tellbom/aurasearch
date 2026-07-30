param(
    [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
    [Parameter(Mandatory = $true)][string]$QueriesTsv,
    [Parameter(Mandatory = $true)][string]$OutputJsonl
)

$ErrorActionPreference = "Stop"
Remove-Item -LiteralPath $OutputJsonl -Force -ErrorAction SilentlyContinue
foreach ($line in Get-Content -Encoding UTF8 -LiteralPath $QueriesTsv) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) { continue }
    $parts = $line -split "`t", 2
    $body = @{ query = $parts[1]; page = 1; pageSize = 50 } | ConvertTo-Json
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Uri "$($ApiBaseUrl.TrimEnd('/'))/api/v1/search" -Body $body
    $watch.Stop()
    $row = @{
        queryId = $parts[0]
        query = $parts[1]
        searchTraceId = $response.searchTraceId
        searchMode = $response.searchMode
        latencyMs = $watch.Elapsed.TotalMilliseconds
        results = $response.results
    } | ConvertTo-Json -Depth 10 -Compress
    Add-Content -Encoding UTF8 -LiteralPath $OutputJsonl -Value $row
}

