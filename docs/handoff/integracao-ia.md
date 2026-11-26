# Handoff — integração da IA (Sprint 4, ADR-0004)

Rascunhos de questões por IA com revisão humana. As quatro pilhas paralelas (backend, tela do lote, criador e lista, trabalhador da fila) foram costuradas, as lacunas entre elas fechadas e o conjunto provado de ponta a ponta com o gerador Fake. A entrega é feita em **8 PRs de bloco** contra a branch padrão (seção 6). As branches citadas nas seções 1, 2, 3 e 7 são de trabalho: ficam como histórico da integração e não entram na branch padrão.

## 1. Branches de trabalho (histórico)

| O quê | Branch | Base |
|---|---|---|
| Integração (só merges) | `ia-integrada` | `issue-261-ia-sugestao-avulsa` + branch padrão + `issue-228-fila-trabalhador-5` + `issue-286-ia-tela-revisar` + `issue-289-ia-lista-selo-filtro` |
| Costura: relógio único | `issue-297-relogio-unico` | `ia-integrada` |
| Costura: tratador no trabalhador real | `issue-298-ia-tratador-no-trabalhador` | #297 |
| Costura: revisão pertence ao pedido | `issue-299-ia-revisao-pertence-ao-pedido` | #298 |
| Costura: lista com "Gerar com IA" e pendentes | `issue-300-ia-lista-gerar-e-pendentes` | #299 |
| Ponta a ponta (Issue 14 do ADR) | `issue-301-ia-ponta-a-ponta` | #300 |
| Testes de tela separados por fatia | `issue-303-ia-testes-por-fatia-1`, `-2`, `-3` (a `-3` foi a branch final da integração) | #301 |
| **Cadeia de entrega** (22 passos, agrupados em 8 blocos; os PRs saem dos blocos) | `entrega-ia-00-base` … `entrega-ia-22-ponta-a-ponta` | seção 6 |

## 2. Conflitos dos merges de `ia-integrada` e como foram resolvidos

