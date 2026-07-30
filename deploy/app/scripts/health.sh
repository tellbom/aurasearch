#!/usr/bin/env sh
set -eu
curl --fail --silent --show-error "${APP_HEALTH_URL:?APP_HEALTH_URL must be set}/health/ready"

