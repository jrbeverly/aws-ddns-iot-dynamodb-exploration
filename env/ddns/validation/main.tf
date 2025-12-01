# Single-table DynamoDB store, a self-contained .NET 10 Lambda on the
# provided.al2023 custom runtime, and an HTTP API in front.

locals {
  name                  = "ddns-val-${var.name_prefix}"
  custom_domain_enabled = var.domain_name != null && var.hosted_zone_id != null
  mtls_enabled          = local.custom_domain_enabled && var.truststore_s3_bucket != null && var.truststore_s3_key != null && var.truststore_bundle != null
}

data "aws_caller_identity" "current" {}

# The self-contained net10.0 package is ~49 MB zipped — too close to Lambda's
# 50 MB direct-upload ceiling, so it ships via S3 (250 MB unzipped headroom).
# force_destroy keeps `terraform destroy` clean.
resource "aws_s3_bucket" "lambda" {
  bucket        = "${local.name}-lambda-${data.aws_caller_identity.current.account_id}"
  force_destroy = true
}

resource "aws_s3_bucket_public_access_block" "lambda" {
  bucket                  = aws_s3_bucket.lambda.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_object" "lambda" {
  bucket = aws_s3_bucket.lambda.id
  key    = "ddns-lambda.zip"
  source = var.lambda_zip
  etag   = filemd5(var.lambda_zip)
}

# mTLS truststore bundle (client certificates the API Gateway should trust).
# Uploaded to an operator-controlled bucket so the domain name can reference it.
resource "aws_s3_object" "truststore" {
  count  = local.mtls_enabled ? 1 : 0
  bucket = var.truststore_s3_bucket
  key    = var.truststore_s3_key
  source = var.truststore_bundle
  etag   = filemd5(var.truststore_bundle)
}

# ---------------------------------------------------------------------------
# Storage: the fleet IP inventory (single-table, on-demand). PK/SK + GSI1
# exactly mirrors DynamoDbDeviceRegistrationRepository and the local gate.
# ---------------------------------------------------------------------------
resource "aws_dynamodb_table" "ddns" {
  name         = local.name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "PK"
  range_key    = "SK"

  attribute {
    name = "PK"
    type = "S"
  }
  attribute {
    name = "SK"
    type = "S"
  }
  attribute {
    name = "GSI1PK"
    type = "S"
  }
  attribute {
    name = "GSI1SK"
    type = "S"
  }

  global_secondary_index {
    name            = "GSI1"
    hash_key        = "GSI1PK"
    range_key       = "GSI1SK"
    projection_type = "ALL"
  }
}

# ---------------------------------------------------------------------------
# Lambda execution role: least-privilege DynamoDB + its own log group.
# ---------------------------------------------------------------------------
resource "aws_iam_role" "lambda" {
  name = "${local.name}-exec"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "lambda.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_cloudwatch_log_group" "lambda" {
  name              = "/aws/lambda/${local.name}"
  retention_in_days = 1
}

resource "aws_iam_role_policy" "lambda" {
  name = "${local.name}-policy"
  role = aws_iam_role.lambda.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:Query"]
        Resource = [aws_dynamodb_table.ddns.arn, "${aws_dynamodb_table.ddns.arn}/index/*"]
      },
      {
        Effect   = "Allow"
        Action   = ["logs:CreateLogStream", "logs:PutLogEvents"]
        Resource = "${aws_cloudwatch_log_group.lambda.arn}:*"
      }
    ]
  })
}

# ---------------------------------------------------------------------------
# The service: self-contained net10.0 on provided.al2023 (apphost renamed to
# `bootstrap` by scripts/package-lambda.sh — same framework the local gate tests).
# ---------------------------------------------------------------------------
resource "aws_lambda_function" "api" {
  function_name    = local.name
  role             = aws_iam_role.lambda.arn
  runtime          = "provided.al2023"
  handler          = "bootstrap"
  architectures    = ["x86_64"]
  s3_bucket        = aws_s3_bucket.lambda.id
  s3_key           = aws_s3_object.lambda.key
  source_code_hash = filebase64sha256(var.lambda_zip)
  memory_size      = 512
  timeout          = 30

  environment {
    variables = {
      Ddns__DynamoDb__TableName             = aws_dynamodb_table.ddns.name
      Ddns__MutualTls__Enabled              = tostring(local.mtls_enabled)
      DOTNET_SYSTEM_GLOBALIZATION_INVARIANT = "1"
    }
  }

  depends_on = [aws_cloudwatch_log_group.lambda]
}

