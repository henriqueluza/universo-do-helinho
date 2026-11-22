output "url_publica" {
  value = "https://${railway_service_domain.web.domain}"
}

output "resumo" {
  value = {
    projeto_id          = railway_project.this.id
    ambiente_id         = railway_project.this.default_environment.id
    servico_web         = railway_service.web.name
    servico_web_id      = railway_service.web.id
    servico_postgres    = railway_service.postgres.name
    servico_postgres_id = railway_service.postgres.id
    variaveis_web       = local.nomes_web
    url_base_app        = local.url_base_app
  }
}

# `nome` é relativo à `zona`, como a Railway devolve. O TXT prova a posse do domínio e é público: não é segredo.
output "dns_dominio_proprio" {
  value = flatten([for d in railway_custom_domain.web : [
    { tipo = "CNAME", nome = d.host_label, zona = d.zone, valor = d.dns_record_value },
    { tipo = "TXT", nome = d.verification_host_label, zona = d.zone, valor = d.verification_record_value },
  ]])
}
