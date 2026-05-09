param(
    [string]$MqttHost = "localhost",
    [int]$MqttPort = 1883,
    [string]$ApiUrl = "http://localhost:5000",
    [string]$SimulatorUrl = "http://localhost:3000",
    [int]$TimeWaitSec = 3,
    [string]$Sector = "A",
    [string]$OutputFile = (Join-Path $PSScriptRoot "e2e-demo-output.json"),
    [int]$MaxFillAttempts = 10,
    [int]$PublishDurationSec = 10
)

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Write-Error "$Name not found in PATH"
        exit 1
    }
}

Require-Command -Name "node"
$env:NODE_PATH = Join-Path $PSScriptRoot "..\simulator\node_modules"

if (-not (Test-Path $env:NODE_PATH)) {
    Write-Error "Missing simulator node_modules. Run 'npm install' in the simulator folder first."
    exit 1
}

Write-Host "Starting demo against MQTT ${MqttHost}:${MqttPort}, API ${ApiUrl}, Simulator ${SimulatorUrl}"

$result = [ordered]@{
    startedAt = (Get-Date).ToUniversalTime().ToString("o")
    mqtt = @{ host = $MqttHost; port = $MqttPort }
    apiUrl = $ApiUrl
    simulatorUrl = $SimulatorUrl
    sector = $Sector
    summary = @{}
    steps = @()
    warnings = @()
}

function Publish-OccupiedBurst {
    param(
        [string]$SectorId,
        [int]$SpotCount,
        [int]$DurationSec
    )

    $sleepMs = [int](($DurationSec * 1000) / [Math]::Max($SpotCount, 1))
    $firstId = $null
    for ($i = 1; $i -le $SpotCount; $i++) {
        $idx = $i.ToString("00")
        $spot = "$SectorId-$idx"
        $eventId = "e2e-$spot-$(Get-Date -Format yyyyMMddHHmmss)-$i"
        $payload = @{ 
            eventId = $eventId
            ts = (Get-Date).ToUniversalTime().ToString("o")
            sectorId = $SectorId
            spotId = $spot
            state = "OCCUPIED"
            source = "test"
        } | ConvertTo-Json -Compress

        $topic = "campus/parking/sectors/$SectorId/spots/$spot/events"
        & node (Join-Path $PSScriptRoot "mqtt-publish.js") $topic $payload $MqttHost $MqttPort | Out-Null

        if (-not $firstId) { $firstId = $eventId }

        if ($sleepMs -gt 0) {
            Start-Sleep -Milliseconds $sleepMs
        }
    }

    return $firstId
}

# Publish occupancy events to reach >=90% for sector A
$target = 28
Write-Host "Publishing OCCUPIED events to sector $Sector..."
$firstEventId = Publish-OccupiedBurst -SectorId $Sector -SpotCount $target -DurationSec $PublishDurationSec

Write-Host "Waiting $TimeWaitSec seconds for backend to ingest events..."
Start-Sleep -Seconds $TimeWaitSec

$sectorsSummary = Invoke-RestMethod -Uri "$ApiUrl/api/v1/sectors"
$result.steps += [ordered]@{ name = "sectors"; data = $sectorsSummary }

$pollDelaySec = 1
$occRate = ($sectorsSummary | Where-Object { $_.sectorId -eq $Sector }).occupancyRate
$attemptsUsed = 0
while ($attemptsUsed -lt $MaxFillAttempts -and $occRate -lt 0.9) {
    $attemptsUsed++
    Publish-OccupiedBurst -SectorId $Sector -SpotCount $target -DurationSec $PublishDurationSec | Out-Null
    Start-Sleep -Seconds $pollDelaySec
    $sectorsSummary = Invoke-RestMethod -Uri "$ApiUrl/api/v1/sectors"
    $occRate = ($sectorsSummary | Where-Object { $_.sectorId -eq $Sector }).occupancyRate
}

$result.steps += [ordered]@{ name = "sectors_after_fill"; data = $sectorsSummary; occupancyRate = $occRate; attempts = $attemptsUsed }
if ($occRate -lt 0.9) {
    $result.warnings += "Occupancy did not reach 0.90 after $attemptsUsed attempts."
}

$recommendation = Invoke-RestMethod -Uri "$ApiUrl/api/v1/recommendation?fromSector=$Sector"
$result.steps += [ordered]@{ name = "recommendation"; data = $recommendation }

$faultResult = Invoke-RestMethod -Method Post -Uri "$SimulatorUrl/simulator/failures" -ContentType "application/json" -Body '{"spotId":"A-07","mode":"stuck_occupied"}'
$result.steps += [ordered]@{ name = "inject_fault"; data = $faultResult }

$incidentsOpen = Invoke-RestMethod -Uri "$ApiUrl/api/v1/incidents?status=open"
$incidentsSample = $incidentsOpen | Select-Object -First 5
$result.steps += [ordered]@{ name = "incidents_open"; count = @($incidentsOpen).Count; sample = $incidentsSample }

# Idempotency test: re-publish the first eventId and check that counts do not change
if ($firstEventId) {
    Write-Host "Testing idempotency by re-publishing eventId $firstEventId"
    $spotRepublish = "$Sector-01"
    $before = Invoke-RestMethod -Uri "$ApiUrl/api/v1/sectors"

    $payloadDup = @{ 
        eventId = $firstEventId
        ts = (Get-Date).ToUniversalTime().ToString("o")
        sectorId = $Sector
        spotId = $spotRepublish
        state = "OCCUPIED"
        source = "test-dup"
    } | ConvertTo-Json -Compress

    $topicDup = "campus/parking/sectors/$Sector/spots/$spotRepublish/events"
    & node (Join-Path $PSScriptRoot "mqtt-publish.js") $topicDup $payloadDup $MqttHost $MqttPort | Out-Null
    Start-Sleep -Seconds 1

    $after = Invoke-RestMethod -Uri "$ApiUrl/api/v1/sectors"
    $result.steps += [ordered]@{ name = "idempotency"; data = @{ before = $before; after = $after } }
}

$result.completedAt = (Get-Date).ToUniversalTime().ToString("o")
$result.summary = [ordered]@{
    occupancyRate = $occRate
    recommendation = $recommendation
    incidentsOpenCount = @($incidentsOpen).Count
}
$result | ConvertTo-Json -Depth 8 | Out-File -FilePath $OutputFile -Encoding UTF8
Write-Host "Demo complete. Output written to $OutputFile"
