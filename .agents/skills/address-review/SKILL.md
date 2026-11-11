---
name: address-review
description: Use quando um PR recebeu a label changes-requested da revisão automática, ou quando o usuário pedir para responder à revisão de um PR. Lê o comentário consolidado, corrige cada bloqueante na branch do PR, responde item a item e pede nova revisão com a label re-review. Não use para implementar uma Issue do zero (isso é implement-issue).
---

# Responder à revisão de um PR

Você é o **autor** respondendo ao revisor, como em um time: lê o que foi pedido, corrige, explica o que fez e devolve para nova revisão. Você não aprova o próprio PR nem faz merge.

1. **Leia a revisão.** `gh pr view <n> --json title,body,labels,headRefName,baseRefName` e `gh pr view <n> --comments`. O comentário consolidado começa com `<!-- review-pr -->` e numera os bloqueantes (`B1`, `B2`…). Leia também os comentários inline: `gh api repos/{owner}/{repo}/pulls/<n>/comments --jq '.[] | {path, line, body}'`. Leia a Issue (`Closes #N`) para lembrar os critérios de aceite.
2. **Esteja na branch do PR.** `gh pr checkout <n>` (se já não estiver) e `git pull`. Nunca crie outra branch nem outro PR para as correções.
3. **Classifique cada bloqueante** antes de mexer no código:
   - **corrigir** — o padrão. Bug, teste faltando, critério não atendido, arquitetura, segurança com correção conhecida, template/registro incompleto.
   - **discordar** — só com evidência concreta (o teste que já cobre, a linha do ADR, o critério da Issue). Discordância sem evidência não vale: corrija.
   - **depende de humano** — a correção exige decisão de produto/escopo/contrato, aceitar risco de segurança, mexer em segredo/dado real/infra, editar arquivo protegido (`.github/workflows`) ou uma exceção a regra da casa. Itens marcados **[humano]** pelo revisor caem aqui.
4. **Corrija**, um bloqueante por vez, com a correção mínima:
   - Bug ou critério não atendido: escreva primeiro o teste que falha, depois a correção.
   - Siga `engineering-standards` e o guia da stack; não mude o nível de arquitetura nem amplie o escopo do PR.
   - PR grande demais ou com propósitos misturados: siga o plano de divisão do revisor — reduza este PR à primeira fatia e crie as Issues de continuação (`Parte de #N`), como em `implement-issue`.
   - **Sugestões** (não bloqueantes): aplique só as triviais e seguras. O resto fica para depois; não transforme a rodada em refatoração.
   - Nunca remova, enfraqueça ou marque teste como skip para a revisão passar.
5. **Verifique.** Rode format, lint/typecheck, build e testes do guia da stack (os mesmos comandos do `implement-issue`) e `python3 scripts/pr-size.py`. Se a `main` avançou e há conflito: `git fetch origin && git merge origin/main` (nunca rebase nem force push).
6. **Commit e push.** Commits `fix(review): <o que foi corrigido> (B<n>)`, depois `git push`. Atualize a descrição do PR se as evidências mudaram (`gh pr edit <n> --body-file …`).
7. **Responda no PR** com um comentário "Resposta à revisão — rodada <k>" (k = quantas respostas suas já existem + 1), uma linha por bloqueante:
   `B1 — corrigido em <sha curto>: <o que mudou e qual teste cobre>` · `B2 — discordo: <evidência>` · `B3 — depende de humano: <qual decisão falta>`.
8. **Devolva para revisão** pelas labels:
   - Todos os bloqueantes corrigidos ou respondidos com evidência: `gh pr edit <n> --remove-label changes-requested --add-label re-review` (a label `re-review` dispara a nova revisão).
   - Sobrou item que depende de humano: `gh pr edit <n> --add-label needs-human` (mantenha `changes-requested`), explique no comentário qual decisão falta e pare. Corrija antes tudo o que não depende dela.

## Limites
- **Nunca** aplique `review:approved` (só o revisor aprova), nunca faça merge, nunca edite `.github/workflows`.
- No máximo 3 rodadas por PR. O controle é da automação (`has-pr-to-fix.sh`): depois da terceira resposta sem aprovação, o PR vai para `needs-human`.
- Um PR por execução. Ao terminar, pare.
