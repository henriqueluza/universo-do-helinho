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
  }
}
