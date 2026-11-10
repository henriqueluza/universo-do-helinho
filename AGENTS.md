# Projeto da AI Software House

Regras deste repositório (somam-se a `~/.codex/AGENTS.md`; os padrões detalhados estão em `.agents/skills/engineering-standards`).

## Fluxo
- Toda tarefa vem de uma Issue com critérios de aceite. Uma Issue = uma branch = um PR.
- Implementação: skill `implement-issue`. Revisão: skill `review-pr` (no Codex, execute as revisões de código, segurança e QA em sequência).
- Padrões de código: skill `engineering-standards` + guia da stack. Arquitetura e nível (`simple`/`clean`/`clean+ddd`) estão no ADR em `docs/architecture/`.
- Ao terminar uma etapa, escreva `docs/handoff/<issue>.md` (estado, decisões, próximo passo, testes pendentes).

## Stack deste projeto
<!-- preenchido pelo project-bootstrap: linguagem, framework, banco, nível de arquitetura, comandos de lint/test/build -->

## Proibido
- Merge em main, deploy, `git push --force`, remover/pular testes para o CI passar, editar `.github/workflows` sem autorização explícita, ler `.env`/segredos.
