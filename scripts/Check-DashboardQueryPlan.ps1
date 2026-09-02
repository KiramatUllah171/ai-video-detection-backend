param(
    [string]$ConnectionString = $env:SACHAI_DATABASE_URL
)

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Provide the database connection string through -ConnectionString or SACHAI_DATABASE_URL."
}

$psql = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psql) {
    throw "psql was not found. Install PostgreSQL client tools on the machine running this check."
}

$queries = @(
    "EXPLAIN (ANALYZE, BUFFERS) SELECT count(*) FROM videos WHERE deleted_at IS NULL;",
    "EXPLAIN (ANALYZE, BUFFERS) SELECT id, original_name, created_at FROM videos WHERE deleted_at IS NULL ORDER BY created_at DESC LIMIT 20;",
    "EXPLAIN (ANALYZE, BUFFERS) SELECT id, user_name, action, created_at FROM audit_logs ORDER BY created_at DESC, id DESC LIMIT 10;",
    "EXPLAIN (ANALYZE, BUFFERS) SELECT provider_name, status, request_started_at FROM ai_provider_requests WHERE request_started_at >= NOW() - INTERVAL '30 days' ORDER BY request_started_at DESC LIMIT 50;"
)

foreach ($query in $queries) {
    Write-Output ""
    Write-Output $query
    & $psql.Source $ConnectionString -v ON_ERROR_STOP=1 -c $query
    if ($LASTEXITCODE -ne 0) {
        throw "Query plan check failed with exit code $LASTEXITCODE."
    }
}

Write-Output ""
Write-Output "Dashboard query plan check completed. Review output for sequential scans on large tables or high execution times."
