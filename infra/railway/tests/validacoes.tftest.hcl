# `terraform test`: providers simulados, só `plan` em memória. Não usa token nem fala com a Railway.
mock_provider "railway" {}
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
      "Seed__Enabled", "HELINHO_SEED_PASSWORD", "Email__Provider", "Ai__Provider",
      "Api__Auth__TokenLifetime", "Api__Auth__MaxFailedAttempts", "Api__Auth__LockoutDuration",
      "Api__Auth__LoginRateLimit", "Api__Auth__LoginRateWindow",
    ])
    error_message = "Variável vazia não pode ser enviada, e as obrigatórias não podem faltar."
  }
}

run "producao_ligada_sem_seed" {
  command = plan

  variables {
    ambientes = {
      homologacao = { seed_enabled = true }
      producao    = {}
    }
    variaveis_web = { Email__Provider = "Brevo" }
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
    seed_password = "senhafraca"
  }

  expect_failures = [var.seed_password]
}

run "recusa_provedor_real_sem_chave" {
  command = plan

  variables {
    variaveis_web = { Email__Provider = "Brevo", Ai__Provider = "Real" }
  }

  expect_failures = [var.brevo_api_key, var.ai_api_key]
}
