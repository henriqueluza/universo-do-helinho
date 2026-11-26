# `terraform test` dos provedores de e-mail e de IA: providers simulados, só `plan` em memória, sem token.
# Chaves e segredos daqui são sintéticos e sem formato de chave real, de propósito.
mock_provider "railway" {}
mock_provider "random" {}

variables {
  seed_password = "Sintetica-de-teste-1!" # valor sintético, só para satisfazer a validação
}

run "padrao_e_fake" {
  command = plan

  assert {
    condition     = output.ambientes["homologacao"].provedor_email == "Fake" && output.ambientes["homologacao"].provedor_ia == "Fake"
    error_message = "Sem configuração, e-mail e IA sobem em Fake. O conjunto exato de variáveis enviadas está em validacoes.tftest.hcl."
  }
}

run "ia_off_sem_chave" {
  command = plan

  variables {
    variaveis_web = { Ai__Provider = "Off" }
  }

  assert {
    condition     = output.ambientes["homologacao"].provedor_ia == "Off" && !contains(output.ambientes["homologacao"].variaveis_web, "Ai__ApiKey")
    error_message = "Off não exige chave e é enviado como Off (Issue #182)."
  }
}

run "ia_fake_explicita_vence_o_padrao_de_producao" {
  command = plan

  variables {
    ambientes     = { homologacao = { seed_enabled = true }, producao = {} }
    variaveis_web = { Ai__Provider = "Fake" }
  }

  assert {
    condition     = output.ambientes["producao"].provedor_ia == "Fake"
    error_message = "Fake não exige chave, e o valor explícito vence o padrão do ambiente."
  }
}

run "anthropic_com_chave_e_modelo" {
  command = plan

  variables {
    variaveis_web = { Ai__Provider = "Anthropic", Ai__Model = "modelo-sintetico-1" }
    ai_api_key    = "chave-sintetica"
  }

  assert {
    condition     = length(setsubtract(["Ai__ApiKey", "Ai__Model"], output.ambientes["homologacao"].variaveis_web)) == 0
    error_message = "Com Anthropic, a chave e o modelo precisam ser enviados."
  }
}

run "recusa_anthropic_sem_chave" {
  command = plan

  variables {
    variaveis_web = { Ai__Provider = "Anthropic", Ai__Model = "modelo-sintetico-1" }
  }

  expect_failures = [var.ai_api_key]
}

