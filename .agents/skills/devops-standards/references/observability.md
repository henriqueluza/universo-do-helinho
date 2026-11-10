# Observabilidade

Se algo quebrar em produção às 3h, você precisa saber **o que** quebrou, **para quem** e **desde quando** sem abrir o código. Observabilidade faz parte da feature: endpoint novo sem log, métrica e tratamento de erro está incompleto.

## Logs estruturados
- Formato **JSON** em staging/produção (texto legível em `dev` é aceitável).
- Campos mínimos em toda linha: `timestamp`, `level`, `message`, `service`, `env`, `version` (SHA/digest), `request_id`/`correlation_id`, `trace_id` quando houver tracing.
- **Correlation id**: lido do header de entrada (`X-Request-Id` ou `traceparent`) ou gerado na borda; propagado para chamadas HTTP, filas e jobs; devolvido na resposta.
- Mensagem estável + campos variáveis: `"pagamento recusado"` com `{"payment_id": ..., "reason": ...}` — não interpole tudo na string.

| Nível | Uso |
|---|---|
| `error` | falha que precisa de ação (erro inesperado, integração caiu). Vai para o error tracking |
| `warn` | anormal mas tratado (retry, fallback, validação de negócio suspeita) |
| `info` | eventos de negócio e ciclo de vida (pedido criado, deploy, startup) |
| `debug` | detalhes de diagnóstico; desligado em produção por padrão |

### Nunca logar
Senhas, tokens, chaves, cookies de sessão, cabeçalho `Authorization`, número de cartão/CVV, documento completo (CPF/RG), dados de saúde, corpo inteiro de request/response. Dado pessoal necessário para suporte (e-mail, telefone): mascarado (`j***@dominio.com`). Configure redaction no logger por nome de campo — não dependa de cada desenvolvedor lembrar.

Bibliotecas idiomáticas: Serilog/`ILogger` (.NET), Logback + encoder JSON (Spring), `log/slog` (Go), pino (Node/NestJS), structlog ou logging com formatter JSON (Python), `tracing` + `tracing-subscriber` (Rust), Monolog com formatter JSON (Laravel).

Exemplo de linha:
```json
{"timestamp":"2026-10-29T19:02:11.481Z","level":"warn","service":"reservas-api","env":"staging",
 "version":"9f1c2ab","request_id":"01JB7Q...","trace_id":"4bf92f35...","message":"pagamento recusado",
 "payment_id":"pay_123","reason":"insufficient_funds","user_email":"j***@dominio.com"}
```

## Health checks
| Endpoint | Pergunta | Verifica | Usado por |
|---|---|---|---|
| `GET /health` (liveness) | o processo está vivo? | só o próprio processo; **não** consulta dependências | orquestrador reinicia se falhar |
| `GET /ready` (readiness) | posso receber tráfego? | conexão com banco, cache, migrations aplicadas | load balancer/rollout, smoke test do CI |

- Respostas rápidas (< 1 s), sem autenticação, sem dados sensíveis (no máximo `{"status":"ok","version":"<sha>"}`).
- Readiness falhando tira a instância do tráfego; liveness falhando reinicia. Colocar dependência no liveness causa reinício em cascata quando o banco oscila.
- Workers/jobs sem HTTP: heartbeat (métrica ou log periódico) monitorado.
- Apps mobile não têm health check; monitore crash rate e erros (Sentry/Crashlytics) e a API que consomem.

## Métricas
**RED** para serviços (por endpoint/rota):
- **Rate** — requisições por segundo
- **Errors** — taxa de respostas 5xx (e 4xx anormais)
- **Duration** — latência p50/p95/p99

**USE** para recursos (CPU, memória, conexões de banco, fila):
- **Utilization**, **Saturation** (fila crescendo, pool de conexões esgotado), **Errors**

Métricas de negócio quando o domínio pede: pagamentos aprovados/recusados por minuto, reservas criadas, falhas de webhook. Cardinalidade controlada: nunca `user_id` ou `request_id` como label de métrica.

