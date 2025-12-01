variable "name_prefix" {
  type = string
}

# Absolute path to the self-contained Lambda zip produced by
# scripts/package-lambda.sh. Passed by the harness, never hand-edited.
variable "lambda_zip" {
  type = string
}

# Optional custom-domain and mTLS inputs.

variable "aws_profile" {
  type    = string
  default = null
}

# Custom domain for the HTTP API. Unset = the default API Gateway invoke URL
# is the only front door.
variable "domain_name" {
  type    = string
  default = null
}

# Route 53 hosted zone where the custom-domain DNS validation record and
# alias will be created. Required only when domain_name is set.
variable "hosted_zone_id" {
  type    = string
  default = null
}

# S3 bucket that holds the mTLS truststore bundle. Required only when the
# stack wires mutual TLS at the API Gateway.
variable "truststore_s3_bucket" {
  type    = string
  default = null
}

# Object key of the truststore inside truststore_s3_bucket.
variable "truststore_s3_key" {
  type    = string
  default = null
}

# Local path to the client certificate truststore bundle (PEM file).
# Required only when the stack wires mutual TLS at the API Gateway.
variable "truststore_bundle" {
  type    = string
  default = null
}
