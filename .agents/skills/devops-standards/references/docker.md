# Docker — imagens de produção

Objetivo: imagem pequena, reproduzível, sem segredos, rodando como usuário sem privilégios, construída **uma vez** no CI e promovida entre ambientes pelo mesmo digest (ver `ci-cd.md`).

## Regras da casa
1. **Multi-stage sempre**: estágio de build com SDK/toolchain; estágio final só com o runtime e o artefato.
2. **Usuário não-root** no estágio final (`USER` numérico, ex.: `10001`).
3. **Imagem base pinada por digest** (`image:tag@sha256:...`) e atualizada por Dependabot/Renovate — nunca `latest`.
4. **Nenhum segredo** em `ARG`, `ENV`, `COPY` ou camada. Segredo de build (token de registry privado) só via BuildKit `--mount=type=secret`.
5. **`HEALTHCHECK`** ou probe equivalente da plataforma apontando para `/health` (ver `observability.md`).
6. **`.dockerignore`** obrigatório — contexto de build mínimo.
7. **Scan com Trivy no CI**; vulnerabilidade CRITICAL com correção disponível reprova o build.
8. **docker compose só para dev local** (banco, fila, cache). Produção é responsabilidade do provedor (`terraform.md`, `paas.md`).

## Exemplos por família

### Node (NestJS, Next.js standalone)
```dockerfile
FROM node:lts-slim@sha256:<digest> AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build && npm prune --omit=dev

FROM gcr.io/distroless/nodejs-debian12@sha256:<digest>
WORKDIR /app
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
USER 10001
CMD ["dist/main.js"]
```
Next.js: use `output: "standalone"` e copie `.next/standalone`, `.next/static` e `public`. Distroless não tem shell — o health check fica na plataforma (probe HTTP), não em `HEALTHCHECK CMD curl`.

### .NET (ASP.NET Core)
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:<lts>@sha256:<digest> AS build
WORKDIR /src
COPY *.sln ./
COPY src/App.Api/App.Api.csproj src/App.Api/
COPY src/App.Application/App.Application.csproj src/App.Application/
RUN dotnet restore
COPY . .
RUN dotnet publish src/App.Api -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:<lts>-chiseled@sha256:<digest>
WORKDIR /app
COPY --from=build /out .
USER app
ENTRYPOINT ["dotnet", "App.Api.dll"]
```
Imagens `chiseled` já rodam sem root e sem shell. Copie cada `.csproj` para o seu caminho antes do `restore` (cache de camadas); liste todos os projetos referenciados.

### Python (FastAPI)
```dockerfile
FROM python:3-slim@sha256:<digest> AS build
WORKDIR /app
COPY pyproject.toml uv.lock ./
RUN pip install uv && uv sync --frozen --no-dev
COPY src ./src

FROM python:3-slim@sha256:<digest>
WORKDIR /app
COPY --from=build /app /app
ENV PATH="/app/.venv/bin:$PATH" PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
RUN useradd -u 10001 -r app
USER 10001
HEALTHCHECK CMD python -c "import urllib.request;urllib.request.urlopen('http://localhost:8000/health')"
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
```

### Go
```dockerfile
FROM golang:1@sha256:<digest> AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /out/app ./cmd/api

