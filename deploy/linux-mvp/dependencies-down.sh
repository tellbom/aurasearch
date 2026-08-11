#!/usr/bin/env sh
set -eu

# Containers are removed, while named data volumes are deliberately retained.
for name in aurasearch-mvp-es aurasearch-mvp-vespa; do
  if docker container inspect "$name" >/dev/null 2>&1; then
    docker rm --force "$name" >/dev/null
    echo "Removed $name (data volume retained)."
  fi
done

