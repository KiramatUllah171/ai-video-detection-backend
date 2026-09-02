param(
    [string]$Project = "AiVideoDetection.Api/AiVideoDetection.Api.csproj",
    [string]$StartupProject = "AiVideoDetection.Api/AiVideoDetection.Api.csproj"
)

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw "dotnet SDK was not found on this machine."
}

& $dotnet.Source ef database update --project $Project --startup-project $StartupProject

if ($LASTEXITCODE -ne 0) {
    throw "Database migration failed with exit code $LASTEXITCODE."
}

Write-Output "Database migrations applied successfully."