FROM gcr.io/distroless/static-debian12:nonroot@sha256:<digest>
COPY --from=build /out/app /app
USER nonroot
ENTRYPOINT ["/app"]
```

### Demais stacks
| Stack | Build | Runtime final | Observação |
|---|---|---|---|
| Spring (Java/Kotlin) | `eclipse-temurin:<lts>-jdk` + Maven/Gradle | `eclipse-temurin:<lts>-jre` ou distroless java | use layered jars (`java -Djarmode=tools`) para cache de camadas |
| Rust (axum) | `rust:<stable>` (cargo-chef para cache) | `distroless/cc` ou `static` com musl | `--release`, binário único |
| Laravel (PHP) | `composer` para dependências | `php:<ver>-fpm` + nginx (ou FrankenPHP) | `composer install --no-dev --optimize-autoloader`; `opcache` ligado |
| Flutter / React Native | não usa Docker para o app | — | builds mobile no Expo EAS / CI de lojas (`firebase-expo.md`) |
| Next.js estático | `node:lts` | nginx ou plataforma (Vercel/Firebase Hosting) | se não tem SSR, não precisa de container |

## Imagens mínimas: distroless vs alpine
| Opção | Prós | Contras |
|---|---|---|
| distroless / chiseled | sem shell nem gerenciador de pacotes, menor superfície | debug só com imagem `:debug` ou container efêmero |
| `-slim` (Debian) | compatível com glibc, fácil de depurar | maior |
| alpine | muito pequena | musl pode quebrar binários nativos (Python wheels, Prisma, .NET nativo); evite se não testou |

Padrão: **distroless/chiseled** quando a stack suporta; senão `-slim`.

## Segredos no build
```dockerfile
# syntax=docker/dockerfile:1
RUN --mount=type=secret,id=npm_token \
    NPM_TOKEN=$(cat /run/secrets/npm_token) npm ci
```
```bash
docker build --secret id=npm_token,env=NPM_TOKEN .
```
Nunca: `ARG NPM_TOKEN` (fica no histórico da imagem), `COPY .env`, `ENV API_KEY=...`. Configuração de runtime chega por variável de ambiente injetada pela plataforma (`environments.md`).

## `.dockerignore` mínimo
```
.git
.github
**/node_modules
**/bin
**/obj
**/.venv
**/__pycache__
**/target
.env
.env.*
!.env.example
docs
*.md
coverage
```

## Pinagem e atualização da base
```dockerfile
FROM node:lts-slim@sha256:3f2a...  # Dependabot atualiza tag e digest
```
`.github/dependabot.yml`:
```yaml
version: 2
updates:
  - package-ecosystem: docker
    directory: "/"
    schedule: { interval: weekly }
```
Cada atualização chega como PR `chore(deps):` próprio — nunca misturado com feature.

## Scan no CI (Trivy)
```yaml
- name: Scan de imagem
  uses: aquasecurity/trivy-action@<sha> # vX
  with:
    image-ref: ${{ env.IMAGE }}@${{ steps.build.outputs.digest }}
    severity: CRITICAL
    ignore-unfixed: true
    exit-code: "1"
```
Achado aceito conscientemente vai para `.trivyignore` com comentário (CVE, motivo, data de revisão) — revisado pelo `devops-reviewer`.

## Compose para dev local
- `compose.yaml` na raiz sobe dependências (Postgres, Redis, fila, mailpit). A aplicação pode rodar fora do compose com hot reload.
- Credenciais do compose são de desenvolvimento e ficam só no compose/`.env.example`; nunca reutilize em staging/produção.
- Nunca use compose como mecanismo de deploy.

## Anti-padrões
- `FROM ...:latest`, `apt-get upgrade` sem pinagem, `RUN curl ... | sh`.
- Imagem final com SDK/compilador, código-fonte ou testes.
- Processo como root; `chmod 777`.
- Uma imagem por ambiente (build de staging ≠ build de produção). Uma imagem, configuração diferente.

## Checklist de PR
- [ ] Multi-stage; estágio final sem SDK, fontes ou ferramentas de build.
- [ ] Imagens base pinadas por digest e cobertas pelo Dependabot/Renovate.
- [ ] Processo roda com usuário não-root.
- [ ] Nenhum segredo em `ARG`/`ENV`/`COPY`; segredos de build via `--mount=type=secret`.
- [ ] `.dockerignore` presente e exclui `.env`, `.git`, dependências locais.
- [ ] Health check definido (no Dockerfile ou na plataforma) apontando para `/health`.
- [ ] Trivy roda no CI e reprova CRITICAL com correção disponível.
- [ ] Compose usado só para dev local.
