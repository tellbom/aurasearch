#!/usr/bin/env sh
set -eu
: "${IMAGE_TAR:?IMAGE_TAR must be set}"
sha256sum -c "$IMAGE_TAR.sha256"
docker load -i "$IMAGE_TAR"

