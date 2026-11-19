# ADR-0002 — Hospedagem na Railway, gerenciada por Terraform

**Data:** 18/11/2026 · **Status:** proposto (aguarda o Henrique; ver seção 12) · **Decisor:** Henrique
**Base:** ADR-0001 §6 (hospedagem ficou para ADR próprio), Issue #93, skill `devops-standards`, respostas da contratante C04, C13, C14, C15 e C19.

Os códigos C04, C13, C14, C15 e C19 são itens do Q&A com a contratante, "Perguntas e Respostas — Universo do Hélinho", mantido pelo Henrique fora do repositório.

Este ADR substitui a seção 6 do ADR-0001 ("Deploy planejado para a fase final").

---

## 1. Contexto

O sistema só roda localmente (`compose.yaml` + `scripts/demo.sh`). A Sprint 1 deixa no repositório tudo o que o Henrique precisa para entrar na Railway, criar um token, rodar `terraform apply` e ter a homologação no ar. Nenhum agente roda `plan`, `apply`, cria recurso, conta ou publica nada.

Restrições que moldam a decisão:

- **Custo mínimo** (C04): a conta Railway já tem pagamento configurado; não há teto numérico aprovado; "gasto mínimo" não é autorização ilimitada. O contato para o projeto na Railway é o Igor (C19).
- **Volume desconhecido** (C13): usuários simultâneos e pico não foram informados. As estimativas de `docs/operations/custos.md` usam premissas explícitas, não medição.
- **Domínio e DNS pendentes** (C15): a homologação usa o domínio `*.up.railway.app`; domínio próprio fica para depois.
- **Só dados sintéticos** em homologação. Receber dado real de menor exige ADR próprio (ADR-0001 §7).
- **Um mantenedor** (Henrique) e código que será entregue à contratante: a infraestrutura precisa ser pequena e legível.

## 2. Fontes consultadas (todas em 18/11/2026)

| Assunto | Fonte |
|---|---|
| Provider Terraform, versão e recursos | https://registry.terraform.io/providers/terraform-community-providers/railway/0.6.3/docs (API do Registry: `/v1/providers/terraform-community-providers/railway`) |
| Código e changelog do provider (tag `v0.6.3`) | https://github.com/terraform-community-providers/terraform-provider-railway |
| Preços e planos | https://railway.com/pricing e https://docs.railway.com/pricing/plans |
| Controle de custo (alerta, limite rígido, serverless) | https://docs.railway.com/pricing/cost-control e https://docs.railway.com/deployments/serverless |
| Config as Code (`railway.toml`) descontinuado | https://docs.railway.com/config-as-code e https://docs.railway.com/config-as-code/reference |
| Infrastructure as Code nativo da Railway | https://docs.railway.com/infrastructure-as-code e https://docs.railway.com/infrastructure-as-code/reference |
| Dockerfile, start command, pre-deploy, healthcheck, restart | https://docs.railway.com/builds/dockerfiles · `/deployments/start-command` · `/deployments/pre-deploy-command` · `/deployments/healthchecks` · `/deployments/restart-policy` |
| Auto-deploy do GitHub, rollback, redeploy | https://docs.railway.com/deployments/github-autodeploys e https://docs.railway.com/deployments/deployment-actions |
| Volumes, backups e PostgreSQL | https://docs.railway.com/volumes/reference · `/volumes` · `/volumes/backups` · `/databases/postgresql` · `/databases/build-a-database-service` |
| Variáveis de referência e variáveis da plataforma | https://docs.railway.com/variables e https://docs.railway.com/variables/reference |
| Rede privada, proxy e cabeçalhos | https://docs.railway.com/networking/private-networking e https://docs.railway.com/networking/public-networking/specs-and-limits |
| Cabeçalhos sobrescritos pela borda e ausência de lista de IPs do proxy (resposta da conta oficial da Railway, 10/10/2026) | https://station.railway.com/questions/asp-net-core-10-secure-forwarded-heade-506a954b |
| Tipos de token e variáveis de ambiente da CLI | https://docs.railway.com/integrations/api e https://docs.railway.com/cli |
| Regiões e ambientes | https://docs.railway.com/deployments/regions e https://docs.railway.com/environments |
| Data Protection com EF Core | https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers (via Context7, `/dotnet/aspnetcore.docs`) |
| Plano gratuito do HCP Terraform | https://developer.hashicorp.com/terraform/cloud-docs/overview |

O que não foi possível confirmar em fonte está na seção 16.

## 3. Decisão (resumo)

