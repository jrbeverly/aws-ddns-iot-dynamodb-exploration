#!/usr/bin/env bash
set -euo pipefail
# tests/Ddns/local-dynamodb/up.sh <run-id>
#
# Stands up a hermetic DynamoDB Local container and prints its endpoint URL to
# stdout. No AWS account, no credentials. This is the LOCAL counterpart of the
# Terraform-provisioned AWS gate: same service code, real DynamoDB API.
#
#   make e2e-local captures the URL and tears the container down on exit.
#
# Pair with down.sh in an `if: always()` step / shell trap.

run_id="${1:?usage: up.sh <run-id>}"
name="ddns-dynamodb-local-${run_id}"

fail() { echo "local-dynamodb/up: $*" >&2; exit 1; }
command -v docker >/dev/null 2>&1 || fail "docker is required but was not found on PATH."

if [ "$(docker inspect -f '{{.State.Running}}' "$name" 2>/dev/null || echo false)" != "true" ]; then
  docker rm -f "$name" >/dev/null 2>&1 || true
  # Docker picks a free 127.0.0.1 host port, so concurrent runs never clash.
  # -sharedDb: one datastore regardless of the SDK's creds/region.
  docker run -d --name "$name" -p 127.0.0.1::8000 \
    amazon/dynamodb-local -jar DynamoDBLocal.jar -inMemory -sharedDb >/dev/null
fi

hostport="$(docker port "$name" 8000/tcp | head -n1)"   # e.g. 127.0.0.1:49154
[ -n "$hostport" ] || fail "could not resolve the DynamoDB Local host port for ${name}."

# DynamoDB Local answers HTTP (400 'missing auth token') once it is up; any
# HTTP status means the port is serving.
for _ in $(seq 1 30); do
  code="$(curl -s -o /dev/null -w '%{http_code}' "http://${hostport}/" 2>/dev/null || echo 000)"
  if [ "$code" != "000" ]; then ready=1; break; fi
  sleep 1
done
[ "${ready:-0}" = 1 ] || fail "DynamoDB Local at ${hostport} did not become ready."

echo "local-dynamodb/up: ${name} ready at http://${hostport}" >&2
echo "http://${hostport}"
