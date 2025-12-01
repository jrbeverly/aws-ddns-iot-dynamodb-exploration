#!/usr/bin/env bash
set -euo pipefail
# tests/Ddns/local-dynamodb/down.sh <run-id>
#
# Tears down the DynamoDB Local container created by up.sh. Idempotent: safe to
# run in an `if: always()` step or shell trap even if up.sh never ran.

run_id="${1:?usage: down.sh <run-id>}"
name="ddns-dynamodb-local-${run_id}"

if command -v docker >/dev/null 2>&1; then
  docker rm -f "$name" >/dev/null 2>&1 || true
  echo "local-dynamodb/down: removed ${name} (if it existed)." >&2
else
  echo "local-dynamodb/down: docker not found; nothing to do." >&2
fi
