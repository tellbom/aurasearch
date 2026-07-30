param(
    [Parameter(Mandatory = $true)][string]$Endpoint,
    [Parameter(Mandatory = $true)][string]$IndexName,
    [Parameter(Mandatory = $true)][string]$AliasName,
    [switch]$Apply
)

$ErrorActionPreference = "Stop"
$template = Get-Content -Raw -Encoding UTF8 "$PSScriptRoot/create-index-template.json" | ConvertFrom-Json
$template.aliases | Add-Member -NotePropertyName $AliasName -NotePropertyValue ([pscustomobject]@{})
$body = $template | ConvertTo-Json -Depth 20

if (-not $Apply) {
    Write-Output $body
    Write-Output "Dry run only. Re-run with -Apply after reviewing the target and mapping."
    exit 0
}

$exists = Invoke-WebRequest -Method Head -Uri "$($Endpoint.TrimEnd('/'))/$IndexName" -SkipHttpErrorCheck
if ($exists.StatusCode -eq 200) {
    throw "Index '$IndexName' already exists; this tool never overwrites it."
}

Invoke-RestMethod -Method Put -ContentType "application/json" `
    -Uri "$($Endpoint.TrimEnd('/'))/$IndexName" -Body $body

