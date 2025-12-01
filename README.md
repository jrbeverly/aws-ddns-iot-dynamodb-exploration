# AWS DDNS IoT DynamoDB Exploration

> [!WARNING]
> **AI-authored:** This change was autonomously planned and implemented by an AI software factory from a human-authored specification, with possible subsequent human review or modification.

Tests a device IP inventory: devices `PUT` their IP to a minimal API on Lambda behind an API Gateway HTTP API, stored as one DynamoDB item per device with a GSI for fleet listing. Optionally identifies devices by mTLS client certificate CN instead of the route GUID. Not DNS: there is no Route 53 record management.

```sh
make test-unit
make e2e-local
make e2e-aws

DDNS_DOMAIN_NAME=ddns.example.com DDNS_HOSTED_ZONE_ID=Z... \
DDNS_TRUSTSTORE_S3_BUCKET=my-bucket DDNS_TRUSTSTORE_S3_KEY=truststore.pem make e2e-aws
```

## Notes

- DDNS DynamoDB system
- recurring issue; IAM policy uses JSON encoding instead of data resources
- excessive comments in Terraform code
- weird naming strategy; not ideal
- overall structure is there
- tendency to overwrite shell scripts somewhat
- likely more a scaffolding/build-process gap than a core Factory problem
- fundamentally straightforward implementation; limited surface area for things to go wrong
- generally pretty decent overall
- not much else notable on this one
- good demonstration of the Factory quickly prototyping a straightforward/simple system
