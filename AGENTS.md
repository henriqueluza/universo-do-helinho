# Projeto da AI Software House

Regras deste repositório (somam-se a `~/.codex/AGENTS.md`; os padrões detalhados estão em `.agents/skills/engineering-standards`).

## Fluxo
- Toda tarefa vem de uma Issue com critérios de aceite. Uma Issue = uma branch = um PR.
- Implementação: skill `implement-issue`. Revisão: skill `review-pr` (no Codex, execute as revisões de código, segurança e QA em sequência).
- Padrões de código: skill `engineering-standards` + guia da stack. Arquitetura e nível (`simple`/`clean`/`clean+ddd`) estão no ADR em `docs/architecture/`.
- Ao terminar uma etapa, escreva `docs/handoff/<issue>.md` (estado, decisões, próximo passo, testes pendentes).

## Stack deste projeto
- **Linguagem e runtime:** C# / .NET 10 (LTS).
- **Framework:** ASP.NET Core, um único host: Razor Pages + Bootstrap (painel) e Minimal APIs sob `/api` (API do jogo Unity).
- **Banco:** PostgreSQL com EF Core (Npgsql); local via `compose.yaml`; testes com Testcontainers.
- **Identidade:** ASP.NET Core Identity; cookie no painel, token opaco Bearer na API.
- **Hospedagem:** deploy planejado para a fase final. Não crie Dockerfile de produção, CD nem configuração de provedor sem Issue própria.
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