# ---------------------------------------------------------------------------
# HTTP API → Lambda proxy. $default route forwards every path/method so the
# Minimal API does its own routing (same as local).
# ---------------------------------------------------------------------------
resource "aws_apigatewayv2_api" "http" {
  name                         = local.name
  protocol_type                = "HTTP"
  disable_execute_api_endpoint = local.mtls_enabled
}

resource "aws_apigatewayv2_integration" "lambda" {
  api_id                 = aws_apigatewayv2_api.http.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.api.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "default" {
  api_id    = aws_apigatewayv2_api.http.id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.lambda.id}"
}

resource "aws_apigatewayv2_stage" "default" {
  api_id      = aws_apigatewayv2_api.http.id
  name        = "$default"
  auto_deploy = true
}

resource "aws_lambda_permission" "apigw" {
  statement_id  = "AllowApiGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.api.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.http.execution_arn}/*/*"
}

# ---------------------------------------------------------------------------
# Custom domain: Regional API Gateway domain + ACM cert + Route 53 alias.
# Only provisioned when both domain_name and hosted_zone_id are supplied.
# The api_mapping has no api_mapping_key so the full request path (including
# /api) arrives at Lambda identically to the default invoke URL path.
# ---------------------------------------------------------------------------
resource "aws_acm_certificate" "api" {
  count             = local.custom_domain_enabled ? 1 : 0
  domain_name       = var.domain_name
  validation_method = "DNS"

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_route53_record" "cert_validation" {
  for_each = {
    for dvo in flatten(aws_acm_certificate.api[*].domain_validation_options) :
    dvo.domain_name => dvo
  }

  zone_id = var.hosted_zone_id
  name    = each.value.resource_record_name
  type    = each.value.resource_record_type
  records = [each.value.resource_record_value]
  ttl     = 60
}

resource "aws_acm_certificate_validation" "api" {
  count = local.custom_domain_enabled ? 1 : 0

  certificate_arn         = aws_acm_certificate.api[0].arn
  validation_record_fqdns = [for r in aws_route53_record.cert_validation : r.fqdn]
}

resource "aws_apigatewayv2_domain_name" "api" {
  count = local.custom_domain_enabled ? 1 : 0

  domain_name = var.domain_name

  domain_name_configuration {
    certificate_arn = aws_acm_certificate_validation.api[0].certificate_arn
    endpoint_type   = "REGIONAL"
    security_policy = "TLS_1_2"
  }

  dynamic "mutual_tls_authentication" {
    for_each = local.mtls_enabled ? [1] : []
    content {
      truststore_uri     = "s3://${var.truststore_s3_bucket}/${var.truststore_s3_key}"
      truststore_version = aws_s3_object.truststore[0].version_id
    }
  }
}

resource "aws_route53_record" "api_alias" {
  count   = local.custom_domain_enabled ? 1 : 0
  zone_id = var.hosted_zone_id
  name    = var.domain_name
  type    = "A"

  alias {
    name                   = aws_apigatewayv2_domain_name.api[0].domain_name_configuration[0].target_domain_name
    zone_id                = aws_apigatewayv2_domain_name.api[0].domain_name_configuration[0].hosted_zone_id
    evaluate_target_health = false
  }
}

resource "aws_apigatewayv2_api_mapping" "root" {
  count       = local.custom_domain_enabled ? 1 : 0
  api_id      = aws_apigatewayv2_api.http.id
  domain_name = aws_apigatewayv2_domain_name.api[0].id
  stage       = aws_apigatewayv2_stage.default.id
}
