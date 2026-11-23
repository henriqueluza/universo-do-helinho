# Handoff — integração da IA (Sprint 4, ADR-0004)

Rascunhos de questões por IA com revisão humana. As quatro pilhas paralelas (backend, tela do lote, criador e lista, trabalhador da fila) foram costuradas, as lacunas entre elas fechadas e o conjunto provado de ponta a ponta com o gerador Fake. **Nenhum PR aberto**: tudo é branch com push.

## 1. Branches

| O quê | Branch | Base |
|---|---|---|
| Integração (só merges) | `ia-integrada` | `issue-261-ia-sugestao-avulsa` + branch padrão + `issue-228-fila-trabalhador-5` + `issue-286-ia-tela-revisar` + `issue-289-ia-lista-selo-filtro` |
| Costura: relógio único | `issue-297-relogio-unico` | `ia-integrada` |
| Costura: tratador no trabalhador real | `issue-298-ia-tratador-no-trabalhador` | #297 |
| Costura: revisão pertence ao pedido | `issue-299-ia-revisao-pertence-ao-pedido` | #298 |
| Costura: lista com "Gerar com IA" e pendentes | `issue-300-ia-lista-gerar-e-pendentes` | #299 |
| Ponta a ponta (Issue 14 do ADR) | `issue-301-ia-ponta-a-ponta` | #300 |
| Testes de tela separados por fatia | `issue-303-ia-testes-por-fatia-1`, `-2`, `-3` (a `-3` é a branch final, com este handoff) | #301 |
| **Cadeia de entrega** (é dela que saem os PRs) | `entrega-ia-00-base` … `entrega-ia-22-ponta-a-ponta` | seção 6 |

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

## 6. Cadeia de entrega (uma fatia ≤ 400 linhas por PR, uma por vez, base na branch padrão)

