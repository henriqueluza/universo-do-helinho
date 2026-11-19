# Universo do Helinho

Painel administrativo (Razor Pages + Bootstrap) e API do jogo Unity (Minimal APIs sob `/api`) num único host ASP.NET Core, em C# / .NET 10.

> **Aviso:** todos os dados (seed, testes, exemplos) são **sintéticos**; não há dado pessoal real. O **deploy é da fase final** do projeto: este repositório roda apenas localmente.

## Pré-requisitos

- .NET 10 SDK (versão fixada em `global.json`)
- Docker (PostgreSQL via `compose.yaml` e Testcontainers nos testes)
- `curl` e `jq` para a demonstração da API

## Como rodar

```bash
cp .env.example .env     # defina POSTGRES_PASSWORD; nunca commite o .env
bash scripts/demo.sh     # sobe o banco, aplica a migração, roda o seed e inicia o sistema
```

Painel em `http://localhost:5018`; API em `/api`. Passo a passo da apresentação, logins do seed e chamadas à API: [`docs/demo/roteiro-apresentacao.md`](docs/demo/roteiro-apresentacao.md).

## Configuração do login da API

`POST /api/login` aceita matrícula, usuário ou e-mail e devolve um token opaco Bearer (256 bits; só o hash SHA-256 fica no banco). Falha de credencial responde `401 invalid_credentials` (uniforme); aluno não aprovado, admin e master respondem `403 forbidden`. Chaves opcionais da seção `Api:Auth` (em `appsettings` ou variável de ambiente com `__`, ex.: `Api__Auth__TokenLifetime`):

| Chave | Padrão | Efeito |
|---|---|---|
| `TokenLifetime` | `08:00:00` | Validade do token (sem refresh) |
| `MaxFailedAttempts` | `5` | Tentativas erradas seguidas até bloquear a conta (Identity) |
| `LockoutDuration` | `00:15:00` | Duração do bloqueio |
| `LoginRateLimit` | `10` | Requisições a `/api/login` por IP por janela |
| `LoginRateWindow` | `00:01:00` | Janela do limite de taxa |

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
| `docs/` | Arquitetura, contexto, produto, design, demo e handoffs |
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
