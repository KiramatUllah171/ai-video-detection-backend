param(
    [int]$Port = 5166
)

$ErrorActionPreference = "Stop"
$connections = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue

if (-not $connections) {
    return
}

$processIds = $connections | Select-Object -ExpandProperty OwningProcess -Unique

foreach ($processId in $processIds) {
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
    if (-not $process) {
        continue
    }

    $commandLine = [string]$process.CommandLine
    $isThisBackend =
        $process.Name -ieq "AiVideoDetection.Api.exe" -or
        (
            $process.Name -ieq "dotnet.exe" -and
            ($commandLine -match "AiVideoDetection\.Api" -or $commandLine -match "ai-video-detection-backend")
        )

    if (-not $isThisBackend) {
        Write-Error "Port $Port is already used by PID $processId ($($process.Name)). Stop that process or choose another backend port."
    }

    Write-Host "Stopping existing sachvideoai backend on port $Port (PID $processId)."
    Stop-Process -Id $processId -Force
}
