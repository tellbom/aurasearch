#!/usr/bin/env sh
set -eu
: "${VESPA_QUERY_URL:=http://127.0.0.1:8080}"
: "${QUERY:?QUERY must be set}"
: "${HITS:=10}"

run() {
  profile=$1
  gram_match=$2
  curl --fail --silent --show-error --get "$VESPA_QUERY_URL/search/" \
    --data-urlencode "yql=select news_id,title from news where userQuery()" \
    --data-urlencode "query=$QUERY" \
    --data-urlencode "ranking=$profile" \
    --data-urlencode "gram.match=$gram_match" \
    --data-urlencode "hits=$HITS" \
    --data-urlencode "tracelevel=3"
}

run cjk_bm25_all all
run cjk_bm25_weakand weakAnd
run cjk_native_all all
run cjk_native_weakand weakAnd
run pretokenized_bm25 all

