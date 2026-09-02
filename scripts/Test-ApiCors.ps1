param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$FrontendOrigin
)

$api = $ApiBaseUrl.TrimEnd("/")
$headers = @{
    Origin = $FrontendOrigin
    "Access-Control-Request-Method" = "GET"
    "Access-Control-Request-Headers" = "authorization,content-type,x-correlation-id,accept-language"
}

$response = Invoke-WebRequest -Uri "$api/health/live" -Method Options -Headers $headers -UseBasicParsing
$allowOrigin = $response.Headers["Access-Control-Allow-Origin"]

if ($allowOrigin -ne $FrontendOrigin) {
    throw "CORS check failed. Expected Access-Control-Allow-Origin '$FrontendOrigin' but received '$allowOrigin'."
}

Write-Output "CORS check passed for origin $FrontendOrigin."
