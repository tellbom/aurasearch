#!/usr/bin/env sh
set -eu
docker compose -f "$(dirname "$0")/../docker-compose.yml" logs -f dual-news-search

