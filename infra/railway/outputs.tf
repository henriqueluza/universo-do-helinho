# Nenhuma saída carrega segredo: a senha do banco e a do seed ficam só no estado e nas variáveis da Railway.

output "url_publica" {
  description = "URL pública do painel, por ambiente."
  value       = { for nome, a in module.ambiente : nome => a.url_publica }
}

output "url_api" {
  description = "URL base da API do jogo, por ambiente."
  value       = { for nome, a in module.ambiente : nome => "${a.url_publica}/api" }
}

output "ambientes" {
  description = "Ids e nomes que o runbook usa (projeto, ambiente, serviços, variáveis enviadas ao web), por ambiente."
  value       = { for nome, a in module.ambiente : nome => a.resumo }
}
