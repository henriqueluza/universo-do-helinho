---
name: devops-standards
description: Padrões de DevOps da software house — Docker, GitHub Actions, ambientes e deploy (staging automático, produção só por disparo manual humano), Terraform (AWS/Azure/GCP), Railway/Render/Fly.io, Firebase, Expo EAS, observabilidade e segredos. Use ao implementar Issues de CI, build, infra ou deploy (tipos ci/build/chore de infra), ao decidir hospedagem num ADR, e ao revisar PRs que tocam Dockerfile, workflows, IaC ou configuração de ambiente.
---

# DevOps Standards

## Política de deploy (inegociável)
1. **Agentes nunca fazem deploy, `apply`, `destroy`, `submit` às lojas nem alteram ambientes vivos.** O hook do repositório bloqueia esses comandos.
2. Depois que **você** (humano) faz o merge em `main`, o CI constrói **um** artefato e publica automaticamente em **staging**.
3. **Produção** usa o mesmo artefato (mesmo digest/build) e roda num workflow separado que **só um humano dispara** (`workflow_dispatch`, botão "Run workflow"), com `environment: production` restrito a `main`. Motivo: em repositório privado, *required reviewers* exigem GitHub Enterprise — o disparo manual funciona em qualquer plano. O hook impede agentes de disparar workflows de deploy.
4. Rollback = o mesmo workflow de produção disparado por humano com o digest anterior (documentado no runbook).
5. Agentes PODEM: escrever Dockerfile, workflows, Terraform e configs de PaaS; rodar `docker build`, `terraform fmt/validate/plan` com credenciais somente leitura, linters e scanners; abrir PR. Mudanças em `.github/workflows` só com Issue que autorize explicitamente.

## Qual guia ler

| Situação | Guia |
|---|---|
| Containerizar um serviço | [docker.md](references/docker.md) |
| Pipeline de PR, build, deploy staging/produção, rollback | [ci-cd.md](references/ci-cd.md) |
| Variáveis, segredos, ambientes, migrations no deploy, preview por PR | [environments.md](references/environments.md) |
| Infra em AWS, Azure ou GCP | [terraform.md](references/terraform.md) |
| Railway, Render ou Fly.io | [paas.md](references/paas.md) |
| Firebase (rules, functions, hosting) ou app Expo (EAS Build/Update/Submit) | [firebase-expo.md](references/firebase-expo.md) |
| Logs, health checks, métricas, tracing, alertas, runbooks | [observability.md](references/observability.md) |

A hospedagem e os ambientes de cada projeto estão no ADR (`docs/architecture/`). Não troque de provedor sem ADR novo.

## Regras gerais
- **Infra também é PR pequeno**: um recurso/módulo ou um workflow por PR, tipo `ci:`, `build:` ou `chore(infra):`. Nada de "PR de infra completa".
- Autenticação do CI na cloud por **OIDC**; segredos de PaaS como secrets do **GitHub Environment**; nunca chave de longa duração no repositório ou em logs.
- Actions de terceiros pinadas por **SHA** (com comentário da versão); `permissions:` mínimas por job.
- Ferramentas e APIs de provedores mudam: confirme sintaxe atual no Context7 antes de escrever config.
- Toda mudança operacional atualiza `docs/infra.md` e/ou um runbook (o subagente `docs-writer` cuida disso no mesmo PR).

## Checklist universal de DevOps
- [ ] Nenhum segredo em código, Dockerfile, workflow, tfvars ou log
- [ ] Mesmo artefato promovido de staging para produção
- [ ] Produção só por `workflow_dispatch` humano, `environment: production` restrito a `main`
- [ ] Health check e smoke test pós-deploy
- [ ] Migrations compatíveis com a versão anterior (expand/contract) e backup antes de mudança destrutiva
- [ ] Rollback descrito no runbook
- [ ] Actions pinadas por SHA, `permissions` mínimas, OIDC
- [ ] `docs/infra.md`/runbook atualizado
