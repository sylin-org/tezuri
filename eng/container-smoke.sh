#!/usr/bin/env sh
set -eu

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
image=${TEZURI_SMOKE_IMAGE:-tezuri-local-smoke:dev}
port=${TEZURI_SMOKE_PORT:-18080}
container="tezuri-local-smoke-$$"
workspace=$(mktemp -d "${TMPDIR:-/tmp}/tezuri-container-smoke.XXXXXX")

cleanup() {
  docker rm --force "$container" >/dev/null 2>&1 || true
  case "$workspace" in
    "${TMPDIR:-/tmp}"/tezuri-container-smoke.*) rm -rf -- "$workspace" ;;
    *) echo "Refusing to remove unexpected smoke workspace: $workspace" >&2 ;;
  esac
}
trap cleanup EXIT INT TERM

cp -R "$repo_root/samples/folder-native-workspace/." "$workspace/"
docker build --build-arg VERSION=0.0.0-dev --build-arg REVISION=local-smoke --tag "$image" "$repo_root"
docker run --detach \
  --name "$container" \
  --platform linux/amd64 \
  --read-only \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  --tmpfs /tmp:rw,nosuid,nodev,size=256m,mode=1777 \
  --tmpfs /app/data:rw,nosuid,nodev,size=64m,mode=1777 \
  --mount "type=bind,src=$workspace,dst=/workspace" \
  --publish "127.0.0.1:${port}:8080" \
  "$image" >/dev/null

attempt=0
until [ "$(docker inspect --format '{{.State.Health.Status}}' "$container")" = healthy; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 60 ] || [ "$(docker inspect --format '{{.State.Running}}' "$container")" != true ]; then
    docker logs "$container"
    exit 1
  fi
  sleep 1
done

test "$(docker exec "$container" id -u)" != 0
curl --fail --silent --show-error "http://127.0.0.1:${port}/health/live" >/dev/null
curl --fail --silent --show-error "http://127.0.0.1:${port}/health/ready" >/dev/null
curl --fail --silent --show-error --head "http://127.0.0.1:${port}/" | grep -qi '^X-Content-Type-Options: nosniff'

nonce=$(docker logs "$container" 2>&1 | sed -n 's#.*http://127\.0\.0\.1:8080/?nonce=\([^[:space:]]*\).*#\1#p' | tail -n 1)
test -n "$nonce"
article_id=$(curl --fail --silent --show-error "http://127.0.0.1:${port}/api/v1/articles" | node -e \
  "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>process.stdout.write(JSON.parse(s).articles[0].id))")
source=$(curl --fail --silent --show-error "http://127.0.0.1:${port}/api/v1/articles/${article_id}/source")
patch=$(printf '%s' "$source" | node -e \
  "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>{const x=JSON.parse(s);process.stdout.write(JSON.stringify({protocol:'tezuri.source-patch-set',version:1,articleId:x.article.id,relativePath:x.article.relativePath,baseSha256:x.base.sha256,operations:[]}))})")
curl --fail --silent --show-error \
  --header "X-Tezuri-Nonce: $nonce" \
  --header "Origin: http://127.0.0.1:${port}" \
  --header 'Content-Type: application/json' \
  --data "$patch" \
  "http://127.0.0.1:${port}/api/v1/articles/${article_id}/source-patches" >/dev/null

echo "Container smoke passed for $image on 127.0.0.1:${port}."

