# ES-NFR-008 on the demo machine: creating an incident and assigning a unit shall each take
# under 2 seconds for 95% of requests. This times both against the REAL running app (Docker + SQL Server).
#
# Run from the repo root while `docker compose up` is running:
#     powershell -ExecutionPolicy Bypass -File scripts\perf-test.ps1
# Options:  -Runs 20   -ApiUrl http://localhost:5000   -Token <JWT>
# The token defaults to VITE_API_TOKEN from frontend\.env and must be an Admin or Dispatcher token.
#
# It needs one Available unit. Each run creates an incident called "PERF TEST", assigns the unit,
# then releases it again so the next run can use it. The test incidents stay on the live board, so
# afterwards reset the demo data with:
#     docker compose down -v
#     docker compose up -d
# (-v deletes the database volume; on startup the seed data - stations, units, hospitals - comes back.)

param(
    [int]$Runs = 20,
    [string]$ApiUrl = "http://localhost:5000",
    [string]$Token = ""
)

$ErrorActionPreference = "Stop"
$limitMs = 2000

if (-not $Token) {
    $envFile = Join-Path $PSScriptRoot "..\frontend\.env"
    if (Test-Path $envFile) {
        $line = Get-Content $envFile | Where-Object { $_ -match '^\s*VITE_API_TOKEN\s*=' } | Select-Object -First 1
        if ($line) { $Token = ($line -split '=', 2)[1].Trim() }
    }
}
if (-not $Token) {
    Write-Host "No token. Put VITE_API_TOKEN in frontend\.env or pass -Token <JWT> (Admin or Dispatcher)." -ForegroundColor Red
    exit 1
}

$headers = @{ Authorization = "Bearer $Token" }
$base = "$($ApiUrl.TrimEnd('/'))/api/emergency/v1"

function Call($method, $url, $body) {
    $params = @{ Method = $method; Uri = $url; Headers = $headers; ContentType = "application/json" }
    if ($null -ne $body) { $params.Body = ($body | ConvertTo-Json -Depth 5) }
    return Invoke-RestMethod @params
}

function Timed($method, $url, $body) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $result = Call $method $url $body
    $watch.Stop()
    return @{ Ms = $watch.Elapsed.TotalMilliseconds; Result = $result }
}

function NewIncident {
    @{
        type        = "Fire"
        severity    = "Low"
        location    = @{ latitude = 43.3601; longitude = -80.3123; address = "1 Perf Test Rd" }
        description = "PERF TEST - safe to delete (scripts/perf-test.ps1)"
        callerName  = "Perf Test"
        callerPhone = "519-555-0199"
    }
}

function P95($times) {
    $sorted = $times | Sort-Object
    $rank = [Math]::Ceiling(0.95 * $sorted.Count)
    return $sorted[[Math]::Max($rank, 1) - 1]
}

function Show($name, $times) {
    $p95 = P95 $times
    $avg = ($times | Measure-Object -Average).Average
    $max = ($times | Measure-Object -Maximum).Maximum
    $ok = $p95 -lt $limitMs
    $color = if ($ok) { "Green" } else { "Red" }
    $verdict = if ($ok) { "PASS" } else { "FAIL" }
    Write-Host ("{0,-16} {1,3} requests   95%: {2,6:N0} ms   avg: {3,6:N0} ms   slowest: {4,6:N0} ms   {5}" -f `
        $name, $times.Count, $p95, $avg, $max, $verdict) -ForegroundColor $color
    return $ok
}

try {
    Write-Host "ES-NFR-008 performance test against $base ($Runs runs each, limit $limitMs ms)" -ForegroundColor Cyan

    $available = Call GET "$base/units?status=Available&pageSize=1" $null
    if (-not $available.items -or $available.items.Count -eq 0) {
        Write-Host "No Available unit to assign. Free one up in the console, then run this again." -ForegroundColor Red
        exit 1
    }
    $unit = $available.items[0]
    Write-Host "Using unit $($unit.callSign)"

    # Warm-up (not counted): the first requests after a start-up are slower while the API gets going.
    $warm = Call POST "$base/incidents" (NewIncident)
    $a = @(Call POST "$base/incidents/$($warm.id)/assignments" @{ unitIds = @($unit.id) })[0]
    Call POST "$base/incidents/$($warm.id)/assignments/$($a.id)/release" $null | Out-Null

    $createTimes = @()
    $assignTimes = @()
    for ($i = 1; $i -le $Runs; $i++) {
        $created = Timed POST "$base/incidents" (NewIncident)
        $createTimes += $created.Ms

        $assigned = Timed POST "$base/incidents/$($created.Result.id)/assignments" @{ unitIds = @($unit.id) }
        $assignTimes += $assigned.Ms

        # Not timed: free the unit for the next run.
        $assignment = @($assigned.Result)[0]
        Call POST "$base/incidents/$($created.Result.id)/assignments/$($assignment.id)/release" $null | Out-Null
        Write-Host -NoNewline "."
    }
    Write-Host ""

    $ok1 = Show "Create incident" $createTimes
    $ok2 = Show "Assign unit" $assignTimes
    Write-Host ("Run at {0:yyyy-MM-dd HH:mm} on {1}" -f (Get-Date), $env:COMPUTERNAME)
    Write-Host "Reset the demo data afterwards: docker compose down -v ; docker compose up -d" -ForegroundColor Yellow
    if ($ok1 -and $ok2) { exit 0 } else { exit 1 }
}
catch {
    $msg = $_.Exception.Message
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $msg = "$msg $($_.ErrorDetails.Message)" }
    Write-Host "Request failed: $msg" -ForegroundColor Red
    if ($msg -match "401") { Write-Host "Token missing or expired - check VITE_API_TOKEN." -ForegroundColor Yellow }
    if ($msg -match "403") { Write-Host "This token's role can't create incidents or assign units - use an Admin or Dispatcher token." -ForegroundColor Yellow }
    exit 1
}
