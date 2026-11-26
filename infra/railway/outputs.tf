# Nenhuma saída carrega segredo: a senha do banco e a do seed ficam só no estado e nas variáveis da Railway.

output "url_publica" {
  description = "URL pública do painel, por ambiente."
  value       = { for nome, a in module.ambiente : nome => a.url_publica }
}

output "url_api" {
  description = "URL base da API do jogo, por ambiente."
  value       = { for nome, a in module.ambiente : nome => "${a.url_publica}/api" }
}

output "dns_dominio_proprio" {
  description = "Registros a cadastrar no DNS do domínio próprio (tipo, nome, zona, valor), por ambiente. Lista vazia sem domínio."
  value       = { for nome, a in module.ambiente : nome => a.dns_dominio_proprio }
}

output "ambientes" {
  description = "Ids e nomes que o runbook usa (projeto, ambiente, serviços, variáveis enviadas ao web, App__PublicBaseUrl), por ambiente."
  value       = { for nome, a in module.ambiente : nome => a.resumo }
}
