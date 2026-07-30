#!/usr/bin/env sh
set -eu
: "${VESPA_CONFIG_URL:=http://127.0.0.1:19071}"
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
application_dir="$script_dir/../application"
temporary_dir=$(mktemp -d)
archive="$temporary_dir/application.zip"
trap 'rm -rf "$temporary_dir"' EXIT HUP INT TERM
(cd "$application_dir" && zip -qr "$archive" .)
curl --fail --silent --show-error \
  --header "Content-Type: application/zip" \
  --data-binary "@$archive" \
  "$VESPA_CONFIG_URL/application/v2/tenant/default/prepareandactivate"
