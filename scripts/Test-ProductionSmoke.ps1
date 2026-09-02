param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl
)

$normalizedBaseUrl = $BaseUrl.TrimEnd("/")
if (-not $normalizedBaseUrl.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Production smoke tests must target an HTTPS URL."
}

$liveResponse = Invoke-WebRequest -Uri "$normalizedBaseUrl/health/live" -Method Get -UseBasicParsing
if ($liveResponse.StatusCode -ne 200) {
    throw "Live health check failed with HTTP $($liveResponse.StatusCode)."
}

$readyResponse = Invoke-WebRequest -Uri "$normalizedBaseUrl/health/ready" -Method Get -UseBasicParsing
if ($readyResponse.StatusCode -ne 200) {
    throw "Ready health check failed with HTTP $($readyResponse.StatusCode)."
}

Write-Output "Production smoke test passed for $normalizedBaseUrl."
