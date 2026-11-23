locals {
  # O modo da aplicação segue o nome do ambiente: não há como pedir Development nem seed em modo Production.
  aspnetcore_environment = { homologacao = "Staging", producao = "Production" }
  # ADR-0004 D9: sem valor explícito, a IA é fake em homologação e desligada em produção.
  ai_provider_padrao = { homologacao = "Fake", producao = "Off" }
}

# Um projeto Railway por ambiente (ADR-0002 §5.2): o provider só lê serviço e volume no ambiente padrão do projeto.
module "ambiente" {
  source   = "./modules/ambiente"
  for_each = var.ambientes

  nome                   = each.key
  workspace_id           = var.workspace_id
  aspnetcore_environment = local.aspnetcore_environment[each.key]
  subdominio             = coalesce(each.value.subdominio, "helinho-${each.key}")
  dominio_proprio        = each.value.dominio_proprio
  repositorio            = var.repositorio
  branch                 = var.branch
  auto_deploy            = var.auto_deploy
  regiao                 = var.regiao
  serverless             = var.serverless
  postgres_imagem        = var.postgres_imagem
  seed_enabled           = each.value.seed_enabled
  seed_password          = each.value.seed_enabled ? var.seed_password : null
  variaveis_web = merge(var.variaveis_web, {
    Ai__Provider = coalesce(var.variaveis_web.Ai__Provider, lookup(local.ai_provider_padrao, each.key, "Off"))
  })
  segredos_web = {
    Brevo__ApiKey        = var.brevo_api_key
    Brevo__WebhookSecret = var.brevo_webhook_secret
    Ai__ApiKey           = var.ai_api_key
  }
}
