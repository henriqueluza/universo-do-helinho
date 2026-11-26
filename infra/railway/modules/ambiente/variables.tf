# Entradas de um ambiente. Descrições, padrões e validações de formato ficam no root (../../variables.tf).

variable "nome" { type = string }
variable "workspace_id" { type = string }
variable "aspnetcore_environment" { type = string }
variable "subdominio" { type = string }
variable "dominio_proprio" { type = string }
variable "repositorio" { type = string }
variable "branch" { type = string }
variable "auto_deploy" { type = bool }
variable "regiao" { type = string }
variable "serverless" { type = bool }
variable "postgres_imagem" { type = string }
variable "seed_enabled" { type = bool }
variable "variaveis_web" { type = map(string) }

variable "segredos_web" {
  type      = map(string)
  sensitive = true
}

variable "seed_password" {
  type      = string
  sensitive = true

  validation {
    condition     = !var.seed_enabled || (var.nome == "homologacao" && var.seed_password != null)
    error_message = "O seed só pode ser ligado em homologacao e exige seed_password."
  }
}
