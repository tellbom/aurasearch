#!/usr/bin/env sh
set -eu
: "${API_URL:?API_URL must be set}"
: "${QUERY_FILE:?QUERY_FILE must be set}"
: "${CONCURRENCY:=8}"
: "${REQUESTS:=200}"
: "${OUTPUT:=search-latencies.tsv}"

rm -f "$OUTPUT"
i=0
while [ "$i" -lt "$REQUESTS" ]; do
  i=$((i + 1))
  query=$(sed -n "$(( (i - 1) % $(wc -l < "$QUERY_FILE") + 1 ))p" "$QUERY_FILE")
  printf '%s\n' "$query"
done | xargs -P "$CONCURRENCY" -I '{}' sh -c '
  escaped=$(printf "%s" "$1" | sed "s/\\/\\\\\\\\/g; s/\"/\\\\\"/g")
  curl --silent --output /dev/null --write-out "%{http_code}\t%{time_total}\n" \
    --header "Content-Type: application/json" \
    --data "{\"query\":\"$escaped\",\"page\":1,\"pageSize\":20}" \
    "$2/api/v1/search"
' sh '{}' "$API_URL" >> "$OUTPUT"

echo "Raw per-request status and latency written to $OUTPUT."
echo "Calculate P50/P95/P99 from raw samples; do not average pre-aggregated percentiles."

