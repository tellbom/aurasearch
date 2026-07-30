#!/usr/bin/env sh
set -eu
docker network inspect "${SEARCH_NETWORK:-dual-news-search}" >/dev/null 2>&1 ||
  docker network create --internal "${SEARCH_NETWORK:-dual-news-search}" >/dev/null
docker network inspect "${SEARCH_UPSTREAM_NETWORK:-dual-news-search-upstream}" >/dev/null 2>&1 ||
  docker network create "${SEARCH_UPSTREAM_NETWORK:-dual-news-search-upstream}" >/dev/null
docker compose -f "$(dirname "$0")/../docker-compose.yml" up -d