`entrega-ia-00-base` = `issue-228-fila-trabalhador-5` + branch padrão (até o PR #302) + `refs/pull/120/head` (PR #120 final). Cada `entrega-ia-NN` é a anterior mais uma fatia; tamanho por `python3 scripts/pr-size.py <anterior>`; cada uma compilou com `-warnaserror`, passou no `dotnet format --verify-no-changes` e teve as classes de teste da fatia verdes antes do push. O corpo de cada PR está em `scratchpad/entrega-ia/NN.md` (caminho completo no relatório da sessão).

| NN | Branch | Commit de topo | Issue | Tamanho |
|---|---|---|---|---|
| 00 | `entrega-ia-00-base` | `6efbb0f` | — (ponto de partida, sem PR) | — |
| 01 | `entrega-ia-01-migracao` | `afb8b8f` | Closes #257 | 397 |
| 02 | `entrega-ia-02-dominio` | `bcb8d12` | Closes #263 | 246 |
| 03 | `entrega-ia-03-pedir-rascunhos` | `610db4e` | Closes #258 | 399 |
| 04 | `entrega-ia-04-tratador` | `b44e8b0` | Closes #259; Parte de #298 | 363 |
| 05 | `entrega-ia-05-leitura-pedido` | `40b1c84` | Closes #275 | 225 |
| 06 | `entrega-ia-06-revisao` | `8244712` | Closes #260 | 348 |
| 07 | `entrega-ia-07-sugestao-avulsa` | `fc8d93f` | Closes #261 | 326 |
| 08 | `entrega-ia-08-componentes-tema` | `48fba9c` | Closes #294; Parte de #286 | 100 |
| 09 | `entrega-ia-09-ai-edited-at` | `b7ebcb9` | Closes #287 | 107 |
| 10 | `entrega-ia-10-lote-acompanhar` | `9d5b80a` | Parte de #286 | 391 |
| 11 | `entrega-ia-11-lote-pedir` | `bee31b4` | Parte de #286 | 397 |
| 12 | `entrega-ia-12-lote-menu` | `3453018` | Parte de #286 | 104 |
| 13 | `entrega-ia-13-lote-editar` | `c402e9c` | Parte de #286; Parte de #299 | 349 |
| 14 | `entrega-ia-14-lote-decidir` | `8dedb6a` | Closes #286; Closes #299 | 223 |
| 15 | `entrega-ia-15-criador-sugerir` | `5f4eda4` | Parte de #288 | 366 |
| 16 | `entrega-ia-16-criador-sugerir-retorno` | `c6c619a` | Closes #288 | 101 |
| 17 | `entrega-ia-17-criador-salvar-origem` | `6d45522` | Closes #295 | 86 |
| 18 | `entrega-ia-18-lista-selo-filtro` | `02d725b` | Closes #289 | 240 |
| 19 | `entrega-ia-19-relogio-unico` | `487861d` | Closes #297 | 34 |
| 20 | `entrega-ia-20-tratador-no-trabalhador` | `631b988` | Closes #298 | 256 |
| 21 | `entrega-ia-21-lista-gerar-e-pendentes` | `d687dec` | Closes #300 | 120 |
| 22 | `entrega-ia-22-ponta-a-ponta` | topo da branch (traz este handoff) | Closes #301; Closes #303 | 281 |

**Antes da cadeia:** o PR #120 (chaves do Data Protection; a branch dele traz junto a fila `QueueJobs` da #237 e a #245) e as cinco fatias do trabalhador (#228). A IA não depende das fatias de e-mail. As fatias do adaptador (#251, repetição em 429/5xx) são independentes da cadeia.

**Como abrir cada PR:** quando o passo anterior estiver na branch padrão, crie a branch do PR a partir dela e aplique só a fatia: `git diff origin/entrega-ia-(NN-1) origin/entrega-ia-NN | git apply --index --3way`. Abrir o PR direto de `entrega-ia-NN` mostraria de novo as fatias anteriores (a base comum é antiga). A migração `20261123011145_AddAiQuestionDrafts` fica depois de `AddQueueJobs` e `AddStudentGuardianEmail` e **não** foi regenerada; o retrato do modelo aplicou sem conflito sobre o PR #120 final.

**Onde a ordem difere da pedida, e por quê:**
- A linha que une as opções de IA (parte da #298) entra no passo 04, com o tratador: é ali que o defeito passa a existir (o teste de `AiProviderTests` ficaria vermelho de 04 a 20).
- A #299 não tem passo próprio: a conferência "pertence ao pedido" está nos mesmos arquivos de página que os passos 13 e 14 trazem no estado final.
- No lote, "editar" (13) vem antes de "aprovar e descartar" (14), para o link Editar dos cartões nunca apontar para uma página inexistente.
- A #298 (20) vem depois do relógio (19) e da tela de pedir (11), que os testes dela usam; a #300 (21) depois da #289 (18).
- A #303 não tem passo próprio: os arquivos de teste já entram separados nos passos 10 a 16; o passo 22 a fecha.

**Árvore final:** `entrega-ia-22-ponta-a-ponta` tem a mesma árvore de `issue-303-ia-testes-por-fatia-3` mais o que a branch padrão (PR #302) e o PR #120 final trouxeram. Conferido com `git diff` (resultado no relatório da sessão).

## 7. Como as quatro fatias grandes foram cortadas

| Fatia original | Cortes na cadeia |
|---|---|
| `issue-286-ia-tela-rascunhos` (~488) | 08 (CSS e ícones, junto com a #294) + 10 (página do pedido, só leitura: `Pedido.cshtml`, `Pedido.cshtml.cs` e `_Cartao.cshtml` no estado daquela fatia) |
| `issue-286-ia-tela-pedir` (~473) | 11 (formulário) + 12 (menu, IA desligada) |
| `issue-286-ia-tela-revisar` (525) | 13 (editar) + 14 (aprovar e descartar; os três arquivos do pedido no estado final) |
| `issue-288-ia-sugerir-criador` (~455) | 15 (bloco e handler) + 16 (retorno no `painel.js` e os testes de confirmação, falhas e cota) |

Exigiu mexer em código só de teste (#303): `AiTelaAcompanharTests`, `AiTelaPedirTests`, `AiTelaRevisarTests` e `QuestoesSugerirIaTests` viraram classes parciais em dois arquivos; métodos movidos sem alteração, mesma contagem de testes (15, 7, 9 e 15).

## 8. Divergências do ADR-0004 que ficaram

1. **§14 "nenhum trabalhador de IA roda":** com `Off` a raia existe e consulta a fila (costura 2).
2. **§15 *polling* com JavaScript:** não há; o `<meta refresh>` de 5 s é o único caminho (decisão da #286). O handler `?handler=Status` também não existe.
3. **§15 "Sugerir" respondendo JSON e campo "Assunto" no criador:** não há (decisões da #288 e da #261).
4. **§9.4 marca "editada":** questão que nasce de sugestão editada na entrada não leva a marca (só edição posterior, por `AiEditedAt`). Se a contratante quiser, é uma linha em `ListQuestions` (sugestão com `Status == Edited`).
5. **§10.2 repetição em 429/5xx dentro do adaptador:** é a #251, fora desta cadeia.

## 9. O que ficou por fazer

- Abrir os 22 PRs na ordem da seção 6 (cada um passa por `review-pr`).
- Issue 13 do plano do ADR (purga das sugestões descartadas, D7) e `scripts/demo.sh` da Issue 14: sem Issue aberta.
- Trabalho de fila de tipo sem tratador registrado nunca expira nem é apagado (decisão da #228): com a raia da IA sempre registrada isso não ocorre para `ai.question-drafts`.
- Medir contraste com ferramenta; `qa-tester`, `docs-writer`, `static-analysis` e `insecure-defaults` na abertura dos PRs.

## 10. O que não rodou

A suíte completa (regra da máquina: uma classe por vez, pela trava). Rodaram verdes, por classe: todas as classes de teste tocadas em cada passo da cadeia (lista no relatório da sessão), mais `ArchitectureTests`, `JobWorkerTests`, `PanelTests`, `PainelQuestoesTests`, `QuestoesRecreioTests`, `QuestoesListaRecreioTests`, `TemaTelasCssTests` e `PainelSemStyleInlineTests` na branch final. `HospedagemTests` e `HealthTests` não foram rodados (eram os do PR #120, já corrigido na base da cadeia).
