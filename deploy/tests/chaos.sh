#!/usr/bin/env sh
set -eu
: "${API_URL:?API_URL must be set}"
: "${VESPA_COMPOSE:=deploy/vespa/docker-compose.yml}"

echo "baseline"
curl --fail --silent --show-error "$API_URL/health/live" >/dev/null

echo "stop Vespa and verify the API remains live"
docker compose -f "$VESPA_COMPOSE" stop vespa
curl --fail --silent --show-error "$API_URL/health/live" >/dev/null

echo "the operator must now POST a search and verify degraded=true/EsOnly fallback"
echo "restart Vespa; automatic RRF recovery is intentionally forbidden"
docker compose -f "$VESPA_COMPOSE" start vespa

echo "remaining required cases: Vespa slow/5xx/malformed/zero, ES stop/slow,"
echo "both down, SQLite lock, and Worker backlog. Record each response and timing."

