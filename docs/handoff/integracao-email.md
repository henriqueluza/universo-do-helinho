# Integração da onda de e-mail (ADR-0003, onda 2): cadeia de entrega

A fila e os fluxos de e-mail foram feitos em seis pilhas paralelas, juntados e provados de ponta a ponta numa branch de prova (`issue-293-email-ponta-a-ponta`) e depois reorganizados numa **cadeia linear de 30 fatias**, uma por PR, cada uma com base na branch padrão e até 400 linhas. Este arquivo entra com a última fatia e registra a cadeia, as reconciliações feitas no caminho e o que ficou por fazer.

## A cadeia

| Fatia | Branch | Issue | Tamanho (contra a anterior) |
|---|---|---|---|
| 01 | `entrega2-01-fila-tabela` | Parte de #223 | 279 |
| 02 | `entrega2-02-fila-porta` | Closes #223 | 223 |
| 03 | `entrega2-03-fila-contratos-tratador` | Closes #234 | 139 |
| 04 | `entrega2-04-email-responsavel` | Closes #235 | 377 |
| 05 | `entrega2-05-trabalhador-1` | Parte de #228 | 190 |
| 06 | `entrega2-06-trabalhador-2` | Parte de #228 | 393 |
| 07 | `entrega2-07-trabalhador-3` | Parte de #228 | 217 |
| 08 | `entrega2-08-trabalhador-4` | Parte de #228 | 121 |
| 09 | `entrega2-09-trabalhador-5` | Closes #228 | 376 |
| 10 | `entrega2-10-tratador-1` | Closes #270 | 391 |
| 11 | `entrega2-11-tratador-2` | Closes #271 | 339 |
| 12 | `entrega2-12-tratador-3` | Closes #272 | 289 |
| 13 | `entrega2-13-webhook-1` | Closes #277 | 217 |
| 14 | `entrega2-14-webhook-2` | Closes #265 | 400 |
| 15 | `entrega2-15-acesso-aluno-1` | Parte de #267 | 380 |
| 16 | `entrega2-16-acesso-aluno-2` | Parte de #267 | 400 |
| 17 | `entrega2-17-acesso-aluno-3` | Parte de #267 | 317 |
| 18 | `entrega2-18-acesso-aluno-4` | Parte de #267 | 368 |
| 19 | `entrega2-19-acesso-aluno-5` | Closes #267 | 52 |
| 20 | `entrega2-20-costura` | Closes #291 | 388 |
| 21 | `entrega2-21-email-troca-senha-aluno` | Closes #292 | 71 |
| 22 | `entrega2-22-ponta-a-ponta-aluno` | Parte de #293 | 247 |
| 23 | `entrega2-23-portao-184` | portão: PR #276 (#184), sem PR próprio | — |
| 24 | `entrega2-24-redefinicao-1` | Closes #278 | 247 |
| 25 | `entrega2-25-redefinicao-2` | Closes #279 | 373 |
| 26 | `entrega2-26-redefinicao-3` | Closes #280 | 176 |
| 27 | `entrega2-27-convite-1` | Closes #266 | 343 |
| 28 | `entrega2-28-convite-2` | Closes #268 | 398 |
| 29 | `entrega2-29-convite-3` | Closes #285 | 269 |
| 30 | `entrega2-30-ponta-a-ponta-convite-redefinicao` | Closes #293 | 69 |

**O portão.** As fatias 01 a 22 não dependem do PR #276 (#184, e-mail repetido). Da 24 em diante (redefinição, convite e o resto do teste de ponta a ponta) o #276 tem de estar na branch padrão: a `entrega2-23-portao-184` é a fatia 22 com o #276 mesclado, só para as seguintes compilarem e serem testadas; ela não vira PR.

**O convite é o trecho que pode esperar.** A fatia 27 tira a senha manual do cadastro de administrador. A cadeia é válida se parar em qualquer ponto antes dela; a 30 (convite e redefinição de ponta a ponta) depende da 27.

## Reconciliações feitas ao montar a cadeia

