#!/usr/bin/env sh
set -eu
if [ "${ENVIRONMENT:-}" != "Development" ] || [ "${CONFIRM_RESET:-}" != "DELETE_DEV_VESPA_DATA" ]; then
  echo "Refusing reset: set ENVIRONMENT=Development and CONFIRM_RESET=DELETE_DEV_VESPA_DATA." >&2
  exit 2
fi
: "${VESPA_DOCUMENT_URL:=http://127.0.0.1:8080}"
curl --fail --silent --show-error -X DELETE \
  "$VESPA_DOCUMENT_URL/document/v1/news/news/docid?selection=true&cluster=news-content"

