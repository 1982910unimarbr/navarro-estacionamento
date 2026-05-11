param(
    [string]$MqttHost = "localhost",
    [int]$MqttPort = 1883,
    [string]$Sector = "A",
    [int]$PublishDelayMs = 1000,
    [int]$StartIndex = 1,
    [int]$EndIndex = 28
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

if ($StartIndex -gt $EndIndex) {
    Write-Error "StartIndex must be <= EndIndex."
    exit 1
}

Write-Host "Publishing manual events to MQTT ${MqttHost}:${MqttPort} for sector ${Sector}"

for ($i = $StartIndex; $i -le $EndIndex; $i++) {
    $idx = $i.ToString("00")
    $spotId = "$Sector-$idx"
    $eventId = "manual-$spotId-$(Get-Date -Format yyyyMMddHHmmss)-$i"
    $payload = @{
        eventId = $eventId
        ts = (Get-Date).ToUniversalTime().ToString("o")
        sectorId = $Sector
        spotId = $spotId
        state = "OCCUPIED"
        source = "manual"
    } | ConvertTo-Json -Compress

    $topic = "campus/parking/sectors/$Sector/spots/$spotId/events"
    & node (Join-Path $PSScriptRoot "mqtt-publish.js") $topic $payload $MqttHost $MqttPort
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Publish failed for $spotId"
    } else {
        Write-Host "Publish ok for $spotId"
    }

    if ($PublishDelayMs -gt 0) {
        Start-Sleep -Milliseconds $PublishDelayMs
    }
}

Write-Host "Manual publish complete."
