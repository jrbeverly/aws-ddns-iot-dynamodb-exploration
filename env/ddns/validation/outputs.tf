output "api_base_url" {
  description = "Default API Gateway stage invoke URL (fallback when custom domain is not configured)."
  value       = aws_apigatewayv2_stage.default.invoke_url
}

output "custom_domain_url" {
  description = "Custom domain base URL (https) for the API. Only set when domain_name and hosted_zone_id are both configured."
  value       = local.custom_domain_enabled ? "https://${var.domain_name}" : null
}

output "table_name" {
  value = aws_dynamodb_table.ddns.name
}
