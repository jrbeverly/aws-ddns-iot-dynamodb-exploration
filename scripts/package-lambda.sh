#!/usr/bin/env bash
set -euo pipefail

# scripts/package-lambda.sh
#
# Produces .build/ddns-lambda.zip for the AWS gate.
#
# AWS has no managed dotnet10 runtime, so we publish a SELF-CONTAINED net10.0
# app and run it on the `provided.al2023` custom runtime. The .NET apphost
# embeds its managed entry-assembly name at publish time (independent of the
# apphost's own filename), so renaming the apphost to `bootstrap` — what
# `provided.al2023` execs — is sufficient and needs no shell shim.
#
# Same artifact, same framework the local gate tests. Runnable locally.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

api_proj="src/Ddns/Ddns.Api/Ddns.Api.csproj"
out_dir=".build/lambda"
zip_path=".build/ddns-lambda.zip"
rid="linux-x64"

rm -rf "$out_dir" "$zip_path"

dotnet publish "$api_proj" \
  -c Release \
  -r "$rid" \
  --self-contained true \
  -o "$out_dir" \
  -p:PublishReadyToRun=true \
  -p:GenerateRuntimeConfigDevFile=false

[ -f "$out_dir/Ddns.Api" ] || { echo "package-lambda: expected apphost $out_dir/Ddns.Api not found" >&2; exit 1; }
mv "$out_dir/Ddns.Api" "$out_dir/bootstrap"
chmod +x "$out_dir/bootstrap"

( cd "$out_dir" && zip -qr "../ddns-lambda.zip" . )
echo "package-lambda: wrote $zip_path ($(du -h "$zip_path" | cut -f1))"
