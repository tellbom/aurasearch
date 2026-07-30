#!/usr/bin/env sh
set -eu
: "${OUTPUT_DIR:?OUTPUT_DIR must be set}"
image="vespaengine/vespa:8.721.11"
mkdir -p "$OUTPUT_DIR"
docker pull "$image"
actual=$(docker image inspect "$image" --format '{{index .RepoDigests 0}}')
case "$actual" in
  *@sha256:b03347e1fdc29667c8d4656f5e19ee21d2a2195d11e629d7f42432bba144da3e) ;;
  *) echo "Unexpected Vespa image digest: $actual" >&2; exit 3 ;;
esac
docker save "$image" -o "$OUTPUT_DIR/vespa-8.721.11-linux-amd64.tar"
sha256sum "$OUTPUT_DIR/vespa-8.721.11-linux-amd64.tar" > "$OUTPUT_DIR/vespa-8.721.11-linux-amd64.tar.sha256"

