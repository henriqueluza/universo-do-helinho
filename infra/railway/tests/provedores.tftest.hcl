# `terraform test` do provedor de e-mail: providers simulados, só `plan` em memória, sem token.
# Chaves e segredos daqui são sintéticos e sem formato de chave real, de propósito.
mock_provider "railway" {}
mock_provider "random" {}

variables {
  seed_password = "Sintetica-de-teste-1!" # valor sintético, só para satisfazer a validação
}

run "padrao_e_fake" {
  command = plan

  assert {
    condition     = output.ambientes["homologacao"].provedor_email == "Fake"
    error_message = "Sem configuração, o e-mail sobe em Fake. O conjunto exato de variáveis enviadas está em validacoes.tftest.hcl."
  }
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
  }

  expect_failures = [var.brevo_api_key]
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
