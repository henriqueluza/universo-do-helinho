locals {
  # O modo da aplicação segue o nome do ambiente: não há como pedir Development nem seed em modo Production.
  aspnetcore_environment = { homologacao = "Staging", producao = "Production" }
}

# Um projeto Railway por ambiente (ADR-0002 §5.2): o provider só lê serviço e volume no ambiente padrão do projeto.
module "ambiente" {
  source   = "./modules/ambiente"
  for_each = var.ambientes

  nome                   = each.key
  workspace_id           = var.workspace_id
  aspnetcore_environment = local.aspnetcore_environment[each.key]
  subdominio             = coalesce(each.value.subdominio, "helinho-${each.key}")
  repositorio            = var.repositorio
  branch                 = var.branch
  auto_deploy            = var.auto_deploy && each.key != "producao" # produção só publica à mão (ADR-0002 §7.5)
  regiao                 = var.regiao
  serverless             = var.serverless
  postgres_imagem        = var.postgres_imagem
  seed_enabled           = each.value.seed_enabled
  seed_password          = each.value.seed_enabled ? var.seed_password : null
  variaveis_web          = var.variaveis_web
  segredos_web           = { Brevo__ApiKey = var.brevo_api_key, Ai__ApiKey = var.ai_api_key }
}
