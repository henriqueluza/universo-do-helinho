# Execução não assistida (sempre ativo)

Vale quando a sessão foi aberta por automação: variável `AISH_UNATTENDED=1`, prompt do supervisor/dispatch/pr-keeper, ou `exec:cloud`. Ninguém vai responder antes do fim da execução; parar e esperar é o pior resultado. Em sessão aberta à mão por um humano, pergunte normalmente.

- **Decisão reversível, decida.** Nome, layout, escolha técnica dentro do ADR, ambiguidade pequena em critério de aceite: escolha a opção mais conservadora, registre em "Decisões tomadas sem humano" (PR e handoff) e siga.
- **`needs-human` é só para o que não se desfaz ou não é seu:** escopo ou contrato pago, segredo ou dado real, deploy, aceitar risco de segurança, arquivo protegido (`.github/workflows`), exceção a regra da casa. Mesmo aí, entregue antes tudo o que não depende da decisão e diga em uma frase qual decisão falta.
- **Falha de ambiente nunca para o trabalho.** Docker fora do ar ou somente leitura, rede, disco, ferramenta ou MCP ausente: não tente consertar a máquina (há uma equipe de manutenção para isso) e não aplique `needs-human`. Garanta format e build limpos, faça commit e push e abra o PR dizendo exatamente o que não rodou localmente; o CI verifica. Teste que não rodou na máquina por falha de ambiente pode ser enviado, desde que o PR diga isso.
- **Sempre faça push antes de encerrar.** A worktree é apagada quando a sessão termina; commit só local é trabalho perdido. Incompleto vai como PR draft com o que falta.
- **Trabalho maior que o orçamento não para.** Entregue a primeira fatia coerente no PR (`Parte de #N`, sem `Closes`); crie as Issues de continuação já com `agent:authorized`, `status:ready`, as mesmas labels `exec:*`/`area:*` e as linhas `Depende de #N` corretas (a última leva o `Closes` da Issue original); acrescente `Depende de #<continuações>` no corpo da Issue original e troque nela `status:in-progress` por `blocked` — ela volta sozinha quando as partes fecharem.
- **Dependência aberta:** aplique `blocked` (não `needs-human`) e pare; a fila remove `blocked` quando as dependências fecham.
- **Continue de onde parou.** Se a branch remota `issue-<N>-*` já existir, continue nela.
- **Mesma falha duas vezes:** não repita a abordagem. Use o subagente `debugger` e mude de estratégia.