| Onde | O que | Fatia |
|---|---|---|
| `Composition/Areas.cs` | uma linha por pilha, ficam todas (`AddFila`, `AddEmailTratador`, `AddBrevoWebhook`, `AddRedefinicaoDeSenha`, `MapBrevoWebhook`) | 09, 14, 25 |
| `PersistenceTests` | lista de migrações: quatro, na ordem Data Protection, `AddQueueJobs`, `AddStudentGuardianEmail` | 04 |
| Tratador × trabalhador | o tratador lia `IOptions<EmailOptions>`; com o trabalhador resolvendo os tratadores na subida, o aviso "Brevo ignorado" saía duas vezes. Lê pelo monitor | 11 |
| Teste que dependia da ordem | `AcessoAlunoTelaTests.Ficha_oferece…` usa um aluno próprio (`NeverSent`) | 16 |
| #184 × redefinição | `/recuperar-senha` fica com a página que enfileira; o teste de recuperação de `EmailRepetidoTests` confere a fila | 25 |
| #184 × convite | uma só recusa de e-mail já usado, a da #184 (`EmailInUseAsync`, `EmailInUse`); saiu `PanelEmailInUseAsync`. Passa a olhar qualquer conta, não só as do painel | 27 |
| Revisão do PR #304 | `QueueJobsTableTests` entrou na fatia 01; o teste de esquema idêntico saiu de `JobQueueTests` | 01, 02 |

Nenhuma migração nova, nenhum pacote novo, nenhuma mudança em DTO ou rota de `/api`, `Program.cs` intacto. As migrações são as dos PRs #237 e #245, sem regenerar.

## Diferença para a árvore provada (`issue-293-email-ponta-a-ponta`)
No topo da cadeia, `git diff issue-293-email-ponta-a-ponta` em `src` e `tests` mostra só:
- o que a branch padrão trouxe depois (adaptador da Anthropic e os testes dele);
- o #120 final (`WithoutGssNegotiation`, `HospedagemTests` com `migrate`, `HealthTests`, `DataProtectionKeysTests`);
- `QueueJobsTableTests` e o teste de esquema fora de `JobQueueTests` (revisão do PR #304);
- uma linha de comentário em `JobQueue.cs` (cancelar libera a chave de idempotência);
- o comentário do aluno `NeverSent` em `AcessoAlunoTelaTests`, numa linha só (para a fatia 16 caber em 400 linhas).

## Verificação

- Cada fatia: `dotnet format --verify-no-changes`, `dotnet build --no-restore -warnaserror` e as classes de teste dela, pela trava (`testar.sh`, uma classe por vez). A lista de cada uma está no handoff e no corpo do PR da fatia.
- Topo da cadeia: `EmailPontaAPontaTests` 4 de 4 (convite, redefinição, acesso do aluno, troca do e-mail do responsável), `EmailCosturaTests` 8, `EmailRepetidoTests` 6, `AcessoAlunoTelaTests` 8, `EmailProviderTests` 19, `HealthTests` 6, `HospedagemTests` 8, `ArchitectureRules` 4.
- `HealthTests` passa com o trabalhador e os tratadores de e-mail registrados: eles não resolvem `IAppDbContext` na subida. O defeito de teste que o PR #306 corrige aparece com o tratador de IA.
- Verificação visual (22/11/2026, na branch integrada, com a caixa de saída do PR #283 juntada só localmente): Administradores, ficha do aluno (painel "Acesso ao jogo"), `/recuperar-senha` e `/conta/definir-senha`, em 1440 e 375 px, tema claro e escuro. Sem rolagem horizontal, contraste AA sem achado, foco visível; nenhum defeito corrigido. 96 capturas em `~/Trabalho/Universo-do-Helinho/capturas-recreio/email/` (fora do repositório).
- A suíte completa de integração não rodou localmente: o CI roda em cada PR.

## O que ficou por fazer

- **`/conta/definir-senha` fala com quem é do painel.** O responsável pelo aluno cai na mesma página: no sucesso o botão é "Entrar no painel"; no link inválido, "Pedir novo link" leva a `/recuperar-senha`, que ignora conta de aluno. Pede decisão de texto (a página não pode revelar o tipo da conta num link inválido). Sem Issue.
- **Limite de taxa responde 429 sem corpo** em `/recuperar-senha` e `/conta/definir-senha`: o navegador mostra a página de erro dele. Sem Issue.
- **"E-mail não entregue" nas telas** (item 12 do plano): o webhook grava `Feedback` e acha o trabalho; nenhuma tela mostra ainda.
- **Índice único de e-mail** (#274, continuação da #184): a unicidade é por trava e conferência no caso de uso.
- **Verificação dos e-mails em cliente de e-mail real** e das telas nos estados não capturados (ficha sem e-mail do responsável, envio que falhou, limite atingido, "e-mail já usado" em Administradores/Novo): todos têm teste, nenhum tem captura.
- **Caixa de saída de desenvolvimento** (PR #283): fora da cadeia; junta sem conflito.
