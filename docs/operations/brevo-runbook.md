# Runbook — envio real de e-mails com a Brevo

**Para quem:** Henrique, sozinho, no navegador e na própria máquina (macOS). **Escrito em:** 19/11/2026 (Issue #206). **Revisto em:** 22/11/2026, depois do aceite do ADR-0003.
**Descreve:** o ADR-0003 (`docs/architecture/ADR-0003-emails-brevo.md`, PR #185, **aceito pelo Henrique em 22/11/2026**, decisões D1 a D18; lido no commit `589d9ff`), o Terraform de `infra/railway/` (PRs #100, #322 e #330, já na `main`) e o [runbook da Railway](railway-runbook.md). Parte do código que envia e-mail ainda está em entrega (PRs abertos, sem data); veja [Antes de começar](#antes-de-começar).

> **Nada deste runbook foi executado.** Nenhum agente criou conta, domínio, remetente ou chave, nem chamou a API da Brevo. Os passos de painel foram conferidos na documentação da Brevo e da Railway em 19/11/2026 (seção [Fontes](#fontes); as marcas `[F1]`, `[F2]`… apontam para ela). O painel da Brevo é em inglês e muda com frequência: se a tela não bater com o texto, vale a tela. Onde a documentação não confirma um nome ou um comportamento, o passo traz **Confira:**; o índice está em [Fatos não confirmados](#fatos-não-confirmados-e-onde-cada-um-é-tratado).

Só dados sintéticos em homologação. A única exceção deste runbook é o seu próprio e-mail, no teste de fumaça.

## O caminho feliz em 10 linhas

1. **Pré-requisitos:** ADR-0003 aceito em 22/11/2026; homologação no ar pelo runbook da Railway, com o domínio próprio do painel; adaptador, modelos, tokens, página de definir senha e a fila (tabela, porta e processador) já na `main`; faltam, em entrega, o worker (Issue #228), o tratador de e-mail e os fluxos (convite, redefinição e acesso do aluno). Sem eles, ligar a Brevo não envia nada. → [Antes de começar](#antes-de-começar)
2. **Conta e plano:** entrar na conta Brevo da contratante; o plano gratuito envia **300 e-mails por dia**; antes de qualquer envio, ligar o rastreamento anônimo, desligar as prévias e pôr a retenção em 1 mês. → [Passo 1](#passo-1--conta-e-plano)
3. **Domínio:** **Settings → Senders, Domains, IPs → Domains → Add a domain**; mandar os registros (código da Brevo, DKIM e DMARC) a quem administra o DNS da FUNDHAS; conferir com `dig`; concluir a autenticação. → [Passo 2](#passo-2--autenticar-o-domínio-institucional)
4. **Remetente:** **Settings → Senders, Domains, IPs → Senders → Add a sender**. → [Passo 3](#passo-3--criar-o-remetente)
5. **Chave de API v3:** **Settings → SMTP & API → API Keys & MCP → Generate API key**, acesso restrito, validade de 1 ano, direto para o gerenciador de senhas; desligar o bloqueio de IP desconhecido. Não é a chave SMTP. → [Passo 4](#passo-4--chave-de-api-v3)
6. **Medição do link: obrigatória antes de ligar a Brevo.** Um envio de teste por `curl` para o seu e-mail, com link de exemplo; anotar se o link chega reescrito, para qual domínio, se a URL chega inteira e o que fica nos registros. É a condição 1 da D17. **Sem o resultado registrado, não faça a linha 7.** → [Passo 4b](#passo-4b--envio-de-medição-do-link)
7. **Terraform, só com a linha 6 feita:** `variaveis_web` no `terraform.tfvars` (`Email__Provider = "Brevo"`, remetente e nome; `App__PublicBaseUrl` vem do `dominio_proprio`), `read -rs TF_VAR_brevo_api_key`, `plan` (1 mudança) e `apply`, que reimplanta o `helinho-web`; backup cifrado do estado; deploy verde e `/health/ready` 200. → [Passo 5](#passo-5--variáveis-na-railway-pelo-terraform)
8. **Webhook (opcional):** segredo no cabeçalho próprio `X-Helinho-Webhook-Secret`, quatro eventos; só depois da Issue do webhook e da Issue #229, que cria `brevo_webhook_secret` no Terraform. → [Passo 6](#passo-6--webhook-transacional-opcional)
9. **Teste de fumaça, só depois que os fluxos existirem:** administrador de teste com o seu e-mail, convite, senha, login, redefinição, sempre a partir do e-mail recebido; conferir a fila e **Transactional → Logs**. → [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta)
10. **Emergência:** `Email__Provider = "Fake"` e novo `apply`. **Chave vazou:** apague-a na Brevo primeiro, depois troque na Railway. → [Operação](#operação) e [Passo 8](#passo-8--rotação-da-chave-e-vazamento)

## Variável → onde pegar → onde colocar

| Variável da aplicação | Onde pegar na Brevo | Onde colocar | Segredo? |
|---|---|---|---|
| `Email__Provider` | Não vem da Brevo. Valor fixo `Brevo`; `Fake` desliga o envio real | `infra/railway/terraform.tfvars`, dentro de `variaveis_web` | não |
| `Brevo__ApiKey` | **Settings → SMTP & API → API Keys & MCP → Generate API key**; o valor aparece uma única vez ([4.2](#42-criar-a-chave)) | Gerenciador de senhas. No uso: `read -rs TF_VAR_brevo_api_key` no terminal do Terraform. Nunca em `tfvars`, arquivo, chat, Issue ou PR | **sim** |
| `Brevo__SenderEmail` | **Settings → Senders, Domains, IPs → Senders**: o campo **From email** do remetente criado no [Passo 3](#passo-3--criar-o-remetente), letra por letra | `terraform.tfvars`, em `variaveis_web` (o arquivo fica fora do git) | não |
| `Brevo__SenderName` | O campo **From name** do mesmo remetente (até 70 caracteres) | `terraform.tfvars`, em `variaveis_web` | não |
| `App__PublicBaseUrl` | Não vem da Brevo. É `https://` mais o domínio próprio do painel (no exemplo, `https://paineladm.example.org`) | Você **não** escreve: o Terraform a monta a partir de `ambientes.homologacao.dominio_proprio`, no `terraform.tfvars` (PR #330). Só preencha `variaveis_web.App__PublicBaseUrl` para um valor diferente; o explícito vence | não |
| `Brevo__WebhookSecret` | Não vem da Brevo: você gera (32 caracteres ou mais) e cola nos dois lados. Na Brevo, em **Integrations → Webhooks**, como valor do cabeçalho próprio `X-Helinho-Webhook-Secret` ([Passo 6](#passo-6--webhook-transacional-opcional)) | Gerenciador de senhas; no uso, `read -rs TF_VAR_brevo_webhook_secret` (variável `brevo_webhook_secret`, **em entrega: Issue #229, PR #333 aberto**). Vazia, não é enviada e o webhook fica desligado. Até o PR entrar, não crie à mão no painel da Railway | **sim** |
| `Email__DailyLimit` | O limite diário do plano ([1.2](#12-plano-e-limite-diário)): `300` no gratuito, ou menos se a conta também fizer outros envios | `terraform.tfvars`, em `variaveis_web`, **quando o PR #333 (Issue #229) entrar** (padrão `300`: só escreva para baixar). Na `main` de 25/11/2026 a chave ainda não existe em `variaveis_web`; precisa estar aplicada antes de qualquer envio em volume (ADR-0003 §13) | não |
| Chave temporária da medição | A mesma tela da chave de API, com outro nome e validade de 7 dias ([Passo 4b](#passo-4b--envio-de-medição-do-link)) | Só na variável `BREVO_KEY` do terminal, lida com `read -rs`; apagada na Brevo ao fim da medição. **Não vai para a Railway** | **sim** |
| Registros DNS (código da Brevo, DKIM, DMARC) | **Settings → Senders, Domains, IPs → Domains** | No DNS do domínio institucional, por quem o administra. Não vão para o repositório | não |

## Antes de começar

**O que precisa estar na `main` e publicado para o envio real funcionar:**

| O quê | Onde está | Sem ele |
|---|---|---|
| ADR-0003 aceito | **Cumprido:** aceito pelo Henrique em 22/11/2026, decisões D1 a D18 (ADR-0003 §16). O PR #185 ainda precisa chegar à `main` | — |
| Terraform de `infra/railway/` e homologação no ar | **Na `main`** (PRs #100, #322, #323 e #330); [runbook da Railway](railway-runbook.md), passos 1 a 7 | Não há onde pôr as variáveis |
| Domínio próprio do painel | `ambientes.homologacao.dominio_proprio` (Issue #220; **na `main`**, PR #330); passo a passo em `docs/operations/railway-dominio.md` | `App__PublicBaseUrl` não é enviada, e os links dos e-mails não têm para onde apontar |
| Variáveis de e-mail no Terraform: `Email__DailyLimit`, `brevo_webhook_secret` e validações de remetente e de URL com `Brevo` | **Em entrega:** Issue #229, PR #333 aberto (a parte de IA vai em PR próprio) | Sem teto diário do nosso lado e sem webhook; remetente ou URL faltando só aparecem no deploy |
| Adaptador `BrevoEmailSender`, opções validadas na subida, registro `Fake`/`Brevo` | **Na `main`** (Issues #204 e #209; PRs #210 e #211) | — |
| Renderizador, layout e modelos dos e-mails | **Na `main`** (Issues #205 e #207; PR #212); caixa de saída de desenvolvimento no PR #283 | — |
| Chaves do Data Protection no PostgreSQL | **Na `main`** (PR #120; D4 aceita em 22/11/2026) | — |
| Fila (`QueueJobs`) | **Na `main`:** tabela (#304), porta (#310), contratos do tratador (#311), processador (#316) e limites (#321, #325). O trabalhador em segundo plano (`BackgroundService`) está em entrega: Issue #228, PR #331 aberto | Nada é enviado: todo e-mail passa pela fila |
| Tratador de e-mail da fila | **Em entrega** (PRs #270, #271 e #272 abertos; ADR-0003 §18, item 8b) | A fila enche e ninguém envia |
| Provedores de token e página `/conta/definir-senha` | **Na `main`** (Issues #239 e #240; PRs #244 e #252). Implementados antes da medição, por decisão do Henrique em 22/11/2026; quem espera a medição é a ligação da Brevo ([Passo 4b](#passo-4b--envio-de-medição-do-link)) | — |
| Convite de administrador, redefinição de senha, acesso do aluno | **Em entrega** (PRs abertos: convite #266, #268 e #285; redefinição #278, #279 e #280; acesso do aluno #267). O defeito #184, de que a redefinição dependia, já está corrigido (PR #276) | Nenhuma tela dispara e-mail; o [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta) não pode ser feito |
| Rota `POST /webhooks/brevo` | **Em entrega** (PR #265 aberto, item 12) | O [Passo 6](#passo-6--webhook-transacional-opcional) não pode ser feito |

**A ordem que vale desde 22/11/2026.** A conta e a chave da Brevo ainda não chegaram; o código (tokens, página e fluxos) é implementado antes, com `Email__Provider=Fake`. Quando a chave chegar: [Passo 4b](#passo-4b--envio-de-medição-do-link) primeiro, e **só depois** o [Passo 5](#passo-5--variáveis-na-railway-pelo-terraform). O texto original do ADR-0003 (§8.4, condição 1, e §16) diz que a medição vem antes da Issue dos tokens; a nota de situação de 25/11/2026 no próprio ADR registra a ordem nova (linha 11 de [Diferenças](#diferenças-entre-o-adr-0003-o-terraform-e-o-runbook-da-railway)).

**O que dá para adiantar sem esperar código:** os passos 1, 2 e 3 (conta, domínio e remetente) e a medição do link ([Passo 4b](#passo-4b--envio-de-medição-do-link)), assim que houver conta e chave. O domínio é o que mais demora: depende de outra pessoa e de até 48 horas de propagação. **Não deixe a chave da aplicação parada:** chave sem uso por 90 dias é desativada pela Brevo [F8]; a medição já conta como uso.

**O que você precisa ter em mãos** (C15 e C20; pendências M4 e M5 de `docs/context/pendencias.md`):

- acesso à conta Brevo da contratante, ou autorização para criá-la;
- o domínio institucional, por escrito, e o endereço do remetente;
- o contato de quem cria registros no DNS desse domínio.

Sem os três, pare no Passo 1. O sistema segue com `Email__Provider=Fake`.

## Convenções

- **Terminal do Terraform:** o mesmo do runbook da Railway ([4.3](railway-runbook.md#43-exportar-os-segredos-sem-deixá-los-no-histórico)). A partir deste runbook ele carrega três segredos: `RAILWAY_TOKEN`, `TF_VAR_seed_password` e `TF_VAR_brevo_api_key`. Nunca rode a CLI `railway`, `env` ou `set` nele.
- Todos os comandos partem da **raiz do repositório**.
- `<…>` é marcador: troque pelo seu valor. Nenhum exemplo traz segredo, e-mail real ou domínio real.
- **O domínio institucional e o endereço do remetente não são escritos no repositório**, em Issue, em PR nem em `docs/operations/ambientes.md`. O mesmo vale para o domínio próprio do painel; os exemplos usam `paineladm.example.org`. Eles vivem no `terraform.tfvars` (fora do git) e no painel da Brevo.
- **✔ Confira:** é o resultado esperado. **Se der errado:** é a saída mais provável. **Confira:** (sem o ✔) marca o que a documentação não confirma.
- No painel da Brevo, **Settings** fica no menu da conta, no canto inferior esquerdo segundo a central de ajuda [F3]; o lugar pode variar.

---

## Passo 1 — Conta e plano

### 1.1 Entrar na conta da contratante

A conta é da contratante, não sua (C20). Pergunte se já existe uma conta Brevo da FUNDHAS.

- **Existe:** peça para ser adicionado como usuário, com permissão para domínios, remetentes, chaves de API e segurança. **Confira:** o nome das permissões e o lugar do convite podem variar; a documentação lida só cita as permissões "API keys" e "SMTP & API – Authorized IPs" [F8][F12].
- **Não existe:** combine com a contratante quem cria. O e-mail dono da conta deve ser institucional e controlado por ela: é para ele que a Brevo manda os alertas de chave e de IP [F8][F12].

Nos dois casos, **confirme quem lê a caixa do dono da conta.** Os avisos de chave prestes a ser desativada e de IP bloqueado só chegam lá (ADR-0003 §13.1); se ninguém lê, o envio para sem aviso. Combine que eles sejam encaminhados a você.

Guarde a sua senha e o segundo fator no gerenciador de senhas. O cofre institucional e quem recupera a conta ainda não foram definidos (C18).

**✔ Confira:** você entra em `https://app.brevo.com` e a conta mostrada é a da contratante.

**Confira:** conta nova pode ficar em validação antes de poder enviar. A referência da API lista o erro `account_under_validation` [F15], mas a documentação lida não descreve o processo nem o prazo. Se aparecer aviso de validação no painel, siga-o antes do Passo 5.

### 1.2 Plano e limite diário

Conferido em 19/11/2026:

| Fato | Fonte |
|---|---|
| Plano gratuito: **300 envios por dia**. O limite zera todo dia e o que sobra não acumula | [F1] |
| Ao estourar: até **1.000 e-mails transacionais a mais ficam em uma fila de nova tentativa** da Brevo; o que passar disso **não é entregue** | [F1] |
| Todo e-mail do plano gratuito leva o selo **"Sent with Brevo"** | [F1] |
| Plano Starter: a partir de US$ 9 por mês, com 5.000 e-mails por mês; o complemento que tira o selo custa US$ 12 por mês | [F2] |

**Confira:** a documentação não diz o que a **API** responde quando o limite estoura (201 com a mensagem na fila da Brevo, 400 `not_enough_credits` ou 402). O worker trata as três hipóteses (ADR-0003 §6.3 e §17, item 1). A hora em que o limite zera também não está documentada.

**Comparação com o pico de convites.** C13 não informa o volume. Pelas premissas do ADR-0003 §15, cada acesso liberado gasta 2 e-mails (o acesso e o aviso de senha alterada):

| Cenário (premissa, não medida) | E-mails no dia | Cabe no gratuito? |
|---|---|---|
| Teste de fumaça (um convite e uma redefinição) | 4 | sim |
| Uma turma de 30 alunos liberada no mesmo dia | 60 | sim |
| 150 alunos liberados no mesmo dia | 300 | no limite |
| 400 alunos liberados no mesmo dia | 800 | não: 300 saem, 500 esperam na fila da Brevo |

**Por que a fila da Brevo não substitui o teto do nosso lado** (ADR-0003 §15): o link é gerado na hora em que o sistema entrega a mensagem à Brevo. Uma redefinição de senha vale 1 hora; se ela esperar na fila da Brevo até a cota voltar, chega vencida, e lá não somos nós que escolhemos quem sai primeiro. O teto na aplicação (`Email__DailyLimit`) segura o e-mail **antes** de gerar o link e deixa os tipos urgentes passarem na frente.

- **`Email__DailyLimit` ainda não chega à Railway:** entra com a Issue #229, com padrão `300`. Para a medição e o teste de fumaça isso não pesa: são poucos e-mails. **Antes de qualquer envio em volume, essa Issue precisa estar aplicada.**
- Se a conta da Brevo também fizer outros envios da contratante, eles gastam a mesma cota: o teto tem de ficar **abaixo** de 300.

**Decisão (D7 do ADR-0003, sua e da contratante):** gratuito na homologação e no teste de fumaça, com o teto de 300 do nosso lado; plano pago ou créditos antes da primeira carga de alunos reais (perguntas M9 e M14 de `pendencias.md`; o gasto é da contratante, C04). O plano pago compra o fim do limite diário e, com o complemento, o fim do selo. **Não compra o fim da reescrita dos links** ([1.3](#13-rastreamento-de-cliques-e-de-abertura)).

**✔ Confira:** você sabe em que plano a conta está. **Confira:** o plano e o consumo do dia aparecem na área de plano da conta (o nome do menu pode variar; não há fonte lida para o caminho).

### 1.3 Rastreamento de cliques e de abertura

**Leia antes de qualquer envio.** No plano gratuito **não existe botão para desligar o rastreamento de cliques**:

- A Brevo mede cliques com **links individualizados** [F17]: o link do e-mail é trocado por um endereço de rastreamento, que redireciona para o original. Usuários relatam que o endereço fica em um domínio de terceiros da Brevo [F18].
- Quem fala pela Brevo no fórum oficial informou, em 10/07/2024 e 18/10/2024, que **desligar** o rastreamento em e-mails transacionais só é feito **sob pedido e para clientes Enterprise** [F18]. É fonte fraca: fórum, de 2024, sem confirmação mais nova.
- A API não tem campo por mensagem para os cliques. `removeTrackingPixel` tira só o pixel de abertura, só no plano Enterprise ("Only available to Enterprise plans: for other accounts the field is accepted but ignored") e "Click tracking is unchanged" (relido na referência em 22/11/2026); `contactPixelTrackingConsent: false` apenas torna os eventos anônimos [F15].

Consequência: **o link de acesso, que leva o token, muito provavelmente chega reescrito**, passa pelo redirecionador da Brevo quando a pessoa clica, e o endereço que aparece ao passar o mouse sobre o botão não é o institucional.

**Decisão D17 do ADR-0003 (§8.4), aceita pelo Henrique em 22/11/2026, com as cinco condições vigentes:** manter o link com token e aceitar o redirecionamento como risco documentado. O plano B, já esboçado no ADR, é o código digitado, sem segredo na URL.

| # | Condição da D17 | Onde este runbook trata |
|---|---|---|
| 1 | Medir se o link chega reescrito. O ADR escreve "antes de construir a Issue dos tokens"; desde 22/11/2026 a medição é **pré-requisito obrigatório de ligar `Email__Provider=Brevo`**, e os tokens são implementados antes | [Passo 4b](#passo-4b--envio-de-medição-do-link); trava no início do [Passo 5](#passo-5--variáveis-na-railway-pelo-terraform) |
| 2 | Rastreamento anônimo ligado, prévias desligadas e retenção de 1 mês, antes do primeiro envio | Logo abaixo e [1.4](#14-retenção-dos-registros-na-brevo) |
| 3 | Texto do e-mail conforme o ADR-0003 §8.4 (botão com texto de ação, endereço do site por extenso) | É das Issues dos modelos (#205 e #207); conferido no [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta), item 3 |
| 4 | Perguntas ao suporte da Brevo respondidas antes de dado real | [Perguntas para o suporte da Brevo](#perguntas-para-o-suporte-da-brevo) |
| 5 | Teste de fumaça percorrendo o fluxo a partir do e-mail recebido | [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta) |

**Ligue o rastreamento anônimo:**

1. **Settings → Automations → Transactional emails → Tracking** [F16].
2. Em **Anonymous email tracking**, escolha **Yes**.
3. Clique em **Save**, no canto superior direito.

**Confira:** no caminho das campanhas (**Settings → Default settings → Tracking**) o mesmo artigo põe um aviso com o botão **Activate** antes do **Save**; no caminho dos transacionais ele não cita esse botão [F16]. Se o aviso aparecer aqui também, clique em **Activate** e depois em **Save**.

Com isso, os eventos "Opened" e "Clicked" dos registros deixam de mostrar o endereço, o id da mensagem e os **links clicados** [F16]. É o que tira do painel a URL com o token depois do clique.

**✔ Confira:** ao reabrir a tela de **Tracking**, a opção continua em **Yes**.

**Confira:** a documentação não diz se o rastreamento anônimo também impede a reescrita do link [F16]. O [Passo 4b](#passo-4b--envio-de-medição-do-link) mede isso.

### 1.4 Retenção dos registros na Brevo

A fila do sistema apaga o que terminou depois de 30 dias (D9 do ADR-0003, §5.6). Isso só vale de ponta a ponta se a Brevo for configurada: por padrão ela guarda os registros dos e-mails transacionais **sem prazo** [F19]. Faça antes do primeiro envio:

1. **Settings → Transactional emails → Retention rules** [F19].
2. Remetentes: **All senders**.
3. Prazo dos registros: **1 mês**, que é o mínimo aceito (a Brevo exige um mês para detectar spam e calcular reputação); o máximo é 24 meses [F19]. Faz parte da D9, aceita em 22/11/2026; continua `PROPOSTA` até a resposta da contratante (C14 não traz regra institucional).
4. Prévias (o HTML completo de cada mensagem): **Never store previews** [F19]. O e-mail de acesso do aluno leva o primeiro nome e a matrícula; a prévia guardaria isso e o link. **Olhe a tela, não suponha:** o artigo da Brevo se contradiz sobre o padrão (diz que as prévias são guardadas sem prazo e, mais abaixo, que essa opção já vem marcada).
5. Salve (o nome do botão pode variar).

**✔ Confira:** ao reabrir **Retention rules**, a regra mostra 1 mês para todos os remetentes e **Never store previews** marcada. A mudança de prazo vale também para os registros antigos; a das prévias só para os e-mails novos; a exclusão pode levar até 24 horas depois do prazo [F19].

**Se der errado:** a tela não aceita menos de 1 mês → é o mínimo; fique com ele. A opção de prévias não aparece → anote e inclua nas [perguntas ao suporte](#perguntas-para-o-suporte-da-brevo); não envie e-mail de acesso de aluno antes de resolver.

---

## Passo 2 — Autenticar o domínio institucional

Gmail e Yahoo exigem domínio autenticado desde 01/02/2024, e a Microsoft anunciou a mesma exigência em 05/05/2025 [F5]. Sem isso os e-mails de acesso caem em spam ou são recusados.

### 2.1 O que ter em mãos

- **O domínio** (o que vem depois do `@` no remetente). C15: será o domínio institucional, mas o nome literal ainda não foi informado.
- **Quem administra o DNS dele.** C15 não identifica a pessoa; é a pendência M5. Os registros deste passo vão para ela.
- **Se o domínio já tem registro DMARC.** Pergunte ao administrador, ou veja você mesmo no [2.4](#24-conferir-a-propagação-com-dig). Um domínio só pode ter **um** registro DMARC [F3].

A Brevo autentica o domínio **ou o subdomínio** de onde você envia [F3]. Usar o domínio principal mexe no DMARC de todo o e-mail da instituição; um subdomínio só para o sistema isola esse risco, mas muda o endereço do remetente. A escolha é da contratante: registre a resposta em `pendencias.md` (M5), sem escrever o domínio.

**São dois pedidos de DNS, para a mesma pessoa.** Os registros deste passo são do domínio do **remetente**. O domínio próprio do **painel** (no exemplo, `paineladm.example.org`) pede outros dois, um `CNAME` e um `TXT`, que saem de `terraform output dns_dominio_proprio` e seguem o `docs/operations/railway-dominio.md`. Se nenhum dos dois foi pedido ainda, mande juntos.

### 2.2 Adicionar o domínio na Brevo

1. **Settings → Senders, Domains, IPs → Domains** [F3].
2. Clique em **Add a domain** (ou em **Authenticate**, se o domínio já estiver na lista) [F3].
3. Digite só o domínio, sem `https://` e sem `www` [F4].

A partir daqui a conta pode mostrar um de dois fluxos; a Brevo está trocando um pelo outro [F3][F4].

**Fluxo clássico** [F3]: clique em **Add domain** e escolha **Authenticate the domain yourself**. Existe também **Ask someone else to authenticate the domain**, que manda as instruções por e-mail a outra pessoa.

**Fluxo guiado** [F4]:

| Tela | O que escolher |
|---|---|
| Domínio | Digite e **Continue** |
| **Branded subdomain** | Opcional em IP compartilhado: pule. O ADR-0003 §8.4 (opção D) deixa o subdomínio de marca para depois, e não está confirmado que ele vale para e-mail transacional |
| IP dedicado (só aparece se a conta tiver um) | **Do not associate with a dedicated IP** |
| Como autenticar (só aparece para subdomínio) | **Individual DNS records**. A outra opção, **Brevo-managed setup**, entrega o subdomínio inteiro à Brevo por dois registros `NS`; é decisão do administrador do DNS, não sua |
| Como os registros são criados | **Manual** (você copia e encaminha) ou **Delegate** (digita o e-mail do administrador e clica em **Send request**; ele recebe os registros e não precisa de conta na Brevo) |

**Não use a opção automática** (**Authenticate the domain automatically** ou **Automatic**): ela pede o login do provedor de DNS em uma janela da Brevo [F3][F4], e essa credencial não é sua.

**Registros que a Brevo mostra** [F3][F6]:

| Registro | Tipo | Nome (host) | Para quê |
|---|---|---|---|
| Código da Brevo (*Brevo code*) | `TXT` | a raiz do domínio; o valor começa com `brevo-code:` | Prova que você controla o domínio |
| DKIM | **1 `TXT`** (host `mail._domainkey`) **ou 2 `CNAME`**, conforme a conta | o que o painel mostrar | Assina as mensagens |
| DMARC | `TXT` | `_dmarc` | Diz aos provedores o que fazer com mensagem que falha na autenticação |

- **Copie o que o painel mostrar, não esta tabela.** Podem ser três ou quatro registros [F3]; com subdomínio de marca, até sete [F4].
- **SPF e MX não são exigidos.** A Brevo só os fornece para quem tem IP dedicado [F3]. Não peça para alterar o SPF da instituição.
- **Confira:** os nomes exatos dos dois `CNAME` de DKIM não aparecem na documentação lida.

**✔ Confira:** a tela lista os registros, cada um com nome, tipo e valor, e você os copiou para a mensagem do 2.3. Os valores não são segredo, mas identificam o domínio: não cole em Issue nem em PR.

### 2.3 Mensagem para quem administra o DNS

Use se escolheu **Manual**. Troque os marcadores e encaminhe:

```text
Assunto: Registros DNS para os e-mails do sistema Universo do Hélinho

Olá, <nome>.

O sistema Universo do Hélinho vai enviar e-mails de acesso (convite de
administrador, redefinição de senha e acesso do aluno para o responsável)
com o remetente <remetente>@<dominio-institucional>, pelo serviço Brevo.
Para os provedores aceitarem essas mensagens, a Brevo pede os registros
abaixo no DNS de <dominio-institucional>:

  Tipo    Nome (host)        Valor
  <tipo>  <nome 1>           <valor 1>
  <tipo>  <nome 2>           <valor 2>
  <tipo>  <nome 3>           <valor 3>

Pontos de atenção:
1. O domínio já tem registro DMARC (_dmarc)? Se tiver, não crie um
   segundo: me avise, e ajustamos o que existe.
2. Não é preciso mexer em SPF nem em MX. O e-mail institucional continua
   como está.
3. Se o DNS estiver na Cloudflare, os CNAME de DKIM ficam com o proxy
   desligado e sem "CNAME flattening".
4. TTL padrão. Os registros precisam continuar lá enquanto o sistema
   enviar e-mails por esse domínio.

Me avise quando estiverem criados: eu confiro e concluo a autenticação.
A propagação pode levar até 48 horas.

Obrigado,
Henrique
```

Os itens 1, 3 e 4 vêm de [F3]; o item 2, da nota sobre SPF e MX da mesma fonte.

### 2.4 Conferir a propagação com `dig`

O `read -r` guarda o domínio em uma variável sem deixá-lo no histórico do shell. O cursor fica parado, sem prompt: digite o domínio e aperte Enter.

```sh
read -r DOMINIO
```

```sh
dig +short TXT "$DOMINIO" | grep -c 'brevo-code:'
```

**✔ Confira:** imprime `1`.

```sh
dig +short TXT "_dmarc.$DOMINIO"
```

**✔ Confira:** **uma** linha, começando com `"v=DMARC1;`.

DKIM, conforme o tipo que o painel mostrou. Com um `TXT`:

```sh
dig +short TXT "mail._domainkey.$DOMINIO"
```

Com dois `CNAME`, uma vez para cada nome mostrado no painel:

```sh
dig +short CNAME "<nome do registro DKIM>.$DOMINIO"
```

**✔ Confira:** o `TXT` devolve um valor que começa com `"k=rsa;p=` [F6]; cada `CNAME` devolve o destino que o painel mostrou.

Para tirar a dúvida do cache do seu provedor, repita qualquer consulta em um resolvedor público, acrescentando `@1.1.1.1` ao fim do comando.

**Se der errado:**

- Resposta vazia → o registro ainda não propagou (até 48 horas [F3]) ou foi criado com o nome errado. O erro mais comum é o painel de DNS acrescentar o domínio de novo; teste com `dig +short TXT "_dmarc.$DOMINIO.$DOMINIO"`.
- Duas linhas no DMARC → há dois registros; tem de sobrar um só [F3].
- O `CNAME` de DKIM está no painel de DNS e o `dig` não o mostra → pode haver proxy ou *CNAME flattening* ligado no provedor [F3].

### 2.5 Concluir a autenticação

1. **Settings → Senders, Domains, IPs → Domains** → **Authenticate**, ao lado do domínio [F3].
2. No fluxo clássico: escolha autenticar você mesmo, **Continue**, e clique em **Authenticate this email domain**, no fim da página dos registros [F3]. No fluxo guiado: **Authenticate domain** [F4].

**✔ Confira:** o domínio aparece como **Authenticated**. Mantenha os registros no DNS: mudar ou apagar depois faz as mensagens falharem ou irem para o spam [F3].

**Se der errado:** aparece "The domain is not authenticated yet." → se o `dig` já mostra os registros, repita ao longo das 48 horas [F3]. Depois disso, compare valor por valor com o painel (aspas a mais, valor cortado, nome duplicado) e consulte o artigo de diagnóstico da Brevo [F3].

---

## Passo 3 — Criar o remetente

Faça depois do Passo 2: com o domínio autenticado, todo remetente dele já nasce verificado. Sem isso, a Brevo manda um código de 6 dígitos para o endereço do remetente, e um endereço de "não responda" pode nem ter caixa de entrada [F7].

1. **Settings → Senders, Domains, IPs → Senders** [F7].
2. Na aba **Senders**, clique em **Add a sender**.
3. **From name:** o nome que aparece na caixa de entrada. Proposta em uso (M4): `Universo do Hélinho · FUNDHAS`. A API aceita até 70 caracteres (ADR-0003 §7); a Brevo avisa que muitas caixas de entrada mostram só os primeiros 20 a 30 [F7].
4. **From email:** o endereço do remetente. Proposta em uso (M4): `nao-responda@<dominio-institucional>`. A definição é da contratante (C15).
5. **Save**.

**✔ Confira:** o remetente aparece na lista, sem ícone de aviso nas colunas de DKIM e DMARC [F7]. Anote, fora do repositório, o nome e o endereço **exatamente** como estão na tela: são os valores de `Brevo__SenderName` e `Brevo__SenderEmail`. A API só envia com remetente cadastrado e verificado (ADR-0003 §4.1).

**Se der errado:** há ícone de aviso → o domínio do endereço não é o que foi autenticado no Passo 2 (confira subdomínio e grafia).

Respostas a um endereço sem caixa de entrada voltam com erro para quem respondeu. O contato para dúvidas vai no rodapé dos e-mails (pergunta M10).

---

## Passo 4 — Chave de API v3

### 4.1 Chave de API não é chave SMTP

A página **SMTP & API** tem duas abas, e só uma serve.

| | Chave de API v3 | Chave SMTP |
|---|---|---|
| Aba | **API Keys & MCP** [F8] | **SMTP** [F11] |
| O que é | Um valor só, enviado no cabeçalho `api-key` para `https://api.brevo.com/v3` [F10] | Um par: login SMTP e senha, para o servidor SMTP da Brevo [F11] |
| O sistema usa? | **Sim.** É o valor de `Brevo__ApiKey` (ADR-0003 §4.1) | **Não.** O envio por SMTP foi descartado (ADR-0003 §19) |

A Brevo avisa: "Use an SMTP key, not an API key, for SMTP relay connections" [F11]. Uma não substitui a outra. Não crie chave SMTP.

### 4.2 Criar a chave

Crie perto do dia de usar: a medição do [Passo 4b](#passo-4b--envio-de-medição-do-link) e o Passo 5.

**Se a contratante entregar a chave pronta**, em vez de você criá-la: receba por item compartilhado do gerenciador de senhas, nunca por e-mail ou chat ("Never send keys by email" [F8]). Com acesso ao painel, confira na lista o nome, a validade e o acesso restrito (itens 3 a 5). Se ela chegou por canal aberto, trate como exposta: crie outra e apague aquela ([8.1](#81-rotação-de-rotina)).

1. **Settings → SMTP & API → API Keys & MCP** [F8].
2. Clique em **Generate API key**. A Brevo manda um código de verificação para o seu dispositivo: digite e clique em **Verify** [F8].
3. **Key name:** um nome que diga quem usa, por exemplo `helinho-homologacao-web`. Uma chave por integração e por ambiente [F8].
4. **Expiry:** de 7 dias a 1 ano, ou sem validade [F8]. O ADR-0003 §13.1 pede **1 ano**, com lembrete na sua agenda 30 dias antes para fazer a [rotação](#81-rotação-de-rotina). A Brevo avisa 3 dias antes e no dia, mas para o e-mail da conta [F8].
5. **Access:** escolha **Restricted**. Todo recurso começa em **None**; mude só **Transactional email** para **All** [F9]. Esse recurso só oferece None, Read e All, e enviar é uma escrita [F9]. **Confira:** a documentação não diz qual recurso cobre `POST /v3/smtp/email`; se o envio falhar com "Access denied for resource" [F9], edite a chave (três pontos → **Edit API key**).
6. **Não** ative **Create MCP server API key**: essa opção desativa a chave comum [F8].
7. Copie o valor **direto para o gerenciador de senhas**, em entrada própria (sugestão: "Brevo — chave de API helinho homologação"). "Your API key is only visible during this step" [F8].
   **Por que o gerenciador, se a Issue #206 fala em guardar a chave "só na Railway":** todo `plan` e `apply` precisa dela em `TF_VAR_brevo_api_key` (sem ela o `plan` falha de propósito), e a Brevo não a mostra de novo. A chave fica, então, em dois lugares que você controla, o gerenciador e a Railway, mais o estado do Terraform ([8.3](#83-onde-a-chave-fica-guardada)), e em nenhum outro.
8. Clique em **I've saved my key**.

**✔ Confira:** a chave aparece na lista com o nome dado, e o valor está no gerenciador. Ele não passou por chat, e-mail, nota, arquivo nem pelo histórico do shell.

**Se der errado:** você fechou a janela sem copiar → apague a chave (três pontos → **Delete API key**) e crie outra; o valor não é mostrado de novo [F8].

**Primeiro jeito de o envio parar sem ninguém mexer** (ADR-0003 §13.1): chave sem nenhuma chamada bem-sucedida por **90 dias** é desativada, mesmo criada sem validade; e chave com validade vence na data [F8]. Em homologação, com pouco uso, é provável acontecer.

- **Como aparece:** a API passa a responder erro de autenticação; o worker suspende os envios, registra `Error` e não perde a fila até o prazo de cada e-mail (ADR-0003 §6.3).
- **Como evitar:** validade de 1 ano com lembrete na agenda (item 4); ler os avisos da Brevo, que chegam 7 dias antes e no dia, no e-mail do dono da conta, que precisa ser uma caixa que alguém lê ([1.1](#11-entrar-na-conta-da-contratante)); em homologação parada há mais de dois meses, repita a checagem opcional do [5.2](#52-exportar-a-chave): se ela imprimir `200`, é uma chamada bem-sucedida e conta como uso.
- **Não faça:** chamada automática só para manter a chave viva. O ADR-0003 §13.1 descartou: esconderia uma integração parada.
- **Se aconteceu:** [rotação](#81-rotação-de-rotina).

O segundo jeito é o bloqueio de IP, logo abaixo.

### 4.3 Bloqueio por IP

A Brevo tem uma lista de **IPs autorizados**, em **Settings → Security → Authorized IPs** [F12]. A lista é da conta inteira e vale para chaves de API e SMTP; o bloqueio é ligado em separado para cada tipo [F12]. Com o bloqueio ligado, chamada vinda de IP fora da lista é recusada mesmo com a chave certa [F12].

**Como ele se liga sozinho** [F12]: quando a chave de API começa a ser usada, a Brevo autoriza automaticamente os IPs que chamam (a faixa `/24` inteira de cada um) e o bloqueio fica inativo. **Se nenhum IP novo aparecer por 30 dias, a Brevo ativa o bloqueio.** Daí em diante, IP desconhecido é barrado, e a Brevo manda um e-mail ao dono da conta com o IP e a chave usada.

**Por que restringir por IP provavelmente não funciona na Railway** [F14]:

- IP de saída fixo é recurso do plano **Pro**; no Hobby não existe.
- Mesmo no Pro, a Railway não garante que os endereços sejam exclusivos ("There is no guarantee that the IPv4 addresses assigned to your service are dedicated"), e eles mudam se o serviço mudar de região.
- É um clique de painel por serviço (**Settings → Networking → Enable Static IPs**, seguido de redeploy), que o Terraform de `infra/railway/` não gerencia.

Sem IP fixo, o cenário é este: o serviço envia por semanas a partir dos mesmos endereços, a Brevo ativa o bloqueio, a Railway troca o IP de saída e **o envio para**. É o segundo jeito de o envio parar sozinho (ADR-0003 §13.1); o worker suspende sem perder a fila, como no caso da chave.

**O que fazer para evitar:**

1. **Settings → Security → Authorized IPs** [F12].
2. Em **Blocking unauthorized IP addresses**, veja a linha das chaves de API.
3. Se estiver **Activated**: clique em **Deactivate for API** e confirme em **Deactivate blocking** [F12]. Faça isso **antes do primeiro envio**.
4. Repita essa conferência **uns 35 dias depois do primeiro envio** (ponha na agenda) e sempre que o envio parar com erro de autenticação. **Confira:** a documentação não diz se o bloqueio volta a se ligar sozinho depois de desativado à mão.
5. Se chegar e-mail da Brevo avisando de IP bloqueado, ele traz o IP e a chave usada [F12]: confira no log do `helinho-web` se os envios pararam naquela hora e refaça o item 3. Se o IP não tiver relação com a Railway, trate como [vazamento](#82-se-a-chave-vazar).

**✔ Confira:** a linha das chaves de API não está **Activated**.

**Confira:** uma fonte de terceiros diz que o bloqueio já nasce ligado em contas criadas depois de 16/05/2024 e que o erro devolvido é 401 com "unrecognised IP address" [F13]. Não há fonte da Brevo para nenhum dos dois; por isso o item 3 vem antes do primeiro envio.

Desligar o bloqueio tira uma camada de proteção [F12]. O que compensa: chave restrita a um recurso, guardada só no gerenciador e na Railway, com validade e rotação, e os alertas por e-mail da Brevo.

**Alternativa, só no plano Pro e por Issue própria:** ligar o IP de saída fixo no `helinho-web`, autorizar esses endereços na Brevo (**Authorize IP addresses** [F12]) e manter o bloqueio. Protege menos do que parece, porque os endereços podem ser de outros clientes da Railway.

---

## Passo 4b — Envio de medição do link

**É pré-requisito obrigatório de ligar `Email__Provider=Brevo`.** Sem os dez itens do 4b.3 e do 4b.4 respondidos e o resultado registrado (4b.5), **não faça o Passo 5**. É a condição 1 da decisão D17 do ADR-0003 (§8.4), aceita em 22/11/2026.

**Deixou de ser pré-requisito da implementação.** Por decisão do Henrique em 22/11/2026, os tokens e a página `/conta/definir-senha` são implementados **antes** desta medição, porque a conta e a chave ainda não tinham chegado. O que a medição decide agora é se o envio real pode ser ligado com o link como está, ou se o plano B (código digitado) precisa entrar antes.

Não depende de fila, de fluxo nem da Railway: só da conta, do remetente do [Passo 3](#passo-3--criar-o-remetente) e de uma chave. Fica fora da numeração 1 a 8 porque a Issue #206 fixou os oito passos.

**Nunca executado.** O envio é por `curl` na API, e não pelo painel, porque é pela API que o sistema envia; um teste disparado pelo painel poderia tratar os links de outro jeito. Gasta 1 ou 2 e-mails da cota do dia.

**Antes:** rastreamento anônimo ligado e prévias desligadas ([1.3](#13-rastreamento-de-cliques-e-de-abertura) e [1.4](#14-retenção-dos-registros-na-brevo)). Se o domínio ainda não estiver autenticado, dá para medir com um remetente provisório: um endereço seu, com caixa de entrada, verificado pelo código de 6 dígitos [F7]. A mensagem pode cair no spam, o que não atrapalha a medição.

### 4b.1 Chave temporária

Crie uma chave como no [4.2](#42-criar-a-chave), com três diferenças: nome `helinho-medicao-link`, **Expiry** de 7 dias, e ela **não** vai para a Railway nem para o gerenciador de senhas (copie do painel direto para o `read -rs` abaixo). Acesso **Restricted**, só **Transactional email** em **All**.

**Se você só tem a chave entregue pela contratante** e não pode criar outra: use-a aqui, lida do gerenciador pelo mesmo `read -rs`, e pule o item 1 do [4b.6](#4b6-limpar). Ela continua valendo para o Passo 5.

### 4b.2 Enviar

Em um terminal novo, que **não** é o do Terraform. Depois de cada `read` o cursor fica parado, sem prompt: digite ou cole o valor e aperte Enter. O `read -rs` não mostra a chave nem a grava no histórico.

```sh
read -rs BREVO_KEY
```

```sh
read -r REMETENTE
```

(o **From email** do Passo 3, ou o remetente provisório)

```sh
read -r DESTINO
```

(o seu e-mail)

O link de exemplo aponta para `example.org`, domínio reservado, e leva um "token" de 288 caracteres sem valor nenhum, do tamanho aproximado de um token de verdade:

```sh
LINK="https://example.org/conta/definir-senha?usuario=00000000-0000-0000-0000-000000000000&token=$(printf 'exemplo-sem-valor_%.0s' $(seq 1 16))"
```

O `jq` monta o corpo, com as duas partes (HTML com botão e texto puro com a URL sozinha em uma linha, como pede o ADR-0003 §8.4). A chave entra por um cabeçalho lido de um descritor de arquivo, escrito pelo `printf` interno do shell: não aparece na lista de processos.

```sh
jq -n --arg de "$REMETENTE" --arg para "$DESTINO" --arg link "$LINK" '{sender:{email:$de,name:"Medição do link"},to:[{email:$para}],subject:"Medição do link (teste)",htmlContent:("<!doctype html><html><body><p>Teste de medição do link.</p><p><a href=\"" + ($link|@html) + "\">Definir minha senha</a></p></body></html>"),textContent:("Teste de medição do link.\n\n" + $link + "\n"),tags:["medicao-link"]}' | curl -sS -X POST 'https://api.brevo.com/v3/smtp/email' -H 'content-type: application/json' -H 'accept: application/json' -H @<(printf 'api-key: %s\n' "$BREVO_KEY") --data-binary @- | jq '{enviado: has("messageId"), code, message}'
```

**✔ Confira:** imprime `"enviado": true`, com `code` e `message` nulos.

Se for parar aqui sem concluir a medição, limpe as variáveis:

```sh
unset BREVO_KEY REMETENTE DESTINO LINK
```

**Se der errado:**

| Saída | Causa provável | O que fazer |
|---|---|---|
| `code` `unauthorized`, ou mensagem sobre a chave | Chave colada errada, ou IP da sua rede barrado | Repita o `read -rs`; veja o [4.3](#43-bloqueio-por-ip) |
| Mensagem "Access denied for resource" | A chave restrita não tem permissão de envio | [4.2](#42-criar-a-chave), item 5. Anote: responde a dúvida sobre qual recurso cobre o envio |
| `code` `invalid_parameter` citando o remetente | `REMETENTE` não é um remetente verificado da conta | [Passo 3](#passo-3--criar-o-remetente) |
| `code` `invalid_parameter` citando o corpo | A API não aceitou `htmlContent` e `textContent` juntos (ADR-0003 §17, item 2) | Anote, e repita tirando `textContent:(…),` do comando |
| `account_under_validation` ou `permission_denied` | A conta ainda não pode enviar | [1.1](#11-entrar-na-conta-da-contratante) |

### 4b.3 O que olhar no e-mail recebido

Abra a mensagem e também o código-fonte dela ("mostrar original", no seu provedor de e-mail).

| # | O que olhar | Como |
|---|---|---|
| 1 | **O link do HTML foi reescrito?** | Sem clicar, copie o endereço do botão "Definir minha senha" (botão direito → copiar endereço do link). Começa com `https://example.org/conta/definir-senha?` ou com outro domínio? |
| 2 | **Qual é o domínio do redirecionador?** | Se foi reescrito: só o nome do host do endereço copiado |
| 3 | **O link do texto puro foi reescrito?** | No código-fonte, ache a parte `text/plain` e a linha da URL. Se a parte vier codificada (`base64` ou `quoted-printable`), use a opção do seu provedor de ver a mensagem só em texto |
| 4 | **As duas partes saíram?** | O código-fonte tem `text/plain` e `text/html` (ADR-0003 §17, item 2) |
| 5 | **O redirecionamento preserva a URL inteira?** | Agora clique no botão. A barra de endereço do navegador deve terminar exatamente no valor de `echo "$LINK"`, com os 288 caracteres do token. A página de `example.org` em si não importa |
| 6 | **Onde aparece o selo "Sent with Brevo"?** | Na mensagem aberta [F1] |
| 7 | **Autenticação** (só com o domínio do Passo 2) | No código-fonte: `dkim=pass` e `dmarc=pass` [F3] |

### 4b.4 O que olhar nos registros da Brevo

**Transactional → Logs**, filtrando pelo seu endereço ou pela etiqueta `medicao-link` [F20].

| # | O que olhar | Esperado com o [1.3](#13-rastreamento-de-cliques-e-de-abertura) e o [1.4](#14-retenção-dos-registros-na-brevo) feitos |
|---|---|---|
| 8 | Depois do clique, existe um evento "Clicked"? Ele mostra a URL completa, com o token de exemplo? | O evento pode existir, mas **sem** o link e sem o seu endereço [F16] |
| 9 | O ícone do olho mostra o conteúdo da mensagem, com o link? | Não: as prévias estão desligadas [F19] |
| 10 | A URL de destino aparece em algum outro lugar do registro ou do CSV (**Download CSV**)? | Não deveria. Se aparecer, é resposta para a pergunta 4 do suporte. Apague o CSV em seguida |

**Opcional, para saber se o rastreamento anônimo muda a reescrita:** ponha **Anonymous email tracking** em **No**, repita o 4b.2 e os itens 1, 3 e 8, e **volte para Yes** em seguida. O link é de exemplo; não há dado a proteger nesse envio.

### 4b.5 O que anotar e para onde vai

Anote as respostas dos dez itens. Nada disso é segredo, e o domínio do redirecionador é da Brevo; **não anote o seu e-mail**. O resultado entra no ADR-0003 (§8.4 e §17, item 7), em PR `docs`, **antes do Passo 5**, e decide se a Brevo pode ser ligada:

| Resultado | Pode ligar `Email__Provider=Brevo`? |
|---|---|
| O link não é reescrito | **Sim.** A D17 perde o motivo; o ADR registra o fato |
| É reescrito, a URL chega inteira (item 5) e não fica visível nos registros (itens 8 a 10) | **Sim**, pela opção A da D17, com as outras quatro condições |
| O redirecionamento corta ou altera a URL, ou a URL com o token fica visível nos registros mesmo com o rastreamento anônimo | **Não.** `Email__Provider` continua em `Fake`. É gatilho do plano B do ADR-0003 §8.4 (código digitado): como os tokens e a página já estarão implementados com o link, o plano B vira Issue nova, e a Brevo só é ligada depois dela |

### 4b.6 Limpar

1. Na Brevo: três pontos ao lado de `helinho-medicao-link` → **Delete API key** [F8].
2. No terminal:

```sh
unset BREVO_KEY REMETENTE DESTINO LINK
```

3. Se usou remetente provisório, apague-o em **Senders**.
4. Os registros do envio somem com a regra de 1 mês, ou apague-os à mão (**Manually delete the transactional log** [F20]).

**✔ Confira:** a chave temporária não está mais na lista de **API Keys & MCP**.

---

## Passo 5 — Variáveis na Railway pelo Terraform

**Trava: não ligue a Brevo sem a medição.** Antes de qualquer comando deste passo, confirme os quatro itens:

| # | Conferência | Onde |
|---|---|---|
| 1 | O [Passo 4b](#passo-4b--envio-de-medição-do-link) foi feito, os dez itens estão respondidos e o resultado está registrado no ADR-0003 | 4b.5 |
| 2 | O resultado permite ligar (uma das duas primeiras linhas da tabela do 4b.5) | 4b.5 |
| 3 | Rastreamento anônimo ligado, prévias desligadas e retenção de 1 mês | [1.3](#13-rastreamento-de-cliques-e-de-abertura), [1.4](#14-retenção-dos-registros-na-brevo) |
| 4 | Bloqueio de IP desativado para chaves de API | [4.3](#43-bloqueio-por-ip) |

Faltou algum: pare. `Email__Provider` fica em `Fake`.

Pré-requisito: a homologação já foi criada pelo [runbook da Railway](railway-runbook.md), o domínio próprio do painel já responde e o commit publicado tem o adaptador (Issues #204 e #209). Tudo no **terminal do Terraform**, com `RAILWAY_TOKEN` e `TF_VAR_seed_password` exportados como no [4.3 daquele runbook](railway-runbook.md#43-exportar-os-segredos-sem-deixá-los-no-histórico).

### 5.1 Editar o terraform.tfvars

**Primeiro, o domínio do painel.** `App__PublicBaseUrl` é a base de todo link enviado por e-mail (ADR-0003 §10.3). Você não a escreve: o Terraform a monta como `https://` mais o `dominio_proprio` do ambiente (PR #330). O bloco abaixo já deve estar no `infra/railway/terraform.tfvars` desde o `docs/operations/railway-dominio.md`; aqui o domínio é de exemplo:

```hcl
ambientes = {
  homologacao = { seed_enabled = true, dominio_proprio = "paineladm.example.org" }
}
```

Confira o valor que vai para a aplicação e se o domínio responde:

```sh
BASE=$(terraform -chdir=infra/railway output -json ambientes | jq -r .homologacao.url_base_app)
```

```sh
echo "$BASE"
```

```sh
curl -sS -o /dev/null -w '%{http_code}\n' "$BASE/health/ready"
```

**✔ Confira:** o `echo` imprime `https://` mais o domínio do painel (no exemplo, `https://paineladm.example.org`), sem barra no fim, e o `curl` imprime `200`.

**Se der errado:**

- O `echo` não imprime nada, ou imprime `null` → o ambiente não tem `dominio_proprio`, ou o Terraform aplicado é anterior ao `dominio_proprio` (PR #330). Pare e faça antes o `docs/operations/railway-dominio.md`. Não troque pelo endereço `*.up.railway.app` só para seguir: o link dos e-mails ficaria com ele.
- O `curl` falha ou reclama do certificado → o DNS do painel ainda não propagou, ou a Railway ainda não emitiu o certificado. Não ligue a Brevo: os links dos e-mails abririam com erro.

**Depois, as variáveis da Brevo.** No mesmo arquivo, descomente ou acrescente o bloco abaixo. As chaves que você não escrever continuam com o padrão:

```hcl
variaveis_web = {
  Email__Provider    = "Brevo"
  Brevo__SenderEmail = "<From email do Passo 3>"
  Brevo__SenderName  = "<From name do Passo 3>"
}
```

- `Brevo` com B maiúsculo: o Terraform só aceita `Fake` ou `Brevo`.
- **Não escreva `App__PublicBaseUrl` aqui**, a não ser para um valor diferente do domínio próprio: o valor explícito vence o do `dominio_proprio`. Ela tem de ser `https://`, sem caminho e sem barra no fim (ADR-0003 §7).
- **`Email__DailyLimit`:** quando a Issue #229 entrar, o padrão já é `300`; escreva só para baixar (conta dividida com outros envios). Copie a forma do `terraform.tfvars.example` daquele PR.
- **Antes da Issue #229, não escreva `Email__DailyLimit` nem `Brevo__WebhookSecret`:** `variaveis_web` só envia as chaves declaradas em `infra/railway/variables.tf`. Chave não declarada é, muito provavelmente, **ignorada em silêncio** (a conversão para o tipo objeto descarta o atributo extra, sem erro): você acharia que configurou e nada chegaria à Railway. **Confira** pelo `plan`: a chave tem de aparecer na lista de nomes.
- O segredo do webhook nunca entra nesse arquivo: vai por `TF_VAR_brevo_webhook_secret` ([Passo 6](#passo-6--webhook-transacional-opcional)).
- **A chave de API nunca entra nesse arquivo.**

```sh
git status --short infra/railway
```

**✔ Confira:** não lista o `terraform.tfvars` (ele é ignorado pelo git).

### 5.2 Exportar a chave

O cursor fica parado, sem prompt: cole a chave do gerenciador de senhas e aperte Enter.

```sh
read -rs TF_VAR_brevo_api_key && export TF_VAR_brevo_api_key
```

```sh
echo "chave da Brevo: ${#TF_VAR_brevo_api_key} caracteres"
```

**✔ Confira:** mostra só o tamanho, maior que zero.

**Se der errado:** você digitou `export TF_VAR_brevo_api_key=<valor>` → o valor ficou no histórico do shell. Faça a [rotação](#81-rotação-de-rotina) agora: a chave está exposta. E limpe o histórico: no terminal onde digitou, rode `unset HISTFILE` (zsh e bash deixam de gravar o histórico ao sair); apague a linha do arquivo de histórico (`~/.zsh_history` no zsh, `~/.bash_history` no bash); feche esse terminal e abra outro. Só apagar a linha não basta: o shell regrava o histórico da memória quando fecha.

**Opcional (nunca executado): provar a chave antes do `apply`.** O `printf` é interno do shell e o `curl` lê o cabeçalho da entrada padrão, então a chave não aparece na lista de processos:

```sh
printf 'api-key: %s\n' "$TF_VAR_brevo_api_key" | curl -sS -o /dev/null -w '%{http_code}\n' -H @- 'https://api.brevo.com/v3/smtp/statistics/events?limit=1'
```

**✔ Confira:** imprime `200`. **Confira:** a documentação não diz qual recurso da chave restrita cobre essa rota [F9]; outro código que não seja `401` não prova que a chave está errada. Com `401`, a chave foi colada errada ou o IP da sua máquina foi barrado ([4.3](#43-bloqueio-por-ip)). A chamada põe a faixa de IP da sua rede na lista de autorizados [F12].

A partir de agora, **todo** `plan` precisa das três variáveis exportadas. Sem `TF_VAR_brevo_api_key` e com `Email__Provider = "Brevo"`, o `plan` falha de propósito, em vez de apagar a chave da Railway.

### 5.3 Plan e apply

```sh
umask 077
```

O arquivo de plano guarda os segredos em texto; com o `umask 077`, ele nasce legível só por você.

```sh
terraform -chdir=infra/railway plan -out="$HOME/.helinho-infra/brevo.tfplan"
```

**✔ Confira, na saída do `plan`:**

1. O resumo mostra **uma** mudança e **nada** a destruir, em `module.ambiente["homologacao"].railway_variable_collection.web`.
2. `railway_project.this` e `railway_service.postgres` **não aparecem**. Se aparecerem, não aplique ([8.1 do runbook da Railway](railway-runbook.md#81-o-que-dispara-um-deploy-e-como-conferir)).
3. Em `Changes to Outputs`, a lista `variaveis_web` ganha **três** nomes: `Brevo__ApiKey`, `Brevo__SenderEmail` e `Brevo__SenderName`. `Email__Provider` e `App__PublicBaseUrl` já estavam lá (a segunda, desde o domínio próprio), e `Email__DailyLimit` também, se a Issue #229 já entrou.
4. `url_base_app` **não muda**: continua `https://` mais o domínio do painel. Se aparecer mudando ou ficando vazia, não aplique.
5. Todos os valores de variável aparecem como `(sensitive value)`.

```sh
terraform -chdir=infra/railway show "$HOME/.helinho-infra/brevo.tfplan" | grep -cF -f <(printf '%s\n' "$TF_VAR_brevo_api_key")
```

**✔ Confira:** imprime `0` (a chave não aparece no plano legível).

**Se der errado:**

- `Email__Provider aceita Fake ou Brevo, e Brevo exige TF_VAR_brevo_api_key.` → a chave não foi exportada, ou o provedor foi escrito diferente de `Brevo`.
- O plano quer mudar mais de um recurso → alguém mexeu no painel ou no `tfvars`. Não aplique; descubra a diferença. Se o recurso a mais for `railway_custom_domain.web`, o domínio próprio ainda não tinha sido aplicado: faça-o antes, em `apply` separado.
- Depois da Issue #229, o `plan` também recusa `Brevo` sem remetente, sem nome ou sem URL pública em `https`. **Confira:** o texto das mensagens vem do PR dela.

**O `apply` reimplanta o `helinho-web`** (toda mudança de variável é um deploy). Faça em horário combinado.

```sh
terraform -chdir=infra/railway apply "$HOME/.helinho-infra/brevo.tfplan"
```

```sh
rm "$HOME/.helinho-infra/brevo.tfplan"
```

**✔ Confira:** `Apply complete!` com 1 recurso alterado.

**Faça agora o backup cifrado do estado** ([5.7 do runbook da Railway](railway-runbook.md#57-backup-cifrado-do-estado)). A partir deste `apply`, o `terraform.tfstate` guarda a chave da Brevo **em texto**, junto com a senha do banco e a do seed ([8.9](railway-runbook.md#89-onde-fica-o-estado-do-terraform)).

### 5.4 Conferir o deploy

1. Painel da Railway → `helinho-web` → aba **Deployments**: o deploy disparado pelo `apply` termina **verde**.
2. Aba **Variables**: existem `Email__Provider`, `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SenderName` e `App__PublicBaseUrl`.
3. Saúde, como no [6.4 do runbook da Railway](railway-runbook.md#64-healthready):

```sh
BASE=$(terraform -chdir=infra/railway output -json ambientes | jq -r .homologacao.url_base_app)
```

```sh
curl -sS -o /dev/null -w '%{http_code}\n' "$BASE/health/ready"
```

**✔ Confira:** imprime `200`.

**Se der errado:** o deploy falha logo na subida → com `Email__Provider=Brevo`, a aplicação **recusa subir** se faltar chave, remetente ou URL pública, se o e-mail do remetente for inválido, se o nome passar de 70 caracteres ou se a URL não for `https://` sem caminho (ADR-0003 §7). Até a Issue #229 entrar, o Terraform valida só a chave e o resto é a aplicação que barra; depois dela, remetente e URL faltando já param no `plan`. Abra os logs do deploy: a mensagem diz qual variável está errada, nunca o valor. Corrija o `terraform.tfvars` e repita o 5.3. Para voltar ao estado anterior na hora, use [Desligar o envio em emergência](#desligar-o-envio-em-emergência).

4. Registre em `docs/operations/ambientes.md`, em PR `docs` próprio: a linha "E-mail e IA" passa a `Email__Provider=Brevo`, e o "Registro de mudanças" ganha uma linha com a data. **Sem** chave, endereço do remetente ou domínio.

Ao terminar o dia:

```sh
unset RAILWAY_TOKEN TF_VAR_seed_password TF_VAR_brevo_api_key TF_VAR_brevo_webhook_secret SEGREDO_WEBHOOK DOMINIO BASE
```

Ligar a Brevo não envia nada por si só. O primeiro e-mail real sai no [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta).

---

## Passo 6 — Webhook transacional (opcional)

**Depende de duas Issues:** a do webhook (`POST /webhooks/brevo`; ADR-0003 §18, item 12; PR #265, em entrega) e a **Issue #229**, que cria `brevo_webhook_secret` no Terraform (em entrega: PR #333). Sem as duas na `main`, pule este passo. Sem o segredo, a rota nem existe e responde 404 (ADR-0003 §12).

Para que serve: a Brevo avisa quando uma mensagem não pôde ser entregue, e as telas passam a mostrar "e-mail não entregue". É aviso de melhor esforço, não registro oficial de entrega.

**Como a Brevo autentica a chamada.** Não há assinatura do corpo (HMAC). As formas documentadas são quatro: usuário e senha, token, cabeçalhos próprios e restrição pelas faixas de IP da Brevo [F22][F23]. **O ADR-0003 §12 (decisão D18) escolheu o cabeçalho próprio `X-Helinho-Webhook-Secret: <segredo>`**, com a autenticação do painel em **No authentication**. Motivo: nenhuma fonte diz em qual cabeçalho, nem em que formato, a opção **Token** envia o valor; com cabeçalho próprio, o nome e o formato são nossos.

- **Não use a opção Token nem Basic**, e **nunca ponha o segredo na URL:** ela fica em registro de acesso e aparece em texto no painel.
- **Não existe modo "sem segredo".** Se a Brevo não enviar o cabeçalho, a rota responde 401 e o webhook fica desligado até resolver.

**Os quatro eventos** (ADR-0003 §12 e D18; o roteiro da sprint, que fica fora do repositório, citava três, e o quarto, `invalid_email`, foi acrescentado de propósito):

| Evento no corpo | Rótulo no painel (pode variar [F23]) | Por que entra |
|---|---|---|
| `hard_bounce` | **Hard bounce** | Falha definitiva: a mensagem não vai chegar àquele endereço |
| `invalid_email` | **Invalid email** | Endereço inválido: é o caso do e-mail do responsável digitado errado |
| `blocked` | **Blocked** | A Brevo não enviou |
| `spam` | **Spam** | O destinatário marcou como spam; reenviar não resolve |

Todos os outros ficam **desligados**: não mudam nada na tela, e cada evento a mais é endereço e assunto de alguém trafegando sem necessidade.

**O que a Issue do webhook precisa garantir** (ADR-0003 §12; confira no PR dela antes de ligar): comparação do segredo em tempo constante; rota só por HTTPS; corpo limitado a 64 KB; o cabeçalho `X-Helinho-Webhook-Secret` e os campos `email`, `subject` e `reason` nunca gravados nem logados; limite de 60 chamadas por minuto por IP.

1. **Gere o segredo** no gerenciador de senhas: 32 caracteres ou mais (ADR-0003 §7), só letras e dígitos. Entrada sugerida: "Brevo — segredo do webhook helinho homologação".
2. **Railway, pelo Terraform**, com a variável `brevo_webhook_secret` da Issue #229 (confirme o nome no PR dela): `read -rs TF_VAR_brevo_webhook_secret && export TF_VAR_brevo_webhook_secret`, depois `plan`, `apply` e backup do estado, como no Passo 5. A partir daí o segredo também fica em texto no estado. Não crie a variável à mão no painel.
3. **Brevo:** menu da conta → **Integrations → Webhooks** → **Add webhook** → **Outbound webhook** → **Add webhook** [F22].
4. Dê um nome (por exemplo `helinho-homologacao`) e **Continue**.
5. **URL:** o valor de `App__PublicBaseUrl` mais `/webhooks/brevo` (no exemplo, `https://paineladm.example.org/webhooks/brevo`). Sempre `https://`.
6. **Authentication:** deixe em **No authentication**, que é o padrão [F22].
7. **Custom headers:** clique em **+ Add an object** e preencha o nome `X-Helinho-Webhook-Secret` e, no valor, o segredo, sem prefixo e sem aspas [F22].
8. **Sending strategy:** **Send one at a time** [F22]. O envio em lote muda o formato do corpo e não é usado (ADR-0003 §12).
9. **Continue**. Na categoria **Transactional email**, deixe ligados só os quatro eventos da tabela e desligue os demais.
10. Nos três pontos de um dos eventos, **Send test request** [F22]. Depois, **Activate webhook**.

**✔ Confira, em três partes.** Em terminal novo, defina `BASE` de novo:

```sh
BASE=$(terraform -chdir=infra/railway output -json ambientes | jq -r .homologacao.url_base_app)
```

Primeiro, sem o segredo:

```sh
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$BASE/webhooks/brevo" -H 'Content-Type: application/json' -d '{}'
```

Imprime `401`: a rota existe e recusa chamada sem segredo. Depois, com o segredo, lido sem aparecer na tela nem na lista de processos:

```sh
read -rs SEGREDO_WEBHOOK
```

```sh
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$BASE/webhooks/brevo" -H 'Content-Type: application/json' -H @<(printf 'X-Helinho-Webhook-Secret: %s\n' "$SEGREDO_WEBHOOK") -d '{"event":"delivered","message-id":"teste-do-runbook"}'
```

```sh
unset SEGREDO_WEBHOOK BASE
```

Imprime `200`: o segredo da Railway é o mesmo do gerenciador, e evento fora da lista é aceito e ignorado (ADR-0003 §12). **Confira:** o comportamento exato vem da Issue do webhook.

Por fim, na Brevo: **Integrations → Webhooks → Outbound webhooks** → clique no webhook → em **Statistics**, o **Send test request** aparece como **Delivered** [F22]. É a prova de que **a Brevo envia o cabeçalho próprio**, ponto que o ADR-0003 §12 deixou para este teste. **Confira:** se ela o envia também nas novas tentativas só dá para ver em uso.

**Se der errado:**

- O primeiro `curl` imprime `404` → o segredo não chegou ao serviço, ou o commit publicado não tem a rota.
- O segundo `curl` imprime `401` → o valor na Railway não é o do gerenciador; refaça o item 2.
- Qualquer que seja o erro, não deixe o segredo na sessão: `unset SEGREDO_WEBHOOK TF_VAR_brevo_webhook_secret`.
- Os dois `curl` passam e o teste da Brevo aparece em **Retries** ou **Failed** → a Brevo não mandou o cabeçalho, ou mandou alterado. Confira nome e valor no item 7 (espaço no fim, aspas). Se estiver certo, **desative o webhook** (o botão de ligar e desligar na lista [F22]), inclua o caso nas [perguntas ao suporte](#perguntas-para-o-suporte-da-brevo) e abra uma Issue. Não troque para a opção **Token** por conta própria: a rota só aceita o cabeçalho próprio.

Resposta 4xx (menos 429) ou 5xx do sistema faz a Brevo **descartar o evento, sem nova tentativa** [F23]. Enquanto o segredo estiver errado, os eventos se perdem. Para trocar o segredo, mude os dois lados em seguida; os eventos do intervalo são perdidos.

---

## Passo 7 — Teste de fumaça de ponta a ponta

**Só depois que os fluxos existirem** (worker da fila, Issue #228; tratador de e-mail, convite de administrador, redefinição de senha e acesso do aluno, em entrega; a fila, os tokens e a página `/conta/definir-senha` já estão na `main`). Os nomes de tela e de situação abaixo vêm do ADR-0003 §11 e podem mudar nessas Issues.

Use **um e-mail seu**, de uma caixa que você abre. É o único dado real em homologação: não o escreva no repositório, em Issue nem em PR.

**É a condição 5 da D17** (ADR-0003 §8.4): o fluxo é percorrido **a partir do e-mail recebido**, clicando no botão, para provar que o redirecionamento da Brevo não quebra o link de verdade. Não copie a URL para o navegador nem monte o link à mão.

1. **Crie uma unidade de teste.** No painel da homologação, como `master@helinho.example`, crie uma unidade com nome fictício (por exemplo "Unidade Teste Brevo"). As duas unidades do seed já têm administrador, e só cabe um ativo por unidade.
2. **Crie o administrador de teste** nessa unidade, com o seu e-mail. O formulário não pede senha (ADR-0003 §11.2).
   **✔ Confira:** na lista, a situação passa de "Aguardando envio" para "Convite enviado" em menos de um minuto.
3. **Receba o convite** e, antes de clicar, confira:
   - remetente e nome iguais aos do Passo 3; a mensagem não está no spam;
   - no "mostrar original" do seu provedor de e-mail: `dkim=pass` com o domínio institucional e `dmarc=pass` [F3];
   - a mensagem tem as duas partes, `text/plain` e `text/html` (ADR-0003 §17, item 2);
   - o assunto **não** traz nome de aluno nem matrícula (ADR-0003 §5.6): ele fica nos registros da Brevo;
   - o selo "Sent with Brevo" aparece, como em todo e-mail do plano gratuito [F1]; anote onde, e se atrapalha o layout;
   - o texto segue o ADR-0003 §8.4: o botão tem texto de ação ("Definir minha senha"), a URL com o token **não** aparece escrita no HTML, o endereço do site aparece por extenso e o e-mail não manda conferir o link antes de clicar;
   - **o link:** o endereço do botão é o do domínio da homologação ou o de um redirecionador? O esperado é o mesmo resultado do [Passo 4b](#passo-4b--envio-de-medição-do-link). Anote se for diferente, sem copiar o link para lugar nenhum.
4. **Defina a senha clicando no botão do e-mail.**
   **✔ Confira:** depois do redirecionamento, a barra de endereço mostra o domínio próprio do painel (o valor de `App__PublicBaseUrl`; no exemplo, `paineladm.example.org`), e a página aceita o link; depois de salvar, você cai na tela de entrar, sem login automático; entra com a senha nova; chega um segundo e-mail avisando que a senha foi alterada. Clicar de novo no botão do primeiro e-mail leva à saída "pedir novo link": ele é de uso único.
5. **Peça a redefinição** na tela de recuperar senha, com o mesmo e-mail.
   **✔ Confira:** chega o e-mail; o link funciona dentro de 1 hora; você define outra senha e entra; chega o aviso de senha alterada. Repita com `nao-existe@example.org`: a tela responde igual e **nenhum** e-mail sai.
6. **Confira a fila.** As linhas `email.*` do teste terminam como concluídas com sucesso, com o id da mensagem da Brevo e **sem** endereço, corpo ou token gravados (ADR-0003 §5.3). **Confira:** os nomes de tabela e de coluna e a consulta vêm da Issue da fila (#223); o acesso ao banco é pelo túnel do [8.4 do runbook da Railway](railway-runbook.md#84-pg_dump-para-fora-da-plataforma).
7. **Confira o painel da Brevo:** **Transactional → Logs**, filtrando pelo seu endereço [F20].
   **✔ Confira:** quatro mensagens (convite, aviso, redefinição, aviso) com evento de entrega e a etiqueta do tipo (`email.…`), nunca um dado pessoal na etiqueta. O ícone do olho não mostra o conteúdo, por causa do [1.4](#14-retenção-dos-registros-na-brevo). Nenhuma mensagem para `nao-existe@example.org`. O consumo do dia subiu 4.
8. **Limpe.** Bloqueie ou apague o administrador de teste, conforme a tela permitir, e apague a unidade de teste. Na Brevo, abra cada registro do seu endereço e use **Manually delete the transactional log** (a exclusão pode levar até 72 horas [F20]), ou deixe a regra de retenção apagar.

**Se der errado:**

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| A situação fica em "Aguardando envio" | O worker não está rodando, ou os envios estão suspensos | Logs do `helinho-web`: procure a linha em `Error` do worker, com o código da resposta |
| Log com 401 | Chave errada, vencida, desativada por 90 dias sem uso, ou IP barrado | [4.2](#42-criar-a-chave), [4.3](#43-bloqueio-por-ip) e [8.1](#81-rotação-de-rotina). Nada se perde: o worker tenta de novo a cada 15 minutos, até o prazo de cada e-mail (ADR-0003 §6.3) |
| Log com 402, 403, `not_enough_credits` ou `account_under_validation` | Cota do dia esgotada, ou conta em validação | [1.1](#11-entrar-na-conta-da-contratante) e [1.2](#12-plano-e-limite-diário) |
| Log com "Access denied for resource" | A chave restrita não tem permissão de envio | [4.2](#42-criar-a-chave), item 5 |
| Erro 400 e o trabalho falha na hora | Remetente diferente do cadastrado, ou domínio não autenticado | Compare `Brevo__SenderEmail` com o Passo 3, letra por letra |
| A situação vira "Não entregue" | A caixa de destino recusou | **Transactional → Logs** mostra o motivo |
| Chega no spam | DKIM ou DMARC falhando | [2.4](#24-conferir-a-propagação-com-dig) e [2.5](#25-concluir-a-autenticação) |
| Depois do redirecionamento, a página abre no host errado | `App__PublicBaseUrl` errada | [5.1](#51-editar-o-terraformtfvars) |
| A página diz "link inválido" no primeiro clique, e a URL na barra de endereço está cortada ou diferente | O redirecionador da Brevo alterou o link | É um dos gatilhos do plano B da D17 (código digitado): registre no ADR-0003 §8.4 e não ligue em produção |
| "Link inválido" logo depois de um deploy | As chaves do Data Protection não persistem | PR #120 (ADR-0003 §10.4) |

---

## Passo 8 — Rotação da chave e vazamento

### 8.1 Rotação de rotina

Quando: antes de a validade vencer, ao trocar de máquina, quando alguém com acesso sai, quando a Brevo desativa a chave por falta de uso e antes de ligar produção.

1. Crie uma chave nova ([4.2](#42-criar-a-chave)), com outro nome (por exemplo com o mês). **Não apague a antiga ainda.**
2. No terminal do Terraform:

```sh
read -rs TF_VAR_brevo_api_key && export TF_VAR_brevo_api_key
```

3. `plan` e `apply`, como no [5.3](#53-plan-e-apply). Esperado: uma mudança, em `railway_variable_collection.web`. O `apply` reimplanta o web.
4. Prove que a chave nova funciona: um envio real (peça uma redefinição de senha para você) ou a checagem opcional do [5.2](#52-exportar-a-chave).
5. Na Brevo, nos três pontos ao lado da chave **antiga**: **Delete API key**. É irreversível [F8].
6. Backup cifrado do estado ([5.7 do runbook da Railway](railway-runbook.md#57-backup-cifrado-do-estado)), troca do valor na entrada do gerenciador de senhas (o antigo é apagado, não guardado ao lado) e uma linha no "Registro de mudanças" de `ambientes.md`, sem o valor.

**✔ Confira:** a lista da Brevo só tem a chave nova e um e-mail de teste chegou depois da troca.

**Se der errado:** o envio falha com a chave nova → **não apague a antiga**; volte a exportá-la e refaça o `apply`.

### 8.2 Se a chave vazar

Vazou se o valor apareceu em tela compartilhada, chat, e-mail, log, histórico do shell, commit, Issue ou PR, ou se alguém sem autorização leu o `terraform.tfstate`, um arquivo `.tfplan` ou as variáveis do serviço na Railway.

**A ordem importa: revogar na Brevo primeiro, trocar na Railway depois.**

1. **Brevo, já:** três pontos ao lado da chave → **Delete API key** [F8]. A partir daí ninguém envia com ela. O sistema também para: a API responde erro de autenticação, o worker suspende e **não perde** os e-mails que ainda estão no prazo (redefinição 30 minutos; convite 24 horas; acesso do aluno 72 horas; ADR-0003 §6.2 e §6.3).
2. Crie a chave nova, exporte, `plan`, `apply` e backup do estado ([8.1](#81-rotação-de-rotina), itens 1 a 4 e 6). No gerenciador de senhas, **substitua** o valor antigo pelo novo: a chave revogada não fica guardada em lugar nenhum.
3. **Veja o que foi feito com a chave antiga.** Em **Transactional → Logs**, procure envios que o sistema não fez: destinatários, assuntos ou horários estranhos [F20]. Em **Settings → Security → Authorized IPs**, procure IPs que não são seus nem da Railway [F12]. A chave restrita do [4.2](#42-criar-a-chave) também **lê** os registros: quem a teve pôde ver os endereços dos destinatários.
4. **Feche a origem do vazamento:**

| Por onde vazou | O que mais fazer |
|---|---|
| `terraform.tfstate`, `.tfstate.backup` ou arquivo `.tfplan` | O arquivo tem **todos** os segredos em texto: troque também o token da Railway, a senha do seed e a do banco ([8.6](railway-runbook.md#86-rotação-do-token-da-railway) e [8.7](railway-runbook.md#87-rotação-das-senhas) do runbook da Railway) e reveja onde o estado fica ([8.9](railway-runbook.md#89-onde-fica-o-estado-do-terraform)) |
| Commit, Issue ou PR | A rotação é a correção: o histórico não é reescrito (`git push --force` é proibido). Abra uma Issue `security:sensitive`, sem o valor |
| Histórico do shell | No terminal onde digitou, rode `unset HISTFILE` (zsh e bash deixam de gravar o histórico ao sair); apague a linha do arquivo de histórico (`~/.zsh_history` no zsh, `~/.bash_history` no bash); feche esse terminal e abra outro. Só apagar a linha não basta: o shell regrava o histórico da memória quando fecha |
| **Segredo do webhook** (`Brevo__WebhookSecret`) | Não dá acesso a dado: quem o tem consegue forjar avisos de "e-mail não entregue" (ADR-0003, ameaça T11). Gere outro, troque na Railway (`TF_VAR_brevo_webhook_secret`, `plan`, `apply`) e, em seguida, no cabeçalho do webhook na Brevo ([Passo 6](#passo-6--webhook-transacional-opcional)). Os eventos do intervalo se perdem |
| Painel da Railway (alguém com acesso ao projeto) | Reveja com o Igor quem está no workspace |

5. **Trate como incidente:** avise a contratante e anote data, origem e o que os registros mostraram. Se houve envio indevido ou leitura dos registros, há dado pessoal envolvido (endereços dos responsáveis): quem avalia a comunicação exigida pela LGPD é a contratante.

Os backups cifrados antigos do estado ainda guardam a chave antiga; depois do item 1 ela não vale mais nada.

### 8.3 Onde a chave fica guardada

| Lugar | Quem lê |
|---|---|
| Gerenciador de senhas do Henrique | Henrique |
| Variável `Brevo__ApiKey` do serviço `helinho-web`, na Railway | Quem tem acesso ao projeto na Railway |
| `infra/railway/terraform.tfstate` e `.tfstate.backup`, **em texto** | Quem lê o disco do Henrique ([8.9 do runbook da Railway](railway-runbook.md#89-onde-fica-o-estado-do-terraform)) |
| Arquivos `.tfplan` em `~/.helinho-infra/`, até serem apagados | Idem |
| Backups cifrados `tfstate-*.age` | Quem tem a frase-senha |

Em nenhum outro lugar: nem no repositório, nem no `terraform.tfvars`, nem em `ambientes.md`, nem em log (o adaptador marca o cabeçalho `api-key` como sensível; ADR-0003 §4.4).

---

## Operação

### Desligar o envio em emergência

Quando: e-mails saindo errados, para quem não devia, ou em laço.

**Freio imediato, sem deploy:** na Brevo, três pontos ao lado da chave → **Deactivate API key** [F8]. O envio para em seguida, o worker suspende sem perder a fila (ADR-0003 §6.3) e dá para voltar com **Activate API key**.

**Desligar de verdade:**

1. Em `infra/railway/terraform.tfvars`, troque para `Email__Provider = "Fake"`.
2. `plan` (esperado: uma mudança, em `railway_variable_collection.web`) e `apply`, como no [5.3](#53-plan-e-apply). O `apply` reimplanta o web.
3. Backup do estado e uma linha em `ambientes.md`.

**✔ Confira:** a aba **Variables** mostra `Email__Provider=Fake` e nenhuma mensagem nova aparece em **Transactional → Logs**.

Com `Fake`, convite e redefinição **não se completam**: o link não chega a ninguém (ADR-0003 §7). **Confira (depende da Issue da fila):** o que estava pendente pode ser consumido pelo envio fake e dado como concluído; depois de religar, reenvie os convites pelo painel.

Não troque a variável à mão no painel da Railway: o Terraform não fica sabendo, e o `apply` seguinte desfaz.

### Onde ver os registros de envio

- **Brevo → Transactional → Logs** (também em `https://app-smtp.brevo.com/log` [F21]): cada evento de cada mensagem (enviada, entregue, devolvida, aberta, clicada), com filtro por destinatário, domínio, id da mensagem, assunto, período, evento, remetente e etiqueta [F20]. **É o único lugar que diz para qual endereço uma mensagem foi**: o banco do sistema guarda só o tipo, as datas e o id da mensagem (ADR-0003 §5.3).
- **Download CSV** exporta até 200.000 linhas [F20]. O arquivo tem endereços de destinatários: não vai para o repositório nem para chat, e é apagado depois do uso.
- **Fila do sistema:** as situações nas telas de Administradores e de Alunos, e os logs do `helinho-web` (id do trabalho, tipo, tentativa, classe do resultado, código HTTP e duração; nunca destinatário ou conteúdo, ADR-0003 §5.6).

### O que vigiar

Os dois primeiros são os jeitos de o envio parar sem ninguém mexer (ADR-0003 §13.1). Nos dois, o worker suspende sem perder a fila, e o aviso da Brevo vai para o e-mail do dono da conta.

| O quê | Como evitar | Quando conferir | Onde |
|---|---|---|---|
| **Chave desativada por 90 dias sem uso, ou vencida** | Validade de 1 ano com lembrete na agenda 30 dias antes; em homologação parada, uma chamada bem-sucedida a cada dois meses | Ao receber o aviso da Brevo (7 dias antes e no dia); erro de autenticação no log | [4.2](#42-criar-a-chave), [8.1](#81-rotação-de-rotina) |
| **Bloqueio de IP ligado sozinho depois de 30 dias sem IP novo** | Desativar o bloqueio para chaves de API antes do primeiro envio | Cerca de 35 dias depois do primeiro envio; sempre que o envio parar com erro de autenticação | [4.3](#43-bloqueio-por-ip) |
| Consumo do dia perto de 300 | `Email__DailyLimit` do nosso lado, quando a Issue #229 entrar; plano pago antes de carga real | Antes de liberar muitos alunos de uma vez | [1.2](#12-plano-e-limite-diário) |
| Linha em `Error` do worker | — | É o gatilho de alerta da suspensão (ADR-0003 §6.3) | Logs do `helinho-web` |
| Domínio deixou de estar **Authenticated** | Manter os registros no DNS | Depois de qualquer mudança no DNS da instituição | [2.5](#25-concluir-a-autenticação) |
| Rastreamento anônimo, prévias e retenção voltaram ao padrão | — | Depois de mudar de plano ou de alguém mexer nas configurações da conta | [1.3](#13-rastreamento-de-cliques-e-de-abertura), [1.4](#14-retenção-dos-registros-na-brevo) |

### LGPD: o que a Brevo guarda

| Dado | Fica na Brevo? | Como limitar |
|---|---|---|
| Endereço do destinatário, assunto, remetente, etiquetas, id da mensagem, eventos e links clicados | Sim, nos registros; **sem prazo por padrão** [F19][F20] | Regra de retenção de 1 mês ([1.4](#14-retenção-dos-registros-na-brevo)); rastreamento anônimo esconde o endereço e os links nos eventos de abertura e clique ([1.3](#13-rastreamento-de-cliques-e-de-abertura)) |
| Conteúdo da mensagem (prévia em HTML: primeiro nome do aluno, matrícula e o link) | Só se as prévias estiverem ligadas [F19] | **Never store previews** ([1.4](#14-retenção-dos-registros-na-brevo)); uma prévia já guardada pode ser apagada à mão no registro [F19] |
| A URL de destino de cada link, com o token, no redirecionador de cliques | Muito provavelmente sim, enquanto o link vale (ADR-0003 §8.4, ameaça T17); por quanto tempo e quem vê, sem fonte | Não há como desligar no plano gratuito. O que limita: token de uso único e de validade curta, rastreamento anônimo, e as [perguntas ao suporte](#perguntas-para-o-suporte-da-brevo) respondidas antes de dado real |
| Etiqueta | Sim | Leva só o tipo do trabalho, nunca dado pessoal (ADR-0003 §4.1) |
| Assunto | Sim, nos registros, e volta no webhook | Nunca leva nome de aluno nem matrícula (ADR-0003 §5.6) |

- **Pedido de exclusão de um titular:** apague os registros daquele endereço. Pelo painel, um a um (**Manually delete the transactional log** [F20]). Pela API, `DELETE /v3/smtp/log/<endereço>` apaga os registros e as prévias daquele destinatário [F21]; exige chave com permissão total em e-mail transacional e ainda não tem procedimento próprio neste runbook. A exclusão pode levar até 72 horas [F20].
- **Os dados saem do Brasil:** passam pela Brevo e pela Railway. Antes de dado real é preciso a resposta da contratante sobre fornecedores (C14, C20; pergunta M12), contrato de tratamento de dados com a Brevo e o ADR de dado real (ADR-0003 §14). Até lá, só dado sintético e o seu próprio e-mail.
- **Sem link de descadastro:** são mensagens transacionais, sem conteúdo promocional (ADR-0003 §8.3). SPF, DKIM e DMARC são o Passo 2; a identificação do remetente e o contato no rodapé são a pergunta M10.

---

## Perguntas para o suporte da Brevo

É a condição 4 da D17 (ADR-0003 §8.4): respondidas **antes de dado real**. Envie pelo canal de suporte da conta (o caminho no painel pode variar), depois de fazer o [Passo 4b](#passo-4b--envio-de-medição-do-link), para já contar o que você mediu. O texto está em inglês, pronto para colar; não inclua chave, endereço de destinatário nem o domínio institucional.

```text
Subject: Transactional emails sent through the API: link tracking, quota and webhooks

Hello,

We send transactional emails (account invitations and password resets)
through POST /v3/smtp/email, on a shared IP. Each email carries a
single-use link with a token in the query string. Before going live we
need to confirm the following:

1. Link rewriting: are the links of a transactional email sent through
   the API rewritten for click tracking on our account? Is the
   plain-text part (textContent) rewritten as well?
2. Can click tracking (link rewriting) be turned off for transactional
   emails, for the whole account or per message? On which plan?
3. Does "Anonymous email tracking" stop the links from being rewritten,
   or does it only hide the events in the logs?
4. What is stored about the destination URL of a tracked link, for how
   long, and who in the account can see it?
5. Which domain hosts the click redirect? Does a branded subdomain apply
   to transactional emails on a shared IP? Does the redirect always keep
   the full destination URL, including a long query string?
6. Daily quota on the Free plan: once the 300 emails are used, what does
   POST /v3/smtp/email return (HTTP status and error code)? At what time
   does the quota reset?
7. Outbound webhooks: with the "Token" authentication option, which HTTP
   header carries the token, and in which format? Are custom headers
   sent on every call, including "Send test request" and retries?

Thank you.
```

| Pergunta | Para que serve | Onde a resposta entra |
|---|---|---|
| 1, 3 e 5 | Confirmar o que o Passo 4b mediu | ADR-0003 §8.4 e §17, item 7 |
| 2 | Saber se existe saída sem reescrita, e a que custo (hoje a fonte é um fórum de 2024) | ADR-0003 §8.4, opção B; decisão D7; pergunta M14 |
| 4 | Tamanho real do risco aceito na D17 (ameaça T17) | ADR-0003 §8.4; [LGPD](#lgpd-o-que-a-brevo-guarda) |
| 6 | Como o worker classifica a resposta ao estourar a cota | ADR-0003 §6.3 e §17, item 1; [1.2](#12-plano-e-limite-diário) |
| 7 | Confirmar a escolha do cabeçalho próprio | ADR-0003 §12 e §17, item 5; [Passo 6](#passo-6--webhook-transacional-opcional) |

Guarde a resposta fora do repositório e leve os fatos para o ADR em PR `docs`, sem copiar dado da conta.

## Fatos não confirmados e onde cada um é tratado

| # | Fato não confirmado | Origem | Passo |
|---|---|---|---|
| 1 | O que a API responde quando o limite diário estoura; a hora em que o limite zera | ADR-0003 §17, item 1 | [1.2](#12-plano-e-limite-diário); pergunta 6 ao suporte |
| 2 | Processo e prazo de validação de uma conta nova | este runbook | [1.1](#11-entrar-na-conta-da-contratante) |
| 3 | Onde ver o plano e o consumo do dia no painel | este runbook | [1.2](#12-plano-e-limite-diário) |
| 4 | Se o link chega reescrito nesta conta, no HTML e no texto puro; para qual domínio; se o rastreamento anônimo muda isso; se a URL chega inteira; o que fica guardado do destino; se desligar continua restrito ao Enterprise; se o subdomínio de marca vale para transacional | ADR-0003 §17, item 7 | [Passo 4b](#passo-4b--envio-de-medição-do-link) (mede); perguntas 1 a 5 ao suporte; [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta) |
| 5 | Nomes exatos dos registros `CNAME` de DKIM; qual dos dois fluxos de domínio a conta mostra | ADR-0003 §17, item 9 | [2.2](#22-adicionar-o-domínio-na-brevo) |
| 6 | Qual recurso da chave restrita cobre `POST /v3/smtp/email` e as rotas de leitura | este runbook | [4.2](#42-criar-a-chave), [5.2](#52-exportar-a-chave) |
| 7 | Se o bloqueio de IP nasce ligado em conta nova; o erro devolvido; se ele volta sozinho depois de desativado | ADR-0003 §17, item 6 | [4.3](#43-bloqueio-por-ip) |
| 8 | O que o Terraform faz com chave não declarada em `variaveis_web` (muito provavelmente ignora em silêncio) | este runbook | [5.1](#51-editar-o-terraformtfvars) |
| 9 | Mensagens de erro da aplicação na subida com configuração incompleta | Issues #204 e #209 | [5.4](#54-conferir-o-deploy) |
| 10 | Se a Brevo envia o cabeçalho próprio em toda chamada do webhook, inclusive no teste e nas novas tentativas; rótulos dos eventos no painel; em qual cabeçalho vai o token da opção **Token** (que por isso não é usada) | ADR-0003 §17, item 5 | [Passo 6](#passo-6--webhook-transacional-opcional); pergunta 7 ao suporte |
| 11 | Nomes finais no Terraform (`brevo_webhook_secret`, `Email__DailyLimit`) e o texto das validações novas | Issue #229 (em entrega, PR #333) | [5.1](#51-editar-o-terraformtfvars), [5.3](#53-plan-e-apply), [Passo 6](#passo-6--webhook-transacional-opcional) |
| 12 | Nomes de tela, de situação, de tabela e de coluna dos fluxos e da fila | Fila na `main`; worker (Issue #228) e fluxos em entrega | [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta) |
| 13 | Se `htmlContent` e `textContent` saem juntos; onde o selo "Sent with Brevo" aparece em e-mail enviado pela API | ADR-0003 §17, itens 2 e 8 | [Passo 4b](#passo-4b--envio-de-medição-do-link), itens 4 e 6; [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta), item 3 |
| 14 | O que acontece com a fila pendente ao trocar para `Fake` | Issues #223 e #228 | [Desligar o envio em emergência](#desligar-o-envio-em-emergência) |
| 15 | O que a API responde com a chave desativada por falta de uso ou vencida (supõe-se erro de autenticação) | ADR-0003 §17, item 13 | [4.2](#42-criar-a-chave) |
| 16 | Se o padrão das prévias é guardar ou não guardar (o artigo da Brevo se contradiz) | ADR-0003 §17, item 10 | [1.4](#14-retenção-dos-registros-na-brevo) |
| 17 | Se o caminho dos transacionais também mostra o aviso com **Activate** ao ligar o rastreamento anônimo | este runbook | [1.3](#13-rastreamento-de-cliques-e-de-abertura) |

Quando um desses pontos for resolvido na prática, troque o "Confira:" pelo fato, em PR `docs`.

## Diferenças entre o ADR-0003, o Terraform e o runbook da Railway

Encontradas ao escrever a primeira versão deste runbook. O ADR-0003 foi alinhado a elas no commit `c036dd4` e **aceito pelo Henrique em 22/11/2026** (commit `589d9ff`); a tabela diz onde cada uma ficou e o que ainda pede Issue.

| # | Diferença encontrada | Situação em 22/11/2026 | Onde neste runbook |
|---|---|---|---|
| 1 | O ADR pedia "rastreamento desligado" como passo deste runbook; esse controle não existe fora do plano Enterprise | **Decidido:** ADR-0003 §8.4, decisão D17 aceita (manter o link, com cinco condições; plano B: código digitado) | [1.3](#13-rastreamento-de-cliques-e-de-abertura), [Passo 4b](#passo-4b--envio-de-medição-do-link), [perguntas ao suporte](#perguntas-para-o-suporte-da-brevo) |
| 2 | `Email__DailyLimit` e `Brevo__WebhookSecret` não existem em `infra/railway/variables.tf` (na `main` de 25/11/2026 continuam ausentes) | **Em entrega:** Issue #229, PR #333; precisa estar aplicada antes de qualquer envio em volume | Tabela de variáveis; [1.2](#12-plano-e-limite-diário); [5.1](#51-editar-o-terraformtfvars) |
| 3 | `App__PublicBaseUrl` era preenchida à mão | **Resolvido no Terraform:** vem de `dominio_proprio` (Issue #220; na `main`, PR #330); o valor explícito em `variaveis_web` vence. Derivá-la do domínio `*.up.railway.app` ficou fora do escopo da Issue #229 | [5.1](#51-editar-o-terraformtfvars) |
| 4 | O Terraform valida só a chave; remetente ou URL pública faltando passam no `plan` e derrubam o deploy | **Em entrega:** validações da Issue #229 (PR #333) | [5.3](#53-plan-e-apply), [5.4](#54-conferir-o-deploy) |
| 5 | O bloqueio de IP se liga sozinho depois de 30 dias sem IP novo; a chave é desativada depois de 90 dias sem uso | **Decidido:** ADR-0003 §13.1 e ameaça T19 | [4.2](#42-criar-a-chave), [4.3](#43-bloqueio-por-ip), [O que vigiar](#o-que-vigiar) |
| 6 | A fila de 1.000 da Brevo não substitui o teto do nosso lado: a redefinição (1 h) venceria parada nela | **Decidido:** ADR-0003 §15 e D7 | [1.2](#12-plano-e-limite-diário) |
| 7 | A retenção de 30 dias da fila não vale do lado da Brevo sem configurar a conta | **Decidido:** ADR-0003 §5.6 e D9 | [1.4](#14-retenção-dos-registros-na-brevo) |
| 8 | Webhook: o ADR supunha `Authorization: Bearer`, que a documentação não confirma; o roteiro da sprint citava três eventos | **Decidido:** ADR-0003 §12 e D18 (cabeçalho próprio `X-Helinho-Webhook-Secret` e quatro eventos, com `invalid_email` de propósito) | [Passo 6](#passo-6--webhook-transacional-opcional) |
| 9 | Fatos que estavam sem fonte direta: selo no gratuito, registros DNS, retenção, fila de 1.000 | **No ADR:** fontes F22 a F31, lidas pela API pública da central de ajuda | [Fontes](#fontes) |
| 10 | O runbook da Railway diz que o terminal do Terraform carrega dois segredos e lista 13 variáveis do serviço web ([4.3](railway-runbook.md#43-exportar-os-segredos-sem-deixá-los-no-histórico), [5.3](railway-runbook.md#53-plan)). Com a Brevo ligada são três segredos (quatro com o webhook) e mais variáveis | **Pendente, depende do código em entrega:** na `main` de 25/11/2026 o runbook da Railway está certo (13 variáveis, dois segredos). Ele muda quando o PR #333 (Issue #229) entrar: ajustar então, em PR `docs` próprio | | [Convenções](#convenções); [5.3](#53-plan-e-apply) |
| 11 | **Ordem da medição.** O ADR-0003 (§8.4, condição 1; §16; §18, item 5) diz que a medição vem **antes** da Issue dos tokens. Em 22/11/2026 o Henrique decidiu implementar os tokens e a página antes, porque a chave ainda não tinha chegado | **Resolvido em 25/11/2026:** o ADR-0003 ganhou uma nota de situação com a ordem nova (a medição trava ligar `Email__Provider=Brevo`, não a construção). Este runbook já seguia a decisão | | [Antes de começar](#antes-de-começar), [Passo 4b](#passo-4b--envio-de-medição-do-link), [Passo 5](#passo-5--variáveis-na-railway-pelo-terraform) |
| 12 | A Issue #206 fala em guardar a chave "só na Railway"; o Terraform exige a chave em todo `plan` | **Decidido neste runbook:** gerenciador de senhas e Railway, mais o estado do Terraform | [4.2](#42-criar-a-chave), [8.3](#83-onde-a-chave-fica-guardada) |

## Fontes

Todas consultadas em **19/11/2026**; F15 e F16 foram relidas em **22/11/2026**, na resposta à revisão. As páginas HTML de `help.brevo.com` responderam 403 à leitura automática, como o ADR-0003 já registrava; os artigos da central de ajuda foram lidos pela API pública dela (`https://help.brevo.com/api/v2/help_center/en-us/articles/<id>.json`, com o mesmo `<id>` do endereço abaixo). A página HTML de preços não devolveu texto à ferramenta.

| # | Assunto | Fonte |
|---|---|---|
| F1 | Plano gratuito: 300 envios por dia, fila de 1.000 transacionais, selo "Sent with Brevo" | https://help.brevo.com/hc/en-us/articles/208580669 |
| F2 | Preços: Starter e o complemento que tira o selo | https://www.brevo.com/pricing.md ("Generated on: 2026-09-29") |
| F3 | Autenticar o domínio (fluxo clássico): menus, botões, registros, prazo de 48 horas, SPF e MX, DMARC único, Cloudflare | https://help.brevo.com/hc/en-us/articles/12163873383186 |
| F4 | Domínio pelo fluxo guiado: subdomínio de marca, registros individuais, **Delegate**, **Authenticate domain** | https://help.brevo.com/hc/en-us/articles/35337929909778 |
| F5 | Autenticação de domínio: exigência de Gmail, Yahoo e Microsoft | https://help.brevo.com/hc/en-us/articles/17286219877778 |
| F6 | Domínios pela API: registros `TXT` (código, DKIM `mail._domainkey`, DMARC `_dmarc`) | https://developers.brevo.com/docs/domain-authentication-and-verification e https://developers.brevo.com/docs/domain-creation-and-management |
| F7 | Criar remetente: menus, campos, código de 6 dígitos, verificação automática com domínio autenticado | https://help.brevo.com/hc/en-us/articles/208836149 |
| F8 | Chaves de API: criar, validade, acesso restrito, valor visível uma vez, 90 dias sem uso, desativar e apagar (atualizado em 04/11/2026) | https://help.brevo.com/hc/en-us/articles/40159485019794 |
| F9 | Permissões da chave de API: recursos, níveis, "Access denied for resource" | https://help.brevo.com/hc/en-us/articles/40158285046930 |
| F10 | Chave de API no cabeçalho `api-key` | https://developers.brevo.com/docs/api-key-authentication |
| F11 | Chaves SMTP: login e senha, aba **SMTP** | https://help.brevo.com/hc/en-us/articles/7959631848850 e https://developers.brevo.com/docs/smtp-integration |
| F12 | IPs autorizados: lista da conta, fase de aprendizado, ativação depois de 30 dias, aviso por e-mail, como desativar | https://help.brevo.com/hc/en-us/articles/5740111683858 e https://developers.brevo.com/docs/ip-security |
| F13 | Fonte de terceiros: bloqueio ligado por padrão em contas criadas depois de 16/05/2024; texto do erro | https://wpmailsmtp.com/docs/brevo-unauthorized-not-verified/ |
| F14 | IP de saída fixo na Railway: só no plano Pro, sem garantia de exclusividade, muda com a região | https://docs.railway.com/networking/static-outbound-ips |
| F15 | Envio transacional: parâmetros, `removeTrackingPixel`, `contactPixelTrackingConsent`, códigos de erro. Relida em 22/11/2026: `removeTrackingPixel` traz "Only available to Enterprise plans: for other accounts the field is accepted but ignored" e "Click tracking is unchanged" | https://developers.brevo.com/reference/sendtransacemail e https://developers.brevo.com/changelog/2026/7/20 |
| F16 | Rastreamento anônimo em e-mails transacionais (atualizado em 18/11/2026; relido em 22/11/2026: o botão **Activate** aparece no caminho das campanhas, não no dos transacionais) | https://help.brevo.com/hc/en-us/articles/11643306229906 |
| F17 | Cliques medidos com links individualizados (atualizado em 11/11/2026) | https://help.brevo.com/hc/en-us/articles/37113920427922 |
| F18 | Fórum oficial, **fonte fraca**: desligar o rastreamento em transacionais só sob pedido e para Enterprise (10/07/2024 e 18/10/2024); relatos sobre o domínio de rastreamento | https://community.brevo.com/t/no-way-to-disable-by-option-tracking-in-transactional-e-mail/201/28 e https://community.brevo.com/t/why-no-custom-domain-for-click-tracking/710 |
| F19 | Retenção dos registros e das prévias: sem prazo por padrão, regra de 1 a 24 meses, **Never store previews** | https://help.brevo.com/hc/en-us/articles/4415743225746 |
| F20 | Registros transacionais: onde ver, filtros, prévia, exportar, apagar | https://help.brevo.com/hc/en-us/articles/360021533839 |
| F21 | Apagar registros por destinatário pela API; endereço da tela de registros | https://developers.brevo.com/docs/deleting-transactional-logs-based-on-recipient-address-to |
| F22 | Webhooks de saída no painel: menus, autenticação, cabeçalhos, estratégia de envio, teste, estatísticas | https://help.brevo.com/hc/en-us/articles/27824932835474 |
| F23 | Webhooks: eventos e corpo, formas de proteção, criação pela API, novas tentativas | https://developers.brevo.com/docs/transactional-webhooks, https://developers.brevo.com/docs/secured-webhooks, https://developers.brevo.com/reference/create-webhook e https://developers.brevo.com/docs/retry-mechanism |

Documentos do repositório usados como especificação: `docs/architecture/ADR-0003-emails-brevo.md` e `docs/context/pendencias.md` (lidos na branch do PR #185, commit `589d9ff`), `infra/railway/variables.tf`, `terraform.tfvars.example`, `main.tf`, `modules/ambiente/main.tf`, `outputs.tf` e `README.md` (lidos na branch do PR #100, commit `61f4d51`, que já traz o `dominio_proprio`), a Issue #229, `docs/operations/railway-runbook.md` e `docs/operations/ambientes.md`. Os códigos C04, C13, C14, C15, C18 e C20 são itens do Q&A com a contratante, mantido pelo Henrique fora do repositório.
