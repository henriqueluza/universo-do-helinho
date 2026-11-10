# PRs pequenos e de propósito único

Um PR deve poder ser revisado com atenção em 10–15 minutos. Acima de ~400 linhas, a qualidade da revisão despenca: o revisor passa a aprovar sem ler.

## Regras
1. **Um propósito por PR.** Cada PR é exatamente um destes tipos (declarado no título no formato Conventional Commits):
   | Tipo | Exemplo | Pode conter |
   |---|---|---|
   | `feat` | `feat(reservas): confirmar reserva paga` | uma fatia de comportamento + seus testes |
   | `fix` | `fix(pagamentos): idempotência no webhook` | a correção + teste que reproduz o bug |
   | `refactor` | `refactor(reservas): extrair política de cancelamento` | mudança de estrutura **sem** mudança de comportamento |
   | `test` | `test(reservas): cobrir expiração` | só testes |
   | `chore`/`build`/`ci` | `chore: atualizar dependências do NestJS` | dependências, configuração, CI |
   | `docs` | `docs: ADR-0002 fila de e-mails` | só documentação |
   Misturou dois tipos? São dois PRs.
2. **Orçamento de tamanho** (linhas adicionadas + removidas, sem contar arquivos gerados, lockfiles e `.md` — ver `.github/pr-size-ignore`):
   | Label da Issue | Orçamento | Observação |
   |---|---|---|
   | `size:xs` | ≤ 50 | ajuste pontual |
   | `size:s` | ≤ 150 | o tamanho ideal |
   | `size:m` | ≤ 400 | limite de um PR de agente |
   | `size:l` | — | **não é implementável**: deve ser dividida antes do Gate 3 |
   O CI (`pr-size`) avisa acima de 400 e **reprova acima de 800**. Só um humano pode aplicar `size:exception` (ex.: scaffold gerado por CLI do framework).
3. **Testes vêm no mesmo PR do código que testam.** Nunca "PR de feature agora, testes depois".
4. **Sem carona.** Nada de reformatar arquivo vizinho, renomear coisas fora do escopo ou "aproveitar para atualizar a lib". Viu algo para melhorar? Crie uma Issue.
5. **Cada PR deixa `main` verde e utilizável.** Funcionalidade incompleta fica atrás de feature flag ou sem rota exposta, nunca quebrada.

## Como fatiar (da forma que o desenvolvimento deve ser)

**Prefira fatias verticais finas**: cada PR entrega um comportamento ponta a ponta, por menor que seja.
```
Épico "Reservas"
  #1 feat: criar reserva (domínio + caso de uso + endpoint POST + testes)       size:m
  #2 feat: listar reservas do usuário (GET + paginação)                        size:s   Depende de #1
  #3 feat: cancelar reserva com política de prazo                              size:m   Depende de #1
  #4 feat(app): tela de nova reserva                                           size:m   Depende de #1
  #5 feat(app): tela de minhas reservas                                        size:s   Depende de #2
```

**Quando a fatia vertical ainda é grande**, divida em camadas, cada uma com testes e mergeável sozinha:
```
  #1a feat: entidade Reserva e regras de domínio (só domínio + testes unitários)
  #1b feat: caso de uso CriarReserva com repositório (porta + adapter + teste de integração)  Depende de #1a
  #1c feat: endpoint POST /reservas (validação, autorização, teste de API)                     Depende de #1b
```

**Técnicas que mantêm PRs pequenos**
- **Refactor preparatório:** se a feature exige mudar estrutura existente, primeiro um PR `refactor` sem mudança de comportamento, depois o `feat`.
- **Feature flag:** para algo que só faz sentido completo (fluxo de checkout), mergeie as partes com a flag desligada.
- **Expand/contract em banco:** PR 1 adiciona coluna nova (nullable) e escreve nos dois lugares; PR 2 migra dados; PR 3 passa a ler da nova; PR 4 remove a antiga. Nunca migração destrutiva junto com código novo.
- **Mudança mecânica separada:** renomeação ampla, formatação, bump de dependência — cada uma no seu PR, sem lógica junto.
- **Contrato primeiro:** em integração front/back, um PR com o contrato (DTOs/OpenAPI/tipos) e depois back e front em paralelo.

## Dependências entre Issues
- Toda Issue que depende de outra tem no corpo uma linha exatamente assim: `Depende de #<número>` (uma por dependência).
- O `dispatch-next.sh` não despacha uma Issue enquanto qualquer dependência estiver aberta. Não use branches empilhadas: cada PR parte de `main` atualizada depois do merge da dependência.

## Se o trabalho crescer no meio da implementação
1. Pare antes de passar do orçamento. Rode `python3 scripts/pr-size.py` para medir.
2. Se o que já está feito é uma fatia coerente, verde e mergeável: abra o PR só com ela e crie Issues para o restante (`Depende de #<esta>`), sem `agent:authorized`.
3. Se não é coerente: não abra PR. Crie as sub-Issues propostas, comente o plano de divisão na Issue original e aplique `needs-human`.

## Descrição do PR
Primeira linha da seção "Escopo": **"Este PR faz uma coisa: …"**. Se a frase precisar de "e", provavelmente são dois PRs.
