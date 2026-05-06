#!/usr/bin/env bash
set -euo pipefail

# e2e-demo.sh - script to automate a simple demo using mosquitto_pub and HTTP API
# Usage:
#   ./scripts/e2e-demo.sh
# Env:
#   MQTT_HOST (default localhost)
#   MQTT_PORT (default 1883)
#   API_URL (default http://localhost:3000)
#   TIME_WAIT (seconds to wait for backend ingestion, default 3)

MQTT_HOST="${MQTT_HOST:-localhost}"
MQTT_PORT="${MQTT_PORT:-1883}"
API_URL="${API_URL:-http://localhost:3000}"
TIME_WAIT="${TIME_WAIT:-3}"
SECTOR="A"

command -v mosquitto_pub >/dev/null 2>&1 || { echo "mosquitto_pub not found in PATH"; exit 1; }
command -v curl >/dev/null 2>&1 || { echo "curl not found in PATH"; exit 1; }
command -v jq >/dev/null 2>&1 || echo "jq not found, output will be raw JSON"

echo "Starting e2e demo against MQTT ${MQTT_HOST}:${MQTT_PORT} and API ${API_URL}"

# Publish occupancy events to reach >=90% for sector A (27 of 30)
TARGET=27
echo "Publishing $TARGET OCCUPIED events to sector $SECTOR..."
PUBLISHED_FIRST_EVENT_ID=""
for i in $(seq 1 $TARGET); do
  idx=$(printf "%02d" "$i")
  SPOT="${SECTOR}-${idx}"
  EVENT_ID="e2e-${SPOT}-$(date +%s)-${i}"
  TS=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
  PAYLOAD=$(printf '{"eventId":"%s","ts":"%s","sectorId":"%s","spotId":"%s","state":"OCCUPIED","source":"test"}' "$EVENT_ID" "$TS" "$SECTOR" "$SPOT")
  mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" -t "campus/parking/sectors/$SECTOR/spots/$SPOT/events" -m "$PAYLOAD" -q 1
  if [ -z "$PUBLISHED_FIRST_EVENT_ID" ]; then PUBLISHED_FIRST_EVENT_ID="$EVENT_ID"; fi
done

echo "Waiting $TIME_WAIT seconds for backend to ingest events..."
sleep "$TIME_WAIT"

echo "Current sectors summary from API:"
curl -s "$API_URL/api/v1/sectors" | (command -v jq >/dev/null 2>&1 && jq '.' || cat)

# Request recommendation
echo "Requesting recommendation for sector $SECTOR..."
curl -s "$API_URL/api/v1/recommendation?fromSector=$SECTOR" | (command -v jq >/dev/null 2>&1 && jq '.' || cat)

# List open incidents
echo "Open incidents (if any):"
curl -s "$API_URL/api/v1/incidents?status=open" | (command -v jq >/dev/null 2>&1 && jq '.' || cat)

# Idempotency test: re-publish the first eventId and check that counts don't change
if [ -n "$PUBLISHED_FIRST_EVENT_ID" ]; then
  echo "Testing idempotency by re-publishing eventId $PUBLISHED_FIRST_EVENT_ID"
  # Find spot for that event (first published was A-01)
  SPOT_REPUBLISH="${SECTOR}-01"
  TS=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
  PAYLOAD_DUP=$(printf '{"eventId":"%s","ts":"%s","sectorId":"%s","spotId":"%s","state":"OCCUPIED","source":"test-dup"}' "$PUBLISHED_FIRST_EVENT_ID" "$TS" "$SECTOR" "$SPOT_REPUBLISH")
  BEFORE=$(curl -s "$API_URL/api/v1/sectors" )
  mosquitto_pub -h "$MQTT_HOST" -p "$MQTT_PORT" -t "campus/parking/sectors/$SECTOR/spots/$SPOT_REPUBLISH/events" -m "$PAYLOAD_DUP" -q 1
  sleep 1
  AFTER=$(curl -s "$API_URL/api/v1/sectors" )
  echo "Before vs After (sectors JSON):"
  if command -v jq >/dev/null 2>&1; then
    echo "BEFORE:"; echo "$BEFORE" | jq '.'
    echo "AFTER:"; echo "$AFTER" | jq '.'
  else
    echo "$BEFORE"
    echo "$AFTER"
  fi
  echo "If counts are unchanged, idempotency likely holds."
fi

echo "e2e demo complete."
