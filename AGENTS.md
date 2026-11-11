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

## Sprint do MVP (regra temporária — até a demonstração de 10/11/2026)

- **Orçamento de tamanho: 800 linhas por PR** (o limite em que o CI reprova), em vez das 400 de `size:m`. Entre 400 e 800 linhas, abra o PR normalmente e registre o tamanho na seção Escopo; **não pare, não aplique `needs-human` e não crie sub-Issues por causa do tamanho**. Acima de 800, divida em dois PRs em sequência na mesma Issue-mãe (crie as sub-Issues e siga para a primeira), sem esperar decisão humana.
- **Revisão:** PR entre 400 e 800 linhas **não é achado bloqueante** neste sprint. Continua bloqueante PR que mistura propósitos.
- Migrações do EF Core (`**/Migrations/*.cs`) são código gerado e não contam no tamanho.
- Depois da demonstração esta seção é removida e volta a valer o orçamento de `size:m`.

## Design no sprint do MVP (protocolo reduzido — decisão do Henrique em 10/11/2026)

O protocolo completo de `.claude/rules/10-ui-design.md` (oito skills) **não se aplica neste sprint**. O front será refeito com o protocolo completo em um sprint futuro. Até lá, toda Issue ou PR que toca UI (`*.cshtml`, `Pages/`, `wwwroot/css`, componentes) segue o protocolo reduzido:

1. **`DESIGN.md` manda.** Use os tokens e componentes de `wwwroot/css/theme.css`; não invente paleta, tipografia nem espaçamento.
2. **`unslop-design`:** rode `python3 .claude/skills/unslop-design/scripts/scan.py <pasta alterada>` e corrija os sinais encontrados (ou justifique).
3. **`web-design-guidelines`:** auto-revisão dos arquivos de UI alterados — contraste, foco visível, labels, navegação por teclado, estados (vazio, erro, carregando) e responsividade.
4. **Registre na seção Design do PR, só o que foi feito de verdade:** "Protocolo reduzido do sprint — DESIGN.md: seções seguidas · unslop-design: N sinais → corrigidos/justificados · web-design-guidelines: itens verificados · verificação visual: não realizada (sem navegador)". Nunca registre skill que não rodou.

**Para a revisão (`review-pr`, `ui-reviewer`):** neste sprint, o registro reduzido acima **satisfaz** o protocolo de design. A ausência das outras skills (`ui-ux-pro-max`, `design-taste-frontend`, `impeccable`, `frontend-design`, `emil-design-eng`, `design-references`) e da verificação visual **não é bloqueante** e não precisa de decisão humana. Continuam bloqueantes: registro reduzido ausente, registro falso, tela que contradiz o `DESIGN.md`, e problemas de acessibilidade.