| Merge | Arquivo | Resolução |
|---|---|---|
| branch padrão | `Infrastructure/Ai/AnthropicQuestionDraftGenerator.cs`, `AnthropicQuestionDraftGeneratorTests.cs`, `AiExtensions.cs`, `docs/handoff/249.md` | lado da branch padrão (as versões antigas das fatias #250 e #251 que a pilha carregava saíram; elas voltam pelas branches delas). `docs/handoff/250.md` e `251.md` antigos foram removidos |
| branch padrão | `Composition/Areas/Ia.cs`, `AiProviderTests.cs` | lado da branch padrão para o adaptador, mais os registros dos casos de uso e do tratador da pilha |
| branch padrão | `QuestionDraftValidator.cs` | fica `ListStoredSuggestionStatementsAsync` da pilha |
| branch padrão | `Helinho.Infrastructure.csproj`, `README.md` | ficam os dois pacotes (Anthropic e DataProtection); a linha do README sobre o SDK é da #250 e saiu |
| trabalhador (#228) | `Composition/Areas.cs` | todas as áreas: `AddEmail`, `AddFila`, `AddIa` |
| tela do lote | — | sem conflito |
| criador e lista | `theme.css` | uma cópia de `.btn-ai`, `.tag-ai`, `.note.ai`; seletores próprios de cada frente mantidos; `.ai-progress` (sem uso) saiu |
| criador e lista | `_Icones.cshtml` | o merge automático deixou **dois** `i-ai` (sem marcar conflito); ficou o da prévia |
| criador e lista | `TemaRecreioTests.cs` | um teste de guarda com as duas regras (seletor de IA só em `components`; toda linha com `var(--ai` é de elemento de IA), mais declaração única e ausência de `.ai-progress`: mais estrito que cada lado |
| criador e lista | `PanelIconesTests.cs`, `PainelScriptTests.cs` | lista com `i-ai` e `i-edit`; marcadores das duas frentes |

Depois da integração a branch padrão recebeu o PR #302 (registro do adaptador, a #250). A cadeia de entrega já parte dela; os conflitos foram só linhas de `using` em `Ia.cs` e `AiProviderTests.cs` (ficam as dos dois lados).

## 3. Costuras

| # | Havia defeito? | O que mudou |
|---|---|---|
| 1. Tratador no trabalhador real (#298) | **Sim, um.** Raia, política (2 tentativas, lote 1, posse de 150 s; `BatchSize × ItemTimeout ≤ Lease` de 5 a 300 s) e escopo sem usuário estavam certos. O defeito: tratador e porta liam duas instâncias de `AiOptions`, e com o trabalhador resolvendo o tratador na subida a regra de ambiente avisava duas vezes (`AiProviderTests` vermelho em `ia-integrada`) | `IOptions<AiOptions>` devolve a instância do monitor (`Composition/Areas/Ia.cs`); `AiTrabalhadorTests` (5) com o `IJobProcessor` real |
| 2. IA desligada (#298) | Não é defeito, é **divergência do ADR §14** ("nenhum trabalhador de IA roda"), mantida | Sem mudança. A #228 monta uma raia por tratador registrado e `AddAreas()` não recebe configuração: tirar a raia exigiria mudar o contrato do tratador ou editar `Program.cs`. Com `Off` a raia só consulta a fila e encerra com `ai_disabled` (código que o §10.2 lista) o que tiver sobrado; nenhum gerador de verdade é chamado (teste) |
| 3. `ReviewAiDrafts` e o `jobId` (#299) | Sim, pequeno: sugestão de outro pedido era decidida | As páginas conferem (404) antes de chamar o caso de uso; assinatura do caso de uso intacta |
| 4. Pendentes e lista (#300) | Lacuna: o ADR §15 e a prévia pedem o botão "Gerar com IA" e o aviso `.note.ai`; cada frente deixou para a outra | `PendingAiDrafts.CountAsync` (Application) e a marcação na lista; o aviso leva ao pedido mais antigo com pendente |
| 5. Dois relógios (#297) | Risco, não defeito em produção | `SystemClock` lê o `TimeProvider` registrado: uma fonte só. Nos testes basta trocar o `TimeProvider` (`ManualTime`) |
| 6. Fatias acima de 400 | — | Seção 7 |

## 4. Ponta a ponta (#301, `AiPontaAPontaTests`, um host, gerador Fake, `IJobProcessor` real, sem `Task.Delay`)

| Cenário | Resultado |
|---|---|
| Pedir 5 → processar → pronto com 5 → aprovar 2, editar 1, descartar 2 → 3 questões de origem IA | verde |
| `GET /api/quiz/random`: `no_questions` antes de aprovar; depois só as 2 aprovadas; contrato conferido | verde |
| Cota por administrador e limite global, com a mensagem certa | verde |
| `Ai:Provider=Off`: 404, menu, "Gerar com IA" e "Sugerir" ausentes, selo mantido | verde |
| "Sugerir" → preenchido → salvar → origem IA; aspas, `&` e `<` codificados; `<script>` descartado | verde |

## 5. Verificação visual (feita)

Chromium do cache do Playwright, app com `--no-launch-profile`, Postgres em contêiner próprio (removido no fim), `Ai__Provider=Fake`, trabalhador ligado. 1440 e 375 px, claro e escuro. Capturas em `~/Trabalho/Universo-do-Helinho/capturas-recreio/ia/` (fora do repositório).

- **Lote com o trabalhador rodando:** "Na fila para gerar" → "Gerando as sugestões" → 5 cartões, só pela atualização automática da página (`lote-trabalhador-1..3-*`).
- **Criador:** inicial com o aviso de demonstração, sugerindo (`aria-busy`, "Sugerindo…"), sugerido, confirmação, falha, cota (`criador-1..6-*`); "Sugerir" acionado por teclado; foco visível (contorno de 3 px).
- **Lista:** selo "IA" e "IA · editada", filtro Origem aplicado só por teclado, "Gerar com IA" e aviso de pendentes (`lista-*`).
- Sem rolagem horizontal em nenhuma captura; nenhum erro de console. Contraste conferido a olho, sem ferramenta.
- Observações, sem correção: em 375 px o selo fica na coluna "Status", que rola dentro da tabela (a lista já era assim); em captura de página inteira a barra fixa do criador aparece sobre o formulário (efeito da captura). Nenhum defeito corrigido nesta etapa.

## 6. Cadeia de entrega (8 PRs de bloco, um por vez, base na branch padrão)

A cadeia tem 22 passos (branches de trabalho `entrega-ia-01` a `entrega-ia-22`, cada uma a anterior mais um passo). Os passos são entregues em **blocos**: um PR por bloco, contra a branch padrão, com o diff acumulado dos passos do bloco. Esta tabela não cita commit nem número de PR, que mudam a cada merge por squash: o PR de cada bloco é o que fechou as Issues da linha (campo "Development" da Issue).

| Bloco | Passos | O que entra | Issues |
|---|---|---|---|
| 1 | 01–02 | migração única `AddAiQuestionDrafts`; regras e transições do domínio | Closes #257, #263 |
| 2 | 03–05 | pedir rascunhos (cota, pedido, fila); tratador do trabalho; leitura do pedido | Closes #258, #259, #275; Parte de #298 |
| 3 | 06–07 | revisão humana das sugestões; sugestão avulsa | Closes #260, #261 |
| 4 | 08–09 | componentes de IA do tema; `AiEditedAt` | Closes #294, #287; Parte de #286 |
| 5 | 10–14 | tela do lote: acompanhar, pedir, menu, editar, aprovar e descartar | Closes #286, #299 |
| 6 | 15–17 | criador: "Sugerir", retorno no `painel.js`, salvar com origem IA | Closes #288, #295 |
| 7 | 18–21 | lista (selo e filtro Origem); relógio único; tratador no trabalhador real; "Gerar com IA" e pendentes | Closes #289, #297, #298, #300 |
| 8 | 22 | ponta a ponta e os handoffs da integração | Closes #301, #303 |

**Antes da cadeia (pré-requisitos na branch padrão):** as chaves do Data Protection (PR #120), a fila `QueueJobs` e os contratos do tratador (#223 e #234), o trabalhador (#228, cuja última fatia, o PR #331, entra antes do bloco 1) e o PR #306 (#305): `HealthTests.Falha_inesperada_na_consulta_vira_503_sem_detalhe` quebrava a subida do host assim que um tratador da fila passa a existir, o que acontece no passo 04; a correção é só de teste e já está na branch padrão. A repetição em 429/5xx do adaptador (#251) é independente da cadeia e já foi entregue. A IA não depende das fatias de e-mail.

**Migração:** `20261123011145_AddAiQuestionDrafts` fica depois de `AddQueueJobs` e `AddStudentGuardianEmail` e não foi regenerada. É a única migração da cadeia.

**Handoffs:** cada `docs/handoff/<issue>.md` entra no bloco que fecha (ou avança) a Issue; Issue com mais de um passo traz a tabela "Entrega em fatias".

**Onde a ordem difere do plano do ADR, e por quê:**
- A linha que une as opções de IA (parte da #298) entra no passo 04 (bloco 2), com o tratador: é ali que o defeito passa a existir.
- A #299 não tem passo próprio: a conferência "pertence ao pedido" está nos arquivos de página dos passos 13 e 14 (bloco 5).
- No lote, "editar" (13) vem antes de "aprovar e descartar" (14), para o link Editar dos cartões nunca apontar para uma página inexistente.
- A #298 (passo 20) vem depois do relógio (19) e da tela de pedir (11), que os testes dela usam; a #300 (21) depois da #289 (18).
- A #303 não tem passo próprio: os arquivos de teste entram separados nos passos 10 a 16 (blocos 5 e 6); o bloco 8 a fecha.

## 7. Como as quatro fatias grandes foram cortadas

| Fatia original | Cortes na cadeia |
|---|---|
| `issue-286-ia-tela-rascunhos` (~488) | 08 (CSS e ícones, junto com a #294) + 10 (página do pedido, só leitura: `Pedido.cshtml`, `Pedido.cshtml.cs` e `_Cartao.cshtml` no estado daquela fatia) |
| `issue-286-ia-tela-pedir` (~473) | 11 (formulário) + 12 (menu, IA desligada) |
| `issue-286-ia-tela-revisar` (525) | 13 (editar) + 14 (aprovar e descartar; os três arquivos do pedido no estado final) |
| `issue-288-ia-sugerir-criador` (~455) | 15 (bloco e handler) + 16 (retorno no `painel.js` e os testes de confirmação, falhas e cota) |

Exigiu mexer em código só de teste (#303): `AiTelaAcompanharTests`, `AiTelaPedirTests`, `AiTelaRevisarTests` e `QuestoesSugerirIaTests` viraram classes parciais em dois arquivos; métodos movidos sem alteração, mesma contagem de testes (15, 7, 9 e 15).

Com a entrega por bloco, os dois cortes de cada fatia chegam juntos ou em blocos vizinhos (blocos 4 e 5 para a primeira linha; bloco 5 para a segunda e a terceira; bloco 6 para a quarta). As branches da coluna "Fatia original" são de trabalho.

## 8. Divergências do ADR-0004 que ficaram

Cada uma é decisão registrada no bloco em que nasce e tem Issue de continuação; mudar qualquer uma passa pela Issue.

1. **§14 "nenhum trabalhador de IA roda":** com `Off` a raia existe e consulta a fila (costura 2; bloco 7). Continuação: #339.
2. **§15 *polling* com JavaScript:** não há; o `<meta refresh>` de 5 s é o único caminho, e o handler `?handler=Status` não existe (decisão da #286, bloco 5). Continuação: #340.
3. **§15 "Sugerir" respondendo JSON e campo "Assunto" no criador:** não há (decisões da #288 e da #261, blocos 6 e 3). Continuação: #341.
4. **§9.4 marca "editada":** questão que nasce de sugestão editada na entrada não leva a marca (só edição posterior, por `AiEditedAt`); o comportamento é fixado por `AiPontaAPontaTests` (bloco 8). Continuação: #342.

A repetição em 429/5xx dentro do adaptador (§10.2) não é divergência: foi entregue pela #251, fora desta cadeia.

## 9. O que ficou por fazer

- Purga das sugestões descartadas (Issue 13 do plano do ADR-0004, depende da D7): #337.
- `scripts/demo.sh` com a IA (a outra metade da Issue 14 do plano; a #301 o deixa fora): #338. O script existente sobe o sistema sem `Ai__Provider`, isto é, com a IA desligada.
- Divergências do ADR-0004 (seção 8): #339, #340, #341 e #342.
- Contraste de dois pares que o tema já tinha e ficam abaixo do mínimo (letra branca sobre `--ok` em `.rcard ol li.right b`, 3,7:1; borda de reforço de `.rcard:hover`): #343.
- Issues abertas da IA fora desta cadeia: #229 (variáveis de IA no Terraform) e #256 (limite de gasto vira `ProviderUnavailable`).
- Trabalho de fila de tipo sem tratador registrado nunca expira nem é apagado (decisão da #228): com a raia da IA sempre registrada isso não ocorre para `ai.question-drafts`.

## 10. O que não rodou

A suíte completa não rodou localmente (regra da máquina: uma classe por vez, pela trava); o CI roda em cada PR. `static-analysis` e `insecure-defaults` como skills separadas não rodaram. Rodaram verdes, por classe: as classes de teste tocadas em cada passo da cadeia (a lista está no handoff de cada Issue), mais `ArchitectureRules`, `JobWorkerTests`, `PanelTests`, `PainelQuestoesTests`, `QuestoesRecreioTests`, `QuestoesListaRecreioTests`, `TemaTelasCssTests` e `PainelSemStyleInlineTests` na branch final da integração. No topo da cadeia rodaram também `AiProviderTests` (11), `HospedagemTests` (8), `JobWorkerTests` (6), `ArchitectureRules` (4) e `HealthTests` (6, com a correção do PR #306). Como só rodaram as classes tocadas, outro teste que quebre um serviço na subida pode ter o mesmo problema da #305: só a suíte completa do CI mostra.
