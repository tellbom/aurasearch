#!/usr/bin/env sh
set -eu

# The Linux VM hosts only the two Dockerized search dependencies. The API and
# MVP test client run on the developer machine and connect through mapped ports.
: "${ES_IMAGE:=elasticsearch-ik:1.0}"
: "${VESPA_IMAGE:=vespaengine/vespa:8.721.11@sha256:b03347e1fdc29667c8d4656f5e19ee21d2a2195d11e629d7f42432bba144da3e}"
: "${DEPENDENCY_BIND_ADDRESS:=0.0.0.0}"
: "${VESPA_CONFIG_BIND_ADDRESS:=0.0.0.0}"
: "${ES_PORT:=29200}"
: "${VESPA_QUERY_PORT:=28080}"
: "${VESPA_CONFIG_PORT:=29071}"
: "${ES_JAVA_OPTS:=-Xms30g -Xmx30g}"
: "${VESPA_MEMORY_LIMIT:=160g}"
: "${VESPA_CPU_LIMIT:=60}"

project_name=aurasearch-mvp
network_name="$project_name"
es_container="$project_name-es"
vespa_container="$project_name-vespa"

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Required command is missing: $1" >&2
    exit 1
  }
}

wait_http() {
  url=$1
  attempts=$2
  label=$3
  i=1
  while [ "$i" -le "$attempts" ]; do
    if curl --fail --silent --show-error "$url" >/dev/null 2>&1; then
      return 0
    fi
    if [ $((i % 10)) -eq 0 ]; then
      echo "Waiting for $label ($i/$attempts)..."
    fi
    sleep 3
    i=$((i + 1))
  done
  echo "$label did not become ready: $url" >&2
  return 1
}

remove_owned_container() {
  name=$1
  if docker container inspect "$name" >/dev/null 2>&1; then
    echo "Recreating managed container $name; named data volumes are preserved."
    docker rm --force "$name" >/dev/null
  fi
}

require_command docker
require_command curl

docker info >/dev/null

current_map_count=$(sysctl -n vm.max_map_count)
if [ "$current_map_count" -lt 262144 ]; then
  if [ "$(id -u)" -ne 0 ]; then
    echo "Run as root once to raise vm.max_map_count to 262144." >&2
    exit 1
  fi
  printf '%s\n' 'vm.max_map_count=262144' > /etc/sysctl.d/99-aurasearch-mvp.conf
  sysctl --system >/dev/null
fi

if ! docker image inspect "$ES_IMAGE" >/dev/null 2>&1; then
  echo "Elasticsearch IK image is not present locally: $ES_IMAGE" >&2
  echo "Load or pull an approved Elasticsearch 7.x image containing analysis-ik, then retry." >&2
  exit 1
fi

if ! docker image inspect "$VESPA_IMAGE" >/dev/null 2>&1; then
  echo "Pulling pinned Vespa image..."
  docker pull "$VESPA_IMAGE"
fi

docker network inspect "$network_name" >/dev/null 2>&1 ||
  docker network create "$network_name" >/dev/null
docker volume inspect "$project_name-es-data" >/dev/null 2>&1 ||
  docker volume create "$project_name-es-data" >/dev/null
docker volume inspect "$project_name-vespa-var" >/dev/null 2>&1 ||
  docker volume create "$project_name-vespa-var" >/dev/null
docker volume inspect "$project_name-vespa-logs" >/dev/null 2>&1 ||
  docker volume create "$project_name-vespa-logs" >/dev/null

remove_owned_container "$es_container"
remove_owned_container "$vespa_container"

docker run --detach \
  --name "$es_container" \
  --restart unless-stopped \
  --network "$network_name" \
  --ulimit nofile=65535:65535 \
  --env discovery.type=single-node \
  --env xpack.security.enabled=false \
  --env "ES_JAVA_OPTS=$ES_JAVA_OPTS" \
  --volume "$project_name-es-data:/usr/share/elasticsearch/data" \
  --publish "$DEPENDENCY_BIND_ADDRESS:$ES_PORT:9200" \
  "$ES_IMAGE" >/dev/null

docker run --detach \
  --name "$vespa_container" \
  --hostname vespa \
  --restart unless-stopped \
  --network "$network_name" \
  --ulimit nofile=262144:262144 \
  --memory "$VESPA_MEMORY_LIMIT" \
  --cpus "$VESPA_CPU_LIMIT" \
  --volume "$project_name-vespa-var:/opt/vespa/var" \
  --volume "$project_name-vespa-logs:/opt/vespa/logs" \
  --publish "$DEPENDENCY_BIND_ADDRESS:$VESPA_QUERY_PORT:8080" \
  --publish "$VESPA_CONFIG_BIND_ADDRESS:$VESPA_CONFIG_PORT:19071" \
  "$VESPA_IMAGE" >/dev/null

if ! wait_http "http://127.0.0.1:$ES_PORT/" 60 "Elasticsearch"; then
  docker logs --tail 200 "$es_container" >&2
  exit 1
fi

es_version=$(curl --fail --silent "http://127.0.0.1:$ES_PORT/" | sed -n 's/.*"number"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)
case "$es_version" in
  7.*) ;;
  *) echo "Expected Elasticsearch 7.x, got '$es_version'." >&2; exit 1 ;;
esac

if ! curl --fail --silent "http://127.0.0.1:$ES_PORT/_cat/plugins?h=component" | grep -qx 'analysis-ik'; then
  echo "Elasticsearch image does not expose the required analysis-ik plugin." >&2
  exit 1
fi

if ! wait_http "http://127.0.0.1:$VESPA_CONFIG_PORT/state/v1/health" 120 "Vespa config server"; then
  docker logs --tail 200 "$vespa_container" >&2
  exit 1
fi

echo "Dependencies are ready."
echo "Elasticsearch: $DEPENDENCY_BIND_ADDRESS:$ES_PORT (Elasticsearch $es_version with analysis-ik)"
echo "Vespa Query/Document API: $DEPENDENCY_BIND_ADDRESS:$VESPA_QUERY_PORT"
echo "Vespa Config API: $VESPA_CONFIG_BIND_ADDRESS:$VESPA_CONFIG_PORT"
echo "The .NET API owns Elasticsearch index/alias and Vespa Application Package provisioning."
