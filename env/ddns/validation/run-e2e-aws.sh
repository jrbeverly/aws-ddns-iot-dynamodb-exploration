#!/usr/bin/env bash
set -euo pipefail

# env/ddns/validation/run-e2e-aws.sh <run-id>
# terraform apply -> curl register/get/list/health -> terraform destroy (EXIT trap).
# Optional mTLS: set DDNS_DOMAIN_NAME, DDNS_HOSTED_ZONE_ID, DDNS_TRUSTSTORE_S3_BUCKET, DDNS_TRUSTSTORE_S3_KEY.

name_prefix="$(printf '%s' "${1:?usage: run-e2e-aws.sh <run-id>}" | tr '[:upper:]' '[:lower:]' | tr -c 'a-z0-9-' '-' | cut -c1-48)"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
lambda_zip="$(cd "$script_dir/../../.." && pwd)/.build/ddns-lambda.zip"

tf_vars=(-var "name_prefix=$name_prefix" -var "lambda_zip=$lambda_zip")
add_var() { if [ -n "${2:-}" ]; then tf_vars+=(-var "$1=$2"); fi; }
add_var aws_profile          "${DDNS_AWS_PROFILE:-}"
add_var domain_name          "${DDNS_DOMAIN_NAME:-}"
add_var hosted_zone_id       "${DDNS_HOSTED_ZONE_ID:-}"
add_var truststore_s3_bucket "${DDNS_TRUSTSTORE_S3_BUCKET:-}"
add_var truststore_s3_key    "${DDNS_TRUSTSTORE_S3_KEY:-}"

device="cccccccc-cccc-cccc-cccc-cccccccccccc"
ip="203.0.113.10"

cert_dir=""
mtls_active=false
CURL_MTLS=()
if [ -n "${DDNS_DOMAIN_NAME:-}" ] && [ -n "${DDNS_HOSTED_ZONE_ID:-}" ] \
   && [ -n "${DDNS_TRUSTSTORE_S3_BUCKET:-}" ] && [ -n "${DDNS_TRUSTSTORE_S3_KEY:-}" ]; then
  mtls_active=true
  cert_dir="$(mktemp -d)"
  # Throwaway CA (the API Gateway truststore) and a client cert with CN = device GUID.
  openssl req -x509 -newkey rsa:2048 -keyout "$cert_dir/ca.key" -out "$cert_dir/ca.pem" \
    -days 1 -nodes -subj "/CN=DDNS Prototype CA" >/dev/null 2>&1
  openssl req -newkey rsa:2048 -keyout "$cert_dir/client.key" -out "$cert_dir/client.csr" \
    -nodes -subj "/CN=$device" >/dev/null 2>&1
  openssl x509 -req -in "$cert_dir/client.csr" -CA "$cert_dir/ca.pem" -CAkey "$cert_dir/ca.key" \
    -days 1 -set_serial 01 -out "$cert_dir/client.pem" >/dev/null 2>&1
  cat "$cert_dir/client.pem" "$cert_dir/client.key" > "$cert_dir/client-bundle.pem"
  CURL_MTLS=(--cert "$cert_dir/client-bundle.pem")
  tf_vars+=(-var "truststore_bundle=$cert_dir/ca.pem")
fi

cd "$script_dir"
cleanup() {
  terraform destroy -auto-approve -input=false "${tf_vars[@]}" || true
  [ -z "$cert_dir" ] || rm -rf "$cert_dir"
}
trap cleanup EXIT

terraform init -input=false
terraform apply -auto-approve -input=false "${tf_vars[@]}"

base_url="$(terraform output -raw custom_domain_url 2>/dev/null || true)"
[ -n "$base_url" ] || base_url="$(terraform output -raw api_base_url)"
echo "run-e2e-aws: API at $base_url"

fail() { echo "run-e2e-aws: CHECK FAILED: $*" >&2; exit 1; }

if $mtls_active; then
  default_url="$(terraform output -raw api_base_url)"
  if curl -fsS "${CURL_MTLS[@]}" "$default_url/api/health" >/dev/null 2>&1; then
    fail "default execute-api endpoint still accepts traffic at $default_url"
  fi
  echo "run-e2e-aws: default execute-api endpoint disabled"
  code="$(curl -s -o /dev/null -w '%{http_code}' "$base_url/api/health" || true)"
  echo "run-e2e-aws: custom domain without client cert -> ${code}"
fi

register() {
  curl -fsS "${CURL_MTLS[@]}" -X PUT "$base_url/api/devices/$device/registration" \
    -H 'content-type: application/json' -d "{\"ip\":\"$ip\"}"
}
body=""
for attempt in $(seq 1 10); do
  if body="$(register 2>&1)"; then break; fi
  echo "run-e2e-aws: register attempt $attempt failed: $body" >&2
  sleep 5
done
grep -q "\"$ip\"" <<<"$body" || fail "register response missing ip: $body"
if $mtls_active; then
  grep -q '"identitySource":"client-cert"' <<<"$body" || fail "identity source is not client-cert: $body"
fi
echo "run-e2e-aws: register OK -> $body"

get_body="$(curl -fsS "${CURL_MTLS[@]}" "$base_url/api/devices/$device")"
grep -q "\"$ip\"" <<<"$get_body" || fail "get response missing ip: $get_body"
echo "run-e2e-aws: get OK -> $get_body"

fleet_body="$(curl -fsS "${CURL_MTLS[@]}" "$base_url/api/devices")"
grep -q "$device" <<<"$fleet_body" || fail "fleet listing missing $device: $fleet_body"
echo "run-e2e-aws: fleet OK -> $fleet_body"

health_code="$(curl -s "${CURL_MTLS[@]}" -o /dev/null -w '%{http_code}' "$base_url/api/health")"
[ "$health_code" = "200" ] || fail "health returned $health_code"

echo "run-e2e-aws: ALL CHECKS PASSED (teardown runs on exit)."
