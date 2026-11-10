# PaaS — Railway, Render e Fly.io

Para APIs, workers e bancos gerenciados sem operar cloud. Leia junto com `docker.md` (imagem), `ci-cd.md` (pipeline), `environments.md` (staging/produção) e `observability.md`. Recursos, nomes de campos e CLIs desses provedores mudam com frequência: **confirme na documentação atual via Context7** antes de escrever configuração.

**Política da casa:** agentes nunca fazem deploy. Depois do merge humano em `main`, o CI publica em **staging**; **produção** só por disparo manual humano (`workflow_dispatch` do workflow de produção, ver `ci-cd.md`) no GitHub Environment `production`. Tokens dos provedores ficam como **secrets do Environment** correspondente — nunca em repositório secret global nem no código.

## 1. Quando escolher cada um

| | Railway | Render | Fly.io |
|---|---|---|---|
| Melhor para | MVP rápido, vários serviços + Postgres/Redis em um projeto | apps web clássicos, Blueprint declarativo completo, cron jobs | latência baixa multi-região, apps perto do usuário, controle de VM (Machines) |
| Configuração versionada | `railway.json` / `railway.toml` por serviço | `render.yaml` (Blueprint) cobre serviços + bancos + env groups | `fly.toml` por app |
| Banco gerenciado | Postgres/Redis/MySQL no projeto | Postgres e Key Value gerenciados | Postgres gerenciado (confirme oferta atual) ou externo |
| Preview por PR | ambientes de PR | Preview Environments (Blueprint) | via CI (app efêmero por PR) |
| Atenção | custo por uso cresce rápido | instâncias gratuitas dormem; banco gratuito expira | mais operação (volumes, regiões, máquinas) |

Regra: escolha registrada no ADR. Precisa de rede privada complexa, compliance pesado ou muitos serviços? Vá para `terraform.md`.

## 2. Railway

**Configuração versionada** — um `railway.json` (ou `.toml`) por serviço:

```json
{
  "$schema": "https://railway.com/railway.schema.json",
  "build": { "builder": "DOCKERFILE", "dockerfilePath": "Dockerfile" },
  "deploy": {
    "preDeployCommand": "npm run migrate",
    "healthcheckPath": "/health",
    "restartPolicyType": "ON_FAILURE"
  }
}
```

- **Ambientes:** use os environments do projeto Railway (`staging`, `production`), cada um com variáveis próprias. Bancos são **separados por ambiente**, nunca compartilhados.
- **Migrations:** `preDeployCommand` — roda antes da troca de versão; se falhar, o deploy não sobe.
- **Health check:** `healthcheckPath` obrigatório; o deploy só troca o tráfego quando responde 200.
- **Deploy via CI:** `railway up --service <svc> --environment staging` com `RAILWAY_TOKEN` (token de projeto, escopo de um ambiente) no secret do Environment.
- **Preview por PR:** PR environments do Railway, com banco descartável — nunca apontando para dados de staging/produção.
- **Rollback:** redeploy de um deployment anterior pelo painel/CLI; migração reversível (ver expand/contract em `environments.md`).

## 3. Render

**Blueprint** — `render.yaml` na raiz descreve tudo:

```yaml
services:
  - type: web
    name: reservas-api-staging
    runtime: docker
    branch: main
    autoDeploy: false            # o deploy é disparado pelo CI
    healthCheckPath: /health
    preDeployCommand: npm run migrate
    envVars:
      - key: DATABASE_URL
        fromDatabase: { name: reservas-db-staging, property: connectionString }
      - fromGroup: reservas-staging
databases:
  - name: reservas-db-staging
```

- **Ambientes:** serviços e bancos com sufixo por ambiente, ou Projects/Environments do Render (confirme o recurso atual). Segredos em Environment Groups, marcados como `sync: false` no Blueprint (o valor nunca vai para o YAML).
- **Migrations:** `preDeployCommand`.
- **Deploy via CI:** `autoDeploy: false` e disparo via **deploy hook** (URL secreta) ou API com `RENDER_API_KEY`, guardados no secret do Environment.
- **Preview por PR:** Preview Environments do Blueprint (`previews` no YAML), com banco próprio.
- **Rollback:** rollback para deploy anterior no painel/API.
- **Limites:** instâncias gratuitas **dormem** após inatividade (primeira requisição lenta) e o Postgres gratuito **expira** — nunca use em produção.

## 4. Fly.io

**`fly.toml`** por app (um app por ambiente: `reservas-api-stg`, `reservas-api-prod`):

```toml
app = "reservas-api-stg"
primary_region = "gru"

[deploy]
  release_command = "npm run migrate"
  strategy = "rolling"

[http_service]
  internal_port = 8080
  force_https = true
  min_machines_running = 1

[[http_service.checks]]
  path = "/health"
  interval = "15s"
  timeout = "5s"
```

- **Ambientes:** apps separados por ambiente, cada um com seu `fly.<env>.toml` ou `--config`. Segredos via `fly secrets set` (feito por humano ou pelo CI a partir do Environment), nunca no `fly.toml`.
- **Migrations:** `release_command` roda numa máquina temporária antes do deploy; falhou, não troca.
- **Deploy via CI:** `flyctl deploy --config fly.staging.toml --remote-only` com `FLY_API_TOKEN` (token de deploy restrito ao app) no secret do Environment.
- **Preview por PR:** app efêmero criado pelo CI (`pr-<n>-<app>`) e destruído ao fechar o PR — só com banco descartável.
- **Rollback:** `fly releases` + redeploy da imagem anterior (`flyctl deploy --image <imagem-anterior>`), feito pelo CI ou humano.
- **Limites:** `min_machines_running = 0` faz a máquina **parar** sem tráfego (cold start); volumes são por máquina/região — banco com volume próprio exige plano de backup.

## 5. Padrões comuns aos três

- Imagem construída **uma vez** no CI (ver `docker.md`) e promovida de staging para produção; produção nunca recompila.
- Endpoint `/health` (liveness) e, se houver dependências, `/ready` — ver `observability.md`.
- Migrations sempre **compatíveis com a versão anterior** (expand/contract), porque o rollback do app não desfaz o banco.
- Região escolhida perto dos usuários (Brasil: São Paulo quando disponível) e o banco na **mesma região** da API.
- Alerta de custo/uso configurado no painel do provedor.
- Logs exportados ou acessíveis por quem dá suporte; nada de dado pessoal em log.

## O que agentes podem e não podem fazer

| Pode | Não pode |
|---|---|
| Escrever/alterar `railway.json`, `render.yaml`, `fly.toml` e workflows de deploy | Rodar `railway up`, `flyctl deploy`, deploy hooks ou API de deploy |
| Validar configuração localmente (`flyctl config validate`, lint de YAML) | `fly secrets set`, alterar variáveis/segredos no painel |
| Escrever migrations e o comando de pré-deploy | Rollback, scale, restart ou destroy de apps/bancos |
| Propor região, plano e health checks no PR | Criar/excluir projetos, apps ou bancos |

## Checklist de PR

- [ ] Configuração do provedor versionada e com health check.
- [ ] Migrations no comando de pré-deploy/release, compatíveis com a versão anterior.
- [ ] Nenhum segredo no arquivo de configuração (só referências/`sync: false`).
- [ ] Staging e produção separados (serviços/apps e bancos distintos).
- [ ] Workflow usa o Environment correto; produção só por `workflow_dispatch` humano.
- [ ] Preview por PR usa banco descartável.
- [ ] Rollback descrito no PR quando a mudança for arriscada.
- [ ] Limites do plano (sleep, expiração, região) considerados para o ambiente.
