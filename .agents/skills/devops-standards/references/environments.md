# Ambientes, configuração e segredos

## Os três ambientes
| Ambiente | Para quê | Quem publica | Dados |
|---|---|---|---|
| `dev` | máquina do desenvolvedor/agente, compose local | ninguém (roda local) | fictícios, gerados por seed |
| `staging` | validar o que foi mergeado, demo para cliente, smoke/E2E | CI, automaticamente após merge humano em `main` | fictícios ou anonimizados — **nunca dados pessoais reais** |
| `production` | usuários reais | CI, **só por disparo manual humano** (`workflow_dispatch`) no Environment `production` | reais |

Agentes trabalham apenas em `dev` e leem resultados de `staging` (logs, smoke tests). Nunca publicam em nenhum ambiente nem acessam dados de produção.

## Paridade
Staging deve ser uma cópia em escala reduzida de produção:
- mesma imagem/artefato (mesmo digest — `ci-cd.md`), mesma versão de banco, mesmos serviços gerenciados (fila, cache, storage);
- mesma topologia lógica (ex.: API + worker + banco), mesmo que menor e com menos réplicas;
- infraestrutura dos dois criada pelo **mesmo código** Terraform com variáveis diferentes (`terraform.md`), ou pela mesma configuração de PaaS (`paas.md`).

O que pode diferir: tamanho de máquina, número de réplicas, domínio, credenciais, integrações de terceiros em modo sandbox (gateway de pagamento em test mode, e-mail para caixa de captura).

## Configuração (12-factor)
- Toda configuração que muda entre ambientes vem de **variável de ambiente**. O código não sabe em que ambiente está a não ser pelo que recebe.
- Nada de `if (env == "production")` espalhado: o comportamento muda por configuração explícita (`PAYMENTS_MODE=sandbox`, `EMAIL_DRIVER=log`).
- Validação de configuração **no startup**: faltou variável obrigatória ou o formato é inválido → a aplicação não sobe (fail fast). Cada stack tem seu mecanismo (Options com validação no .NET, `@ConfigurationProperties` + `@Validated`, `pydantic-settings`, `zod` em Node, `envconfig` em Go).
- Variáveis públicas de frontend (`NEXT_PUBLIC_*`, `EXPO_PUBLIC_*`) são **públicas**: vão para o bundle. Nunca coloque segredo nelas.

### `.env.example`
Commitado, lista **todas** as variáveis, sem valores reais:
```dotenv
# Banco
DATABASE_URL=postgres://dev:dev@localhost:5432/app   # só valor de dev local
# Pagamentos
PAYMENTS_API_KEY=            # segredo — staging/produção via environment secrets
PAYMENTS_MODE=sandbox        # sandbox | live
# Observabilidade
SENTRY_DSN=
LOG_LEVEL=info
```
`.env` e `.env.*` (exceto `.env.example`) estão no `.gitignore`; o `gitleaks` reprova PR com segredo.

## Segredos
| Onde | O quê |
|---|---|
| Secret manager do provedor (AWS Secrets Manager/SSM, Azure Key Vault, GCP Secret Manager, secrets do Railway/Render/Fly, EAS secrets, Firebase/Google Secret Manager) | segredos de **runtime** lidos pela aplicação |
| GitHub **Environment** secrets (`staging`, `production`) | credenciais que o **CI** usa para publicar (preferir OIDC — então nem existem) |
| GitHub repository secrets | só o que é igual para todos os ambientes e não dá acesso a produção (raro) |
| Repositório, imagem, logs, issues, PRs | **nunca** |

Regras:
- Segredo de produção nunca existe em `dev` nem no ambiente do agente.
- Rotação documentada em `docs/runbooks/rotacao-de-segredos.md`; segredo vazado = rotação imediata + incidente.
- Cada serviço tem credencial própria com escopo mínimo (banco: usuário da aplicação sem `DROP`/`ALTER`; o usuário de migration é separado quando o provedor permite).

## Feature flags
Use para mergear código incompleto sem expor (mantém PRs pequenos) e para liberar gradualmente.
- Comece simples: flag por variável de ambiente ou tabela de configuração. Serviço dedicado (Unleash, LaunchDarkly, Firebase Remote Config, GrowthBook) só quando houver necessidade de segmentação.
- Toda flag tem dono e data de remoção prevista (Issue `chore:` para remover). Flag esquecida é dívida.
- Padrão **desligada** em produção; ligada em staging para teste.
- Flags não substituem autorização: recurso restrito continua protegido no backend.

