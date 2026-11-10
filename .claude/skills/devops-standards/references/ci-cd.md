# CI/CD — GitHub Actions

Política da casa:
- **Agentes nunca fazem deploy.** Nenhum comando de deploy é executado por agente; o hook do repositório bloqueia.
- Depois do **merge humano em `main`**, o CI publica automaticamente em **staging**.
- **Produção só por ação humana**: workflow separado, disparado **somente por `workflow_dispatch`** (botão "Run workflow"), promovendo o digest que já está em staging. O clique humano é a aprovação; o hook impede agentes de disparar workflows de deploy.
- Funciona em qualquer plano. Em repositório **privado**, *required reviewers* em environments só existem no GitHub Enterprise; em Pro/Team o environment `production` ainda restringe a branch (`main`) e guarda os segredos. Se o repo for público ou o plano for Enterprise, adicione *required reviewers* + *Prevent self-review* por cima.
- Editar `.github/workflows` exige autorização explícita (regra do `CLAUDE.md`). Mudanças de CI são PRs próprios com tipo `ci:` — nunca junto de feature.

## Estrutura de workflows
| Arquivo | Gatilho | Jobs |
|---|---|---|
| `validate.yml` | `pull_request` | lint, typecheck, testes, build da stack + checagem do kit |
| `security.yml` | `pull_request`, `push: main` | `semgrep`, `gitleaks` |
| `pr-size.yml` | `pull_request` | `pr-size` |
| `deploy-staging.yml` | `push: main` | `build` → `deploy-staging` |
| `deploy-production.yml` | `workflow_dispatch` (humano) com input `digest` | `deploy-production` (promove o digest de staging; também serve de rollback) |

Checks obrigatórios no ruleset: `validate`, `semgrep`, `gitleaks`, `pr-size`. O build da imagem também roda no PR (dentro do `validate` ou job próprio) para que `main` nunca receba algo que não compila.

## Regras de segurança dos workflows
1. **`permissions:` mínimas por job.** Topo do arquivo com `contents: read`; cada job pede só o que usa (`id-token: write` para OIDC, `packages: write` para publicar imagem).
2. **OIDC para cloud.** AWS (`configure-aws-credentials` com `role-to-assume`), Azure (`azure/login` com federated credential), GCP (`google-github-actions/auth` com Workload Identity). **Nada de chave de acesso de longa duração** em secrets. PaaS sem OIDC (Railway, Render, Fly, EAS): token com escopo mínimo, guardado como secret **do environment**, não do repositório.
3. **Actions de terceiros pinadas por SHA** com comentário da versão: `uses: org/action@<sha> # vX`. Dependabot (`package-ecosystem: github-actions`) atualiza.
4. **Nunca interpolar `${{ github.event.* }}` direto em `run:`** (injeção via título de PR/branch). Passe por `env:` e use a variável de shell.
5. `pull_request_target` é proibido salvo ADR — roda código do fork com segredos.
6. Segredos só nos environments que precisam deles; logs nunca imprimem segredo (o GitHub mascara, mas não confie em formatos derivados).

## Build uma vez, promova o mesmo artefato
- O job `build` gera **uma** imagem/artefato, identificada pelo **digest** (`sha256:...`) ou pelo SHA do commit.
- `deploy-staging` e `deploy-production` recebem o **mesmo digest** via `outputs`. Produção nunca reconstrói.
- Configuração muda por ambiente (variáveis/segredos do environment), o artefato não.
- Mobile (Expo EAS) e Firebase seguem o mesmo princípio com o build/canal correspondente — ver `firebase-expo.md`.

## Migrations
- Step **explícito** antes do rollout, no mesmo job de deploy, com o mesmo artefato (comando de migration da stack).
- Só migrations **compatíveis com a versão anterior** (expand/contract — `environments.md`). Assim o rollback da aplicação não exige rollback de banco.
- Migration destrutiva (drop/rename) só em PR próprio, depois que nenhuma versão em produção usa a coluna, e com backup confirmado.

## Concorrência, cache e smoke test
- `concurrency: { group: deploy-<ambiente>, cancel-in-progress: false }` — nunca dois deploys simultâneos no mesmo ambiente, e um deploy em andamento não é cancelado no meio.
- Cache de dependências pela action de setup da stack (`cache: npm`, `cache: pip`...) e cache de camadas Docker (`cache-from/cache-to: type=gha`).
- **Smoke test pós-deploy**: `curl --fail` em `/health` e `/ready` e 1–3 requisições do fluxo crítico. Falhou → job falha e o rollback é acionado/documentado.

## Rollback
- Rollback = rodar `deploy-production.yml` (humano) com o **digest anterior**. Os digests implantados ficam no resumo de cada execução (`$GITHUB_STEP_SUMMARY`).
- Cada provedor tem seu atalho (revisão anterior do Cloud Run, `fly releases`, rollback do Render, `firebase hosting:rollback`) — documente em `docs/runbooks/rollback.md`. Executar é ação humana.
- Rollback de banco não é o caminho padrão; por isso as migrations são compatíveis com a versão anterior.

