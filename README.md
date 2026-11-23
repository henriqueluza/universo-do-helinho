# Universo do Helinho

Painel administrativo (Razor Pages + Bootstrap) e API do jogo Unity (Minimal APIs sob `/api`) num único host ASP.NET Core, em C# / .NET 10.

> **Aviso:** todos os dados (seed, testes, exemplos) são **sintéticos**; não há dado pessoal real. O único ambiente hospedado é a **homologação**, publicada só pelo Henrique (seção [Deploy](#deploy)); produção está desligada.

## Pré-requisitos

- .NET 10 SDK (versão fixada em `global.json`)
- Docker (PostgreSQL via `compose.yaml` e Testcontainers nos testes)
- `curl` e `jq` para a demonstração da API

## Como rodar

```bash
cp .env.example .env     # defina POSTGRES_PASSWORD; nunca commite o .env
bash scripts/demo.sh     # sobe o banco, aplica a migração, roda o seed e inicia o sistema
```

Painel em `http://localhost:5018`; API em `/api`. Saúde: `GET /health` (o processo está vivo; não consulta o banco) e `GET /health/ready` (200 se o banco responde, 503 se não; teto de 3 s), ambas anônimas. Passo a passo da apresentação, logins do seed e chamadas à API: [`docs/demo/roteiro-apresentacao.md`](docs/demo/roteiro-apresentacao.md).

Logs: fora de `Development` o console sai em JSON, uma linha por evento, com timestamp em UTC e sem escopos (`Logging:Console` em `appsettings.json`); em `Development` continua o formato legível. SQL do EF Core e requisições do ASP.NET Core só aparecem a partir de `Warning`.

## Imagem Docker

O `Dockerfile` da raiz gera a imagem que a hospedagem constrói (multi-stage, usuário sem privilégios, sem SDK). O processo escuta em `0.0.0.0:$PORT` (8080 se `PORT` não existir) e repassa argumentos ao binário (`seed`).

```bash
docker build -t helinho-web .
docker compose up -d --wait db     # banco local publicado em 127.0.0.1:5432
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__Default="Host=host.docker.internal;Port=5432;Database=helinho;Username=helinho;Password=$POSTGRES_PASSWORD" \
  helinho-web                      # acrescente `migrate` ao final para aplicar as migrações
curl http://localhost:8080/health
```

`host.docker.internal` vale no Docker Desktop. Segredos entram só por variável de ambiente em tempo de execução; nada vai para a imagem.

## Configuração do banco: `migrate`, `seed` e `Seed__Enabled`

Aplicar migração e gravar dados sintéticos são passos separados. Os dois comandos encerram o processo ao terminar, sem subir o servidor:

| Comando | O que faz | Código de saída |
|---|---|---|
| `dotnet run --project src/Helinho.Web -- migrate` | Aplica as migrações. Com `Seed__Enabled=true`, roda o seed em seguida; sem a flag, não toca em dados | `0` |
| `dotnet run --project src/Helinho.Web -- seed` | Uso local: aplica as migrações e roda o seed, mas só com `Seed__Enabled=true`; sem a flag, recusa e não grava nada | `0`, ou `1` quando recusado |

`Seed__Enabled` (chave `Seed:Enabled`) vem **desligada por padrão**: um ambiente que só define a string de conexão nunca recebe dados sintéticos. Ela é ligada em `appsettings.Development.json` e pelo `scripts/demo.sh`, então o fluxo local não muda. O seed é idempotente (rodar de novo não duplica dados) e exige `HELINHO_SEED_PASSWORD`. No deploy, `migrate` é o comando de pré-deploy.

## Configuração do login da API

`POST /api/login` aceita matrícula, usuário ou e-mail e devolve um token opaco Bearer (256 bits; só o hash SHA-256 fica no banco). Falha de credencial responde `401 invalid_credentials` (uniforme); aluno não aprovado, admin e master respondem `403 forbidden`. Chaves opcionais da seção `Api:Auth` (em `appsettings` ou variável de ambiente com `__`, ex.: `Api__Auth__TokenLifetime`):

| Chave | Padrão | Efeito |
|---|---|---|
| `TokenLifetime` | `08:00:00` | Validade do token (sem refresh) |
| `MaxFailedAttempts` | `5` | Tentativas erradas seguidas até bloquear a conta (Identity) |
| `LockoutDuration` | `00:15:00` | Duração do bloqueio |
| `LoginRateLimit` | `10` | Requisições a `/api/login` por IP por janela |
| `LoginRateWindow` | `00:01:00` | Janela do limite de taxa |

## Envio de e-mail: `Fake` ou Brevo

O padrão é `Email__Provider=Fake`: nenhum e-mail sai e o log registra só o fato do envio. Com `Email__Provider=Brevo`, o envio usa a API transacional da Brevo e a aplicação **não sobe** se faltar alguma destas variáveis (a mensagem de erro nomeia a variável, nunca o valor):

| Variável | Regra com `Brevo` |
|---|---|
| `Brevo__ApiKey` | obrigatória; segredo, só por variável de ambiente |
| `Brevo__SenderEmail` | obrigatório; e-mail válido de um remetente verificado na Brevo |
| `Brevo__SenderName` | obrigatório; até 70 caracteres |
| `App__PublicBaseUrl` | obrigatória; URL `https` absoluta, sem caminho nem query |

Qualquer outro valor em `Email__Provider` também derruba a subida. Em `Development` o provedor é sempre `Fake`, mesmo com `Email__Provider=Brevo` no ambiente; a aplicação avisa no log que ignorou a configuração. Decisões no ADR-0003 (`docs/architecture/ADR-0003-emails-brevo.md`).

## Atrás do proxy (Railway): HTTPS, HSTS e IP do cliente

A Railway termina o TLS e entrega a requisição em HTTP ao contêiner. Fora de `Development` (`ASPNETCORE_ENVIRONMENT` diferente de `Development`) a aplicação sempre:

- redireciona HTTP para HTTPS (307), **menos** as rotas sob `/health`, que o healthcheck da plataforma chama em HTTP puro;
- envia `Strict-Transport-Security: max-age=31536000` nas respostas HTTPS;
- marca todo cookie como `Secure` (painel, antiforgery, TempData).

Para ela enxergar o esquema e o IP reais, ligue a seção `Proxy` (variável de ambiente com `__`):

| Chave | Variável de ambiente | Padrão | Efeito |
|---|---|---|---|
| `Proxy:TrustForwardedHeaders` | `Proxy__TrustForwardedHeaders` | `false` | Confia em `X-Forwarded-Proto` (esquema) e no cabeçalho de IP abaixo. **Ligue (`true`) na Railway**; nunca onde a aplicação é alcançável sem passar pelo proxy |
| `Proxy:ClientIpHeader` | `Proxy__ClientIpHeader` | `X-Real-IP` | Cabeçalho de onde sai o IP do cliente (limite de taxa do login). Só o último valor é usado |

- Fora de `Development` com `Proxy:TrustForwardedHeaders` desligado, toda requisição parece HTTP e entra em laço de redirecionamento: é o sinal de que a chave faltou.
- Não use `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: ela liga o middleware do framework lendo `X-Forwarded-For`, que a Railway não documenta.
- A porta do redirecionamento é 443; `ASPNETCORE_HTTPS_PORT` a substitui.
- `AllowedHosts` (chave do ASP.NET Core, hoje `*`) pode restringir os hosts aceitos; se for definida, inclua `healthcheck.railway.app`, o host do healthcheck.
- O serviço não deve ter TCP Proxy público, e os demais serviços do mesmo projeto Railway alcançam o contêiner pela rede privada sem passar pela borda: eles são confiáveis por definição.

## Geração de questões por IA: `Off`, `Fake` ou `Anthropic`

A IA sugere rascunhos de questões e a equipe decide (ADR-0004). **O padrão é desligada.** A seção `Ai` é validada na subida: com valor inválido a aplicação não sobe, e a mensagem cita a variável, nunca o valor.

| Chave | Variável de ambiente | Padrão | Efeito |
|---|---|---|---|
| `Ai:Provider` | `Ai__Provider` | `Off` | `Off` (ou vazio): IA desligada. `Fake`: gerador fictício, para demonstração. `Anthropic`: provedor real. Outro valor não sobe |
| `Ai:ApiKey` | `Ai__ApiKey` | vazio | Chave do provedor. Obrigatória com `Anthropic`. Segredo: só por variável de ambiente |
| `Ai:Model` | `Ai__Model` | vazio | Identificador do modelo. Obrigatório com `Anthropic`; não há modelo fixo no código |
| `Ai:TimeoutSeconds` | `Ai__TimeoutSeconds` | `90` | Prazo de uma geração, de 5 a 300 segundos |
| `Ai:DailyQuotaPerAdmin` | `Ai__DailyQuotaPerAdmin` | `60` | Sugestões que uma pessoa pode pedir em 24 horas |
| `Ai:DailyGlobalLimit` | `Ai__DailyGlobalLimit` | `300` | Sugestões que todos, somados, podem pedir em 24 horas |

- Em `Development` o provedor real nunca é usado: `Anthropic` vira `Fake`, com um aviso no log.
- Com a IA desligada, a porta `IQuestionDraftGenerator` resolve um gerador que recusa toda chamada.

## Deploy

A homologação roda na Railway e é descrita pelo Terraform de `infra/railway/`. **Só o Henrique** cria token, roda `terraform plan` e `apply` e publica; agentes e o CI não fazem deploy.

- Passo a passo (token, `apply`, primeiro deploy, rollback, backup, rotação e destruição): [`docs/operations/railway-runbook.md`](docs/operations/railway-runbook.md)
- Envio real de e-mails (conta Brevo, domínio, remetente, chave de API e variáveis na Railway): [`docs/operations/brevo-runbook.md`](docs/operations/brevo-runbook.md)
- O que está no ar, URLs e usuários sintéticos de teste (sem senha): [`docs/operations/ambientes.md`](docs/operations/ambientes.md)
- Decisão de hospedagem e custos: `docs/architecture/ADR-0002-hospedagem-railway.md` e `docs/operations/custos.md`

Publicar uma versão: merge na `main` e, no painel da Railway, serviço `helinho-web` → `Cmd+K` → **Deploy Latest Commit**. Push na `main` não publica sozinho. Produção está desligada.

## Como testar

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build --no-restore -warnaserror
dotnet test --no-build                 # integração usa Testcontainers (Docker ligado)
dotnet list package --vulnerable --include-transitive
```

## Estrutura de pastas

| Caminho | Conteúdo |
|---|---|
| `src/Helinho.Domain` | Entidades e regras de negócio, sem dependência de framework |
| `src/Helinho.Application` | Casos de uso e portas |
| `src/Helinho.Infrastructure` | EF Core/Npgsql, Identity, adaptadores |
| `src/Helinho.Web` | Host: Razor Pages do painel e Minimal APIs em `/api` |
| `tests/Helinho.UnitTests`, `IntegrationTests`, `ArchitectureTests` | Testes (NetArchTest.Rules valida as camadas) |
| `docs/` | Arquitetura, contexto, produto, design, demo, operações e handoffs |
| `scripts/` | Scripts de apoio (demo, tamanho de PR) |
| `compose.yaml` | PostgreSQL local |

## Documentação

- Arquitetura: [`docs/architecture/ADR-0001-stack.md`](docs/architecture/ADR-0001-stack.md)
- Produto: [`docs/product/PRD.md`](docs/product/PRD.md)
- Contrato da API e propostas P1–P16: [`docs/context/`](docs/context/) (`rotas-api.md`, `propostas-contrato-api.md`, `premissas-integracao.md`)
- Sistema visual do painel: [`docs/design/DESIGN.md`](docs/design/DESIGN.md)
- Regras de trabalho do repositório: [`CLAUDE.md`](CLAUDE.md)

## Fluxo de trabalho

Uma Issue = uma branch = um PR pequeno. O contrato (entidades, migração, DTOs e rotas) é congelado; mudá-lo exige Issue própria com ADR.
