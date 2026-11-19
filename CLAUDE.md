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
- **Hospedagem:** Railway, gerenciada por Terraform em `infra/` (ADR em `docs/architecture/ADR-0002-hospedagem-railway.md`; custos em `docs/operations/custos.md`). `terraform plan`, `apply`, deploy e criação de recurso, conta ou token são só do Henrique; agentes escrevem o código e rodam `fmt`, `init -backend=false`, `validate` e `tflint`. Dockerfile, `railway.toml` e Terraform entram cada um por Issue própria (o ADR-0002 registra que a Railway descontinuou o `railway.toml`: não crie esse arquivo sem decisão do Henrique).
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

## Design no sprint do MVP (protocolo reduzido — decisão do Henrique em 10/11/2026)

O protocolo completo de `.claude/rules/10-ui-design.md` (oito skills) **não se aplica neste sprint**. O front será refeito com o protocolo completo em um sprint futuro. Até lá, toda Issue ou PR que toca UI (`*.cshtml`, `Pages/`, `wwwroot/css`, componentes) segue o protocolo reduzido:

1. **`DESIGN.md` manda.** Use os tokens e componentes de `wwwroot/css/theme.css`; não invente paleta, tipografia nem espaçamento.
2. **`unslop-design`:** rode `python3 .claude/skills/unslop-design/scripts/scan.py <pasta alterada>` e corrija os sinais encontrados (ou justifique).
3. **`web-design-guidelines`:** auto-revisão dos arquivos de UI alterados — contraste, foco visível, labels, navegação por teclado, estados (vazio, erro, carregando) e responsividade.
4. **Registre na seção Design do PR, só o que foi feito de verdade:** "Protocolo reduzido do sprint — DESIGN.md: seções seguidas · unslop-design: N sinais → corrigidos/justificados · web-design-guidelines: itens verificados · verificação visual: não realizada (sem navegador)". Nunca registre skill que não rodou.

**Para a revisão (`review-pr`, `ui-reviewer`):** neste sprint, o registro reduzido acima **satisfaz** o protocolo de design. A ausência das outras skills (`ui-ux-pro-max`, `design-taste-frontend`, `impeccable`, `frontend-design`, `emil-design-eng`, `design-references`) e da verificação visual **não é bloqueante** e não precisa de decisão humana. Continuam bloqueantes: registro reduzido ausente, registro falso, tela que contradiz o `DESIGN.md`, e problemas de acessibilidade.