## Exemplo: deploy staging + produção (agnóstico)
`deploy-staging.yml` — automático depois do merge humano:
```yaml
name: deploy-staging
on:
  push:
    branches: [main]
permissions:
  contents: read
env:
  IMAGE: ghcr.io/${{ github.repository }}
jobs:
  build:
    runs-on: ubuntu-latest
    permissions: { contents: read, packages: write }
    outputs:
      digest: ${{ steps.push.outputs.digest }}
    steps:
      - uses: actions/checkout@<sha> # vX
      - uses: docker/setup-buildx-action@<sha> # vX
      - uses: docker/login-action@<sha> # vX
        with: { registry: ghcr.io, username: "${{ github.actor }}", password: "${{ secrets.GITHUB_TOKEN }}" }
      - id: push
        uses: docker/build-push-action@<sha> # vX
        with:
          push: true
          tags: ${{ env.IMAGE }}:${{ github.sha }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
      - uses: aquasecurity/trivy-action@<sha> # vX
        with: { image-ref: "${{ env.IMAGE }}@${{ steps.push.outputs.digest }}", severity: CRITICAL, ignore-unfixed: true, exit-code: "1" }

  deploy-staging:
    needs: build
    runs-on: ubuntu-latest
    environment: staging
    concurrency: { group: deploy-staging, cancel-in-progress: false }
    permissions: { contents: read, id-token: write }
    env:
      DIGEST: ${{ needs.build.outputs.digest }}
      APP_URL: ${{ vars.APP_URL }}
    steps:
      - uses: actions/checkout@<sha> # vX
      # TODO provedor: autenticação OIDC/token (ver terraform.md, paas.md ou firebase-expo.md)
      - name: Migrations
        run: echo "TODO provedor - rodar migrations com $IMAGE@$DIGEST"
      - name: Deploy
        run: echo "TODO provedor - publicar $IMAGE@$DIGEST em staging"
      - name: Smoke test
        run: |
          curl --fail --retry 5 --retry-delay 5 "$APP_URL/health"
          curl --fail "$APP_URL/ready"
      - name: Registrar digest para promoção
        run: echo "Digest em staging (use em deploy-production) → \`$DIGEST\`" >> "$GITHUB_STEP_SUMMARY"
```

`deploy-production.yml` — **só humano** dispara, com o digest validado em staging (também é o rollback):
```yaml
name: deploy-production
on:
  workflow_dispatch:
    inputs:
      digest:
        description: "Digest validado em staging (sha256:...)"
        required: true
permissions:
  contents: read
env:
  IMAGE: ghcr.io/${{ github.repository }}
jobs:
  deploy-production:
    if: ${{ github.ref == 'refs/heads/main' }}
    runs-on: ubuntu-latest
    environment: production   # restringe a main e guarda segredos; required reviewers se o plano permitir
    concurrency: { group: deploy-production, cancel-in-progress: false }
    permissions: { contents: read, id-token: write }
    env:
      DIGEST: ${{ inputs.digest }}
      APP_URL: ${{ vars.APP_URL }}
    steps:
      - name: Validar formato do digest
        run: '[[ "$DIGEST" =~ ^sha256:[a-f0-9]{64}$ ]] || { echo "digest inválido"; exit 1; }'
      - uses: actions/checkout@<sha> # vX
      # TODO provedor: autenticação OIDC/token
      - name: Migrations
        run: echo "TODO provedor - rodar migrations com $IMAGE@$DIGEST"
      - name: Deploy
        run: echo "TODO provedor - publicar $IMAGE@$DIGEST em produção"
      - name: Smoke test
        run: curl --fail --retry 5 --retry-delay 5 "$APP_URL/health"
      - run: echo "Produção agora em \`$DIGEST\` (por ${{ github.actor }})" >> "$GITHUB_STEP_SUMMARY"
```
Os passos `TODO provedor` vêm de `terraform.md` (AWS/Azure/GCP), `paas.md` (Railway/Render/Fly.io) ou `firebase-expo.md`. `vars.APP_URL` é uma variável **do environment** (valor diferente em staging e produção).

## Fatiamento de mudanças de CI/CD
PRs pequenos valem para infra. Ordem típica para um projeto novo:
1. `ci: validate com lint/test/build da stack`
2. `build: Dockerfile multi-stage + .dockerignore`
3. `ci: build e scan da imagem no PR`
4. `ci: deploy em staging após merge` (environments já criados pelo `setup-project.sh`)
5. `ci: promoção para produção com aprovação`
6. `docs: runbook de deploy e rollback`

## Anti-padrões
- `secrets.AWS_ACCESS_KEY_ID` de usuário IAM permanente.
- `permissions: write-all` ou omitir `permissions` (herda o padrão amplo do repositório).
- Build separado para produção ("só um rebuild com outra flag").
- Deploy disparado por agente, por `workflow_dispatch` chamado por agente ou por comando de CLI do provedor.
- Migration destrutiva no mesmo deploy que remove o uso da coluna.

## Checklist de PR
- [ ] PR de tipo `ci:`/`build:` separado de qualquer feature, com autorização explícita para editar workflows.
- [ ] `permissions:` declaradas no topo (`contents: read`) e mínimas por job.
- [ ] Cloud via OIDC; PaaS com token de escopo mínimo em secret do environment.
- [ ] Actions de terceiros pinadas por SHA com comentário `# vX`.
- [ ] Nenhum `${{ github.event.* }}` interpolado direto em `run:`.
- [ ] Artefato construído uma vez e promovido pelo mesmo digest.
- [ ] Produção só via `workflow_dispatch` humano com digest de staging, `environment: production` e `concurrency` sem cancelamento.
- [ ] Migrations em step explícito, compatíveis com a versão anterior.
- [ ] Smoke test pós-deploy e rollback documentado em `docs/runbooks/`.
