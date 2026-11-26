# `terraform test`: providers simulados, só `plan` em memória. Não usa token nem fala com a Railway.
mock_provider "railway" {
  # Valores sintéticos no lugar do que a Railway devolveria, já no `plan`, para conferir as saídas de DNS.
  mock_resource "railway_custom_domain" {
    override_during = plan
    defaults = {
      host_label                = "painel"
      zone                      = "example.org"
      dns_record_value          = "abc123.up.railway.app"
      verification_host_label   = "_railway-verify.painel"
      verification_record_value = "railway-verify=sintetico"
    }
  }
}
mock_provider "random" {}

variables {
  seed_password = "Sintetica-de-teste-1!" # valor sintético, só para satisfazer a validação
}

run "padrao_cria_so_homologacao" {
  command = plan

  assert {
    condition     = keys(output.ambientes) == ["homologacao"]
    error_message = "O padrão deve criar só homologacao; producao nasce desligada."
  }
  assert {
    condition = toset(output.ambientes["homologacao"].variaveis_web) == toset([
      "ASPNETCORE_ENVIRONMENT", "PORT", "ConnectionStrings__Default", "Proxy__TrustForwardedHeaders",
      "Seed__Enabled", "HELINHO_SEED_PASSWORD", "Email__Provider", "Email__DailyLimit", "Ai__Provider",
      "Ai__TimeoutSeconds", "Ai__DailyQuotaPerAdmin", "Ai__DailyGlobalLimit",
      "Api__Auth__TokenLifetime", "Api__Auth__MaxFailedAttempts", "Api__Auth__LockoutDuration",
      "Api__Auth__LoginRateLimit", "Api__Auth__LoginRateWindow",
    ])
    error_message = "Variável vazia não pode ser enviada, e as obrigatórias não podem faltar."
  }
  assert {
    condition     = output.dns_dominio_proprio == { homologacao = [] }
    error_message = "Sem dominio_proprio não pode existir railway_custom_domain nem registro DNS a cadastrar."
  }
}

run "dominio_proprio_cria_o_recurso_e_as_saidas" {
  command = plan

  variables {
    ambientes = { homologacao = { seed_enabled = true, dominio_proprio = "painel.example.org" } }
  }

  assert {
    condition = output.dns_dominio_proprio["homologacao"] == [
      { tipo = "CNAME", nome = "painel", zona = "example.org", valor = "abc123.up.railway.app" },
      { tipo = "TXT", nome = "_railway-verify.painel", zona = "example.org", valor = "railway-verify=sintetico" },
    ]
    error_message = "Com dominio_proprio, a saída traz o CNAME e o TXT de verificação que a Railway devolve."
  }
  assert {
    condition     = output.ambientes["homologacao"].url_base_app == "https://painel.example.org"
    error_message = "Com dominio_proprio, App__PublicBaseUrl vale https://<domínio> por padrão."
  }
  assert {
    condition     = contains(output.ambientes["homologacao"].variaveis_web, "App__PublicBaseUrl")
    error_message = "App__PublicBaseUrl precisa ser enviada ao serviço web quando há domínio próprio."
  }
}

run "url_base_explicita_vence_o_dominio_proprio" {
  command = plan

  variables {
    ambientes     = { homologacao = { seed_enabled = true, dominio_proprio = "painel.example.org" } }
    variaveis_web = { App__PublicBaseUrl = "https://outro.example.org" }
  }

  assert {
    condition     = output.ambientes["homologacao"].url_base_app == "https://outro.example.org"
    error_message = "Valor explícito de App__PublicBaseUrl em variaveis_web vence o domínio próprio."
  }
}

run "recusa_dominio_com_esquema" {
  command = plan

  variables {
    ambientes = { homologacao = { seed_enabled = true, dominio_proprio = "https://painel.example.org" } }
  }

  expect_failures = [var.ambientes]
}

run "recusa_dominio_com_barra" {
  command = plan

  variables {
    ambientes = { homologacao = { seed_enabled = true, dominio_proprio = "painel.example.org/" } }
  }

  expect_failures = [var.ambientes]
}

run "producao_ligada_sem_seed" {
  command = plan

  variables {
    ambientes = {
      homologacao = { seed_enabled = true }
      producao    = {}
    }
    variaveis_web = {
      Email__Provider    = "Brevo"
      Brevo__SenderEmail = "nao-responda@example.org"
      Brevo__SenderName  = "Remetente sintético"
      App__PublicBaseUrl = "https://painel.example.org"
    }
    brevo_api_key = "chave-sintetica"
  }

  assert {
    condition     = length(setintersection(output.ambientes["producao"].variaveis_web, ["Seed__Enabled", "HELINHO_SEED_PASSWORD"])) == 0
    error_message = "Produção não pode receber as variáveis do seed."
  }
  assert {
    condition     = contains(output.ambientes["producao"].variaveis_web, "Brevo__ApiKey")
    error_message = "Segredo com valor precisa ser enviado."
  }
  assert {
    condition     = [for nome in ["homologacao", "producao"] : output.ambientes[nome].provedor_ia] == ["Fake", "Off"]
    error_message = "Sem Ai__Provider explícito, a IA é Fake em homologacao e Off em producao (ADR-0004 D9)."
  }
}

run "recusa_seed_em_producao" {
  command = plan

  variables {
    ambientes = { producao = { seed_enabled = true } }
  }

  expect_failures = [var.ambientes]
}

run "recusa_ambiente_desconhecido" {
  command = plan

  variables {
    ambientes = { desenvolvimento = {} }
  }

  expect_failures = [var.ambientes]
}

run "recusa_seed_sem_senha" {
  command = plan

  variables {
    seed_password = null
  }

  expect_failures = [var.seed_password]
}

run "recusa_senha_fraca" {
  command = plan

  variables {
    seed_password = "fraca"
  }

  expect_failures = [var.seed_password]
}

run "recusa_provedor_real_sem_chave" {
  command = plan

  variables {
    variaveis_web = { Email__Provider = "Brevo", Brevo__SenderEmail = "nao-responda@example.org", Brevo__SenderName = "Remetente sintético", App__PublicBaseUrl = "https://painel.example.org", Ai__Provider = "Anthropic", Ai__Model = "modelo-sintetico-1" }
  }

  expect_failures = [var.brevo_api_key, var.ai_api_key]
}

# `null` explícito não escapa da validação: com `nullable = false` vale o padrão vazio.
run "recusa_chave_nula_com_provedor_real" {
  command = plan

  variables {
    variaveis_web = { Email__Provider = "Brevo", Brevo__SenderEmail = "nao-responda@example.org", Brevo__SenderName = "Remetente sintético", App__PublicBaseUrl = "https://painel.example.org", Ai__Provider = "Anthropic", Ai__Model = "modelo-sintetico-1" }
    brevo_api_key = null
    ai_api_key    = null
  }

  expect_failures = [var.brevo_api_key, var.ai_api_key]
}

run "nulo_explicito_vale_o_padrao" {
  command = plan

  variables {
    ambientes   = null
    regiao      = null
    auto_deploy = null
  }

  assert {
    condition     = keys(output.ambientes) == ["homologacao"]
    error_message = "`null` explícito deve cair no padrão, não em erro nem em valor vazio."
  }
}
