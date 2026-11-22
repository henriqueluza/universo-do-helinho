# Segredo nunca tem valor real em `default` (`null` ou vazio = não definido) e leva `sensitive = true`.
# `nullable = false`: `null` explícito vale o padrão e não escapa das validações. Só `workspace_id` e
# `seed_password` aceitam `null`, que neles quer dizer "não definido".

variable "workspace_id" {
  description = "Id do workspace da Railway. Obrigatório se o token enxergar mais de um workspace."
  type        = string
  default     = null
}

variable "ambientes" {
  description = "Ambientes a criar: um projeto Railway por chave. `producao` nasce desligada e só existe se for acrescentada aqui."
  type = map(object({
    seed_enabled = optional(bool, false)
    subdominio   = optional(string) # padrão: helinho-<ambiente>; precisa ser único em up.railway.app
    # Domínio próprio do serviço web (FQDN em minúsculas, sem esquema nem barra); vazio = só o *.up.railway.app.
    dominio_proprio = optional(string, "")
  }))
  default  = { homologacao = { seed_enabled = true } }
  nullable = false

  validation {
    condition     = alltrue([for nome in keys(var.ambientes) : contains(["homologacao", "producao"], nome)])
    error_message = "Ambientes aceitos: homologacao e producao."
  }
  validation {
    condition     = alltrue([for nome, a in var.ambientes : nome == "homologacao" || !a.seed_enabled])
    error_message = "O seed sintético só pode ser ligado em homologacao."
  }
  validation {
    condition = alltrue([
      for a in var.ambientes : a.dominio_proprio == "" || can(regex("^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", a.dominio_proprio))
    ])
    error_message = "dominio_proprio aceita só o nome do host em minúsculas, como painel.example.org: sem https://, sem barra, sem porta e sem curinga."
  }
}

variable "repositorio" {
  description = "Repositório GitHub do serviço web, no formato dono/nome."
  type        = string
  default     = "henriqueluza/universo-do-helinho"
  nullable    = false
}

variable "branch" {
  description = "Branch que a Railway implanta."
  type        = string
  default     = "main"
  nullable    = false
}

variable "auto_deploy" {
  description = "Implantar a cada push na branch. Desligado, a publicação é manual (Deploy Latest Commit no painel)."
  type        = bool
  default     = false
  nullable    = false
}

variable "regiao" {
  description = "Região da Railway do serviço web e do banco."
  type        = string
  default     = "us-east4-eqdc4a"
  nullable    = false
}

variable "serverless" {
  description = "Deixar o serviço web dormir quando ocioso."
  type        = bool
  default     = false
  nullable    = false
}

variable "postgres_imagem" {
  description = "Imagem oficial do PostgreSQL 17, com a minor fixada."
  type        = string
  default     = "postgres:17.12"
  nullable    = false

  validation {
    condition     = can(regex("^postgres:17(\\.[0-9]+)?$", var.postgres_imagem))
    error_message = "Use a imagem oficial postgres:17 ou postgres:17.<minor>."
  }
}

variable "seed_password" {
  description = "Senha dos usuários do seed sintético (HELINHO_SEED_PASSWORD). Obrigatória enquanto algum ambiente tiver seed_enabled."
  type        = string
  sensitive   = true
  default     = null

  validation {
    condition = !anytrue([for a in var.ambientes : a.seed_enabled]) || alltrue([
      for padrao in ["^.{16,}$", "[a-z]", "[A-Z]", "[0-9]", "[^a-zA-Z0-9]"] : can(regex(padrao, var.seed_password))
    ])
    error_message = "Com o seed ligado, defina TF_VAR_seed_password com 16 ou mais caracteres, com minúscula, maiúscula, dígito e símbolo."
  }
}

variable "variaveis_web" {
  description = "Configuração sem segredo do serviço web, pelo nome da variável de ambiente. Valor vazio não é enviado à Railway."
  type = object({
    Api__Auth__TokenLifetime     = optional(string, "08:00:00")
    Api__Auth__MaxFailedAttempts = optional(string, "5")
    Api__Auth__LockoutDuration   = optional(string, "00:15:00")
    Api__Auth__LoginRateLimit    = optional(string, "10")
    Api__Auth__LoginRateWindow   = optional(string, "00:01:00")
    # Sprints 3 e 4: já declaradas; ficam vazias até lá.
    Email__Provider    = optional(string, "Fake")
    Brevo__SenderEmail = optional(string, "")
    Brevo__SenderName  = optional(string, "")
    App__PublicBaseUrl = optional(string, "")
    Ai__Provider       = optional(string, "Fake")
    Ai__Model          = optional(string, "")
  })
  default  = {}
  nullable = false
}

variable "brevo_api_key" {
  description = "Brevo__ApiKey (Sprint 3)."
  type        = string
  sensitive   = true
  default     = ""
  nullable    = false

  validation {
    condition     = contains(["Fake", "Brevo"], var.variaveis_web.Email__Provider) && (var.variaveis_web.Email__Provider == "Fake" || var.brevo_api_key != "")
    error_message = "Email__Provider aceita Fake ou Brevo, e Brevo exige TF_VAR_brevo_api_key."
  }
}

variable "ai_api_key" {
  description = "Ai__ApiKey (Sprint 4)."
  type        = string
  sensitive   = true
  default     = ""
  nullable    = false

  validation {
    condition     = var.variaveis_web.Ai__Provider == "Fake" || var.ai_api_key != ""
    error_message = "Ai__Provider diferente de Fake exige TF_VAR_ai_api_key."
  }
}
