#!/usr/bin/env sh
set -eu

: "${API_URL:=http://127.0.0.1:28088}"
: "${DOCUMENTS:=2000}"
: "${SAMPLES:=200}"
: "${CONCURRENCY:=16}"
: "${SYNC_TIMEOUT:=1800}"

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/../.." && pwd)
load_tool="$repo_root/tools/loadtest/api_load.py"

python3 "$load_tool" --api-url "$API_URL" ingest \
  --documents "$DOCUMENTS" --batch-size 200 --parallel-batches 4
python3 "$load_tool" --api-url "$API_URL" wait-sync \
  --documents "$DOCUMENTS" --timeout "$SYNC_TIMEOUT"

for mode in EsOnly VespaOnly Rrf; do
  python3 "$load_tool" --api-url "$API_URL" accuracy \
    --mode "$mode" --documents "$DOCUMENTS" --samples "$SAMPLES" --concurrency "$CONCURRENCY"
done

curl --fail --silent --show-error "$API_URL/api/v1/operations/indexing-snapshot"
printf '\nMVP dual-engine ingest, synchronization, and search accuracy checks passed.\n'

