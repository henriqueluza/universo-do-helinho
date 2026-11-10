# project-template — AI Software House

Template de todo projeto novo. `scripts/setup-project.sh <nome>` (no repositório ai-software-house) cria o repositório a partir daqui e configura labels, Dependabot e o ruleset `protect-main`.

## O que vem pronto

| Caminho | Para quê | Carrega na nuvem? |
|---|---|---|
| `CLAUDE.md` / `AGENTS.md` | Regras do projeto (Claude Code / Codex) | sim |
| `.claude/rules/00-engineering.md` | Princípios sempre ativos (curto) | sim |
| `.claude/rules/lang-*.md` | Regras por linguagem, carregam só ao mexer em arquivos daquela linguagem | sim |
| `.claude/skills/engineering-standards/` | Clean Architecture, SOLID, patterns, testes + 10 guias de stack | sim |
| `.claude/skills/devops-standards/` | Docker, CI/CD, ambientes, Terraform (AWS/Azure/GCP), Railway/Render/Fly, Firebase/Expo EAS, observabilidade | sim |
| `.claude/skills/implement-issue`, `review-pr`, `update-docs` | Fluxo de trabalho dos agentes | sim |
| `.claude/skills/owasp-security`, `security-review`, `differential-review`, `fp-check`, `supply-chain-risk-auditor` | Segurança | sim |
| `.claude/skills/frontend-design`, `web-design-guidelines`, `emil-design-eng` | Design de UI | sim |
| `.claude/agents/` | Subagentes de revisão e apoio (revisão, segurança, QA, UI, documentação, debug, DevOps) — um arquivo por papel | sim |
| `.claude/settings.json` + `.claude/hooks/` | Deny rules + hook que bloqueia force push, push em main, `gh pr merge`, `rm -rf` crítico | sim |
| `.mcp.json` | Context7 (docs de frameworks), Playwright (navegador), Figma (designs) | sim* |
| `.agents/skills/` | Espelho das skills para o Codex (gerado, não edite à mão) | — |
| `scripts/pr-size.py` + `.github/workflows/pr-size.yml` | Mede o PR (aviso > 400, falha > 800 linhas; ignora gerados/lock/docs via `.github/pr-size-ignore`). `size:exception` só por humano | — |
| `.github/workflows/review-pr.yml` | Roda a skill `review-pr` automaticamente em todo PR (Claude Code Action, autenticada com o segredo `CLAUDE_CODE_OAUTH_TOKEN` da assinatura; federação disponível comentada no workflow). Pula drafts e PRs com label `skip-review`/`kit`; push novo não re-revisa — aplique `re-review` | — |

\* Figma usa login OAuth — funciona nas sessões locais depois que você autentica; na nuvem pode não autenticar.

## Fluxo de um projeto

```
architecture-discovery → project-bootstrap (ADR + PRD + Issues) → sprint-planning (Gate 3)
→ dispatch automático → implement-issue (+ qa-tester) → PR
→ review-pr automático no GitHub Actions (code-reviewer ‖ security-reviewer ‖ qa-tester ‖ ui-reviewer)
→ comentário consolidado (+ label needs-human se bloqueante) → você faz o merge
```

## Tutorial: primeiro dia de um projeto novo

1. **Criado o repositório**, abra-o no Orca e confira que o `CLAUDE.md` tem a seção "Stack deste projeto" preenchida pelo `project-bootstrap`.
2. **Ative o CI da stack:** abra `.claude/skills/engineering-standards/references/stacks/<stack>.md`, seção 9, e cole os steps no job `validate` de `.github/workflows/validate.yml` (substituindo o aviso). Faça isso como primeira Issue do projeto.
3. **Aprove os MCPs** na primeira sessão (o Claude Code pergunta antes de usar servidores do `.mcp.json`). Para Figma, rode `/mcp` e autentique.
4. **Context7 (opcional):** sem chave funciona com limite menor. Para mais cota, exporte `CONTEXT7_API_KEY` no seu shell (nunca commite a chave).
5. A partir daí, tudo segue pelo fluxo de Issues.

## Deploy

Agentes nunca fazem deploy (o hook bloqueia deploy, `apply` e `submit`). Depois do **seu** merge em `main`, o CI publica em **staging**; **produção** usa o mesmo artefato e só sai quando **você** dispara o workflow `deploy-production` (botão "Run workflow") com o digest validado em staging — o mesmo workflow serve de rollback. (Em repositório privado, aprovação obrigatória em Environments exige GitHub Enterprise; o disparo manual funciona em qualquer plano.) Detalhes: `.claude/skills/devops-standards/`.

## PRs pequenos

Cada Issue vira **um PR de propósito único** (feat, fix, refactor, test, chore ou docs) com orçamento pela label: `size:xs` ≤ 50, `size:s` ≤ 150, `size:m` ≤ 400 linhas. `size:l` não é implementável — é dividida em várias Issues ligadas por `Depende de #N`, e o dispatch só começa uma Issue quando as dependências fecharam. Se o trabalho crescer no meio, o agente abre o PR só com a fatia coerente e cria Issues de continuação. Guia completo: `.claude/skills/engineering-standards/references/small-prs.md`.

Para liberar um PR grande legítimo (ex.: scaffold gerado por CLI), aplique você mesmo a label `size:exception` no PR — o hook impede agentes de aplicá-la.

## Como os agentes usam os guias de stack

1. O agente lê os manifestos (`package.json`, `*.csproj`, `go.mod`, `pubspec.yaml`, ...) e abre o guia da stack.
2. Lê o **nível de arquitetura** no ADR: `simple` (CRUD/sites), `clean` (regras de negócio reais) ou `clean+ddd` (pagamentos, domínio rico).
3. Implementa seguindo o guia; patterns só com problema presente, citado no PR.
4. Antes do PR, roda os comandos de qualidade (seção 9) e o checklist (seção 10).

Stacks cobertas: .NET · Spring (Java/Kotlin) · Go · NestJS · React/Next.js · React Native/Expo · Flutter · FastAPI · Rust · Laravel.

## Manutenção do kit

- Fonte canônica do kit: **este repositório**.
- Editou algo em `.claude/skills/`? Rode `scripts/sync-agents-mirror.sh` (o CI falha se o espelho do Codex estiver desatualizado).
- Para levar uma versão nova do kit a um projeto que já existe, use `scripts/sync-kit.sh <repo>` no ai-software-house — ele abre um PR no projeto.
- Novas stacks: crie `references/stacks/<stack>.md` com as mesmas 10 seções, adicione a linha na tabela do `SKILL.md` e uma `rules/lang-*.md` se for linguagem nova.
