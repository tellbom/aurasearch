#!/usr/bin/env sh
set -eu
: "${VESPA_CONFIG_URL:=http://127.0.0.1:19071}"
: "${VESPA_QUERY_URL:=http://127.0.0.1:8080}"
curl --fail --silent --show-error "$VESPA_CONFIG_URL/state/v1/health"
curl --fail --silent --show-error "$VESPA_QUERY_URL/ApplicationStatus"

