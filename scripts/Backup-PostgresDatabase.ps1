param(
    [string]$ConnectionString = $env:SACHAI_DATABASE_URL,
    [string]$OutputDirectory = "backups/database"
)

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Provide the database connection string through -ConnectionString or SACHAI_DATABASE_URL."
}

$pgDump = Get-Command pg_dump -ErrorAction SilentlyContinue
if (-not $pgDump) {
    throw "pg_dump was not found. Install PostgreSQL client tools on the backup runner."
}

$resolvedOutputDirectory = Join-Path (Get-Location) $OutputDirectory
New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $resolvedOutputDirectory "sachai-db-$timestamp.dump"

& $pgDump.Source --dbname=$ConnectionString --format=custom --no-owner --no-acl --file=$backupPath

if ($LASTEXITCODE -ne 0) {
    throw "Database backup failed with exit code $LASTEXITCODE."
}

Write-Output "Database backup created: $backupPath"
