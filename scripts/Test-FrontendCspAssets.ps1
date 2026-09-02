param(
    [Parameter(Mandatory = $true)]
    [string]$FrontendBaseUrl
)

$base = $FrontendBaseUrl.TrimEnd("/")
if (-not $base.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Frontend CSP/assets verification must target an HTTPS URL."
}

$response = Invoke-WebRequest -Uri $base -Method Get -UseBasicParsing
if ($response.StatusCode -ne 200) {
    throw "Frontend returned HTTP $($response.StatusCode)."
}

$html = [string]$response.Content
$assetMatches = [regex]::Matches($html, '(?:src|href)="([^"]+\.(?:js|css|woff2?|png|jpg|jpeg|webp|svg|ico)(?:\?[^"]*)?)"', 'IgnoreCase')
if ($assetMatches.Count -eq 0) {
    throw "No frontend assets were found in the HTML response."
}

foreach ($match in $assetMatches) {
    $assetUrl = $match.Groups[1].Value
    if ($assetUrl.StartsWith("http://", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Insecure asset URL found: $assetUrl"
    }

    $absoluteAssetUrl = if ($assetUrl.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase)) {
        $assetUrl
    } else {
        "$base/$($assetUrl.TrimStart('/'))"
    }

    $assetResponse = Invoke-WebRequest -Uri $absoluteAssetUrl -Method Head -UseBasicParsing
    if ($assetResponse.StatusCode -ge 400) {
        throw "Frontend asset failed with HTTP $($assetResponse.StatusCode): $absoluteAssetUrl"
    }
}

Write-Output "Frontend CSP/assets check passed. Verified $($assetMatches.Count) assets."
