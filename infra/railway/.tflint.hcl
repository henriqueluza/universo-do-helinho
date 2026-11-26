tflint {
  required_version = ">= 0.66"
}

# Ruleset da linguagem Terraform, embutido no binário do tflint (nada é baixado no `tflint --init`).
plugin "terraform" {
  enabled = true
  preset  = "recommended"
}