## Tracing (OpenTelemetry)
- SDK OpenTelemetry da stack com auto-instrumentação de HTTP, banco e filas; propagação W3C `traceparent`.
- Exportação via OTLP para o backend escolhido no ADR (ferramenta nativa do provedor — CloudWatch/X-Ray, Azure Monitor, Cloud Trace — ou Grafana/Honeycomb/Datadog).
- Spans manuais só em operações de negócio relevantes (`processar_pagamento`), com atributos sem dados pessoais.
- Amostragem: 100% em staging; em produção, taxa definida no ADR + sempre amostrar erros.

## Error tracking
- Sentry ou equivalente (Crashlytics para mobile) em todos os serviços e apps, com `release` = versão/SHA e `environment`.
- Erros não tratados capturados automaticamente; erros tratados relevantes enviados com contexto (sem PII — habilitar scrubbing).
- Source maps/símbolos enviados no build do CI (frontend, React Native, Flutter) — nunca publicados junto do app.
- DSN público é aceitável no cliente; token de upload de source maps é segredo do CI.

## SLOs
Defina no ADR, por serviço, 1–3 objetivos simples antes de configurar alertas:
| SLI | Exemplo de SLO |
|---|---|
| Disponibilidade (requisições não-5xx / total) | 99,5% em 30 dias |
| Latência (p95 das rotas principais) | < 400 ms |
| Sucesso de operação de negócio (ex.: pagamentos processados sem erro técnico) | 99,9% |

Alertas de latência e erro derivam do SLO. Projeto pequeno (site institucional) pode ter só disponibilidade + error tracking.

## Dashboards mínimos
Um dashboard por serviço, criado como código quando o provedor permitir (Terraform/JSON versionado):
1. RED por rota (rate, erros, p95/p99).
2. Recursos (CPU, memória, conexões de banco, tamanho de fila).
3. Deploys marcados no tempo (anotação com versão/SHA) — permite ligar regressão a um deploy.
4. Métricas de negócio do domínio.

## Alertas mínimos
| Alerta | Condição inicial (ajuste no ADR) | Severidade |
|---|---|---|
| Taxa de erro | 5xx > 2% por 5 min | alta |
| Latência | p95 acima do SLO por 10 min | média |
| Saturação | CPU/memória > 85% por 15 min, pool de conexões > 90%, fila crescendo por 15 min | média |
| Disponibilidade | `/ready` falhando em todas as instâncias | crítica |
| Falha de deploy | job de deploy ou smoke test falhou | alta |
| Error tracking | novo tipo de erro em produção / pico de ocorrências | média |
| Mobile | crash-free sessions abaixo do alvo | alta |

- Alerta só existe se alguém age sobre ele; alerta que ninguém olha é removido.
- Canal: Slack `#alertas` (separado de `#relatorios`); críticos também por notificação ao responsável.
- **Todo alerta aponta para um runbook** em `docs/runbooks/<alerta>.md`.

## Runbooks
`docs/runbooks/<alerta>.md`, curto:
1. **O que significa** e impacto para o usuário.
2. **Como confirmar** (dashboard, query de log, comando de leitura).
3. **Mitigação imediata** (rollback via `workflow_dispatch` — `ci-cd.md`, desligar feature flag, escalar).
4. **Causa provável e investigação**.
5. **Quem decidir** se a mitigação não funcionar.

Ações de mitigação em produção são executadas por humanos; agentes podem redigir runbooks e investigar logs de staging.

## Checklist de PR
- [ ] Logs novos estruturados, com nível correto e correlation id; nenhum segredo ou dado pessoal sem máscara.
- [ ] Endpoint/serviço novo coberto pelas métricas RED (auto-instrumentação ou explícita), sem labels de alta cardinalidade.
- [ ] `/health` e `/ready` existem e seguem a separação liveness/readiness.
- [ ] Erros relevantes chegam ao error tracking com `release` e `environment`.
- [ ] Integração externa nova tem log de falha, métrica de erro e timeout.
- [ ] Alerta novo tem runbook em `docs/runbooks/`.
- [ ] Spans/atributos de tracing sem dados pessoais.
