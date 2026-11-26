terraform {
  required_version = ">= 1.16.0, < 1.18.0"

  required_providers {
    railway = {
      source  = "terraform-community-providers/railway"
      version = "0.6.3"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
  }
}
