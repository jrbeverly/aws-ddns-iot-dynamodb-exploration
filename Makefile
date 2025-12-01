SLN        := src/Ddns/Ddns.slnx
UNIT_PROJ  := tests/Ddns/Ddns.UnitTests/Ddns.UnitTests.csproj
ITEST_PROJ := tests/Ddns/Ddns.IntegrationTests/Ddns.IntegrationTests.csproj
DDB_DIR    := tests/Ddns/local-dynamodb
TF_DIR     := env/ddns/validation

CONFIG      ?= Release
DDNS_RUN_ID ?= local

SHELL := /bin/bash
export DOTNET_NOLOGO := 1
export DOTNET_CLI_TELEMETRY_OPTOUT := 1

.ONESHELL:
.PHONY: build test-unit e2e-local e2e-aws package

build:
	dotnet build $(SLN) -c $(CONFIG)

test-unit: build
	dotnet test $(UNIT_PROJ) -c $(CONFIG) --no-build

# Real API + DynamoDB Local in Docker.
e2e-local: build
	set -euo pipefail
	url="$$(bash $(DDB_DIR)/up.sh $(DDNS_RUN_ID))"
	trap 'bash $(DDB_DIR)/down.sh $(DDNS_RUN_ID)' EXIT
	export Ddns__DynamoDb__ServiceUrl="$$url"
	export Ddns__DynamoDb__TableName="ddns-itest-$(DDNS_RUN_ID)"
	dotnet test $(ITEST_PROJ) -c $(CONFIG) --no-build

# Terraform apply -> curl -> destroy.
e2e-aws: package
	bash $(TF_DIR)/run-e2e-aws.sh "$(DDNS_RUN_ID)"

package:
	bash scripts/package-lambda.sh
