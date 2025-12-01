provider "aws" {
  region  = "us-west-2"
  profile = var.aws_profile
  default_tags {
    tags = {
      ManagedBy = "terraform"
      Project   = "aws-ddns-iot-dynamodb-exploration"
      Purpose   = "disposable-pr-validation"
    }
  }
}
