#!/usr/bin/env sh
set -eu
: "${API_URL:?API_URL must be set}"
: "${VESPA_CONTAINER:=aurasearch-mvp-vespa}"

echo "baseline"
curl --fail --silent --show-error "$API_URL/health/live" >/dev/null

echo "stop Vespa and verify the API remains live"
docker stop "$VESPA_CONTAINER" >/dev/null
curl --fail --silent --show-error "$API_URL/health/live" >/dev/null

echo "the operator must now POST a search and verify degraded=true/EsOnly fallback"
echo "restart Vespa; automatic RRF recovery is intentionally forbidden"
docker start "$VESPA_CONTAINER" >/dev/null

echo "remaining required cases: Vespa slow/5xx/malformed/zero, ES stop/slow,"
echo "both search engines down, DM connection interruption, and Worker backlog. Record each response and timing."
