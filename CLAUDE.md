# Projeto da AI Software House

Regras deste repositório (somam-se a `~/.claude/CLAUDE.md` e a `.claude/rules/`).

## Fluxo
- Toda tarefa vem de uma Issue com critérios de aceite. Uma Issue = uma branch = um PR.
- Implementação: skill `implement-issue`. Revisão: skill `review-pr` (dispara os subagentes code-reviewer, security-reviewer, qa-tester e, conforme o diff, ui-reviewer, seo-reviewer, compliance-reviewer e devops-reviewer).
- Padrões de código: skill `engineering-standards` + guia da stack. Arquitetura e nível (`simple`/`clean`/`clean+ddd`) estão no ADR em `docs/architecture/`.
- Ao terminar uma etapa, escreva `docs/handoff/<issue>.md` (estado, decisões, próximo passo, testes pendentes).

## Stack deste projeto
- **Linguagem e runtime:** C# / .NET 10 (LTS).
- **Framework:** ASP.NET Core, um único host: Razor Pages + Bootstrap (painel) e Minimal APIs sob `/api` (API do jogo Unity).
- **Banco:** PostgreSQL com EF Core (Npgsql); local via `compose.yaml`; testes com Testcontainers.
- **Identidade:** ASP.NET Core Identity; cookie no painel, token opaco Bearer na API.
- **Hospedagem:** Railway, gerenciada por Terraform em `infra/` (ADR em `docs/architecture/ADR-0002-hospedagem-railway.md`; custos em `docs/operations/custos.md`). `terraform plan`, `apply`, deploy e criação de recurso, conta ou token são só do Henrique; agentes escrevem o código e rodam `fmt`, `init -backend=false`, `validate` e `tflint`. Dockerfile e Terraform entram cada um por Issue própria. Não crie `railway.toml` (descontinuado pela Railway, ADR-0002 §7.2).
- **Nível de arquitetura:** `clean` (ADR em `docs/architecture/ADR-0001-stack.md`): `Helinho.Domain`, `Helinho.Application`, `Helinho.Infrastructure`, `Helinho.Web`; teste de arquitetura com NetArchTest.Rules.
- **Contexto obrigatório antes de implementar:** `docs/context/rotas-api.md` (contrato do jogo e as 8 regras do JsonUtility), `docs/context/propostas-contrato-api.md` (P1–P16) e `docs/product/PRD.md`.
- **Dados:** só dados sintéticos em seed, testes e exemplos. Nunca dado pessoal real.
- **Contrato congelado:** entidades, migração única e DTOs/rotas da API nascem nas Issues de abertura. Nenhuma outra Issue gera migração, muda DTO ou adiciona pacote; isso exige Issue própria com ADR.
- **Comandos:**
  - format: `dotnet format --verify-no-changes`
  - build (lint + typecheck): `dotnet build --no-restore -warnaserror`
  - test: `dotnet test --no-build`
  - dependências: `dotnet list package --vulnerable --include-transitive`

## Proibido
- Merge em main, deploy, `git push --force`, remover/pular testes para o CI passar, editar `.github/workflows` sem autorização explícita, ler `.env`/segredos.
- **Autorização vigente (Henrique, Sprint 1):** criar `.github/workflows/infra.yml` com `terraform fmt -check`, `terraform init -backend=false`, `terraform validate` e `tflint`. Esse workflow não roda `plan` nem `apply`, não lê segredo e só dispara em PR que toca `infra/**`. Nenhum outro workflow muda.

## Design (tema Recreio)

Vale o protocolo completo de `.claude/rules/10-ui-design.md`, com duas adaptações:

- **(a) `design-references` é dispensado.** A referência é a preview em `docs/design/recreio/preview/`, não galerias públicas.
- **(b) A preview manda sobre gosto pessoal.** `ui-ux-pro-max`, `design-taste-frontend`, `impeccable`, `frontend-design` e `emil-design-eng` servem para corrigir o que estiver quebrado ou fraco na preview, nunca para mudar a identidade. `unslop-design` e `web-design-guidelines` continuam obrigatórios.

**Verificação visual obrigatória em cada PR de tela:** capturas em 1440 px e 375 px, nos temas claro e escuro, comparadas com a preview. Sem navegador na sessão, registre isso no PR e peça a captura ao Henrique. Nunca marque verificação visual sem ter feito.

## Tema do painel (Recreio)
Antes de tocar em `*.cshtml`, `Pages/` ou `wwwroot/css|js`, leia `docs/design/TEMA-PARA-IA.md` e siga-o.
Tela, campo, gráfico ou animação nova seguem as receitas de `docs/design/GUIA-DE-EVOLUCAO.md`.
Não crie cor, tamanho, raio ou duração fora dos tokens de `wwwroot/css/theme.css`.
