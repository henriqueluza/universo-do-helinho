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
    # E-mail (ADR-0003 §7 e D7). Com Brevo, remetente e URL pública passam a ser obrigatórios.
    Email__Provider    = optional(string, "Fake")
    Email__DailyLimit  = optional(string, "300") # teto diário de envios; 0 = sem teto
    Brevo__SenderEmail = optional(string, "")
    Brevo__SenderName  = optional(string, "")
    App__PublicBaseUrl = optional(string, "")
    # IA (Sprint 4): já declaradas; ficam como estão até lá.
    Ai__Provider = optional(string, "Fake")
    Ai__Model    = optional(string, "")
  })
  default  = {}
  nullable = false

  validation {
    condition     = contains(["Fake", "Brevo"], var.variaveis_web.Email__Provider)
    error_message = "Email__Provider aceita Fake ou Brevo."
  }
  # As três validações abaixo repetem no `plan` o que a aplicação recusa na subida: EmailOptions.Errors() (remetente
  # que seja e-mail, nome com até 70 caracteres, URL https absoluta sem caminho). De propósito o `plan` é mais
  # estrito: confere o formato mesmo com Fake e recusa barra final e maiúscula na URL.
  validation {
    condition = (var.variaveis_web.Brevo__SenderEmail == "" ? var.variaveis_web.Email__Provider != "Brevo" :
    can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.variaveis_web.Brevo__SenderEmail)))
    error_message = "Brevo__SenderEmail precisa ser um e-mail válido e é obrigatório com Email__Provider = \"Brevo\"."
  }
  validation {
    condition = length(var.variaveis_web.Brevo__SenderName) <= 70 && (
    var.variaveis_web.Email__Provider != "Brevo" || trimspace(var.variaveis_web.Brevo__SenderName) != "")
    error_message = "Brevo__SenderName tem até 70 caracteres e é obrigatório com Email__Provider = \"Brevo\"."
  }
  validation {
    condition = (var.variaveis_web.App__PublicBaseUrl != ""
      ? can(regex("^https://[a-z0-9]([a-z0-9.-]*[a-z0-9])?(:[0-9]{1,5})?$", var.variaveis_web.App__PublicBaseUrl))
    : var.variaveis_web.Email__Provider != "Brevo" || alltrue([for a in var.ambientes : a.dominio_proprio != ""]))
    error_message = "App__PublicBaseUrl precisa ser https://<host em minúsculas>, sem caminho, barra final nem query. Com Email__Provider = \"Brevo\" ela é obrigatória: preencha-a ou defina o dominio_proprio de cada ambiente."
  }
  # O `regex` só casa inteiro sem sinal; o que não casa cai no `false` do `try`.
  validation {
    condition = var.variaveis_web.Email__DailyLimit == "" || try(
      tonumber(regex("^[0-9]{1,6}$", var.variaveis_web.Email__DailyLimit)) <= 100000,
    false)
    error_message = "Email__DailyLimit é um inteiro sem sinal de 0 a 100000 (0 = sem teto). Vazio = não enviada (vale o padrão da aplicação)."
  }
}

variable "brevo_api_key" {
  description = "Brevo__ApiKey: chave de API da Brevo (ADR-0003). Obrigatória com Email__Provider = \"Brevo\"."
  type        = string
  sensitive   = true
  default     = ""
  nullable    = false

  validation {
    condition     = var.variaveis_web.Email__Provider != "Brevo" || var.brevo_api_key != ""
    error_message = "Email__Provider = \"Brevo\" exige TF_VAR_brevo_api_key."
  }
  validation {
    condition     = can(regex("^[!-~]*$", var.brevo_api_key))
    error_message = "brevo_api_key vai em cabeçalho HTTP: não pode ter espaço, quebra de linha nem caractere fora do ASCII visível."
  }
}

variable "brevo_webhook_secret" {
  description = "Brevo__WebhookSecret: valor do cabeçalho X-Helinho-Webhook-Secret do webhook da Brevo (ADR-0003 §12). Vazio = não enviada, e o webhook fica desligado na aplicação."
  type        = string
  sensitive   = true
  default     = ""
  nullable    = false

  validation {
    condition     = var.brevo_webhook_secret == "" || can(regex("^[!-~]{32,}$", var.brevo_webhook_secret))
    error_message = "brevo_webhook_secret, quando preenchido, precisa de 32 ou mais caracteres ASCII visíveis, sem espaço."
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
