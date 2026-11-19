# ADR-0003 — E-mails de acesso com Brevo e fila persistente

**Data:** 19/11/2026 · **Status:** proposto (aguarda o Henrique; ver seção 16) · **Decisor:** Henrique
**Base:** ADR-0001 (§2, provedores externos atrás de portas; §7, ameaças; e o parágrafo "Dados" da §1, que este ADR revisa), ADR-0002 (§9 Data Protection, §10 variáveis), Issue #176, skills `engineering-standards`, `owasp-security`, `site-compliance` e `devops-standards`, respostas da contratante C05, C13, C14, C15 e C20.

Os códigos C04, C05, C09, C13, C14, C15, C20 e A01 são itens do Q&A com a contratante, "Perguntas e Respostas — Universo do Hélinho", mantido pelo Henrique fora do repositório. O ADR-0002 ainda não está na `main`: foi lido em `origin/issue-93-adr-hospedagem` (PR #101).

Este ADR não muda código. Ele fecha as decisões da Sprint 3 para que as Issues de código possam ser abertas.

**Este ADR revisa um ponto do ADR-0001** (aceito): o parágrafo "Dados" diz que "não há caso para consistência eventual, cache distribuído ou fila" (`docs/architecture/ADR-0001-stack.md:23`). Agora há caso para fila. A revisão está na seção 5 e espera o Henrique (seção 16, D15).

---

## 1. Contexto

O PRD deixou o envio real de e-mail fora do MVP e dentro do contrato (`docs/product/PRD.md`, seção 6). A Sprint 3 entrega os e-mails de acesso de ponta a ponta, de modo que só falte a configuração manual na Brevo (conta, domínio, remetente, chave) e as variáveis na Railway. Sem chave, o sistema continua com o envio fake.

Restrições:

- **Contrato congelado** (`CLAUDE.md`): migração, pacote novo e mudança de DTO pedem Issue própria com ADR. Este é o ADR; ele autoriza **duas migrações** (seções 5 e 9) e **nenhum pacote novo**.
- **Custo mínimo** (C04) e **volume desconhecido** (C13).
- **Remetente, domínio e DNS pendentes** (C15); **regras de retenção e de fornecedor não informadas** (C14).
- **Dados de menores:** o e-mail vai para adultos (administrador, responsável), nunca para a criança. Em homologação só há dado sintético.
- **Sprint 4** (rascunhos de questões por IA) precisa de fila para trabalhos demorados. A fila nasce aqui e não pode ser só de e-mail.

### 1.1 O que existe hoje

| Ponto | Onde | Estado |
|---|---|---|
| Porta de e-mail | `src/Helinho.Application/Abstractions/IEmailSender.cs:3-6` | `SendAsync(recipient, subject, body, ct)`: um corpo só, sem retorno |
| Envio fake | `src/Helinho.Infrastructure/Fakes/FakeEmailSender.cs:7-17` | descarta a mensagem e loga só o fato |
| Registro do fake | `src/Helinho.Infrastructure/DependencyInjection/InfrastructureExtensions.cs:14` | `AddSingleton<IEmailSender, FakeEmailSender>()`; o método não recebe `IConfiguration` |
| Recuperação de senha | `src/Helinho.Web/Pages/Conta/RecuperarSenha.cshtml.cs:30-39` | procura por `FindByEmailAsync`, envia texto **sem token nem link** direto pela porta e mostra a mesma tela nos dois casos; só contas `Master` e `Admin` (linhas 42-43) |
| Criação de administrador | `src/Helinho.Web/Pages/Administradores/Novo.cshtml.cs:22-31,72-74` e `src/Helinho.Infrastructure/Identidade/AdminAccountGateway.cs:25-35` | o master digita a senha; `users.CreateAsync(user, account.Password)` (linha 28) |
| Um administrador ativo por unidade | `src/Helinho.Application/Administradores/CriarAdministradorHandler.cs:34-39,52` | checagem e criação sob o lock da unidade (`AppDbContext.BeginLockedTransactionAsync`, `AppDbContext.cs:20-33`); "ativo" = sem bloqueio vigente |
| Conta do aluno | `src/Helinho.Infrastructure/Identidade/StudentAccounts.cs:11-27` | usuário = matrícula, **sem e-mail e sem senha**; não há tela que defina a senha do aluno |
| Responsável | `src/Helinho.Domain/Entities/Student.cs:45,49,57-59` | `GuardianName` e `GuardianApproved`; **não há e-mail do responsável**. A aprovação é uma caixa marcada pelo administrador (`src/Helinho.Application/Painel/SaveStudent.cs:56,79`) |
| Login do aluno | `src/Helinho.Infrastructure/Identidade/LoginIdentityGateway.cs:46-57` | matrícula, depois `UserName`, depois `AppUser.Email` |
| Identity do painel | `src/Helinho.Web/Composition/Panel.cs:21-27` | `AddIdentity` com `Lockout.MaxFailedAccessAttempts = 5` e `RequireUniqueEmail = false`; **nenhum provedor de token registrado** |
| Identity da API | `src/Helinho.Infrastructure/DependencyInjection/IdentidadeExtensions.cs:24-31` | `AddIdentityCore`, bloqueio configurado por `ApiAuthOptions`; também sem provedor de token |
| Limite de taxa | `src/Helinho.Web/Composition/Areas/Identidade.cs:22-37,43` | uma política (`api-login`), por IP, só no login da API |
| Composição | `src/Helinho.Web/Program.cs:6-12` e `src/Helinho.Web/Composition/Areas.cs:6-11` | `Program.cs` não é editado; cada área tem arquivo próprio em `Composition/Areas/` e entra na cadeia de `Areas.cs` |
| Regra de arquitetura | `tests/Helinho.ArchitectureTests/ArchitectureRules.cs:36-43` | `Web` não pode depender do `AppDbContext` nem de Npgsql |
| Terraform | `infra/railway/variables.tf` em `origin/issue-99-terraform-esqueleto` (PR #100, rascunho) | já declara `Email__Provider` (`Fake`), `Brevo__SenderEmail`, `Brevo__SenderName`, `App__PublicBaseUrl` e `brevo_api_key` (sensível, com validação). `infra/` ainda não existe na `main` |
| Data Protection | PR #120 (Issue #103), aberto | chaves na tabela `DataProtectionKeys`, `SetApplicationName("universo-do-helinho")` |
| Imagens | `src/Helinho.Web/wwwroot/img/` | 28 arquivos, todos `.webp` |

Três fatos do código pesam nas decisões:

1. **Hoje `GeneratePasswordResetTokenAsync` lançaria exceção.** `AddIdentity` não registra provedor de token (conferido no código-fonte do ASP.NET Core 10, fonte F14); só `AddDefaultTokenProviders` ou um provedor próprio fazem isso.
2. **`FindByEmailAsync` usa `SingleOrDefaultAsync`** (F15). Com `RequireUniqueEmail = false`, duas contas com o mesmo e-mail fazem a chamada lançar exceção, em `RecuperarSenha.cshtml.cs:30`, `Entrar.cshtml.cs:32` e `LoginIdentityGateway.cs:56`. Na recuperação de senha, um erro 500 para um e-mail específico denuncia a conta. A conclusão vem da leitura do código; não foi reproduzida em execução. Virou a Issue #184.
3. **`Helinho.Infrastructure` não referencia o framework ASP.NET Core.** `dotnet list package --include-transitive` mostra `Microsoft.Extensions.Options`, `Configuration` e `Logging`, mas não `Microsoft.Extensions.Http` (`AddHttpClient`) nem `Microsoft.Extensions.Hosting.Abstractions` (`BackgroundService`).

## 2. Fontes consultadas (todas em 19/11/2026)

| # | Assunto | Fonte |
|---|---|---|
| F1 | Envio transacional: endpoint, corpo, respostas | https://developers.brevo.com/reference/sendtransacemail |
| F2 | Guia de envio transacional (exemplo `curl`, resposta 201) | https://developers.brevo.com/docs/send-a-transactional-email |
| F3 | Códigos HTTP e códigos de erro da API | https://developers.brevo.com/docs/how-it-works |
| F4 | Limites de taxa por endpoint e por plano | https://developers.brevo.com/docs/api-limits |
| F5 | Cabeçalhos de limite e espera depois de um 429 | https://developers.brevo.com/docs/limit-headers |
| F6 | Chave de idempotência | https://developers.brevo.com/docs/heterogenous-versions-batch-emails |
| F7 | Autenticação por chave de API | https://developers.brevo.com/docs/api-key-authentication |
| F8 | IPs autorizados da chave | https://developers.brevo.com/docs/ip-security |
| F9 | Webhooks transacionais: eventos e corpo | https://developers.brevo.com/docs/transactional-webhooks e https://developers.brevo.com/docs/how-to-use-webhooks |
| F10 | Webhooks: proteção e criação pela API | https://developers.brevo.com/docs/secured-webhooks e https://developers.brevo.com/reference/createwebhook |
| F11 | Webhooks: repetição | https://developers.brevo.com/docs/retry-mechanism |
| F12 | Preços e limite do plano gratuito | https://www.brevo.com/pricing/ (HTML da página) e https://www.brevo.com/pricing.md ("Generated on: 2026-09-29") |
| F13 | Autenticação de domínio | https://developers.brevo.com/docs/domain-authentication-and-verification |
| F14 | Identity: `AddIdentity`, `AddDefaultTokenProviders`, `DataProtectorTokenProvider`, `UserManager` (tag `v10.0.0`) | https://github.com/dotnet/aspnetcore/tree/v10.0.0/src/Identity (`Core/src/IdentityServiceCollectionExtensions.cs`, `Core/src/IdentityBuilderExtensions.cs`, `Core/src/DataProtectorTokenProvider.cs`, `Extensions.Core/src/UserManager.cs`) |
| F15 | Identity: `UserStore.FindByEmailAsync` | https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/EntityFrameworkCore/src/UserStore.cs |
| F16 | Identity: validade diferente por tipo de token; validador do carimbo de segurança | Context7 `/dotnet/aspnetcore.docs`: `aspnetcore/blazor/security/account-confirmation-and-password-recovery.md`, `aspnetcore/security/authentication/identity-configuration.md`, `aspnetcore/blazor/security/index.md` |
| F17 | Data Protection: nome da aplicação, validade das chaves, contêiner | https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0 |
| F18 | Limite de taxa em Razor Pages | Context7 `/dotnet/aspnetcore.docs`: `aspnetcore/performance/rate-limit.md` |
| F19 | IP de saída fixo na Railway | https://docs.railway.com/networking/static-outbound-ips |
| F20 | Recuperação de senha: resposta e tempo consistentes, token na URL, `no-referrer`, limite por conta, aviso depois da troca | https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html |
| F21 | Política de senha padrão do Identity | Context7 `/dotnet/aspnetcore.docs`: `aspnetcore/security/authentication/identity-configuration.md` |

O que não foi possível confirmar está na seção 17. A central de ajuda da Brevo (`help.brevo.com`) respondeu 403 à leitura automática; o que depende dela está marcado.

## 3. Decisão (resumo)

| Item | Decisão | Seção |
|---|---|---|
| Integração | API transacional v3 por `HttpClient` tipado; sem SDK e sem pacote novo | 4 |
| Modelos | HTML e texto puro versionados no repositório e renderizados pela aplicação; nada criado no editor da Brevo | 8 |
| Relação com o ADR-0001 | **Revisa** o trecho "não há caso para […] fila". Mantém "um PostgreSQL único resolve": sem Redis e sem broker, a fila é uma tabela no PostgreSQL | 5 |
| Fila | Uma tabela genérica de trabalhos (`QueueJobs`), com tipo e parâmetros; a "outbox de e-mail" são as linhas de tipo `email.*` | 5 |
| O que fica gravado | Só referências (tipo e id do usuário ou do aluno). Destinatário, corpo e token **não ficam em repouso**: o token nasce na hora do envio | 5.3 |
| Worker | `BackgroundService` com um laço por tratador, `FOR UPDATE SKIP LOCKED`, política de repetição por tipo (e-mail: exponencial, teto de 8), erro permanente direto para falha | 6 |
| Retenção | Linhas terminadas são apagadas depois de 30 dias (`PROPOSTA`) | 5.6 |
| Provedor | `Email__Provider` = `Fake` (padrão) ou `Brevo`; `Brevo` com configuração incompleta derruba a inicialização; `Development` sempre `Fake` | 7 |
| Tokens | Tokens do Identity com dois provedores próprios: convite 72 h, redefinição 1 h; uso único pelo carimbo de segurança; link só de `App__PublicBaseUrl` | 10 |
| E-mail do responsável | `Student.GuardianEmail`: obrigatório no formulário, anulável no banco, não único, nunca identificador de login (`PROPOSTA`) | 9 |
| Senha do aluno | Definida pelo responsável, pelo link (`PROPOSTA`) | 11.4 |
| Webhook | `POST /webhooks/brevo`, desligado sem segredo, `Authorization: Bearer` comparado em tempo constante | 12 |
| Terraform | Acrescenta `Brevo__WebhookSecret` e validações; o resto já está declarado | 13 |

As linhas de relação com o ADR-0001, fila, gravação, retenção, tokens, e-mail do responsável e senha do aluno são **recomendações** que esperam o Henrique (seção 16).

## 4. Integração com a Brevo

### 4.1 Requisição

Confirmado em F1, F2 e F7:

- `POST https://api.brevo.com/v3/smtp/email`, com os cabeçalhos `api-key: <chave>`, `content-type: application/json` e `accept: application/json`.
- Campos do corpo: `sender` (`email`, `name` até 70 caracteres), `to` (lista de `{ email, name }`), `subject`, `htmlContent`, `textContent` (opcional), `replyTo`, `headers` (mapa de cabeçalhos não padronizados), `tags` (lista de textos), além de `cc`, `bcc`, `templateId`, `params`, `attachment`, `messageVersions`, `scheduledAt` e `batchId`, que não usamos.
- `sender`, `subject` e `htmlContent` são obrigatórios quando não há `templateId`. O remetente precisa estar cadastrado e verificado na conta.
- Resposta de sucesso: **201** com `{ "messageId": "<…@…>" }`.

Corpo que o adaptador envia (endereços fictícios):

```json
{
  "sender": { "email": "nao-responda@example.org", "name": "Universo do Hélinho · FUNDHAS" },
  "to": [ { "email": "responsavel@example.com" } ],
  "subject": "Acesso ao Universo do Hélinho",
  "htmlContent": "<!doctype html>…",
  "textContent": "…",
  "tags": [ "email.student-access" ],
  "headers": { "idempotencyKey": "<id do trabalho>" }
}
```

Escolhas:

- **`to` com um destinatário por mensagem** e sem `name`: o nome do responsável não precisa ir para a Brevo.
- **`tags` leva o tipo do trabalho**, nunca dado pessoal. É o que aparece nos relatórios da Brevo e volta no webhook.
- **`idempotencyKey` é o id do trabalho** (um UUID; no exemplo acima aparece como marcador). Segundo F6, a chave precisa ser UUID, vale por 30 minutos e uma repetição devolve o erro `duplicate_parameter` sem processar. O guia a descreve dentro do objeto `headers` do corpo, não como cabeçalho HTTP.
- **URL fixa no código**, não em configuração: não há como apontar o cliente para outro host por variável de ambiente.
- **`HttpClient.Timeout` de 10 s.** Sem política de retry no cliente: quem repete é a fila.
- **Sem SDK.** O SDK oficial seria pacote novo (Issue própria, auditoria de cadeia de suprimentos) para substituir uma chamada HTTP com um corpo JSON.

### 4.2 Respostas e o que o adaptador faz

Confirmado em F1, F3, F4 e F5. O corpo de erro é `{ "code": "...", "message": "..." }`.

| Resposta | Significado na documentação | Classe | O que acontece com o trabalho |
|---|---|---|---|
| 201 | enviado | sucesso | `Succeeded`; guarda o `messageId` sem `<` e `>` |
| 400 `invalid_parameter`, `missing_parameter`, `out_of_range` | requisição inválida | permanente | `Failed` na hora, sem nova tentativa |
| 400 `duplicate_parameter` ou `duplicate_request`, com a nossa chave de idempotência | repetição dentro de 30 minutos | já processado | `Succeeded` (sem `messageId`) |
| 404, 405, 406 e outro 4xx não listado | rota, método ou `Content-Type` errados | permanente | `Failed` na hora; é defeito nosso, log em `Error` |
| 401 | chave inválida ou ausente | conta | suspensão (seção 6.3) |
| 402 e `not_enough_credits` | conta sem ativação ou sem créditos | conta | suspensão |
| 403, `permission_denied`, `account_under_validation` | conta sem permissão ou em validação | conta | suspensão |
| 429 | limite de taxa; cabeçalhos `x-sib-ratelimit-limit`, `-remaining` e `-reset` | transitório | nova tentativa depois do maior valor entre o backoff e o `x-sib-ratelimit-reset` (em segundos) |
| 5xx, tempo esgotado, falha de rede | não documentado | transitório | nova tentativa com backoff |

O limite de taxa do `POST /v3/smtp/email` é de 1.000 requisições por segundo e 3.600.000 por hora em todos os planos, inclusive o gratuito (F4). O lote do worker fica ordens de grandeza abaixo; o 429 é tratado por correção, não por necessidade.

O adaptador **nunca grava nem loga o campo `message`** da Brevo: ele pode repetir o endereço do destinatário. Só o `code`, e só se estiver na lista conhecida; fora dela vira `http_<status>_unknown`.

### 4.3 Mudança na porta `IEmailSender`

A porta atual tem um `body` só e não devolve nada. O adaptador precisa de HTML e texto, de uma etiqueta, da chave de idempotência e de devolver o `messageId` ou a classe do erro. A Issue do adaptador troca a assinatura para algo como `SendAsync(EmailMessage message, ct) → EmailSendResult`. É porta interna de `Application`, não DTO nem rota da API do jogo: não fere o contrato congelado. `FakeEmailSender` e o `RecordingEmail` dos testes (`tests/Helinho.IntegrationTests/PanelSessionTests.cs:19`) acompanham.

### 4.4 Onde cada peça mora, sem pacote novo

- `BrevoEmailSender` fica em `Helinho.Infrastructure` e recebe um `HttpClient` no construtor (`System.Net.Http` é da biblioteca base).
- O registro `AddHttpClient<BrevoEmailSender>` fica em `Helinho.Web`, numa área nova `Composition/Areas/Email.cs`, porque `Microsoft.Extensions.Http` só existe no framework compartilhado do projeto Web. As áreas rodam depois de `AddInfrastructureServices` (`Program.cs:8-12`), então o registro delas vence o `FakeEmailSender` registrado em `InfrastructureExtensions.cs:14`.
- As opções (`Email`, `Brevo`, `App`) e a validação ficam em `Helinho.Infrastructure`, que já tem `Microsoft.Extensions.Options`.
- O cabeçalho `api-key` é marcado como sensível no log do `HttpClient` e a categoria `System.Net.Http.HttpClient` fica em `Warning`. A API exata (`RedactLoggedHeaders`) é confirmada no Context7 pela Issue.

Alternativa descartada: acrescentar `FrameworkReference Microsoft.AspNetCore.App` em `Helinho.Infrastructure`. Não é pacote NuGet, mas dá à camada de infraestrutura acesso a todo o ASP.NET Core para economizar dois registros.

## 5. Fila persistente

### 5.0 O que o ADR-0001 diz, e o que este ADR revisa

O ADR-0001 **não** decidiu fila. A palavra aparece nele uma vez, no parágrafo "Dados" (`docs/architecture/ADR-0001-stack.md:23`):

> "Um PostgreSQL único resolve; não há caso para consistência eventual, cache distribuído ou fila."

O que ele decide para os provedores externos é outra coisa: "E-mail, push e IA atrás de portas em `Application`, com implementação fake em `Infrastructure`" (`ADR-0001-stack.md:44`). A frase "`BackgroundService` + fila persistente no PostgreSQL, sem Redis" vem do roteiro das sprints, que a atribui ao ADR-0001; a primeira versão deste ADR a repetiu sem conferir no arquivo. Quem apontou o erro foi o ADR-0004 (PR #181, seção 10.5).

**Este ADR revisa o trecho "não há caso para […] fila".** Quando o ADR-0001 foi escrito, e-mail, push e IA eram fakes e nenhum trabalho dependia de terceiro. Agora há caso:

- **Sprint 3:** enviar e-mail depende da Brevo, que pode demorar, recusar por limite de taxa ou estar fora do ar. Sem fila, a página ficaria presa ao tempo da Brevo, não haveria nova tentativa e o tempo de resposta da recuperação de senha denunciaria a conta (seção 11.1).
- **Sprint 4:** gerar rascunhos de questões por IA é uma chamada de dezenas de segundos, com uma pessoa esperando na tela.

O que **continua valendo** do ADR-0001: "um PostgreSQL único resolve". **Sem Redis e sem broker: a fila é uma tabela no PostgreSQL.** Consistência eventual e cache distribuído continuam sem caso. A revisão é só da palavra "fila", e é decisão do Henrique (seção 16, D15). O ADR-0001 ganha uma nota de uma linha apontando para cá; a seção dele não é reescrita.

### 5.1 Uma tabela genérica

**Tabela `QueueJobs`** (entidade `QueueJob` em `Helinho.Infrastructure/Persistence`, como `AppUser`). A Issue fala em `email_outbox`; o nome proposto segue a convenção que o banco já usa (nomes padrão do EF, como `Students` e `AspNetUsers`) e não diz "e-mail" porque a tabela não é só de e-mail.

| Coluna | Tipo | Para quê |
|---|---|---|
| `Id` | `uuid`, chave primária | identifica o trabalho; é também a chave de idempotência enviada à Brevo |
| `Type` | `varchar(60)`, obrigatória | `email.admin-invite`, `email.password-reset`, `email.password-changed`, `email.student-access`; na Sprint 4, `ai.question-drafts` |
| `SubjectId` | `uuid`, opcional | a quem o trabalho se refere: id do usuário (convite, redefinição) ou do aluno (acesso). Sem chave estrangeira |
| `Payload` | `jsonb`, obrigatória | parâmetros do tipo, com versão (`{"v":1,…}`). **Sem destinatário, corpo, token, dado cadastral ou texto livre digitado por alguém**; quando o trabalho tem dados próprios, o `Payload` leva só o id do registro que os guarda |
| `Status` | `varchar(20)`, obrigatória | `Pending`, `Processing`, `Succeeded`, `Failed`, `Cancelled` (texto, como os outros enums do banco) |
| `Attempts` | `int`, obrigatória | tentativas já iniciadas |
| `NextAttemptAt` | `timestamptz`, obrigatória | quando o trabalho volta a ficar visível para o worker (seção 6.1) |
| `ExpiresAt` | `timestamptz`, obrigatória | depois disso não adianta mais processar; vira `Failed` com `expired` |
| `LastError` | `varchar(100)`, opcional | código de vocabulário fechado (`http_429`, `timeout`, `expired`, `subject_missing`…). Nunca mensagem livre |
| `IdempotencyKey` | `varchar(200)`, opcional, **única** | impede enfileirar duas vezes o mesmo pedido (seção 5.4) |
| `ExternalId` | `varchar(200)`, opcional | id no provedor: o `messageId` da Brevo; na Sprint 4, o id da requisição de IA |
| `Feedback`, `FeedbackAt` | `varchar(30)` e `timestamptz`, opcionais | retorno posterior do provedor: `hard_bounce`, `blocked`, `spam`, `invalid_email` (seção 12) |
| `CreatedAt` | `timestamptz`, obrigatória | criação |
| `FinishedAt` | `timestamptz`, opcional | quando chegou a um estado final ("enviado em") |

Estados:

```
Pending ──(worker pega)──▶ Processing ──▶ Succeeded
   ▲                          │
   └──(erro transitório)──────┤
                              └──▶ Failed      (erro permanente, teto de tentativas ou ExpiresAt)
Pending ──(reenvio ou cancelamento)──▶ Cancelled
```

`Cancelled` nunca é decisão da fila: só acontece por `IJobQueue.CancelPending(...)`, chamado por um caso de uso dentro da transação dele (por exemplo, o reenvio de convite).

Índices:

- `IX_QueueJobs_Type_NextAttemptAt`, parcial em `Status IN ('Pending','Processing')`: a busca do próximo lote, que é feita por tipo (seção 6.1).
- `UX_QueueJobs_IdempotencyKey`, único e parcial em `IdempotencyKey IS NOT NULL`.
- `IX_QueueJobs_Type_SubjectId_CreatedAt`: contagem dos limites por conta e situação mostrada nas telas.
- `IX_QueueJobs_ExternalId`, parcial em `ExternalId IS NOT NULL`: localizar o trabalho quando o webhook chega.

### 5.2 Alternativa descartada: `email_outbox` e uma tabela irmã para IA

| Opção | A favor | Contra |
|---|---|---|
| **A. `QueueJobs` genérica** (recomendada) | um worker, um jeito de travar, repetir, expirar e apagar; a Sprint 4 só acrescenta um tipo e um tratador, sem migração para o mecanismo | colunas que só o e-mail usa (`Feedback`) ficam vazias nos outros tipos |
| B. `email_outbox` + `ai_jobs` | colunas com nome de negócio em cada tabela | dois workers e duas cópias do mecanismo mais delicado do sistema; segunda migração e segundo ADR de fila na Sprint 4 |
| C. Biblioteca de fila (Hangfire, Quartz) | pronta | pacote novo, esquema próprio no banco, painel a proteger; desproporcional para quatro e-mails |

### 5.3 O que fica gravado: referências, não a mensagem

| Opção | O que fica no banco | Risco |
|---|---|---|
| **A. Referências** (recomendada) | tipo e `SubjectId`; o tratador resolve o destinatário, gera o token e renderiza na hora de enviar | o conteúdo é decidido no envio, não no pedido |
| B. Corpo renderizado | HTML e texto com o link, mais o destinatário | **token de acesso em repouso** durante toda a validade (quem lê o banco ou um backup assume a conta), mais nome e matrícula de criança duplicados; exige limpeza e talvez cifra |
| C. Parâmetros com o token | destinatário e token | o mesmo risco de B, em menos bytes |

**Recomendação: A.** Consequências:

- Nenhum link de acesso existe em repouso, em nenhum momento, e não há corpo para vazar em log.
- A validade do link conta a partir do envio de verdade, não do clique no painel.
- As condições são conferidas duas vezes: ao enfileirar e ao enviar (por exemplo, o aluno continua aprovado).
- O worker deixa de ser um repassador de texto: ele entrega o trabalho a um tratador por tipo. A Sprint 4 precisa disso de qualquer forma.
- Para saber **para qual endereço** uma mensagem foi, olha-se o registro transacional no painel da Brevo. O banco guarda que foi enviada, quando e com qual `messageId`.

### 5.4 Idempotência

Duas camadas, com papéis diferentes:

- **Ao enfileirar:** `IdempotencyKey` = `<tipo>:<SubjectId>:<janela>`, em que a janela é o instante dividido por 2 minutos. Um clique duplo ou o reenvio do formulário pelo navegador cai na mesma chave e o índice único recusa a segunda linha. Exceção: o aviso de senha alterada usa `email.password-changed:<id>:<resumo do carimbo de segurança>`, um por troca de senha.
- **Ao enviar:** o `Id` do trabalho vai como `idempotencyKey` para a Brevo. Se a resposta se perder (tempo esgotado depois de a Brevo aceitar), as repetições dos primeiros 30 minutos não geram segunda mensagem.

O limite por hora de cada fluxo (seção 11) é contado na própria tabela, por `Type` e `SubjectId`.

### 5.5 Enfileirar junto com a regra de negócio

A porta `IJobQueue.Enqueue(...)` acrescenta a linha ao contexto **sem salvar**, como `IStudentAccounts.StageAsync` já faz; quem chama salva junto. Assim a conta do administrador e o convite dele são gravados na mesma transação: ou os dois, ou nenhum. Na saída vale a mesma regra, espelhada: o que o tratador deixa no contexto é salvo junto com a conclusão do trabalho (seção 6.6).

### 5.6 Retenção (LGPD)

Como não há destinatário nem corpo na fila, não há o que anonimizar. O que resta é metadado ligado a uma pessoa (`SubjectId`, tipo e datas).

- **Linhas em estado final são apagadas 30 dias depois de `FinishedAt`** (`PROPOSTA`; C14 não traz regra institucional). O prazo cobre a situação nas telas ("convite enviado em…"), a chegada tardia de um webhook e a investigação de um problema relatado.
- A limpeza roda no próprio worker, uma vez por dia, em lotes.
- Linhas em `Pending` ou `Processing` não são apagadas: terminam por sucesso, falha ou `ExpiresAt`.
- **Nada do conteúdo vai para log.** O log do worker tem id, tipo, tentativa, classe do resultado, código HTTP e duração.
- O que a Brevo guarda do lado dela (registros e conteúdo das mensagens) não foi confirmado (seção 17) e entra na pergunta sobre fornecedores (C14).
- Se a D2 for decidida pela opção B (corpo renderizado na fila), o prazo passa a ser outro e mais curto: destinatário e corpo anulados **7 dias** depois do envio, já que o link mais longo vale 72 horas.

## 6. Worker

### 6.1 Pegar o próximo lote

Um comando SQL cru, em transação curta, **uma vez por tratador**. Cada tratador declara os tipos que atende, o tamanho do lote e o prazo de posse (seção 6.6):

```sql
UPDATE "QueueJobs"
   SET "Status" = 'Processing',
       "Attempts" = "Attempts" + 1,
       "NextAttemptAt" = now() + @lease
 WHERE "Id" IN (
        SELECT "Id" FROM "QueueJobs"
         WHERE "Type" = ANY(@types)
           AND "Status" IN ('Pending', 'Processing')
           AND "NextAttemptAt" <= now()
         ORDER BY "NextAttemptAt"
         LIMIT @batch
           FOR UPDATE SKIP LOCKED)
RETURNING *;
```

- **`FOR UPDATE SKIP LOCKED`** garante que dois processos não peguem a mesma linha. Hoje há uma réplica, mas durante um deploy o contêiner antigo e o novo rodam juntos por alguns segundos.
- **`NextAttemptAt` faz também o papel de prazo de posse.** Ao pegar, o worker empurra a data para `agora + posse`. Se o processo morrer no meio, a linha reaparece sozinha depois desse prazo, sem coluna extra de trava.
- **Lote e posse são do tipo, não da fila.** E-mails e trabalhos de IA nunca dividem um lote: cada tratador tem a própria captura e o próprio laço (seção 6.4). Uma chamada de IA de 90 s não fica na frente de um e-mail, nem no lote nem na espera.
- **A posse cobre o lote inteiro:** `lote × tempo máximo por item ≤ posse`, conferido na subida da aplicação. No e-mail, 10 × 10 s = 100 s, dentro dos 2 minutos. O tempo máximo por item é imposto pelo `CancellationToken` entregue ao tratador.
- **Última defesa:** a conclusão de um item só é gravada se a linha ainda estiver em `Processing` com o mesmo `Attempts` de quando foi pega (seção 6.6). Se a posse venceu e outro processo pegou o trabalho, a gravação do primeiro é recusada inteira, junto com o resultado dele.
- A chamada externa acontece **fora** de transação. Segurar uma transação aberta durante a chamada à Brevo prenderia uma conexão do banco por até 10 s por item.
- A tentativa é contada ao pegar, então uma queda no meio do envio também conta.
- A forma de executar `UPDATE … RETURNING` pelo EF Core é confirmada no Context7 pela Issue do worker.

Como `Web` não pode usar o `AppDbContext` nem Npgsql, o processamento fica em `Helinho.Infrastructure`, atrás de uma porta `IJobProcessor` em `Application`. O `BackgroundService` em si é uma classe pequena em `Helinho.Web` (área nova `Composition/Areas/Fila.cs`), que só chama a porta em laço: o projeto Web é quem tem o hosting. Cada tipo de trabalho tem um tratador (`IJobHandler`) que devolve sucesso, repetir, falhar ou suspender.

### 6.2 Repetição: política por tipo

Teto de tentativas, espera, lote, tempo por item e posse são **do tratador**, declarados em código junto com ele (`JobPolicy`, seção 6.6). Não há teto global.

| Política | E-mail (`email.*`) | IA (`ai.question-drafts`; valores do ADR-0004) |
|---|---|---|
| Teto de tentativas | **8** | **2**; a segunda só existe para recuperar processo que morreu no meio |
| Espera depois da tentativa *n* que falhou | `min(1 min × 2^(n−1), 60 min)`, com variação aleatória de ±20% (aproximadamente 1, 3, 7, 15, 31, 63 e 123 minutos depois da primeira) | não se aplica: o tratador de IA nunca devolve "repetir" |
| Lote | 10 | 1 |
| Tempo máximo por item | 10 s (o tempo limite do `HttpClient`) | `Ai__TimeoutSeconds` (90 s na proposta do ADR-0004) |
| Prazo de posse | 2 minutos | tempo máximo por item + 60 s (150 s) |
| `ExpiresAt` | redefinição de senha 30 minutos; convite e aviso de senha alterada 24 horas; acesso do aluno 72 horas | 10 minutos |

- Estourou o teto, `Failed` com o último código.
- Passou de `ExpiresAt`, `Failed` com `expired`, mesmo que restem tentativas. Se a próxima espera cairia depois de `ExpiresAt`, o trabalho falha na hora em vez de esperar para expirar.
- **Erro permanente vai direto para `Failed`**, sem gastar as outras tentativas (tabela da seção 4.2).

### 6.3 Suspensão: quando o problema é da conta, não da mensagem

401, 402 e 403 não melhoram repetindo e também não são defeito da mensagem: a chave está errada, a conta ainda não foi validada ou os créditos do dia acabaram. O worker:

- devolve o trabalho para `Pending` **sem consumir a tentativa**, com `NextAttemptAt` em 15 minutos e `LastError` com o código;
- pausa **o laço daquele tratador** até lá; os outros tratadores têm laço próprio e continuam;
- registra um log em `Error`, que é o gatilho de alerta;
- o trabalho só falha de vez quando passar de `ExpiresAt`.

"Suspender" é um resultado que só o tratador de e-mail devolve. O de IA não o usa: com uma pessoa esperando na tela, o certo é falhar com o motivo (ADR-0004, seção 10.2).

Alternativa descartada: mandar 401 e 402 direto para `Failed`. Uma chave colada errada no primeiro dia mataria todos os convites da fila e o master teria de reenviar um por um.

### 6.4 Laços, parada e ambiente

- **Um laço por tratador**, dentro do mesmo `BackgroundService`. Com a fila daquele tipo vazia, nova consulta a cada 5 s; com lote cheio, sem espera. Dentro de um laço os itens são processados um por vez.
- **Parada limpa:** ao receber o sinal de parada, nenhum laço pega lote novo. O item em curso segue até terminar, até o tempo máximo dele ou até a janela de parada do host acabar, o que vier primeiro. O que for interrompido continua em `Processing` e reaparece pelo prazo de posse, com a tentativa contada.
- Os comandos `migrate` e `seed` saem antes de `app.RunAsync()` (`Program.cs:16-19`), então o worker não sobe neles.
- Nos testes de integração o serviço hospedado fica desligado (`Fila__Worker__Enabled=false` na fábrica) e o teste chama `IJobProcessor` diretamente, para ser determinístico.
- Configuração com padrão no código: `Fila__Worker__Enabled`, `Fila__PollInterval` e `Fila__RetentionDays`. Teto de tentativas e lote saíram da configuração: são da política de cada tratador. Nada disso entra no Terraform enquanto ninguém precisar mudar.

### 6.5 Como a Sprint 4 reaproveita

| Peça | E-mail (Sprint 3) | IA (Sprint 4, ADR-0004 seção 10.2) |
|---|---|---|
| `Type` | `email.*` | `ai.question-drafts` |
| `Payload` | quase vazio (versão e motivo) | `{"v":1,"aiJobId":"<id do pedido>"}` **e mais nada**. Tipo de quiz, faixa, quantidade e assunto ficam no registro do pedido (`AiGenerationJobs`), não na fila: o assunto é texto livre e pode trazer um nome |
| `SubjectId` | usuário ou aluno | quem pediu |
| Tratador | resolve destinatário, gera token, renderiza, envia | lê o pedido, chama `IQuestionDraftGenerator` fora de transação e deixa as sugestões no contexto; o processador grava tudo junto com a conclusão (seção 6.6) |
| Resultados que o tratador devolve | sucesso, repetir, falhar e suspender, conforme a seção 4.2 | **só sucesso ou falha.** 429 e 5xx são repetidos dentro do adaptador de IA, no máximo duas vezes e dentro do prazo; 401, 402 e limite de gasto falham com o motivo, sem suspensão |
| Política | 8 tentativas, espera de 1 a 60 min, lote 10, posse de 2 min | 2 tentativas, lote 1, posse de 150 s, `ExpiresAt` de 10 min (seção 6.2) |
| Encerramento pela fila | não precisa de aviso: as telas leem a própria fila | o tratador implementa o gancho da seção 6.7 para tirar o pedido de "gerando" |

O mecanismo (tabela, trava, repetição, expiração, idempotência, limpeza) serve sem migração nova. A fonte da verdade da tela de IA é a tabela de pedidos do ADR-0004, não a `QueueJobs`, cujas linhas são apagadas em 30 dias.

### 6.6 Contrato do tratador

Fica em `Helinho.Application/Abstractions`, sem tipo de framework. Os nomes são ilustrativos; a Issue 1 os fixa.

```csharp
public interface IJobHandler
{
    IReadOnlyCollection<string> Types { get; }
    JobPolicy Policy { get; }
    Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken);
}

public sealed record JobContext(
    Guid Id, string Type, Guid? SubjectId, JsonElement Payload,
    int Attempts, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record JobPolicy(
    int MaxAttempts, TimeSpan MinBackoff, TimeSpan MaxBackoff,
    int BatchSize, TimeSpan ItemTimeout, TimeSpan Lease);

// JobResult: Succeeded(externalId?) | Retry(errorCode, notBefore?)
//          | Fail(errorCode)        | Suspend(errorCode, pauseFor)
```

Regras do contrato:

- **Escopo próprio por item.** O processador abre um escopo de injeção de dependência para cada item; o tratador e o que ele usa (contexto do banco, `UserManager`) nascem e morrem ali.
- **`Attempts` já conta a tentativa em curso** (1 na primeira). É o que permite a um tratador recusar uma tentativa a mais.
- **`CancellationToken`** é cancelado quando o tempo máximo por item se esgota ou quando a janela de parada do host acaba. O sinal de parada, sozinho, não cancela o item em curso (seção 6.4). O tratador repassa o token a toda chamada de I/O.
- **Resultado e conclusão na mesma gravação.** O tratador **não salva** o resultado final: o que ele deixar no contexto ao retornar é salvo pelo processador no mesmo `SaveChanges` que muda o `Status` do trabalho, uma transação só. Vale para qualquer resultado (sucesso, falha ou repetir). É o espelho do que `Enqueue` faz na entrada (seção 5.5).
- **Guarda de posse nessa gravação:** a atualização do trabalho leva a condição `Status = 'Processing'` e `Attempts` igual ao recebido (`Attempts` como token de concorrência). Se a posse foi perdida, nada é gravado: nem a conclusão, nem o resultado.
- **Gravação intermediária é permitida** e fica por conta do tratador (por exemplo, marcar um pedido como "gerando" antes da chamada externa). Ela precisa suportar ser executada de novo.
- **Nenhuma transação fica aberta durante `HandleAsync`.** A chamada externa acontece antes da gravação única.
- **Exceção não tratada:** nada do contexto do tratador é salvo; o processador registra `handler_exception` e trata como "repetir", dentro da política.
- **O que a transação não cobre:** o efeito externo. Se o e-mail saiu (201) e a gravação final falhou, o trabalho será executado de novo; a chave de idempotência enviada à Brevo cobre os primeiros 30 minutos (seção 5.4). Depois disso o pior caso é uma mensagem repetida.

### 6.7 Quando a fila encerra um trabalho sem o tratador decidir

A fila encerra por conta própria em dois casos:

- `expired`: passou de `ExpiresAt`, visto ao pegar o item ou ao calcular a próxima espera;
- `max_attempts`: o teto foi atingido, seja ao pegar de novo um trabalho cujo processo morreu, seja depois de um "repetir" na última tentativa.

Nos dois, quem enfileirou pode não ficar sabendo. Por isso existe um gancho **opcional**, em interface separada para que só o implemente quem precisa:

```csharp
public interface IAbandonedJobHandler
{
    Task OnAbandonedAsync(JobContext job, string reason, CancellationToken cancellationToken);
}
```

- Se o tratador do tipo implementa a interface, o processador a chama no escopo do item e grava o que ela deixar no contexto **junto** com o `Failed`, pelo mesmo mecanismo da seção 6.6.
- Se o gancho lançar exceção, o `Failed` é gravado do mesmo jeito e o erro vai para o log. Quem depende do aviso deve manter também uma conferência na leitura, como o ADR-0004 faz.
- O tratador de e-mail **não** implementa: as telas de convite e de acesso leem a situação direto da fila (seção 11.2).
- `Cancelled` não passa pelo gancho. Quem cancela é sempre um caso de uso, por `IJobQueue.CancelPending(...)`, dentro da própria transação: quem cancela é quem sabe.

### 6.8 Ajustes pedidos pelo ADR-0004

O ADR-0004 (PR #181, seção 10.4) confrontou os requisitos da IA com esta fila e pediu sete ajustes. Decisão em cada um:

| # | Pedido | Decisão | Onde ficou |
|---|---|---|---|
| A1 | Tentativas e espera por tipo de trabalho | **Aceito.** A política é do tratador; o teto global (`Fila__MaxAttempts`) deixou de existir | 6.2 |
| A2 | Contrato de `IJobHandler` escrito, com `Attempts` e `CancellationToken` | **Aceito** | 6.6 |
| A3 | Resultado e conclusão na mesma transação, com mecanismo | **Aceito, com um acréscimo:** a mesma gravação leva a guarda de posse, para que um trabalho pego duas vezes não grave dois resultados | 6.6 |
| A4 | Prazo de posse por tipo e por item | **Aceito com mudança.** Em vez de renovar a posse a cada item, cada tratador tem captura, lote e laço próprios, e a aplicação confere na subida que a posse cobre o lote. Um trabalho de IA nunca divide lote com e-mail | 6.1, 6.2, 6.4 |
| A5 | Corrigir a tabela da seção 6.5 (`Payload` e classes de erro da IA) | **Aceito.** O `Payload` da IA leva só o id do pedido, e a regra geral da coluna passou a proibir texto livre | 5.1, 6.5 |
| A6 | Aviso quando a fila encerra sem chamar o tratador | **Aceito com mudança.** Gancho opcional para `expired` e `max_attempts`. `Cancelled` fica de fora, porque quem cancela é sempre o próprio caso de uso | 6.7 |
| A7 | Corrigir a citação do ADR-0001 e declarar a revisão | **Aceito.** A citação estava errada | 5.0, 16 (D15), 19 |

Nenhum foi recusado. Renovar a posse a cada item (a outra saída sugerida para o A4) foi descartado: com laço e lote por tratador e a conferência na subida, a renovação seria uma segunda escrita por item para um caso que já não acontece.

**O desenho do e-mail não mudou.** A fila continua só com referências (D2): o A5 apertou a regra do `Payload`. 401, 402 e 403 continuam suspendendo sem gastar tentativa (D3): a suspensão passou a pausar o laço do tratador de e-mail, o que é o mesmo efeito com menos código. Tentativas, espera, lote, posse e `ExpiresAt` do e-mail são os de antes.

## 7. Seleção do provedor e validação na inicialização

| Seção | Chave | Regra |
|---|---|---|
| `Email` | `Provider` | `Fake` (padrão) ou `Brevo`; qualquer outro valor derruba a inicialização |
| `Email` | `DailyLimit` | teto diário de envios; `0` = sem teto (seção 15) |
| `Brevo` | `ApiKey` | obrigatória com `Provider=Brevo` |
| `Brevo` | `SenderEmail`, `SenderName` | obrigatórios com `Provider=Brevo`; e-mail válido; nome com até 70 caracteres (limite da Brevo) |
| `Brevo` | `WebhookSecret` | opcional; se preenchido, 32 caracteres ou mais |
| `App` | `PublicBaseUrl` | obrigatória com `Provider=Brevo`; URL absoluta `https://`, sem caminho nem query (`http://localhost` aceito só em `Development`) |

- Validação com `ValidateOnStart`: `Provider=Brevo` com chave, remetente ou URL pública vazios **derruba a aplicação na subida**, com mensagem que diz qual variável falta e nunca o valor.
- **Em `Development` o provedor é sempre `Fake`**, mesmo com `Email__Provider=Brevo` no ambiente do desenvolvedor; a aplicação avisa no log que ignorou a configuração. Uma chave real exportada no shell não envia e-mail por acidente.
- **Nos testes nenhuma chamada sai para a Brevo:** o adaptador é exercitado com um `HttpMessageHandler` falso, e a fábrica dos testes de integração substitui `IEmailSender` por um capturador.
- **Com `Fake`, convite e redefinição não se completam** fora dos testes, porque o link não chega a ninguém. Em homologação isso é esperado até a Brevo ser configurada (o plano gratuito basta). Para desenvolvimento local, ver a decisão D8 (seção 16).

## 8. Modelos de e-mail

### 8.1 No repositório, não na Brevo

A Brevo oferece modelos no editor dela (`templateId` + `params`). Ficam fora:

- o texto de um e-mail de acesso é parte do sistema: passa por revisão de PR, tem histórico e teste;
- nada precisa ser criado à mão no painel da Brevo além de conta, domínio, remetente e chave, e o ambiente pode ser recriado só com variáveis;
- trocar de provedor é trocar o adaptador;
- os parâmetros (nome do aluno, matrícula) não ficam guardados no editor de um terceiro.

Custo aceito: mudar uma vírgula de um e-mail pede PR e deploy.

### 8.2 Renderizador

| Opção | A favor | Contra |
|---|---|---|
| **A. Modelo com substituição** (recomendada) | arquivos `.html` e `.txt` embutidos em `Helinho.Infrastructure`, com marcadores `{{nome}}`; sem pacote, sem host, testável em teste de unidade; mora ao lado do tratador | sem laço nem condição no modelo |
| B. Razor de e-mail | codificação automática, laços e condições | o motor de views e o `HtmlRenderer` são do ASP.NET Core: o renderizador teria de morar em `Web` e ser chamado da infraestrutura por mais uma porta; maquinaria grande para quatro mensagens sem laço |
| C. Biblioteca de modelos | pronta | pacote novo |

Regras do renderizador:

- **Todo parâmetro é codificado.** No HTML, `HtmlEncoder` em todo valor, sem exceção e sem marcador "cru". No texto e no assunto, quebras de linha e caracteres de controle são removidos do valor.
- **Links são montados em código**, com `Uri.EscapeDataString` em cada parte, e só então entram no modelo (codificados como atributo).
- **Marcador sem valor ou valor sem marcador é erro**, não texto vazio. O teste cobre.
- Teste obrigatório com `<script>` no nome do aluno.

### 8.3 Conteúdo e layout

- Tabela e CSS em linha (clientes de e-mail), cores do tema Recreio (`docs/design/TEMA-PARA-IA.md`), contraste AA, legível com as imagens bloqueadas e no modo escuro.
- **Nenhuma senha em e-mail.** Só link.
- **Logo:** `<img>` apontando para `App__PublicBaseUrl/img/fundhas.png`, com `alt`, largura e altura. Não é anexo. **O arquivo ainda não existe em `wwwroot/img`**, que só tem `.webp`, formato que vários clientes de e-mail não mostram. A entrega do design traz `fundhas.png` e `fundhas-invertido.png` (480×127) como "cópias PNG (reserva, para e-mail/PDF)" (`docs/design/ENTREGA.md:72`), mas eles não foram importados para o repositório. A Issue dos modelos importa `fundhas.png` da entrega, do mesmo jeito que a Issue #124 importou os `.webp`.
- **Rodapé:** identificação da FUNDHAS e um contato. O texto exato (razão social, endereço, canal) é pergunta para o Marcos; até lá, marcador `PROPOSTA`.
- **Sem link de descadastro:** são mensagens transacionais, sem conteúdo promocional (`site-compliance`, seção de e-mails).
- **Rastreamento desligado.** Se a Brevo reescrever os links para contar cliques, o link com o token passa pelo domínio de rastreamento dela e fica nos registros dela. Onde se desliga isso não foi confirmado (seção 17); entra no runbook como passo obrigatório.

## 9. E-mail do responsável (`PROPOSTA`)

Não existe hoje. A lista de campos obrigatórios confirmada pela contratante (C09) não o inclui, e a unicidade de e-mail ficou em aberto (C05).

- **Campo novo `Student.GuardianEmail`**, `varchar(254)`.
- **Obrigatório no formulário** de cadastro e de edição; **anulável no banco**. Os alunos que já existem não têm o dado e inventar um valor seria dado falso. Se a resposta do Marcos for "opcional", muda a validação e não o esquema.
- **Não é único.** Um responsável pode ter mais de um aluno.
- **Por isso não vira identificador de login.** Fica em `Student` e nunca é copiado para `AppUser.Email`. O login do aluno continua com matrícula, usuário e o e-mail **do próprio aluno** quando houver (`LoginIdentityGateway.cs:46-57`). Se o e-mail do responsável fosse para `AppUser.Email`, dois irmãos teriam o mesmo e-mail e `FindByEmailAsync` lançaria exceção no login dos dois (seção 1.1, fato 2).
- **Não sai na API do jogo.** Nenhum DTO muda.
- Validação: formato de e-mail, até 254 caracteres, sem caractere de controle. Seed com endereços sintéticos em `helinho.example`.
- **Migração própria**, a segunda da sprint, depois da migração da fila (duas migrações em paralelo disputam o mesmo snapshot do modelo).

Alternativas descartadas: obrigatório também no banco (pede valor inventado para as linhas existentes); único (impede irmãos); usar o e-mail do responsável como login do aluno (ambíguo entre irmãos e entrega a identidade do aluno a quem controla a caixa de entrada).

## 10. Tokens de link

### 10.1 Provedores e validade

Tokens do Identity, protegidos pelo Data Protection. Conferido em F14: o token carrega o instante de criação, o id do usuário, a finalidade e o **carimbo de segurança**; a validação recusa se passou de `TokenLifespan`, se o usuário ou a finalidade não batem ou se o carimbo mudou.

- **Dois provedores próprios**, subclasses de `DataProtectorTokenProvider<AppUser>`, cada um com seu nome e suas opções (padrão documentado em F16):
  - **convite**, 72 horas: convite de administrador e acesso do aluno (quem inicia é a equipe; o destinatário pode demorar a abrir);
  - **redefinição**, 1 hora: pedido feito por formulário anônimo.
- `AddDefaultTokenProviders` não é usado: ele traria também os provedores de e-mail, telefone e autenticador, que o sistema não usa.
- O registro entra por `Composition/` (os provedores são do framework ASP.NET Core, disponível em `Web`). O tratador, na infraestrutura, só chama `UserManager.GenerateUserTokenAsync`.
- As validades são `PROPOSTA` de negócio (seção 16, D4). O padrão do Identity é 1 dia (F16).

### 10.2 Uso único

Definir a senha passa por `UpdatePasswordHash`, que troca o carimbo de segurança (F14). O token usado, e qualquer outro emitido antes para a mesma conta, deixa de valer.

- **Reenviar o convite invalida o anterior:** o reenvio troca o carimbo (`UpdateSecurityStampAsync`) antes de enfileirar, e cancela convites ainda pendentes da mesma conta.
- **Pedir redefinição não troca o carimbo.** Se trocasse, qualquer pessoa derrubaria a sessão de um administrador só por digitar o e-mail dele. É também a orientação da OWASP: nada muda na conta antes de um token válido ser apresentado (F20). Vários links de redefinição podem coexistir por até 1 hora; o primeiro usado mata os outros.
- Depois da troca, as outras sessões do painel caem em até 30 minutos, que é o intervalo padrão do validador de carimbo do cookie (F16).
- **A redefinição não mexe no bloqueio da conta.** `ResetPasswordAsync` não limpa o bloqueio (F14), e o bloqueio é também o que define "administrador ativo" (`CriarAdministradorHandler.cs:52`). Conta bloqueada recebe o e-mail como qualquer outra e continua bloqueada até o prazo acabar.

### 10.3 Montagem do link

- **Base só de `App__PublicBaseUrl`.** Nunca do cabeçalho `Host` nem de `X-Forwarded-Host`. Como o link é montado no worker, onde não há requisição HTTP, isso vale por construção.
- Formato: `https://<base>/conta/definir-senha?usuario=<id>&token=<token>`. `usuario` é o **id** (UUID), não a matrícula nem o e-mail: nada de dado pessoal na URL. O token vai em Base64 seguro para URL.
- **O `GET` não consome o token.** Filtros de e-mail abrem os links antes da pessoa; só o `POST` com a senha nova usa o token. A página põe o token em campo oculto, responde com `Referrer-Policy: no-referrer` e não carrega recurso de terceiros.
- Token inválido ou vencido leva a uma saída clara ("pedir novo link"), com a mesma mensagem para qualquer causa.

**Token na query string é exceção registrada.** A ASVS 5.0 (14.2.1, citada na skill `owasp-security`) pede que dado sensível não vá em URL. O guia de recuperação de senha da OWASP (F20) descreve justamente o token na query string, com uso único, validade curta, `no-referrer` e sem depender do cabeçalho `Host`, e é assim que o Identity trabalha. Mitigações adotadas: as desta seção, mais um script pequeno que tira o token da barra de endereço depois de carregar a página (`history.replaceState`), para ele não ficar no histórico do navegador. A aplicação não registra a URL das requisições (`Microsoft.AspNetCore` em `Warning`, `appsettings.json:5`); o registro de acesso da Railway é o item 11 da seção 17. Alternativa descartada: token no fragmento (`#`), que nunca chega ao servidor mas exige JavaScript para funcionar e depende de cada cliente e filtro de e-mail preservar o fragmento, com um público de responsáveis que não tem a quem recorrer se o link quebrar. Aceitar a exceção é decisão do Henrique (D13).

### 10.4 Dependência do Data Protection

Os tokens são cifrados com as chaves do Data Protection. Sem persistência, **cada deploy invalida todos os links já enviados** (ADR-0002 §9), o que é inaceitável para um convite de 72 horas. Os fluxos desta sprint dependem do PR #120 (Issue #103) mergeado. O nome da aplicação (`SetApplicationName`) não pode mudar: trocá-lo também invalida o que foi emitido (F17). A rotação automática das chaves (90 dias por padrão, F17) não atrapalha: chave vencida continua lendo o que cifrou.

## 11. Fluxos

Todos passam pela fila. Nenhum leva senha.

### 11.1 Redefinição de senha sem denunciar a conta

`RecuperarSenha` precisa responder igual, e em tempo parecido, exista a conta ou não.

1. **A parte cara sai da requisição.** Gerar token, renderizar e chamar a Brevo ficam no worker. A requisição faz, no máximo, uma consulta e uma inserção.
2. **Mesma sequência nos dois casos:** uma consulta que procura contas do painel com aquele e-mail. Se houver exatamente uma, enfileira. Nenhuma ou mais de uma, não faz nada. A consulta não usa `FindByEmailAsync`, que lança exceção com e-mail repetido.
3. **Piso de tempo:** a resposta só sai depois de um tempo mínimo fixo (250 ms), contado do início. A inserção a mais do caso "conta existe" fica escondida embaixo do piso. O teste usa relógio falso e verifica que os dois caminhos passam pelo piso; não mede milissegundos reais.
4. **Limite por IP:** 5 pedidos a cada 15 minutos; estourou, 429 para qualquer e-mail digitado.
5. **Limite por conta:** 3 por hora, contado na fila. Estourou, **a resposta é a mesma de sempre** e nada é enfileirado. Um 429 por conta revelaria que ela existe.
6. Corpo, status e cabeçalhos idênticos nos dois casos; teste obrigatório.

Alternativa descartada: enfileirar sempre, com o endereço digitado no trabalho, e descobrir no worker se a conta existe. Guardaria endereços digitados por anônimos e deixaria qualquer um encher a tabela.

Continuam elegíveis só contas `Master` e `Admin`. Aluno não pede redefinição: quem reenvia o acesso é o administrador (11.4).

### 11.2 Convite de administrador

- O master **não digita senha**. `NewAdminAccount` perde o campo `Password` (`IAdminAccountGateway.cs:8`), a conta nasce com `CreateAsync(user)` e o convite é enfileirado na mesma transação do lock da unidade.
- **Situações na lista**, derivadas do que já existe (sem coluna nova em `AspNetUsers`):

| Situação | Condição |
|---|---|
| Aguardando envio | sem senha; convite mais recente em `Pending` ou `Processing` |
| Convite enviado | sem senha; convite mais recente `Succeeded` há menos de 72 horas |
| Convite expirado | sem senha; convite com mais de 72 horas, ou nenhum registro |
| Não entregue | sem senha; convite `Failed` ou com `Feedback` de devolução |
| Ativo | com senha e sem bloqueio |
| Inativo | com bloqueio vigente (como hoje) |

- **Reenviar convite:** confere o limite (3 por hora por conta), troca o carimbo, cancela convites pendentes e enfileira o novo.
- Uma conta convidada conta como administrador da unidade, como hoje (ativo = sem bloqueio): não dá para convidar dois para a mesma unidade.
- **E-mail único entre contas do painel**, conferido no caso de uso sob o lock (sem migração). `RequireUniqueEmail` continua `false` porque alunos podem não ter e-mail.
- **Cancelar convite** (apagar a conta que nunca teve senha) entra na mesma Issue: sem isso, um e-mail digitado errado prende a unidade, já que não existe tela de edição de administrador.

### 11.3 Página `/conta/definir-senha`

Uma página só para convite, redefinição e acesso do aluno, na pasta `/Conta`, que já é anônima (`Panel.cs:57`). Política de senha do Identity, confirmação e erros no padrão `#resumo-erros`. O token é validado contra os dois provedores; nada na URL diz o tipo. Depois de definir a senha, enfileira o aviso "sua senha foi alterada" (para o responsável, no caso do aluno) e manda para a tela de entrar: **não faz login automático** (F20). Limite por IP no `POST`: 10 a cada 15 minutos.

A política de senha em vigor é a padrão do Identity: 6 caracteres, com maiúscula, minúscula, dígito e símbolo (F21; `Panel.cs` não a altera). A ASVS 5.0 pede o contrário em dois pontos: mínimo de 8 e nenhuma regra de composição (6.2.1 e 6.2.5), além de recusar senhas comuns (6.2.4). Esta sprint **não muda a política**: mudá-la mexe no seed, nos testes e na mensagem de erro de todas as telas, e é assunto próprio (D14).

### 11.4 Acesso do aluno, para o responsável (`PROPOSTA`)

- **Quem define a senha do aluno é o responsável, pelo link.** Nenhuma senha passa por e-mail nem pelas mãos da equipe. Alternativa descartada: o administrador define uma senha provisória e a entrega em papel; a equipe passaria a conhecer a senha da criança. Essa alternativa volta a valer se a resposta do Marcos for que há responsáveis sem e-mail: hoje não existe **nenhum** caminho no painel para um aluno ganhar senha (seção 1.1).
- **Gatilhos:** a aprovação passar de falso para verdadeiro em `SaveStudent`, ou o administrador clicar em **Enviar acesso**.
- **Sem aprovação não envia**, conferido ao enfileirar e de novo ao enviar. Sem `GuardianEmail`, o botão explica o que falta.
- **Só alunos da própria unidade.** O caso de uso consulta pelo filtro de unidade que já existe (ADR-0001, T1); aluno de outra unidade responde como inexistente (404). Teste obrigatório.
- **Conteúdo mínimo:** primeiro nome do aluno, a matrícula (necessária para entrar no jogo) e o link. Sem sobrenome, turma, unidade ou documento.
- Limite: 3 por hora por aluno. Validade do link: 72 horas.

### 11.5 Aviso de bloqueio de conta

Opcional, só se sobrar orçamento: e-mail ao administrador quando o Identity bloquear a conta. Usa a mesma fila; não tem decisão própria.

### 11.6 Limites de taxa: onde ficam

| Onde | Limite | Mecanismo |
|---|---|---|
| `POST /recuperar-senha`, por IP | 5 em 15 minutos | limitador do ASP.NET Core, na memória |
| Redefinição, por conta | 3 por hora | contagem em `QueueJobs`; resposta inalterada |
| `POST /conta/definir-senha`, por IP | 10 em 15 minutos | limitador |
| Reenviar convite, por conta | 3 por hora | contagem em `QueueJobs` |
| Enviar acesso, por aluno | 3 por hora | contagem em `QueueJobs` |
| `POST /webhooks/brevo`, por IP | 60 por minuto | limitador |

O IP vem de `X-Real-IP`, já tratado pela área Hospedagem (ADR-0002 §8). Em Razor Pages o atributo de limite vale para a página inteira, não por handler (F18); a política deixa o `GET` sem limite. O limitador em memória é por instância (hoje há uma); os limites por conta ficam no banco e sobrevivem a reinício.

## 12. Webhook da Brevo (preparado, desligado)

Confirmado em F9, F10 e F11:

- A Brevo chama a URL configurada com **HTTP POST**, um evento por chamada. O corpo traz `event`, `email`, `message-id`, `date`, `ts_event`, `subject`, `tags`, `reason` e outros.
- Valores de `event`: `request`, `delivered`, `deferred`, `soft_bounce`, `hard_bounce`, `blocked`, `spam`, `invalid_email`, `error`, `click`, `opened`, `unique_opened` e `unsubscribed`, entre outros. Ao **criar** o webhook, os nomes são outros (`hardBounce`, `softBounce`, `blocked`, `spam`, `invalid`, `deferred`, `delivered`…).
- **Não há assinatura** (HMAC) do corpo. As formas de proteção documentadas são: usuário e senha na própria URL; `auth: { "type": "bearer", "token": "…" }`; cabeçalhos próprios (`headers: [{ "key", "value" }]`); e restrição pelas faixas de IP da Brevo.
- Repetição: 4 novas tentativas (10 minutos, 1 hora, 2 horas e 8 horas depois). Resposta 4xx (menos 429) ou 5xx **encerra as tentativas e descarta o evento**.

Decisão:

- `POST /webhooks/brevo`, fora de `/api` e fora da descrição OpenAPI do jogo. **Só é mapeado se `Brevo__WebhookSecret` estiver preenchido**; vazio, a rota não existe (404).
- **Segredo no cabeçalho `Authorization: Bearer <segredo>`**, comparado em tempo constante (`CryptographicOperations.FixedTimeEquals` sobre o SHA-256 dos dois valores, para não vazar nem o tamanho). Errado ou ausente: 401 sem corpo.
- **Segredo na URL descartado:** a URL fica em registro de acesso da borda e no painel da Brevo em texto.
- **Lê só `event` e `message-id`.** `email`, `subject` e `reason` são ignorados: não são gravados nem logados.
- `hard_bounce`, `blocked`, `invalid_email` e `spam` preenchem `Feedback` do trabalho com aquele `ExternalId`; os outros eventos são aceitos e ignorados. `message-id` desconhecido também responde 200.
- O `messageId` da resposta de envio vem entre `<` e `>` e o `message-id` do webhook aparece sem eles nos exemplos: os dois são guardados e comparados **sem** os sinais.
- Idempotente: o mesmo evento duas vezes dá o mesmo estado. Corpo limitado a 64 KB.
- Com `Feedback`, a lista de administradores e a ficha do aluno mostram "e-mail não entregue".
- Como um 5xx nosso faz a Brevo desistir do evento, a marcação de devolução é **melhor esforço**: serve de aviso na tela, não de registro oficial de entrega.

## 13. Variáveis e Terraform

| Variável | Sensível | Situação no Terraform (PR #100) | O que a sprint faz |
|---|---|---|---|
| `Email__Provider` | não | declarada, padrão `Fake` | nada |
| `Brevo__ApiKey` | sim | `brevo_api_key`, com validação "Brevo exige a chave" | nada |
| `Brevo__SenderEmail`, `Brevo__SenderName` | não | declaradas, vazias | validação: obrigatórias com `Brevo` |
| `App__PublicBaseUrl` | não | declarada, vazia | derivar do domínio do serviço quando vazia (ADR-0002 §10.1 já previa) e exigir `https://` |
| `Brevo__WebhookSecret` | sim | **não existe** | criar `brevo_webhook_secret` (`sensitive`, padrão vazio, 32 caracteres ou mais quando preenchida) |
| `Email__DailyLimit` | não | não existe | acrescentar a `variaveis_web`, padrão vazio |

Tudo com descrição e com exemplo **sem valor real** em `terraform.tfvars.example`. Os segredos vêm de `TF_VAR_*`, como os demais. Se o PR #100 não estiver mergeado quando a Issue começar, ela cria as variáveis no mesmo padrão.

**Restrição da chave por IP:** a Brevo permite aceitar chamadas só de IPs autorizados (F8). Na Railway isso provavelmente **não serve**: IP de saída fixo é recurso do plano Pro e, mesmo nele, o endereço pode ser compartilhado com outros clientes e muda com a região (F19). O runbook manda conferir se o bloqueio de IP desconhecido está ligado na conta e desligá-lo, senão a primeira chamada falha (seção 17, item 6).

## 14. Segurança

Ameaças novas desta sprint, na numeração do ADR-0001 (T1 a T6):

| # | Ameaça | Mitigação |
|---|---|---|
| T7 | Descobrir quais e-mails têm conta pela recuperação de senha (resposta, tempo, erro ou limite) | 11.1: mesma resposta, piso de tempo, limite por conta silencioso, consulta que não quebra com e-mail repetido |
| T8 | Link de redefinição apontando para o domínio do atacante (cabeçalho `Host` forjado) | 10.3: base só de `App__PublicBaseUrl`, montada fora de qualquer requisição |
| T9 | Token de acesso lido no banco, em backup, em log ou em URL registrada | 5.3: token não existe em repouso; 5.6: nada de conteúdo em log; 10.3: token só consumido por `POST`, sem `Referer`; validade curta e uso único |
| T10 | HTML ou script injetado no e-mail por um campo do cadastro | 8.2: todo parâmetro codificado; teste com `<script>` |
| T11 | Chamada forjada ao webhook marcando mensagens como devolvidas | 12: segredo de 32 caracteres ou mais, tempo constante, rota inexistente sem segredo |
| T12 | Uso do sistema para encher a caixa de alguém ou gastar a cota de envio | 11.6: limites por IP e por conta; 15: teto diário |
| T13 | Administrador de uma unidade envia o acesso de aluno de outra, ou troca o e-mail do responsável para assumir a conta do aluno | 11.4: filtro de unidade e teste; troca de `GuardianEmail` e envio de acesso registrados em log de auditoria sem dado pessoal (id de quem fez e id do aluno) |
| T14 | Chave da Brevo vazada | só em variável da Railway e `TF_VAR_*`; cabeçalho sensível no log; rotação e resposta a vazamento no runbook |
| T15 | Filtro de e-mail abre o link e gasta o token antes da pessoa | 10.3: `GET` sem efeito |
| T16 | E-mail do responsável digitado errado entrega o acesso a um estranho | 11.4: conteúdo mínimo, validade de 72 horas, reenvio invalida; não há confirmação de posse do endereço nesta sprint (risco aceito, registrado) |

Fica para o ADR de dado real (ADR-0001 §7): contrato de tratamento de dados com a Brevo, transferência internacional (os dados passam pela Brevo e pela Railway, fora do Brasil), prazo de guarda dos registros na Brevo e a página de política de privacidade citada no rodapé dos e-mails.

## 15. Limite do plano gratuito e pico de convites

**Limite, conferido em F12 em 19/11/2026:**

- Plano gratuito: **300 e-mails por dia**, somando campanhas e transacionais ("Daily email volume (campaigns & transactional): 300 emails/day"). A conta só envia depois de aprovada pela Brevo.
- Plano Starter: a partir de **US$ 9 por mês**, com 5.000 e-mails por mês. A página diz "Upgrade to remove daily sending limit".
- Créditos pré-pagos: "No daily sending limit, no expiration date".
- **O que acontece ao estourar:** segundo o resumo do artigo oficial da central de ajuda (lido só pelo resultado de busca; a página devolveu 403), até 1.000 e-mails transacionais além do limite ficam numa fila da Brevo e são enviados quando a cota volta; o que passar disso não é entregue. A resposta da API nessa hora não foi confirmada (seção 17, item 1).

**Pico:** C13 não informa o volume. Com premissas explícitas, não medidas:

| Evento | E-mails |
|---|---|
| Aluno ganha acesso | 2 (acesso + aviso de senha alterada) |
| Administrador convidado | 2 (convite + aviso) |
| Redefinição | 2 |

O plano gratuito comporta cerca de **150 alunos liberados por dia**. O ADR-0001 fala em "algumas centenas de alunos". Se uma unidade aprovar a base inteira no mesmo dia, o gratuito não basta; se o cadastro for por turma, ao longo de semanas, basta com folga.

**Decisão técnica:** teto diário configurável (`Email__DailyLimit`, `0` = sem teto). Com teto, o worker conta os envios das últimas 24 horas e, ao chegar nele, adia os e-mails para a hora seguinte sem gastar tentativa. Ao passar de 80% do teto, só saem os tipos urgentes (redefinição e convite de administrador), para que um lote de acessos de alunos não deixe um administrador sem conseguir entrar. É por isso que o acesso do aluno tem `ExpiresAt` de 72 horas.

**Recomendação de plano (D7):** gratuito na homologação e no teste de fumaça, com teto de 300; Starter ou créditos pré-pagos antes da primeira carga de alunos reais. O gasto é decisão da contratante (C04).

## 16. Decisões que esperam o Henrique

| # | Decisão | Recomendação | Alternativa principal |
|---|---|---|---|
| D1 | Formato da fila | **Uma tabela genérica `QueueJobs`** (5.1) | `email_outbox` + tabela irmã para IA (5.2) |
| D2 | O que fica gravado | **Só referências; token gerado no envio** (5.3) | corpo renderizado com o link em repouso |
| D3 | Erros de conta (401, 402, 403) | **Suspender sem gastar tentativa** (6.3) | falha direta, como os outros 4xx |
| D4 | Validade dos links | **Convite e acesso do aluno 72 h; redefinição 1 h** (`PROPOSTA`) | 24 h para tudo (padrão do Identity) |
| D5 | E-mail do responsável | **Obrigatório no formulário, anulável no banco, não único, nunca login** (`PROPOSTA`, seção 9) | esperar a resposta do Marcos antes de migrar |
| D6 | Quem define a senha do aluno | **O responsável, pelo link** (`PROPOSTA`, 11.4) | o administrador define uma senha provisória |
| D7 | Plano da Brevo e teto diário | **Gratuito com `Email__DailyLimit=300` em homologação; pago antes de carga real** (15) | sem teto na aplicação, confiando na fila da Brevo |
| D8 | Como abrir o link com o envio `Fake` em desenvolvimento | **Caixa de saída de desenvolvimento:** o fake guarda as últimas mensagens em memória e uma página só existente em `Development` as mostra; teste garante 404 fora dele | não fazer nada (localmente só se usam os usuários do seed) |
| D9 | Retenção da fila | **30 dias** depois de terminado (`PROPOSTA`, 5.6) | 90 dias |
| D10 | Renderizador | **Modelo com substituição** (8.2) | Razor de e-mail |
| D11 | Cancelar convite e e-mail único entre contas do painel | **Incluir na Issue do convite** (11.2) | deixar para depois e corrigir e-mail errado direto no banco |
| D12 | Envio automático ao aprovar o aluno | **Sim, além do botão** (11.4) | só pelo botão |
| D13 | Token do link na query string, exceção à ASVS 14.2.1 | **Aceitar, com as mitigações da seção 10.3** (é o padrão do Identity e do guia da OWASP) | token no fragmento (`#`), com JavaScript obrigatório |
| D14 | Política de senha (hoje 6 caracteres com regras de composição) | **Não mudar nesta sprint; abrir Issue própria** para alinhar à ASVS (mínimo 8, sem composição, lista de senhas comuns) | mudar junto com a página de definir senha |
| D15 | Revisar o ADR-0001, que diz "não há caso para […] fila" (`ADR-0001-stack.md:23`) | **Aceitar a revisão:** agora há caso para fila (e-mail com repetição; trabalhos de IA na Sprint 4). Sem Redis e sem broker: a fila é uma tabela no PostgreSQL (5.0) | manter o ADR-0001 como está e enviar o e-mail direto da requisição, sem repetição |
| D16 | Ajustes pedidos pelo ADR-0004 na fila: política por tipo, laço por tratador, contrato do tratador, gravação única, gancho de encerramento | **Aceitar os sete** (6.8) | fila só para e-mail, com a IA contornando do lado dela (ADR-0004, coluna "se não for aceito") |

D4, D5, D6, D9, D12 e D14 dependem também do Marcos; as perguntas estão em `docs/context/pendencias.md`. O código nasce com a recomendação marcada `PROPOSTA`, e trocar qualquer uma delas não pede migração (D5 inclusive: a coluna é anulável).

**O que não depende destas decisões e pode andar em paralelo:** adaptador HTTP com as opções e a validação na subida, renderizador e modelos, provedores de token e a página `/conta/definir-senha`, variáveis do Terraform e runbook.

## 17. O que não foi possível confirmar em fonte

1. **O que a API responde quando o limite diário do plano gratuito estoura** (201 com fila do lado da Brevo, 400 `not_enough_credits` ou 402). O tratamento da seção 6.3 cobre as três hipóteses.
2. **Se `htmlContent` e `textContent` podem ir juntos.** A referência (F1) lista os dois campos, com `textContent` opcional; o guia (F2) diz que se escolhe um dos três tipos de corpo por requisição. O teste de fumaça confere, no código-fonte da mensagem recebida, se saíram as duas partes.
3. **Idempotência em envio simples.** O guia (F6) apresenta a chave ao falar de lote e escreve `idempotencyKey`; a referência (F1) escreve `IdempotencyKey` e lista também o código `duplicate_request`. O adaptador aceita os dois códigos. Sem a garantia, o pior caso é uma mensagem repetida depois de um tempo esgotado.
4. **Corpo das respostas 401 e 403 e o comportamento em 5xx**: não documentados. A referência (F1) também lista um cabeçalho `Authorization` (OAuth) como obrigatório ao lado de `api-key`; os exemplos oficiais usam só `api-key`, que é o que adotamos.
5. **Webhook:** qual cabeçalho a Brevo envia com `auth.type = bearer` (supõe-se `Authorization: Bearer`); se a autenticação pode ser configurada pela tela ou só pela API; o `Content-Type`; qual resposta ela considera sucesso; o tempo limite. Plano B se o cabeçalho for outro: usar `headers` próprios com a chave `Authorization`.
6. **Se o bloqueio de IP desconhecido vem ligado em conta nova** e qual erro a API devolve nesse caso.
7. **Onde se desliga o rastreamento de cliques e aberturas** nos e-mails transacionais, e se dá para desligar por mensagem.
8. **Se o plano gratuito acrescenta a marca da Brevo** em e-mail transacional enviado pela API.
9. **Os registros DNS exatos.** A documentação de desenvolvedor (F13) mostra código de verificação `TXT`, DKIM em `TXT` e DMARC em `TXT`; resultados de busca da central de ajuda falam em dois `CNAME` de DKIM. O runbook manda copiar os registros que o painel da Brevo mostrar para o domínio, e não os deste ADR. Se o SPF ainda é exigido também não foi confirmado.
10. **Quanto tempo a Brevo guarda registros e conteúdo** das mensagens e onde.
11. **Se o registro de acesso HTTP da Railway guarda a query string.** Se guardar, o token passa por lá; a mitigação é uso único e validade curta.
12. **`help.brevo.com` respondeu 403** à leitura automática. Os itens 1, 6, 7 e 9 são conferidos pelo Henrique no navegador, seguindo o runbook.

## 18. Plano de Issues da sprint

**Só as Issues 1 e 2 geram migração. Nenhuma acrescenta pacote.** As duas migrações só acrescentam (uma tabela nova e uma coluna anulável): a versão anterior da aplicação continua funcionando sobre o banco migrado, como pede a `devops-standards`.

| # | Issue | Tipo | Depende de |
|---|---|---|---|
| 0 | ADR-0003 (esta, #176) | docs | — |
| 1 | Fila: entidade `QueueJob`, **migração**, porta `IJobQueue` (enfileirar com idempotência e cancelar pendentes) e os contratos das seções 6.6 e 6.7 (`IJobHandler`, `JobContext`, `JobPolicy`, `JobResult`, `IAbandonedJobHandler`) | feat | D1, D2, D15 e D16; PR #120 mergeado (mesmo snapshot do modelo) |
| 2 | `Student.GuardianEmail`: **migração**, campo no formulário do aluno, validação, seed sintético | feat | D5; Issue 1 (ordem das migrações); PR #158 (formulário de Alunos no tema Recreio) |
| 3 | `BrevoEmailSender`, opções validadas na subida, registro `Fake`/`Brevo` na área `Email`, nova assinatura da porta; testes com `HttpMessageHandler` falso | feat | ADR aprovado |
| 4 | Renderizador, modelos HTML e texto dos quatro e-mails, layout comum, importação do `fundhas.png` | feat | D10 |
| 5 | Provedores de token (72 h e 1 h) e página `/conta/definir-senha` | feat | D4; PR #159 (telas de conta no tema Recreio) |
| 6 | Terraform: `brevo_webhook_secret`, `Email__DailyLimit`, validações, `terraform.tfvars.example` | chore | PR #100 mergeado |
| 7 | `docs/operations/brevo-runbook.md` e atualização de `ambientes.md` | docs | ADR aprovado; revisão "no papel" por quem não escreveu |
| 8a | Worker genérico: `BackgroundService` com um laço por tratador, `IJobProcessor` (captura por tipo com `SKIP LOCKED`, política por tipo, gravação única com guarda de posse, expiração, suspensão, gancho de encerramento, limpeza); testes com um tratador falso | feat | 1; D3 e D16 |
| 8b | Tratador de e-mail: resolve o destinatário, gera o token, renderiza, envia, classifica a resposta da Brevo e aplica o teto diário | feat | 1, 3, 4, 5, 8a; D7 |
| 9 | Convite de administrador: criar sem senha, situações, reenviar, limite, cancelar convite | feat | 1, 4, 5, 8b; D11; PR #166 |
| 10 | Redefinição real em `RecuperarSenha` (fila, resposta idêntica, piso de tempo, limites) e aviso de senha alterada | feat | 1, 4, 5, 8b, 13 |
| 11 | Acesso do aluno: botão, gatilho na aprovação, segregação por unidade | feat | 1, 2, 4, 5, 8b; D6 e D12 |
| 12 | Webhook da Brevo e "e-mail não entregue" nas telas | feat | 1, 3; as telas, depois de 9 e 11 |
| 13 | Correção: contas com e-mail repetido quebram `FindByEmailAsync` no login e na recuperação (**já aberta: #184**, sem `agent:authorized`) | fix | — |
| 14 | Caixa de saída de desenvolvimento | feat | 3; D8 |
| 15 | Aviso de bloqueio de conta (opcional) | feat | 8b |
| 16 | Teste de ponta a ponta com capturador de e-mail | test | 9, 10, 11 |

Ondas: **1** em paralelo — Issues 1, 3, 4, 5, 6, 7 e 13 (a 2 e a 8a entram logo depois da 1). **2** — Issues 8b a 12 e 14. **3** — Issue 16 e a revisão independente de cada PR, com `security-reviewer` e `compliance-reviewer` obrigatórios nos fluxos e nos modelos.

A antiga Issue 8 foi dividida em duas. O worker genérico (8a) cresceu com os ajustes do ADR-0004 e não depende de nada de e-mail; o tratador de e-mail (8b) é o que depende do adaptador, dos modelos e dos tokens. **A Sprint 4 só precisa das Issues 1 e 8a**: pode começar o tratador de IA sem esperar os e-mails.

A Issue 13 (#184) é defeito que já existe na `main` e independe da sprint; está aqui porque a Issue 10 esbarra nele. As outras Issues da tabela ainda não foram abertas: esperam o aceite deste ADR.

## 19. Alternativas descartadas

| Alternativa | Por que não |
|---|---|
| SDK oficial da Brevo | pacote novo para uma chamada HTTP |
| SMTP da Brevo | exige cliente SMTP e uma segunda credencial (a chave SMTP não é a chave da API), e não devolve `messageId` nem erro estruturado para a fila classificar |
| Modelos no editor da Brevo | seção 8.1 |
| Enviar direto da requisição, sem fila | a página ficaria presa ao tempo da Brevo, sem repetição, e o tempo de resposta denunciaria a conta |
| Fila em memória (`Channel`) | perde tudo num deploy: um convite enfileirado sumiria sem deixar rastro |
| Redis, broker ou serviço de fila | mais um serviço para pagar e operar, com um mantenedor só. O ADR-0001 diz que "um PostgreSQL único resolve" (`ADR-0001-stack.md:23`), e a fila em tabela mantém isso |
| Renovar a posse a cada item | seção 6.8 (A4) |
| `email_outbox` específica; Hangfire ou Quartz | seção 5.2 |
| Corpo renderizado na fila | seção 5.3 |
| `FrameworkReference` do ASP.NET Core na infraestrutura | seção 4.4 |
| Razor de e-mail; biblioteca de modelos | seção 8.2 |
| `AddDefaultTokenProviders` | seção 10.1 |
| Token próprio (aleatório, com hash em tabela) | mais uma migração e mais código nosso em caminho sensível, para refazer o que o Identity entrega |
| Token do link no fragmento da URL | seção 10.3 |
| Segredo do webhook na URL; lista de IPs da Brevo | seção 12; as faixas mudam e são mais um item a manter |
| E-mail do responsável único, ou como login do aluno | seção 9 |
| Senha provisória do aluno definida pelo administrador | seção 11.4 |

## 20. Consequências

**Positivas**

- Nenhum pacote novo e nada criado à mão na Brevo além de conta, domínio, remetente e chave.
- Nenhum link de acesso nem endereço de destinatário em repouso; a fila guarda só referências.
- Um mecanismo de fila para e-mail e para os trabalhos de IA da Sprint 4, já confrontado com os requisitos do ADR-0004 (seção 6.8).
- Sem chave, tudo continua funcionando com o envio fake; com configuração pela metade, a aplicação recusa subir em vez de falhar no primeiro e-mail.
- Uma chave errada ou uma cota esgotada não queima a fila.
- Há um freio simples: voltar `Email__Provider` para `Fake` desliga o envio real sem mudar código.

**Negativas e riscos**

- Revisa um ponto de um ADR já aceito (ADR-0001, "não há caso para […] fila"); a revisão precisa do aceite do Henrique (D15).
- Duas migrações em uma sprint, em sequência obrigatória, e dependência do PR #120.
- O worker fica mais complexo que um repassador: um laço e uma política por tratador, quatro classes de resultado, gravação única com guarda de posse, gancho de encerramento e teto diário. Por isso virou duas Issues (8a e 8b).
- O banco não registra para qual endereço cada mensagem foi; essa resposta está no painel da Brevo.
- Com o envio fake, convite e redefinição não se completam em homologação até a Brevo ser configurada.
- Os itens da seção 17 dependem de conferência manual; o runbook e o teste de fumaça precisam cobri-los.
- Várias regras nascem como `PROPOSTA` (seção 16); uma resposta diferente do Marcos muda validação e textos, não o esquema.
- Não há confirmação de que o responsável controla o endereço informado (T16).