| Item | Decisão | Seção |
|---|---|---|
| Provedor | Railway, na conta da contratante (workspace do Igor) | 1 |
| IaC | Terraform em `infra/railway/`, provider `terraform-community-providers/railway` fixado em `0.6.3`, lockfile commitado | 4 |
| Topologia | Um projeto, um ambiente (`homologacao`), dois serviços: `helinho-web` e `Postgres`. `producao` existe no código e nasce desligada | 5 |
| Região | `us-east4-eqdc4a` (US East), web e banco na mesma região. A Railway não tem região no Brasil | 5 |
| PostgreSQL | Serviço com imagem `postgres:17` + volume, declarado no Terraform, sem domínio público nem TCP proxy | 6 |
| Build | Dockerfile multi-stage na raiz (Issue #94). O builder automático (Railpack) fica descartado | 7.1 |
| Configuração de build e deploy | Atributos do `railway_service` no Terraform. **`railway.toml` não entra**: a Railway descontinuou o Config as Code | 7.2 |
| Migração | `migrate` como pre-deploy command; seed só com `Seed__Enabled=true`, nunca em produção (Issue #95) | 7.3 |
| Saúde | `/health` (vivo) e `/health/ready` (banco); o healthcheck da Railway aponta para `/health/ready` (Issue #96) | 7.4 |
| Deploy contínuo | `auto_deploy = false` por padrão | 7.5 |
| Proxy e HTTPS | ForwardedHeaders com `X-Real-IP` e `X-Forwarded-Proto`, redirecionamento HTTPS e HSTS fora de `Development` (Issue #97) | 8 |
| Data Protection | Chaves em tabela no PostgreSQL (pacote novo + migração, Issue própria) | 9 |
| Segredos | Só em variável da Railway e em `TF_VAR_*`; token do provider por `RAILWAY_TOKEN` | 10 |
| Estado do Terraform | Local, fora do git, com backup cifrado | 10.3 |
| Logs | JSON no console fora de `Development` (Issue #98); a Railway coleta o stdout | 11 |
| Rollback e backup | Rollback de deploy pelo painel; backup de volume agendado + `pg_dump` por túnel | 11 |

As linhas de topologia, estado do Terraform, deploy contínuo e Data Protection são **recomendações** que esperam o Henrique (seção 12).

## 4. Provider Terraform

### 4.1 Versão e recursos

`terraform-community-providers/railway` **0.6.3**, publicado em 19/10/2026 (versão mais recente no Terraform Registry em 18/11/2026). É comunitário, mantido fora da Railway, e é o único provider para a plataforma.

A versão 0.6.3 tem **nove recursos e nenhum data source**:

| Recurso | Para que serve | Usado aqui |
|---|---|---|
| `railway_project` | Projeto; define também o nome do ambiente padrão (`default_environment.name`), `private`, `has_pr_deploys`, `workspace_id` | sim |
| `railway_environment` | Ambiente adicional em um projeto (`name`, `project_id`) | não (ver 5.2) |
| `railway_service` | Serviço: origem (`source_repo` + `source_repo_branch` ou `source_image`), build e deploy, `regions`, `volume`, `serverless`, `cron_schedule` | sim (web e banco) |
| `railway_variable` | Uma variável de um serviço em um ambiente | não (preferimos a coleção) |
| `railway_variable_collection` | Conjunto de variáveis de um serviço em um ambiente, gerenciado como um todo | sim |
| `railway_shared_variable` | Variável compartilhada do projeto em um ambiente | não |
| `railway_service_domain` | Domínio `*.up.railway.app` do serviço (`subdomain`, `target_port`) | sim (só no web) |
| `railway_custom_domain` | Domínio próprio, com os registros DNS de verificação como saída | depois (C15) |
| `railway_tcp_proxy` | Porta TCP pública para um serviço | **nunca** no banco |

Não existem na 0.6.3: recurso de **volume** separado (o volume é um bloco do `railway_service`), recurso de **gatilho de deploy** (`railway_deployment_trigger` foi removido na 0.5.0), recurso de **banco gerenciado ou template** (`railway_plugin` foi removido na 0.3.0) e qualquer data source.

### 4.2 O que o `railway_service` configura

A 0.6.3 acrescentou as configurações de build e deploy ao serviço. Tudo o que esta sprint precisa cabe nele:

| Necessidade | Atributo |
|---|---|
| Repositório e branch | `source_repo`, `source_repo_branch` |
| Caminho do Dockerfile | `dockerfile_path` (opcional; a Railway usa sozinha o `Dockerfile` da raiz) |
| Pre-deploy command e tempo limite | `predeploy_command`, `predeploy_timeout` |
| Healthcheck e tempo limite | `healthcheck_path`, `healthcheck_timeout` |
| Política de restart | `restart_policy_type` (`ON_FAILURE`, `ALWAYS`, `NEVER`), `restart_policy_max_retries` |
| Filtro de arquivos que disparam deploy | `watch_patterns` |
| Região e réplicas | `regions` (`region`, `num_replicas`) |
| Dormir quando ocioso | `serverless` |
| Volume | `volume` (`name`, `mount_path`; o tamanho é somente leitura) |

O que **não** se configura pelo provider: ligar ou desligar o auto-deploy, "Wait for CI", limite de CPU e memória por réplica, agenda de backup do volume, alerta e limite de gasto do workspace, e o builder (não é preciso: havendo `Dockerfile`, a Railway sempre usa ele).

### 4.3 Limitações encontradas e contorno

| # | Limitação (lida no código da tag `v0.6.3`) | Contorno |
|---|---|---|
| L1 | Não cria PostgreSQL gerenciado nem por template | Serviço `postgres:17` + volume no Terraform (seção 6) |
| L2 | Não tem atributo de auto-deploy; conectar o repositório cria o gatilho da branch | `watch_patterns` que nunca casa quando `auto_deploy = false` (seção 7.5) |
| L3 | O `railway_service` não tem `environment_id`: as configurações são gravadas com `environmentId: null` e **lidas só do ambiente padrão** do projeto; o volume também | Um ambiente por projeto (seção 5.2) |
| L4 | Mudar uma coleção de variáveis faz o provider **reimplantar** o serviço | `terraform apply` com mudança de variável é um deploy; o runbook avisa |
| L5 | Autentica com `Authorization: Bearer`; token de projeto usa outro cabeçalho e não serve | Token de workspace (seção 10.2) |
| L6 | Tamanho do volume é somente leitura | Vale o padrão do plano (5 GB no Hobby, 50 GB no Pro); a cobrança é pelo espaço usado |
| L7 | Versão 0.x, com remoções de recurso em 0.3.0 e 0.5.0 e renomeação de atributo em 0.6.0 | Versão fixada (`= 0.6.3`), lockfile commitado; atualizar é PR `chore` próprio |

## 5. Topologia e ambientes

### 5.1 Homologação

```
Projeto Railway (workspace da contratante)
└── ambiente homologacao   (ambiente padrão do projeto, renomeado)
    ├── helinho-web   repositório GitHub, branch main, Dockerfile   → https://<subdomínio>.up.railway.app
    └── Postgres      imagem postgres:17 + volume                   → só rede privada (postgres.railway.internal)
```

- Uma réplica de cada serviço, `serverless = false`. Com serverless o serviço dorme após 5 a 10 minutos sem tráfego de saída e a primeira requisição pode devolver 502; isso atrapalha o desenvolvedor Unity para economizar poucos dólares (ver `custos.md`). Fica como variável desligada.
- `has_pr_deploys = false` e `private = true` no projeto.
- **Região `us-east4-eqdc4a`.** As regiões da Railway são Califórnia, Virgínia, Amsterdã e Singapura; não há região no Brasil. Virgínia é a de menor latência para o Brasil. Consequência: os dados ficam nos Estados Unidos. Para dado sintético isso é indiferente; para dado real de menor é transferência internacional (LGPD) e depende da resposta da contratante sobre região e fornecedores (C14, Issue #39). **Isso bloqueia produção, não homologação.**

### 5.2 Produção: existe no código, nasce desligada

O Terraform recebe um mapa `ambientes` cujo valor padrão contém só `homologacao`. Cada chave instancia o mesmo módulo. Acrescentar `producao` ao `terraform.tfvars` é o único gesto para ligá-la, e `terraform validate` passa com ela ligada ou desligada.

Cada ambiente vira **um projeto Railway próprio** (`helinho-<ambiente>`), com o ambiente padrão renomeado para `<ambiente>`, em vez de um segundo ambiente dentro do mesmo projeto. Motivos:

- L3: o provider só lê e grava configuração de serviço e volume pelo ambiente padrão. Um segundo ambiente no mesmo projeto ficaria com o volume do banco fora do estado e sem configuração própria de serviço.
- O controle de acesso por ambiente (Environment RBAC) só existe no plano Enterprise. Em projetos separados, quem acessa homologação não enxerga variáveis de produção.
- O custo é o mesmo: a Railway cobra uso, não projeto.

No cenário atual isso continua sendo "um projeto, um ambiente". A escolha só aparece quando a produção for ligada, e está na decisão D1.

Ligar produção exige antes: ADR próprio para dado real (ADR-0001 §7), resposta sobre região e fornecedores (C14), teto de gasto (C04), estado do Terraform separado por ambiente e `Seed__Enabled=false`.

## 6. PostgreSQL

O provider não cria banco gerenciado (L1). As duas alternativas possíveis:

| | (a) Serviço `postgres:17` + volume no Terraform | (b) Banco criado à mão pelo template + `terraform import` |
|---|---|---|
| Passo manual | nenhum | criar o banco no painel entre dois `apply`, copiar o id e importar |
| Versão do PostgreSQL | fixada no código (17, a mesma do `compose.yaml` e dos testes) | a que o template entregar no dia; trocar a imagem tira o banco do suporte do template |
| Reprodutível por código | sim, um `apply` | não |
| Senha | gerada pelo Terraform, fica no estado | gerada pela Railway, fora do estado |
| Recursos do template (upgrade de versão assistido, alta disponibilidade, PITR) | não | sim |
| Backup de volume da Railway | sim (vale para qualquer volume) | sim |

**Escolha: (a).** A sprint existe para o Henrique rodar um `apply` e ter o sistema no ar; (b) quebra isso em duas fases com um passo manual no meio e um `import`. Homologação tem só dado sintético e pode ser destruída e recriada, então upgrade assistido, alta disponibilidade e PITR não fazem falta. O custo de (a) é a senha do banco no estado, que já guarda a senha do seed e por isso é protegido (seção 10.3). Para produção a escolha é refeita no ADR de produção.

Detalhes que a Issue do Terraform precisa respeitar:

- Serviço chamado `Postgres` (o nome é o namespace das referências `${{Postgres.*}}`), `source_image = "postgres:17.<minor>"` com a tag de minor fixada na Issue.
- Volume montado em `/var/lib/postgresql/data` e `PGDATA=/var/lib/postgresql/data/pgdata` (o ponto de montagem não nasce vazio; o PostgreSQL exige um subdiretório).
- A imagem oficial inicia como root e troca para o usuário `postgres`, então não precisa de `RAILWAY_RUN_UID`.
- Variáveis do serviço: `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `PGDATA` e os espelhos `PGHOST=${{RAILWAY_PRIVATE_DOMAIN}}`, `PGPORT=5432`, `PGUSER`, `PGPASSWORD`, `PGDATABASE`.
- Senha gerada por `random_password` (provider `hashicorp/random`, sem caracteres especiais para não quebrar a connection string), nunca digitada nem escrita em arquivo. A alternativa `TF_VAR_postgres_password` obriga o Henrique a guardar e exportar mais um segredo a cada `apply`.
- **Sem `railway_service_domain` e sem `railway_tcp_proxy` no banco.** O acesso é só pela rede privada do ambiente (WireGuard entre serviços).
- No serviço web: `ConnectionStrings__Default = Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}`. A senha não é escrita no Terraform nem fica nessa entrada do estado: a Railway resolve a referência no deploy, e o provider lê as variáveis sem renderizar. O valor resolvido, porém, contém a senha do banco, então a variável é tratada como **sensível** (seção 10.1). Em HCL, `${{` precisa ser escrito `$${{` para não virar interpolação do Terraform.
- A referência entre serviços também ordena o deploy: o web espera o banco quando os dois sobem juntos.
- Serviço com volume tem uma pequena indisponibilidade a cada redeploy e não aceita réplicas. Isso vale para o banco e é aceito.

## 7. Build e deploy

### 7.1 Dockerfile contra o builder automático

A Railway constrói com **Railpack** quando não encontra `Dockerfile` e usa sempre o `Dockerfile` da raiz quando ele existe.

| | Dockerfile multi-stage (escolhido) | Railpack |
|---|---|---|
| Imagem final | só runtime ASP.NET 10 e o `publish`; sem SDK, fontes, testes, `.env` ou `.demo/` | decidida pela ferramenta, muda com a versão do Railpack |
| Usuário | não-root, declarado | o padrão da ferramenta |
| Reprodução local | `docker build` + `docker run` contra o Postgres do compose (definição de pronto da sprint) | não reproduz fora da Railway |
| Portabilidade | a mesma imagem roda em qualquer provedor; importa porque o código será entregue à contratante | preso à Railway |
| Revisão | arquivo versionado e revisado em PR, com imagem base fixada | implícito |

Contrato entre a imagem e a plataforma: **o Terraform define `PORT=8080` no serviço web** e o domínio usa `target_port = 8080`. A imagem escuta em `0.0.0.0:${PORT}` (Issue #94). Fixar a porta evita depender de expansão de variável em shell (o start command de imagem Docker roda em forma exec) e é a porta que a Railway usa no healthcheck.

### 7.2 Onde fica cada configuração: `railway.toml` não entra

A documentação da Railway, em 18/11/2026, diz que o Config as Code (`railway.json` / `railway.toml`) está **descontinuado**: serviços novos não podem aderir e os arquivos existentes deixam de ser lidos em **01/12/2026**. O substituto oficial é o Infrastructure as Code nativo (`.railway/railway.ts`, comparado na seção 12, D5).

Por isso **não haverá `railway.toml`**. As configurações que ele carregaria ficam no `railway_service` do Terraform, que as suporta desde a 0.6.3:

| Configuração | Valor | Onde |
|---|---|---|
| Builder | Dockerfile da raiz, detectado pela Railway | repositório |
| Pre-deploy command | o comando `migrate` da imagem (texto exato definido com as Issues #94 e #95) | `predeploy_command` |
| Tempo limite do pre-deploy | 300 s (sem limite, um `migrate` travado segura o deploy para sempre) | `predeploy_timeout` |
| Healthcheck | `/health/ready`, 120 s | `healthcheck_path`, `healthcheck_timeout` |
| Restart | `ON_FAILURE`, 5 tentativas | `restart_policy_type`, `restart_policy_max_retries` |
| Auto-deploy | desligado por padrão | `watch_patterns` (7.5) |

A Issue planejada para o `railway.toml` deixa de existir; o conteúdo dela passa para a Issue do Terraform.

### 7.3 Migração no deploy (Issue #95)

- `migrate` é o pre-deploy command: aplica as migrações e, **só se `Seed__Enabled=true`**, roda o seed sintético idempotente. `seed` continua existindo para uso local e recusa rodar com a flag desligada.
- `Seed__Enabled` tem padrão `false`. Em homologação o Terraform define `true`. **Nunca liga em produção**: o módulo deve impedir `Seed__Enabled=true` e `HELINHO_SEED_PASSWORD` quando o ambiente for `producao` (validação de variável).
- O pre-deploy roda em contêiner separado, com as variáveis do serviço e acesso à rede privada, **sem volume montado**. Se falhar, o deploy não prossegue e a versão anterior continua no ar.
- Como o rollback do aplicativo não desfaz o banco, toda migração futura precisa ser compatível com a versão anterior (expand/contract, `devops-standards`).

### 7.4 Saúde (Issue #96)

- `/health`: processo vivo, sem tocar no banco.
- `/health/ready`: `Database.CanConnectAsync`, sem pacote novo; 200 com banco, 503 sem banco.
- O healthcheck da Railway aponta para `/health/ready`. Ele só é consultado **durante o deploy**, até a primeira resposta 2xx; a Railway não monitora a rota depois. Monitoramento contínuo não entra nesta sprint.
- A requisição do healthcheck chega por HTTP direto ao contêiner, com `Host: healthcheck.railway.app`. Duas exigências para as Issues #96 e #97: as rotas de saúde respondem 2xx **sem redirecionamento para HTTPS**, e `AllowedHosts`, se um dia for restringido, inclui `healthcheck.railway.app`.

### 7.5 Deploy contínuo desligado

Serviço ligado a repositório faz deploy a cada push na branch conectada. O provider não tem atributo para desligar isso (L2). A variável `auto_deploy` (padrão `false`) é implementada assim:

- `auto_deploy = false` → `watch_patterns` recebe um padrão que nenhum arquivo do repositório casa. Push em `main` vira deploy ignorado. O Henrique publica pelo painel: `Cmd+K` → **Deploy Latest Commit**, que implanta o último commit da branch conectada, ou seja, só código revisado e mergeado.
- `auto_deploy = true` → `watch_patterns = []`: todo push em `main` implanta. Ao ligar, ative também **Wait for CI** nas configurações do serviço (não existe no provider) para a Railway esperar o `validate.yml`.

O contorno depende de um comportamento documentado (deploy ignorado por watch paths; "Deploy Latest Commit" como saída). O runbook deve mandar conferir, no primeiro push depois do `apply`, que o deploy apareceu como ignorado; se não aparecer, o plano B é o botão **Disable** de auto-deploy nas configurações do serviço.

Dois deploys acontecem mesmo com `auto_deploy = false`: o provider reimplanta o serviço quando uma variável muda (L4), e conectar o repositório no primeiro `apply` pode disparar o deploy inicial.

## 8. Proxy, HTTPS e cabeçalhos (Issue #97)

A borda da Railway termina TLS (1.2 e 1.3, certificado Let's Encrypt automático) e fala HTTP com o contêiner. Requisição pública em HTTP na porta 80 é redirecionada para HTTPS pela própria borda.

Cabeçalhos que a borda envia, pela especificação pública: `X-Real-IP` (IP do cliente), `X-Forwarded-Proto` (sempre `https`), `X-Forwarded-Host`, `X-Railway-Edge`, `X-Request-Start` e `X-Railway-Request-Id`. A conta oficial da Railway confirmou em 10/10/2026 que a borda **sobrescreve** `X-Forwarded-For`, `X-Real-IP`, `X-Forwarded-Proto` e `X-Forwarded-Host` vindos do cliente, e que **não publica** faixa de IP do proxy.

Consequências para a aplicação:

- **Esquema:** `X-Forwarded-Proto`, para o cookie `Secure`, os links absolutos e o redirecionamento HTTPS.
- **IP do cliente:** `X-Real-IP` (é o cabeçalho documentado; `X-Forwarded-For` não está na especificação). O middleware de ForwardedHeaders aceita trocar o nome do cabeçalho de origem. O rate limit do login depende disso.
- **Proxies conhecidos:** como não há faixa publicada, as listas de proxies e redes conhecidas ficam vazias e o limite de saltos é 1. A confiança se apoia na topologia: o contêiner só recebe tráfego da borda e da rede privada do ambiente, onde só existem serviços nossos, e nenhum serviço tem TCP proxy. Se um serviço de terceiro entrar no ambiente, isso é revisto.
- **HSTS** fora de `Development`, sem `preload` enquanto o domínio for `*.up.railway.app`.
- **Redirecionamento HTTPS** fora de `Development`, com as rotas de saúde de fora (7.4).
- **Testes da Issue #97:** um `X-Real-IP` forjado pelo cliente é coberto por teste, e `X-Forwarded-For` e `X-Forwarded-Host` vindos do cliente são ignorados pela aplicação.
- **Limite dessa confiança:** ela depende da topologia da Railway, em que a aplicação só é alcançável pela borda, que sobrescreve esses cabeçalhos. Isso ainda **não foi verificado em ambiente real**; a conferência entra no primeiro deploy.
- Tudo entra por `Composition/`, sem editar `Program.cs`.

## 9. Chaves do Data Protection

Sem persistência, cada deploy gera chaves novas: o cookie do painel cai, os tokens antiforgery em formulário aberto falham e, a partir da Sprint 3, os links de convite e de redefinição de senha enviados por e-mail deixam de valer a cada deploy. O contêiner da Railway não guarda disco entre deploys.

| Opção | Custo | Risco |
|---|---|---|
| **A. Tabela no PostgreSQL com `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`** (recomendada) | pacote novo (Microsoft, mesmo trem 10.0.x do ASP.NET Core e do EF Core já usados) + uma migração; poucas linhas de código | chaves em texto no banco (mesma proteção e mesmo backup do resto dos dados) |
| B. Tabela no PostgreSQL com `IXmlRepository` próprio | sem pacote novo, mas a migração é a mesma; `Helinho.Infrastructure` não referencia o framework ASP.NET Core, então seria preciso uma referência nova ou uma porta em `Application` com adaptador em `Web`, mais testes próprios | código nosso em caminho sensível, refazendo o que o pacote oficial já faz |
| C. Volume da Railway no serviço web | US$ 0,15/GB/mês (centavos) | o volume é montado como root: imagem não-root precisa de `RAILWAY_RUN_UID=0`, o que desfaz o usuário não-root da Issue #94; serviço com volume fica indisponível a cada redeploy e não aceita réplicas; as chaves ficam fora do backup do banco |
| D. Não persistir | zero | login do painel cai a cada deploy; inviável quando houver e-mail com link (Sprint 3) |

**Recomendação: A.** É pacote novo e migração, e o contrato está congelado: entra por **Issue própria**, que acrescenta o pacote em `Directory.Packages.props` (versão confirmada no Context7 e auditada com `supply-chain-risk-auditor`), faz `AppDbContext` implementar `IDataProtectionKeyContext`, gera a migração da tabela `DataProtectionKeys` e registra `PersistKeysToDbContext` com `SetApplicationName` fixo. Cifrar as chaves em repouso (`ProtectKeysWith*`) fica para o ADR de produção.

## 10. Segredos, token e estado

### 10.1 Variáveis do serviço web

| Variável | Homologação | Sensível |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` | não |
| `PORT` | `8080` | não |
| `ConnectionStrings__Default` | referência ao `Postgres` (seção 6) | sim (o valor resolvido contém a senha do banco, mesmo sem senha literal no Terraform) |
| `Seed__Enabled` | `true` | não |
| `HELINHO_SEED_PASSWORD` | `TF_VAR_*`, só em homologação | sim |
| `Api__Auth__*` | padrões do README | não |
| `Email__Provider` | `Fake` | não |
| `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SenderName`, `App__PublicBaseUrl` | vazias (Sprint 3) | `Brevo__ApiKey` sim |
| `Ai__Provider` | `Fake` | não |
| `Ai__ApiKey`, `Ai__Model` | vazias (Sprint 4) | `Ai__ApiKey` sim |

As variáveis das Sprints 3 e 4 já nascem declaradas no Terraform com valor padrão vazio. O módulo só envia à Railway as que tiverem valor, para não depender de a API aceitar variável vazia. `App__PublicBaseUrl` pode ser derivada do domínio do serviço quando a Sprint 3 precisar.

Toda variável sensível é `sensitive = true` e vem de `TF_VAR_*` ou de `*.tfvars` fora do git. `ConnectionStrings__Default` é montada dentro do módulo, mas também é sensível: não vai para `output` sem `sensitive = true`, não aparece em log e não é copiada para exemplo. O `.gitignore` bloqueia `*.tfstate*`, `.terraform/` e `*.tfvars` (menos `*.tfvars.example`); o `.terraform.lock.hcl` é commitado.

### 10.2 Token

A Railway tem três tipos de token: de **conta** (tudo o que o usuário alcança), de **workspace** (um workspace) e de **projeto** (um ambiente de um projeto, enviado no cabeçalho `Project-Access-Token`). O provider autentica com `Authorization: Bearer` e a documentação dele pede token de conta ou de workspace; token de projeto não serve e, de todo modo, não poderia criar o projeto.

- Use **token de workspace**, criado no workspace da contratante. É o menor escopo que o provider aceita.
- O provider lê `RAILWAY_TOKEN`. A CLI da Railway usa esse mesmo nome para token de **projeto** e `RAILWAY_API_TOKEN` para conta ou workspace. O runbook deve mandar exportar `RAILWAY_TOKEN` só no shell do Terraform e nunca rodar a CLI nesse shell.
- Se o token enxergar mais de um workspace, `workspace_id` é obrigatório no `railway_project` (variável não sensível).
- O token nunca vai para arquivo, `tfvars`, log ou CI. O workflow `infra.yml` roda `init -backend=false`, `validate`, `fmt` e `tflint`, que não precisam dele.

### 10.3 Estado do Terraform

O estado guarda em texto a senha do banco, `HELINHO_SEED_PASSWORD` e, depois, as chaves de Brevo e de IA.

| Opção | Custo | Risco |
|---|---|---|
| **A. Local, fora do git, com backup cifrado** (recomendada) | zero | sem lock e sem histórico; perder o arquivo obriga a reimportar cerca de sete recursos ou a destruir e recriar a homologação (dado sintético) |
| B. HCP Terraform, plano gratuito (até 500 recursos), execução local | zero | conta e fornecedor novos guardando segredos do projeto, sem resposta da contratante sobre fornecedores permitidos (C14); migração do estado na entrega do código |
| C. Bucket compatível com S3 (bucket da própria Railway a US$ 0,015/GB/mês, ou outro) | centavos | o backend precisaria existir antes do Terraform que o criaria; credencial extra; configuração de endpoint S3 customizado |
| D. Estado no git | zero | segredos em texto no repositório; proibido |

**Recomendação: A**, como exceção registrada à regra da casa ("nunca use estado local"), válida enquanto houver um único operador e só homologação. Backup cifrado com `age` ou `gpg` depois de cada `apply`, guardado fora da máquina (o runbook dá o comando). Quando houver produção, segundo operador ou `apply` em CI, o estado vai para backend remoto com lock, em ADR próprio.

## 11. Observabilidade, rollback e backup

- **Logs:** JSON no console fora de `Development`, por configuração (Issue #98); sem PII, sem corpo de requisição, sem SQL e sem token. A Railway coleta o stdout; a retenção é de 7 dias no plano Hobby e 30 no Pro. `X-Railway-Request-Id` serve de correlação com os logs de rede.
- **Custo:** configurar no workspace o alerta de gasto por e-mail. O limite rígido derruba **todos** os serviços do workspace ao ser atingido, então é decisão do Igor.
- **Rollback de deploy:** no painel, aba Deployments do serviço → menu do deploy anterior → **Rollback**. Restaura a imagem e as variáveis daquele deploy. A imagem de um deploy removido fica guardada por 72 horas no Hobby e 120 no Pro; depois disso só resta **Redeploy**, que reconstrói a partir do código. Rollback não desfaz migração.
- **Backup do banco, dois níveis:**
  1. Backup de volume da Railway (aba Backups do serviço `Postgres`): manual ou agendado; diário guardado 6 dias, semanal 27, mensal 89. É incremental, cobrado como volume, e só restaura no mesmo projeto e ambiente. A agenda não é configurável pelo provider: é um clique no painel, descrito no runbook.
  2. `pg_dump` para fora da plataforma: `railway connect Postgres --tunnel-only` abre um túnel SSH para o banco privado, sem TCP proxy público, e o `pg_dump`/`pg_restore` local usa esse túnel.
- **Destruição:** volume apagado fica 48 horas recuperável; depois é definitivo.

O passo a passo de rollback, backup, restore, rotação e destruição é escrito em `docs/operations/railway-runbook.md` (Issue própria), não só aqui.

## 12. Decisões que esperam o Henrique

### D1. Topologia

| Opção | Custo mensal (ver `custos.md`) | Risco |
|---|---|---|
| **A. Só `homologacao`: um projeto, um ambiente, web + Postgres** (recomendada) | uso de cerca de US$ 4; fatura de US$ 5 no Hobby ou US$ 20 no Pro | nenhum dado real; produção fica para depois |
| B. `homologacao` + `producao` como dois ambientes do mesmo projeto | uso de cerca de US$ 16 | o provider só lê serviço e volume do ambiente padrão (L3); sem RBAC por ambiente fora do Enterprise |
| C. `homologacao` + `producao` em dois projetos, mesmo módulo | uso de cerca de US$ 16 | dois projetos para acompanhar no painel |

**Recomendação: A agora.** O código já nasce no formato de C (um projeto por ambiente), com `producao` desligada.

### D2. Estado do Terraform

Opções e riscos na seção 10.3. **Recomendação: local, fora do git, com backup cifrado.** É exceção à regra da casa e precisa do seu aceite.

### D3. Deploy contínuo

| Opção | Custo | Risco |
|---|---|---|
| **A. `auto_deploy = false`; você publica com "Deploy Latest Commit"** (recomendada) | um clique por publicação | homologação pode ficar atrás da `main` se você esquecer |
| B. `auto_deploy = true` com Wait for CI | zero cliques | toda migração mergeada roda sozinha em homologação; o `apply` deixa de ser o único momento em que a infraestrutura muda |
| C. Sem ligar o repositório; publicar com `railway up` da sua máquina | CLI instalada e logada | publica o que estiver no diretório local, não necessariamente o que foi mergeado |

**Recomendação: A.** B é o que a política da casa prevê para staging e pode ser ligado depois trocando uma variável.

### D4. Data Protection

Opções e riscos na seção 9. **Recomendação: tabela no PostgreSQL com o pacote oficial**, em Issue própria (pacote novo + migração).

### D5. Fato novo: `railway.toml` descontinuado e IaC nativo da Railway

Não estava entre as quatro decisões, mas muda o plano da sprint e você precisa saber:

1. **`railway.toml` não será criado** (seção 7.2). A Issue dele deixa de existir e o conteúdo vai para o Terraform.
2. A Railway lançou um **IaC próprio** (`.railway/railway.ts`, aplicado pela CLI com `railway config plan` e `apply`). Comparado ao provider comunitário:

| | Terraform + provider comunitário (mantido) | IaC nativo da Railway |
|---|---|---|
| Quem mantém | comunidade, versão 0.x | Railway, TypeScript em disponibilidade geral |
| PostgreSQL | serviço de imagem + volume (L1) | `postgres("postgres")` do template oficial |
| Estado com segredos | arquivo de estado para proteger | não tem arquivo de estado; segredos ficam na Railway (`preserve()`) |
| Token | workspace | de projeto, restrito a um ambiente |
| Cria projeto e ambiente | sim | não; atua em projeto e ambiente já existentes |
| Validação no CI sem segredo | `validate`, `fmt`, `tflint` | o `plan` precisa de token |
| Ferramentas no repositório | Terraform | Node, npm e o pacote `railway` em um repositório só de .NET |

**Recomendação: manter o Terraform nesta sprint.** É o objetivo que você definiu, o provider 0.6.3 cobre tudo o que a homologação precisa, e a validação roda no CI sem segredo. O IaC nativo é o caminho de saída se o provider quebrar ou parar de ser mantido, e deve ser reavaliado no ADR de produção.

## 13. Exceções à política da casa (`devops-standards`)

| Regra da casa | O que este ADR faz | Por quê |
|---|---|---|
| `apply` só no CI | `apply` só pelo Henrique, na máquina dele | um operador, um ambiente; o CI não guarda token da Railway |
| Nunca usar estado local | estado local com backup cifrado (D2) | seção 10.3 |
| Autenticação do CI por OIDC | não se aplica: o CI não autentica na Railway | o workflow `infra.yml` só valida |
| Uma imagem construída no CI e promovida pelo digest | a Railway constrói a imagem a partir do commit | há um único ambiente; não existe promoção |
| Staging publicado automaticamente após o merge | manual (D3) | controle e custo; reversível por variável |
| Diretório por ambiente no Terraform | um root em `infra/railway/` com um módulo por ambiente | só existe homologação; separa-se ao ligar produção |

As Issues #40 (pipeline de staging) e #41 (workflow de produção) foram escritas antes desta decisão e precisam ser revistas ou fechadas pelo Henrique.

## 14. Alternativas descartadas

| Alternativa | Por que não |
|---|---|
| Builder automático (Railpack) | seção 7.1 |
| `railway.toml` | descontinuado; serviço novo não pode aderir; deixa de ser lido em 01/12/2026 |
| IaC nativo da Railway nesta sprint | seção 12, D5 |
| Banco pelo template + `terraform import` | seção 6 |
| TCP proxy público no banco para facilitar o `pg_dump` | expõe o banco à internet e cobra egress; o túnel da CLI resolve |
| Imagem `postgres-ssl` do template da Railway | TLS dentro da rede privada, que já é cifrada; a imagem oficial é a mesma do compose e dos testes |
| Serverless (dormir quando ocioso) em homologação | primeira requisição lenta ou 502 para o desenvolvedor Unity; economia de poucos dólares |
| PR environments | cada PR subiria web + banco; custo sem necessidade |
| Volume para o Data Protection | seção 9 |
| HCP Terraform, bucket S3 ou estado no git | seção 10.3 |
| OpenTofu com estado cifrado | troca de ferramenta fora do escopo da sprint |
| Render ou Fly.io | a conta paga e o contato são da Railway (C04, C19) |

## 15. Impacto nas Issues da sprint

| Issue | O que este ADR acrescenta |
|---|---|
| #94 Dockerfile | escutar em `0.0.0.0:${PORT}` com `PORT=8080` vindo do Terraform; se a imagem final não tiver shell, validar no primeiro deploy que o pre-deploy command roda (seção 16) |
| #95 `migrate` | pre-deploy sem volume, com tempo limite de 300 s; seed impedido em produção também pelo Terraform |
| #96 `/health/ready` | responder 2xx por HTTP direto, com `Host: healthcheck.railway.app` |
| #97 ForwardedHeaders | IP do cliente em `X-Real-IP`; listas de proxy vazias; rotas de saúde fora do redirecionamento |
| #98 logs JSON | sem mudança |
| #99 Terraform | provider `= 0.6.3` + `hashicorp/random`; build e deploy no `railway_service`; mapa `ambientes`; `watch_patterns` para `auto_deploy`; `$${{` nas referências; variáveis vazias não enviadas |
| `railway.toml` | **cancelada** |
| Nova: Data Protection | pacote + migração (D4) |
| Runbook | conflito de nome `RAILWAY_TOKEN` com a CLI; agenda de backup e alerta de gasto são cliques no painel; conferência do deploy ignorado |

## 16. O que não foi possível confirmar em fonte

1. **Se conectar o repositório no primeiro `apply` dispara sozinho o deploy inicial**, e se esse deploy respeita o `watch_patterns`. O runbook trata os dois casos.
2. **Se "Deploy Latest Commit" ignora o `watch_patterns`.** A documentação indica esse comando como saída quando os watch paths bloqueiam um deploy, mas não afirma isso com todas as letras. Plano B: botão Disable.
3. **Se o pre-deploy command roda em shell ou em forma exec.** A documentação só diz isso do start command. Importa se a imagem final for sem shell.
4. **Se a requisição de healthcheck traz `X-Forwarded-Proto`.** Por isso as rotas de saúde ficam fora do redirecionamento.
5. **Como o provider se comporta com um segundo ambiente no mesmo projeto** além do que o código mostra (L3). Não foi executado nenhum `plan`.
6. **Se a API da Railway aceita variável com valor vazio.** O módulo não envia as vazias.
7. **Qual plano a conta da contratante tem** (Hobby ou Pro) e o que mais roda no workspace. Pergunta para o Igor; muda a fatura, não a arquitetura.
8. **Qual versão do PostgreSQL o template da Railway entrega hoje.** Irrelevante para a opção escolhida.
9. **Consumo real de CPU e memória.** As estimativas de custo são premissas, a medir na primeira semana.

## 17. Consequências

**Positivas**
- Um `apply` cria projeto, banco, serviço, variáveis e domínio; nenhum passo manual entre a criação do token e o primeiro deploy.
- Banco sem endereço público; segredo nenhum no repositório; senha do banco nunca digitada.
- Tudo o que define o deploy está em um lugar só (Terraform), revisado em PR e validado no CI sem segredo.
- Fatura previsível: no plano Pro a homologação cabe no uso já incluído.

**Negativas e riscos**
- Dependência de um provider comunitário 0.x em uma plataforma que agora investe no próprio IaC.
- O banco de homologação é operado por nós, não pelo template da Railway.
- Estado local com segredos em uma única máquina, protegido por disciplina de backup.
- `auto_deploy = false` usa um contorno que só será validado no primeiro deploy.
- Dados hospedados nos Estados Unidos: aceitável para dado sintético, pendente de decisão para dado real.