## Migrations: expand/contract
Toda migration roda contra um banco que ainda atende a **versão anterior** da aplicação (o rollout não é instantâneo e o rollback precisa funcionar).

| Passo | PR | Exemplo (renomear `nome` → `nome_completo`) |
|---|---|---|
| Expand | 1 | adiciona `nome_completo` nullable; app escreve nas duas colunas |
| Migrate | 2 | backfill em lotes de `nome` → `nome_completo` |
| Switch | 3 | app passa a ler `nome_completo` |
| Contract | 4 | remove `nome` (depois que nenhuma versão em produção a usa) |

- Índices em tabelas grandes: criação concorrente/online (`CREATE INDEX CONCURRENTLY` no Postgres).
- **Backup verificado antes de qualquer migration destrutiva** (drop, rename, mudança de tipo). O passo de contract vira Issue `needs-human` para você confirmar o backup.
- Seeds de dados fictícios para `dev` e `staging` versionados no repositório.

## Dados de staging e LGPD
- Proibido copiar dump de produção para staging/dev. Se precisar de volume realista: dados sintéticos, ou anonimização irreversível feita em ambiente de produção controlado e revisada por humano.
- Contas de teste conhecidas e documentadas (sem senhas no repositório — no secret manager).
- Integrações de terceiros em modo sandbox; e-mails/SMS para caixas de captura.

## Preview environments por PR
Quando o provedor suporta (Render, Fly.io, Railway, Firebase Hosting preview channels, EAS Update em canal de preview, Vercel):
- Criados pelo CI no `pull_request` e destruídos no fechamento do PR.
- Sem segredos de produção; banco efêmero ou de staging isolado por schema.
- Úteis para o `ui-reviewer` e para você validar telas sem baixar o branch.
- Se o provedor não suporta, staging é o ambiente de validação — não crie infra manual por PR.

## GitHub Environments
O `setup-project.sh` cria os environments do repositório; ajustes neles são feitos por humano na interface.
| Environment | Proteção | Conteúdo |
|---|---|---|
| `staging` | só branch `main` pode publicar | `vars`: `APP_URL`, identificadores do provedor; `secrets`: tokens de PaaS sem OIDC |
| `production` | só branch `main`; workflow só por `workflow_dispatch` humano. *Required reviewer* + *Prevent self-review* só se o repo for público ou o plano for Enterprise | mesmas chaves que staging, valores de produção |

- Mesmo **nome** de variável nos dois environments; o workflow não muda entre eles.
- Agente pode propor uma variável nova (no PR e no `.env.example`), mas quem cria o valor no environment é você.

## Domínios, CORS e URLs
- Cada ambiente tem domínio próprio (`api.staging.exemplo.com`, `api.exemplo.com`); nunca compartilhe cookies entre staging e produção (domínios-irmãos com cookie no domínio pai vazam sessão).
- CORS por lista explícita de origens vinda de configuração; `*` proibido em API autenticada.
- URLs de callback (OAuth, webhooks de pagamento, deep links mobile) cadastradas por ambiente no provedor externo, com segredos de webhook distintos.

## Ambientes mobile
- Expo/EAS: perfis `development`, `preview` (staging) e `production` em `eas.json`, cada um apontando para a API do ambiente via `EXPO_PUBLIC_API_URL`; canais de update separados.
- Flutter: flavors/`--dart-define` por ambiente; ícone/nome diferente em staging para ninguém confundir os apps instalados.
- Detalhes de build e publicação: `firebase-expo.md`.

## Checklist de PR
- [ ] Nova configuração entra como variável de ambiente, validada no startup, e está no `.env.example` sem valor real.
- [ ] Nenhum segredo em código, `.env.example`, variável pública de frontend, logs ou fixtures.
- [ ] Segredos novos documentados (nome, onde vivem, quem rotaciona) — criar no provedor é tarefa humana.
- [ ] Migration compatível com a versão anterior (expand/contract); destrutiva só em PR próprio, com backup confirmado.
- [ ] Staging continua sem dados pessoais reais; integrações em sandbox.
- [ ] Feature flag nova tem dono, padrão desligado em produção e Issue de remoção.
- [ ] Staging e produção continuam gerados pela mesma configuração (sem ajuste manual só em um deles).