run "recusa_anthropic_sem_modelo" {
  command = plan

  variables {
    variaveis_web = { Ai__Provider = "Anthropic" }
    ai_api_key    = "chave-sintetica"
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_modelo_com_apelido_latest" {
  command = plan

  variables {
    variaveis_web = { Ai__Provider = "Anthropic", Ai__Model = "modelo-sintetico-latest" }
    ai_api_key    = "chave-sintetica"
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_provedor_de_ia_desconhecido" {
  command = plan

  variables {
    variaveis_web = { Ai__Provider = "Real" }
    ai_api_key    = "chave-sintetica"
  }

  expect_failures = [var.variaveis_web]
}

run "brevo_completa_com_webhook" {
  command = plan

  variables {
    variaveis_web = {
      Email__Provider    = "Brevo"
      Email__DailyLimit  = "0"
      Brevo__SenderEmail = "nao-responda@example.org"
      Brevo__SenderName  = "Remetente sintético"
      App__PublicBaseUrl = "https://painel.example.org"
    }
    brevo_api_key        = "chave-sintetica"
    brevo_webhook_secret = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" # 32 caracteres, sintético
  }

  assert {
    condition = length(setsubtract(
      ["Brevo__ApiKey", "Brevo__WebhookSecret", "Brevo__SenderEmail", "Brevo__SenderName", "App__PublicBaseUrl", "Email__DailyLimit"],
      output.ambientes["homologacao"].variaveis_web,
    )) == 0
    error_message = "Com Brevo, chave, segredo do webhook, remetente, URL e teto precisam ser enviados."
  }
}

run "brevo_aceita_a_url_do_dominio_proprio" {
  command = plan

  variables {
    ambientes     = { homologacao = { seed_enabled = true, dominio_proprio = "painel.example.org" } }
    variaveis_web = { Email__Provider = "Brevo", Brevo__SenderEmail = "nao-responda@example.org", Brevo__SenderName = "Remetente sintético" }
    brevo_api_key = "chave-sintetica"
  }

  assert {
    condition     = output.ambientes["homologacao"].url_base_app == "https://painel.example.org"
    error_message = "Sem App__PublicBaseUrl explícita, a URL efetiva vem do dominio_proprio."
  }
}

run "recusa_brevo_sem_remetente" {
  command = plan

  variables {
    variaveis_web = { Email__Provider = "Brevo", Brevo__SenderName = "Remetente sintético", App__PublicBaseUrl = "https://painel.example.org" }
    brevo_api_key = "chave-sintetica"
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_brevo_sem_nome_do_remetente" {
  command = plan

  variables {
    variaveis_web = { Email__Provider = "Brevo", Brevo__SenderEmail = "nao-responda@example.org", App__PublicBaseUrl = "https://painel.example.org" }
    brevo_api_key = "chave-sintetica"
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_remetente_que_nao_e_email" {
  command = plan

  variables {
    variaveis_web = { Brevo__SenderEmail = "nao-responda.example.org" }
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_remetente_com_nome_entre_sinais" {
  command = plan

  variables {
    variaveis_web = { Brevo__SenderEmail = "Remetente<nao-responda@example.org>" }
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_brevo_sem_url_publica" {
  command = plan

  variables {
    variaveis_web = { Email__Provider = "Brevo", Brevo__SenderEmail = "nao-responda@example.org", Brevo__SenderName = "Remetente sintético" }
    brevo_api_key = "chave-sintetica"
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_url_publica_em_http" {
  command = plan

  variables {
    variaveis_web = { App__PublicBaseUrl = "http://painel.example.org" }
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_chave_com_espaco" {
  command = plan

  variables {
    brevo_api_key = "chave sintetica"
    ai_api_key    = "chave sintetica"
  }

  expect_failures = [var.brevo_api_key, var.ai_api_key]
}

run "recusa_segredo_de_webhook_curto" {
  command = plan

  variables {
    brevo_webhook_secret = "curto-demais"
  }

  expect_failures = [var.brevo_webhook_secret]
}

run "recusa_segredo_de_webhook_com_espaco" {
  command = plan

  variables {
    brevo_webhook_secret = "xxxxxxxxxxxxxxxx xxxxxxxxxxxxxxxx"
  }

  expect_failures = [var.brevo_webhook_secret]
}

run "recusa_prazo_de_ia_fora_da_faixa" {
  command = plan

  variables {
    variaveis_web = { Ai__TimeoutSeconds = "301" }
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_cota_de_ia_zerada" {
  command = plan

  variables {
    variaveis_web = { Ai__DailyQuotaPerAdmin = "0" }
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_teto_global_de_ia_zerado" {
  command = plan

  variables {
    variaveis_web = { Ai__DailyGlobalLimit = "0" }
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_teto_global_de_ia_acima_da_faixa" {
  command = plan

  variables {
    variaveis_web = { Ai__DailyGlobalLimit = "100001" }
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_teto_de_email_acima_da_faixa" {
  command = plan

  variables {
    variaveis_web = { Email__DailyLimit = "100001" }
  }

  expect_failures = [var.variaveis_web]
}

run "recusa_teto_de_email_negativo" {
  command = plan

  variables {
    variaveis_web = { Email__DailyLimit = "-1" }
  }

  expect_failures = [var.variaveis_web]
}

# As fronteiras de Email__DailyLimit não mudam com a validação única das quatro faixas.
run "aceita_teto_de_email_no_limite" {
  command = plan

  variables {
    variaveis_web = { Email__DailyLimit = "100000" }
  }

  assert {
    condition     = contains(output.ambientes["homologacao"].variaveis_web, "Email__DailyLimit")
    error_message = "100000 é o maior teto aceito e precisa ser enviado."
  }
}

run "teto_de_email_vazio_nao_e_enviado" {
  command = plan

  variables {
    variaveis_web = { Email__DailyLimit = "" }
  }

  assert {
    condition     = !contains(output.ambientes["homologacao"].variaveis_web, "Email__DailyLimit")
    error_message = "Vazio é aceito e não é enviado: vale o padrão da aplicação."
  }
}
