# Runbook — envio real de e-mails com a Brevo

**Para quem:** Henrique, sozinho, no navegador e na própria máquina (macOS). **Escrito em:** 19/11/2026 (Issue #206).
**Descreve:** o ADR-0003 (`docs/architecture/ADR-0003-emails-brevo.md`, PR #185, ainda **proposto**), o Terraform de `infra/railway/` (PR #100) e o [runbook da Railway](railway-runbook.md). O código que envia e-mail ainda está em Issues abertas ou por criar; veja [Antes de começar](#antes-de-começar).

> **Nada deste runbook foi executado.** Nenhum agente criou conta, domínio, remetente ou chave, nem chamou a API da Brevo. Os passos de painel foram conferidos na documentação da Brevo e da Railway em 19/11/2026 (seção [Fontes](#fontes); as marcas `[F1]`, `[F2]`… apontam para ela). O painel da Brevo é em inglês e muda com frequência: se a tela não bater com o texto, vale a tela. Onde a documentação não confirma um nome ou um comportamento, o passo traz **Confira:**; o índice está em [Fatos não confirmados](#fatos-não-confirmados-e-onde-cada-um-é-tratado).

Só dados sintéticos em homologação. A única exceção deste runbook é o seu próprio e-mail, no teste de fumaça.

## O caminho feliz em 10 linhas

1. **Pré-requisitos:** homologação no ar pelo runbook da Railway; ADR-0003 (PR #185) aceito; adaptador (Issue #204) e modelos (Issue #205) na `main`. Sem a fila e os fluxos (Issues ainda por criar), ligar a Brevo não envia nada. → [Antes de começar](#antes-de-começar)
2. **Conta e plano:** entrar na conta Brevo da contratante; o plano gratuito envia **300 e-mails por dia**; ligar o rastreamento anônimo e a regra de retenção sem prévias. → [Passo 1](#passo-1--conta-e-plano)
3. **Domínio:** **Settings → Senders, Domains, IPs → Domains → Add a domain**; mandar os registros (código da Brevo, DKIM e DMARC) a quem administra o DNS da FUNDHAS; conferir com `dig`; concluir a autenticação. → [Passo 2](#passo-2--autenticar-o-domínio-institucional)
4. **Remetente:** **Settings → Senders, Domains, IPs → Senders → Add a sender**. → [Passo 3](#passo-3--criar-o-remetente)
5. **Chave de API v3:** **Settings → SMTP & API → API Keys & MCP → Generate API key**, acesso restrito, direto para o gerenciador de senhas; desligar o bloqueio de IP desconhecido. Não é a chave SMTP. → [Passo 4](#passo-4--chave-de-api-v3)
6. **Terraform:** `variaveis_web` no `terraform.tfvars` (`Email__Provider = "Brevo"`, remetente, nome, `App__PublicBaseUrl`), `read -rs TF_VAR_brevo_api_key`, `plan` (1 mudança) e `apply`, que reimplanta o `helinho-web`; backup cifrado do estado. → [Passo 5](#passo-5--variáveis-na-railway-pelo-terraform)
7. **Conferir:** deploy verde, `/health/ready` 200 e a aba **Variables** com as quatro variáveis novas. → [5.4](#54-conferir-o-deploy)
8. **Webhook (opcional):** só depois da Issue do webhook e da Issue do Terraform que cria `Brevo__WebhookSecret`. → [Passo 6](#passo-6--webhook-transacional-opcional)
9. **Teste de fumaça, só depois que os fluxos existirem:** administrador de teste com o seu e-mail, convite, senha, login, redefinição; conferir a fila e **Transactional → Logs**. → [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta)
10. **Emergência:** `Email__Provider = "Fake"` e novo `apply`. **Chave vazou:** apague-a na Brevo primeiro, depois troque na Railway. → [Operação](#operação) e [Passo 8](#passo-8--rotação-da-chave-e-vazamento)

## Variável → onde pegar → onde colocar

| Variável da aplicação | Onde pegar na Brevo | Onde colocar | Segredo? |
|---|---|---|---|
| `Email__Provider` | Não vem da Brevo. Valor fixo `Brevo`; `Fake` desliga o envio real | `infra/railway/terraform.tfvars`, dentro de `variaveis_web` | não |
| `Brevo__ApiKey` | **Settings → SMTP & API → API Keys & MCP → Generate API key**; o valor aparece uma única vez ([4.2](#42-criar-a-chave)) | Gerenciador de senhas. No uso: `read -rs TF_VAR_brevo_api_key` no terminal do Terraform. Nunca em `tfvars`, arquivo, chat, Issue ou PR | **sim** |
| `Brevo__SenderEmail` | **Settings → Senders, Domains, IPs → Senders**: o campo **From email** do remetente criado no [Passo 3](#passo-3--criar-o-remetente), letra por letra | `terraform.tfvars`, em `variaveis_web` (o arquivo fica fora do git) | não |
| `Brevo__SenderName` | O campo **From name** do mesmo remetente (até 70 caracteres) | `terraform.tfvars`, em `variaveis_web` | não |
| `App__PublicBaseUrl` | Não vem da Brevo. É a saída `url_publica` do Terraform, com `https://`, sem caminho e sem barra no fim | `terraform.tfvars`, em `variaveis_web` | não |
| `Brevo__WebhookSecret` | Você gera (32 caracteres ou mais) e cola nos dois lados: na Brevo, em **Integrations → Webhooks**, e na Railway | **Ainda não existe no Terraform.** Entra por Issue própria (ADR-0003 §13 propõe `brevo_webhook_secret`, lida de `TF_VAR_brevo_webhook_secret`). Até lá, não crie à mão no painel da Railway | **sim** |
| `Email__DailyLimit` | O limite diário do plano ([1.2](#12-plano-e-limite-diário)): `300` no gratuito | **Ainda não existe em `variaveis_web`.** Entra pela mesma Issue do Terraform | não |
| Registros DNS (código da Brevo, DKIM, DMARC) | **Settings → Senders, Domains, IPs → Domains** | No DNS do domínio institucional, por quem o administra. Não vão para o repositório | não |

## Antes de começar

**O que precisa estar na `main` e publicado para o envio real funcionar:**

| O quê | Onde está | Sem ele |
|---|---|---|
| ADR-0003 aceito | PR #185 (Issue #176) | As decisões D1 a D16 não valem; este runbook descreve o que foi proposto |
| Terraform de `infra/railway/` e homologação no ar | PR #100; [runbook da Railway](railway-runbook.md), passos 1 a 7 | Não há onde pôr as variáveis |
| Adaptador `BrevoEmailSender`, opções validadas na subida, registro `Fake`/`Brevo` | Issue #204 | `Email__Provider=Brevo` não tem efeito: a aplicação continua com o envio fake |
| Renderizador e modelos dos e-mails | Issue #205 | Não há mensagem para enviar |
| Chaves do Data Protection no PostgreSQL | PR #120 (Issue #103) | Cada deploy invalida os links já enviados (ADR-0003 §10.4) |
| Fila (`QueueJobs`) e worker | **Issue da fila/worker (a criar;** ADR-0003 §18, itens 1, 8a e 8b**)** | Nada é enviado: todo e-mail passa pela fila |
| Provedores de token e página `/conta/definir-senha` | **Issue a criar** (ADR-0003 §18, item 5) | O link do e-mail não abre |
| Convite de administrador, redefinição de senha, acesso do aluno | **Issues a criar** (itens 9, 10 e 11; a 10 depende da Issue #184) | Nenhuma tela dispara e-mail; o [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta) não pode ser feito |
| Terraform: `brevo_webhook_secret`, `Email__DailyLimit` e validações | **Issue a criar** (item 6) | Sem webhook e sem teto diário na aplicação |
| Rota `POST /webhooks/brevo` | **Issue a criar** (item 12) | O [Passo 6](#passo-6--webhook-transacional-opcional) não pode ser feito |

**O que dá para adiantar hoje, sem esperar código:** os passos 1, 2 e 3 (conta, domínio e remetente). O domínio é o que mais demora: depende de outra pessoa e de até 48 horas de propagação. **Deixe a chave (Passo 4) para o dia do Passo 5:** chave sem uso por 90 dias é desativada pela Brevo [F8].

**O que você precisa ter em mãos** (C15 e C20; pendências M4 e M5 de `docs/context/pendencias.md`):

- acesso à conta Brevo da contratante, ou autorização para criá-la;
- o domínio institucional, por escrito, e o endereço do remetente;
- o contato de quem cria registros no DNS desse domínio.

Sem os três, pare no Passo 1. O sistema segue com `Email__Provider=Fake`.

## Convenções

- **Terminal do Terraform:** o mesmo do runbook da Railway ([4.3](railway-runbook.md#43-exportar-os-segredos-sem-deixá-los-no-histórico)). A partir deste runbook ele carrega três segredos: `RAILWAY_TOKEN`, `TF_VAR_seed_password` e `TF_VAR_brevo_api_key`. Nunca rode a CLI `railway`, `env` ou `set` nele.
- Todos os comandos partem da **raiz do repositório**.
- `<…>` é marcador: troque pelo seu valor. Nenhum exemplo traz segredo, e-mail real ou domínio real.
- **O domínio institucional e o endereço do remetente não são escritos no repositório**, em Issue, em PR nem em `docs/operations/ambientes.md`. Eles vivem no `terraform.tfvars` (fora do git) e no painel da Brevo.
- **✔ Confira:** é o resultado esperado. **Se der errado:** é a saída mais provável. **Confira:** (sem o ✔) marca o que a documentação não confirma.
- No painel da Brevo, **Settings** fica no menu da conta, no canto inferior esquerdo segundo a central de ajuda [F3]; o lugar pode variar.

---

## Passo 1 — Conta e plano

### 1.1 Entrar na conta da contratante

A conta é da contratante, não sua (C20). Pergunte se já existe uma conta Brevo da FUNDHAS.

- **Existe:** peça para ser adicionado como usuário, com permissão para domínios, remetentes, chaves de API e segurança. **Confira:** o nome das permissões e o lugar do convite podem variar; a documentação lida só cita as permissões "API keys" e "SMTP & API – Authorized IPs" [F8][F12].
- **Não existe:** combine com a contratante quem cria. O e-mail dono da conta deve ser institucional e controlado por ela: é para ele que a Brevo manda os alertas de chave, de IP e de limite [F8][F12].

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

**Por que a fila da Brevo não resolve:** o link é gerado na hora em que o sistema entrega a mensagem à Brevo (ADR-0003 §5.3). Uma redefinição de senha vale 1 hora; se ela esperar na fila da Brevo até o dia seguinte, chega vencida. O teto na aplicação (`Email__DailyLimit`) segura o e-mail **antes** de gerar o link, mas ainda não existe no Terraform.

**Decisão (D7 do ADR-0003, sua e da contratante):** gratuito na homologação e no teste de fumaça; plano pago ou créditos antes da primeira carga de alunos reais (pergunta M9 de `pendencias.md`; o gasto é da contratante, C04).

**✔ Confira:** você sabe em que plano a conta está. **Confira:** o plano e o consumo do dia aparecem na área de plano da conta (o nome do menu pode variar; não há fonte lida para o caminho).

### 1.3 Rastreamento de cliques e de abertura

**Leia antes de qualquer envio.** O ADR-0003 §8.3 pede "rastreamento desligado" como passo obrigatório deste runbook. **A documentação mostra que não há esse botão no plano gratuito:**

- A Brevo mede cliques com **links individualizados** [F17]: o link do e-mail é trocado por um endereço de rastreamento, que redireciona para o original. Usuários relatam que o endereço fica em um domínio de terceiros da Brevo [F18].
- Quem fala pela Brevo no fórum oficial informou, em 10/07/2024 e 18/10/2024, que **desligar** o rastreamento em e-mails transacionais só é feito **sob pedido e para clientes Enterprise** [F18]. Não há fonte mais nova que diga outra coisa.
- A API não tem chave por mensagem para os cliques. `removeTrackingPixel` tira só o pixel de abertura, só no plano Enterprise, e "click tracking is unchanged"; `contactPixelTrackingConsent: false` apenas torna os eventos anônimos [F15].

Consequência: **o link de acesso, que leva o token, provavelmente chega reescrito** e passa pelo redirecionador da Brevo quando a pessoa clica.

O que fazer:

1. **Ligue o rastreamento anônimo:** **Settings → Automations → Transactional emails → Tracking** → em **Anonymous email tracking**, escolha **Yes** → **Save**, no canto superior direito [F16]. Com isso, os eventos "Opened" e "Clicked" dos registros deixam de mostrar endereço, id da mensagem e **links clicados** [F16]. **Confira:** a documentação não diz se isso também impede a reescrita do link.
2. **Meça no teste de fumaça** se o link chegou reescrito ([Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta), item 3).
3. **Decida, antes de dado real**, e registre no ADR-0003 (§8.3 e §17, item 7). As saídas conhecidas:

| Saída | O que muda | Custo |
|---|---|---|
| Aceitar a reescrita | Nada. As mitigações já são do desenho: o `GET` não consome o token, o link é de uso único e vale 1 h ou 72 h (ADR-0003 §10), e a Brevo já recebe o corpo da mensagem para enviá-la | Risco residual: o token passa por mais um endereço da Brevo |
| Pedir à Brevo que desligue | Sem reescrita | Segundo [F18], só no plano Enterprise |
| Subdomínio de marca (*branded subdomain*) | O link de rastreamento passa a usar um subdomínio do domínio institucional em vez de um domínio da Brevo [F4]; continua passando pela Brevo | Mais registros no DNS da FUNDHAS |
| Mudar o desenho | Por exemplo, outro provedor | Issue e ADR próprios |

**✔ Confira:** a tela de **Tracking** mostra **Anonymous email tracking: Yes** depois de salvar.

### 1.4 Retenção dos registros na Brevo

Por padrão a Brevo guarda os registros dos e-mails transacionais **sem prazo** [F19]. Limite isso antes do primeiro envio:

1. **Settings → Transactional emails → Retention rules** [F19].
2. Remetentes: **All senders**.
3. Prazo dos registros: **1 mês**, que é o mínimo aceito (a Brevo exige um mês para detectar spam e calcular reputação) [F19]. É `PROPOSTA`: acompanha os 30 dias da fila (ADR-0003 §5.6, D9); C14 não traz regra institucional.
4. Prévias (o HTML completo de cada mensagem): **Never store previews** [F19]. O e-mail de acesso do aluno leva o primeiro nome e a matrícula; a prévia guardaria isso e o link.
5. Salve (o nome do botão pode variar).

**✔ Confira:** a regra aparece com 1 mês e sem prévias. A mudança de prazo vale também para os registros antigos; a das prévias só para os e-mails novos; a exclusão pode levar até 24 horas depois do prazo [F19].

---

## Passo 2 — Autenticar o domínio institucional

Gmail e Yahoo exigem domínio autenticado desde 01/02/2024, e a Microsoft anunciou a mesma exigência em 05/05/2025 [F5]. Sem isso os e-mails de acesso caem em spam ou são recusados.

### 2.1 O que ter em mãos

- **O domínio** (o que vem depois do `@` no remetente). C15: será o domínio institucional, mas o nome literal ainda não foi informado.
- **Quem administra o DNS dele.** C15 não identifica a pessoa; é a pendência M5. Os registros deste passo vão para ela.
- **Se o domínio já tem registro DMARC.** Pergunte ao administrador, ou veja você mesmo no [2.4](#24-conferir-a-propagação-com-dig). Um domínio só pode ter **um** registro DMARC [F3].

A Brevo autentica o domínio **ou o subdomínio** de onde você envia [F3]. Usar o domínio principal mexe no DMARC de todo o e-mail da instituição; um subdomínio só para o sistema isola esse risco, mas muda o endereço do remetente. A escolha é da contratante: registre a resposta em `pendencias.md` (M5), sem escrever o domínio.

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
| **Branded subdomain** | Opcional em IP compartilhado: pule, salvo se a decisão do [1.3](#13-rastreamento-de-cliques-e-de-abertura) for usá-lo |
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

Crie só quando for fazer o Passo 5 no mesmo dia.

1. **Settings → SMTP & API → API Keys & MCP** [F8].
2. Clique em **Generate API key**. A Brevo manda um código de verificação para o seu dispositivo: digite e clique em **Verify** [F8].
3. **Key name:** um nome que diga quem usa, por exemplo `helinho-homologacao-web`. Uma chave por integração e por ambiente [F8].
4. **Expiry:** de 7 dias a 1 ano, ou sem validade [F8]. Sugestão (`PROPOSTA`; o ADR não decide): **1 ano**, com lembrete na sua agenda 30 dias antes para fazer a [rotação](#81-rotação-de-rotina). A Brevo avisa 3 dias antes e no dia, mas para o e-mail da conta [F8].
5. **Access:** escolha **Restricted**. Todo recurso começa em **None**; mude só **Transactional email** para **All** [F9]. Esse recurso só oferece None, Read e All, e enviar é uma escrita [F9]. **Confira:** a documentação não diz qual recurso cobre `POST /v3/smtp/email`; se o envio falhar com "Access denied for resource" [F9], edite a chave (três pontos → **Edit API key**).
6. **Não** ative **Create MCP server API key**: essa opção desativa a chave comum [F8].
7. Copie o valor **direto para o gerenciador de senhas**, em entrada própria (sugestão: "Brevo — chave de API helinho homologação"). "Your API key is only visible during this step" [F8].
8. Clique em **I've saved my key**.

**✔ Confira:** a chave aparece na lista com o nome dado, e o valor está no gerenciador. Ele não passou por chat, e-mail, nota, arquivo nem pelo histórico do shell.

**Se der errado:** você fechou a janela sem copiar → apague a chave (três pontos → **Delete API key**) e crie outra; o valor não é mostrado de novo [F8].

**Duas regras da Brevo que derrubam o envio sem ninguém mexer** [F8]:

- Chave sem nenhuma chamada bem-sucedida por **90 dias** é desativada, mesmo sem validade. A Brevo avisa 7 dias antes e no dia. Em homologação, com pouco uso, isso é provável.
- Chave com validade vence na data.

Nos dois casos a API responde erro de autenticação, o worker suspende os envios sem perder a fila (ADR-0003 §6.3) e a saída é a [rotação](#81-rotação-de-rotina).

### 4.3 Bloqueio por IP

A Brevo tem uma lista de **IPs autorizados**, em **Settings → Security → Authorized IPs** [F12]. A lista é da conta inteira e vale para chaves de API e SMTP; o bloqueio é ligado em separado para cada tipo [F12]. Com o bloqueio ligado, chamada vinda de IP fora da lista é recusada mesmo com a chave certa [F12].

**Como ele se liga sozinho** [F12]: quando a chave de API começa a ser usada, a Brevo autoriza automaticamente os IPs que chamam (a faixa `/24` inteira de cada um) e o bloqueio fica inativo. **Se nenhum IP novo aparecer por 30 dias, a Brevo ativa o bloqueio.** Daí em diante, IP desconhecido é barrado, e a Brevo manda um e-mail ao dono da conta com o IP e a chave usada.

**Por que restringir por IP provavelmente não funciona na Railway** [F14]:

- IP de saída fixo é recurso do plano **Pro**; no Hobby não existe.
- Mesmo no Pro, a Railway não garante que os endereços sejam exclusivos ("There is no guarantee that the IPv4 addresses assigned to your service are dedicated"), e eles mudam se o serviço mudar de região.
- É um clique de painel por serviço (**Settings → Networking → Enable Static IPs**, seguido de redeploy), que o Terraform de `infra/railway/` não gerencia.

Sem IP fixo, o cenário é este: o serviço envia por semanas a partir dos mesmos endereços, a Brevo ativa o bloqueio, a Railway troca o IP de saída e **o envio para**.

**O que fazer:**

1. **Settings → Security → Authorized IPs** [F12].
2. Em **Blocking unauthorized IP addresses**, veja a linha das chaves de API.
3. Se estiver **Activated**: clique em **Deactivate for API** e confirme em **Deactivate blocking** [F12].
4. Repita essa conferência **uns 35 dias depois do primeiro envio** e sempre que o envio parar com erro de autenticação. **Confira:** a documentação não diz se o bloqueio volta a se ligar sozinho depois de desativado à mão.

**✔ Confira:** a linha das chaves de API não está **Activated**.

**Confira:** uma fonte de terceiros diz que o bloqueio já nasce ligado em contas criadas depois de 16/05/2024 e que o erro devolvido é 401 com "unrecognised IP address" [F13]. Não há fonte da Brevo para nenhum dos dois; por isso o item 3 vem antes do primeiro envio.

Desligar o bloqueio tira uma camada de proteção [F12]. O que compensa: chave restrita a um recurso, guardada só no gerenciador e na Railway, com validade e rotação, e os alertas por e-mail da Brevo.

**Alternativa, só no plano Pro e por Issue própria:** ligar o IP de saída fixo no `helinho-web`, autorizar esses endereços na Brevo (**Authorize IP addresses** [F12]) e manter o bloqueio. Protege menos do que parece, porque os endereços podem ser de outros clientes da Railway.

---

## Passo 5 — Variáveis na Railway pelo Terraform

Pré-requisito: a homologação já foi criada pelo [runbook da Railway](railway-runbook.md) e o commit publicado tem o adaptador (Issue #204). Tudo no **terminal do Terraform**, com `RAILWAY_TOKEN` e `TF_VAR_seed_password` exportados como no [4.3 daquele runbook](railway-runbook.md#43-exportar-os-segredos-sem-deixá-los-no-histórico).

### 5.1 Editar o terraform.tfvars

Pegue a URL pública:

```sh
terraform -chdir=infra/railway output -json url_publica | jq -r .homologacao
```

**✔ Confira:** imprime `https://<subdomínio>.up.railway.app`, sem barra no fim.

Em `infra/railway/terraform.tfvars`, descomente ou acrescente o bloco abaixo. As chaves que você não escrever continuam com o padrão:

```hcl
variaveis_web = {
  Email__Provider    = "Brevo"
  Brevo__SenderEmail = "<From email do Passo 3>"
  Brevo__SenderName  = "<From name do Passo 3>"
  App__PublicBaseUrl = "<a URL impressa acima>"
}
```

- `Brevo` com B maiúsculo: o Terraform só aceita `Fake` ou `Brevo`.
- `App__PublicBaseUrl` é a base de todo link enviado por e-mail (ADR-0003 §10.3). Tem de ser `https://`, sem caminho e sem barra no fim (ADR-0003 §7). Hoje ela **não** é derivada sozinha do domínio do serviço; o ADR-0003 §13 prevê isso para uma Issue futura.
- **Não escreva `Brevo__WebhookSecret` nem `Email__DailyLimit` aqui.** `variaveis_web` só envia as chaves declaradas em `infra/railway/variables.tf`, e essas duas ainda não existem lá. **Confira:** o Terraform pode recusar a chave desconhecida ou ignorá-la em silêncio.
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

**Se der errado:** você digitou `export TF_VAR_brevo_api_key=<valor>` → o valor ficou no histórico do shell. Apague a linha de `~/.zsh_history` e faça a [rotação](#81-rotação-de-rotina) agora.

**Opcional (nunca executado): provar a chave antes do `apply`.** O `printf` é interno do shell e o `curl` lê o cabeçalho da entrada padrão, então a chave não aparece na lista de processos:

```sh
printf 'api-key: %s\n' "$TF_VAR_brevo_api_key" | curl -sS -o /dev/null -w '%{http_code}\n' -H @- 'https://api.brevo.com/v3/smtp/statistics/events?limit=1'
```

**✔ Confira:** imprime `200`. **Confira:** a documentação não diz qual recurso da chave restrita cobre essa rota [F9]; outro código que não seja `401` não prova que a chave está errada. Com `401`, a chave foi colada errada ou o IP da sua máquina foi barrado ([4.3](#43-bloqueio-por-ip)). A chamada põe a faixa de IP da sua rede na lista de autorizados [F12].

A partir de agora, **todo** `plan` precisa das três variáveis exportadas. Sem `TF_VAR_brevo_api_key` e com `Email__Provider = "Brevo"`, o `plan` falha de propósito, em vez de apagar a chave da Railway.

### 5.3 Plan e apply

```sh
terraform -chdir=infra/railway plan -out="$HOME/.helinho-infra/brevo.tfplan"
```

**✔ Confira, na saída do `plan`:**

1. O resumo mostra **uma** mudança e **nada** a destruir, em `module.ambiente["homologacao"].railway_variable_collection.web`.
2. `railway_project.this` e `railway_service.postgres` **não aparecem**. Se aparecerem, não aplique ([8.1 do runbook da Railway](railway-runbook.md#81-publicar-uma-versão-nova)).
3. Em `Changes to Outputs`, a lista `variaveis_web` ganha **quatro** nomes: `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SenderName` e `App__PublicBaseUrl`. `Email__Provider` já estava lá.
4. Todos os valores aparecem como `(sensitive value)`.

```sh
terraform -chdir=infra/railway show "$HOME/.helinho-infra/brevo.tfplan" | grep -cF -f <(printf '%s\n' "$TF_VAR_brevo_api_key")
```

**✔ Confira:** imprime `0` (a chave não aparece no plano legível).

**Se der errado:**

- `Email__Provider aceita Fake ou Brevo, e Brevo exige TF_VAR_brevo_api_key.` → a chave não foi exportada, ou o provedor foi escrito diferente de `Brevo`.
- O plano quer mudar mais de um recurso → alguém mexeu no painel ou no `tfvars`. Não aplique; descubra a diferença.

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
BASE=$(terraform -chdir=infra/railway output -json url_publica | jq -r .homologacao)
```

```sh
curl -sS -o /dev/null -w '%{http_code}\n' "$BASE/health/ready"
```

**✔ Confira:** imprime `200`.

**Se der errado:** o deploy falha logo na subida → com `Email__Provider=Brevo`, a aplicação **recusa subir** se faltar chave, remetente ou URL pública, se o e-mail do remetente for inválido, se o nome passar de 70 caracteres ou se a URL não for `https://` sem caminho (ADR-0003 §7). O Terraform valida só a chave; o resto é a aplicação que barra. Abra os logs do deploy: a mensagem diz qual variável está errada, nunca o valor. Corrija o `terraform.tfvars` e repita o 5.3. Para voltar ao estado anterior na hora, use [Desligar o envio em emergência](#desligar-o-envio-em-emergência).

4. Registre em `docs/operations/ambientes.md`, em PR `docs` próprio: a linha "E-mail e IA" passa a `Email__Provider=Brevo`, e o "Registro de mudanças" ganha uma linha com a data. **Sem** chave, endereço do remetente ou domínio.

Ao terminar o dia:

```sh
unset RAILWAY_TOKEN TF_VAR_seed_password TF_VAR_brevo_api_key DOMINIO
```

Ligar a Brevo não envia nada por si só. O primeiro e-mail real sai no [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta).

---

## Passo 6 — Webhook transacional (opcional)

**Depende de duas Issues ainda por criar:** a do webhook (`POST /webhooks/brevo`; ADR-0003 §18, item 12) e a do Terraform que cria `Brevo__WebhookSecret` (item 6). Sem as duas na `main`, pule este passo. Sem o segredo, a rota nem existe e responde 404 (ADR-0003 §12).

Para que serve: a Brevo avisa quando uma mensagem não pôde ser entregue, e as telas passam a mostrar "e-mail não entregue". É aviso de melhor esforço, não registro oficial de entrega.

**Como a Brevo autentica a chamada.** Não há assinatura do corpo (HMAC). As formas documentadas são: usuário e senha na URL, token, cabeçalhos próprios e restrição pelas faixas de IP da Brevo [F23]. O ADR-0003 §12 escolheu o segredo no cabeçalho `Authorization: Bearer <segredo>`. **Nunca ponha o segredo na URL:** ela fica em registro de acesso e aparece em texto no painel.

1. **Gere o segredo** no gerenciador de senhas: 32 caracteres ou mais (ADR-0003 §7), só letras e dígitos. Entrada sugerida: "Brevo — segredo do webhook helinho homologação".
2. **Railway, pelo Terraform**, com a variável que a Issue criar (o ADR propõe `TF_VAR_brevo_webhook_secret`; confirme o nome no PR dela): `read -rs`, `plan`, `apply` e backup do estado, como no Passo 5. Não crie a variável à mão no painel.
3. **Brevo:** menu da conta → **Integrations → Webhooks** → **Add webhook** → **Outbound webhook** → **Add webhook** [F22].
4. Dê um nome (por exemplo `helinho-homologacao`) e **Continue**.
5. **URL:** `<url_publica>/webhooks/brevo`.
6. **Authentication:** **Token**, com o segredo [F22]. **Confira:** a documentação não diz em qual cabeçalho o token vai nem em que formato [F22][F23]. O sistema espera `Authorization: Bearer <segredo>`.
7. **Sending strategy:** **Send one at a time** [F22]. O envio em lote muda o formato do corpo, e o ADR-0003 §12 trata um evento por chamada.
8. **Continue**. Na categoria **Transactional email**, deixe ligados só os eventos de devolução definitiva, bloqueio, spam e e-mail inválido (**Hard bounce**, **Blocked**, **Spam**, **Invalid email**; os rótulos podem variar [F23]) e desligue os outros. A Issue #206 cita os três primeiros; o ADR-0003 §12 trata também `invalid_email`. Cada evento leva o endereço do destinatário e o assunto: menos eventos, menos dado pessoal trafegando.
9. Nos três pontos de um dos eventos, **Send test request** [F22]. Depois, **Activate webhook**.

**✔ Confira:**

```sh
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$BASE/webhooks/brevo" -H 'Content-Type: application/json' -d '{}'
```

Imprime `401`: a rota existe e recusa chamada sem segredo (ADR-0003 §12). Na Brevo, **Integrations → Webhooks → Outbound webhooks** → clique no webhook → em **Statistics**, o teste aparece como **Delivered** [F22].

**Se der errado:**

- O `curl` imprime `404` → o segredo não chegou ao serviço, ou o commit publicado não tem a rota.
- O teste da Brevo aparece em **Retries** ou **Failed** → o cabeçalho enviado não é o esperado. **Plano B** (ADR-0003 §17, item 5): edite o webhook, **Authentication: No authentication**, e em **Custom headers** clique em **+ Add an object** com nome `Authorization` e valor `Bearer <segredo>` [F22].

Resposta 4xx (menos 429) ou 5xx do sistema faz a Brevo **descartar o evento, sem nova tentativa** [F23]. Enquanto o segredo estiver errado, os eventos se perdem. Para trocar o segredo, mude os dois lados em seguida; os eventos do intervalo são perdidos.

---

## Passo 7 — Teste de fumaça de ponta a ponta

**Só depois que os fluxos existirem** (fila e worker, tokens e página `/conta/definir-senha`, convite de administrador e redefinição de senha; todas Issues ainda por criar). Os nomes de tela e de situação abaixo vêm do ADR-0003 §11 e podem mudar nessas Issues.

Use **um e-mail seu**, de uma caixa que você abre. É o único dado real em homologação: não o escreva no repositório, em Issue nem em PR.

1. **Crie uma unidade de teste.** No painel da homologação, como `master@helinho.example`, crie uma unidade com nome fictício (por exemplo "Unidade Teste Brevo"). As duas unidades do seed já têm administrador, e só cabe um ativo por unidade.
2. **Crie o administrador de teste** nessa unidade, com o seu e-mail. O formulário não pede senha (ADR-0003 §11.2).
   **✔ Confira:** na lista, a situação passa de "Aguardando envio" para "Convite enviado" em menos de um minuto.
3. **Receba o convite** e, antes de clicar, confira:
   - remetente e nome iguais aos do Passo 3; a mensagem não está no spam;
   - no "mostrar original" do seu provedor de e-mail: `dkim=pass` com o domínio institucional e `dmarc=pass` [F3];
   - a mensagem tem as duas partes, `text/plain` e `text/html` (ADR-0003 §17, item 2);
   - o selo "Sent with Brevo" aparece? É o esperado no plano gratuito [F1]; anote;
   - **o link:** copie o endereço sem abrir. Ele começa com o valor de `App__PublicBaseUrl`, seguido de `/conta/definir-senha?usuario=…&token=…`, ou com outro domínio? Outro domínio quer dizer que a Brevo reescreveu o link ([1.3](#13-rastreamento-de-cliques-e-de-abertura)). Anote o resultado, sem copiar o link para lugar nenhum.
4. **Defina a senha** pelo link.
   **✔ Confira:** a página abre no domínio da homologação; depois de salvar, você cai na tela de entrar, sem login automático; entra com a senha nova; chega um segundo e-mail avisando que a senha foi alterada. Abrir o primeiro link de novo leva à saída "pedir novo link": ele é de uso único.
5. **Peça a redefinição** na tela de recuperar senha, com o mesmo e-mail.
   **✔ Confira:** chega o e-mail; o link funciona dentro de 1 hora; você define outra senha e entra; chega o aviso de senha alterada. Repita com `nao-existe@example.org`: a tela responde igual e **nenhum** e-mail sai.
6. **Confira a fila.** As linhas `email.*` do teste terminam como concluídas com sucesso, com o id da mensagem da Brevo e **sem** endereço, corpo ou token gravados (ADR-0003 §5.3). **Confira:** os nomes de tabela e de coluna e a consulta vêm com a Issue da fila; o acesso ao banco é pelo túnel do [8.4 do runbook da Railway](railway-runbook.md#84-pg_dump-para-fora-da-plataforma).
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
| O link aponta para o host errado | `App__PublicBaseUrl` errada | [5.1](#51-editar-o-terraformtfvars) |
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
6. Backup cifrado do estado ([5.7 do runbook da Railway](railway-runbook.md#57-backup-cifrado-do-estado)), atualização da entrada no gerenciador de senhas e uma linha no "Registro de mudanças" de `ambientes.md`, sem o valor.

**✔ Confira:** a lista da Brevo só tem a chave nova e um e-mail de teste chegou depois da troca.

**Se der errado:** o envio falha com a chave nova → **não apague a antiga**; volte a exportá-la e refaça o `apply`.

### 8.2 Se a chave vazar

Vazou se o valor apareceu em tela compartilhada, chat, e-mail, log, histórico do shell, commit, Issue ou PR, ou se alguém sem autorização leu o `terraform.tfstate`, um arquivo `.tfplan` ou as variáveis do serviço na Railway.

**A ordem importa: revogar na Brevo primeiro, trocar na Railway depois.**

1. **Brevo, já:** três pontos ao lado da chave → **Delete API key** [F8]. A partir daí ninguém envia com ela. O sistema também para: a API responde erro de autenticação, o worker suspende e **não perde** os e-mails que ainda estão no prazo (redefinição 30 minutos; convite 24 horas; acesso do aluno 72 horas; ADR-0003 §6.2 e §6.3).
2. Crie a chave nova, exporte, `plan`, `apply` e backup do estado ([8.1](#81-rotação-de-rotina), itens 1 a 4 e 6).
3. **Veja o que foi feito com a chave antiga.** Em **Transactional → Logs**, procure envios que o sistema não fez: destinatários, assuntos ou horários estranhos [F20]. Em **Settings → Security → Authorized IPs**, procure IPs que não são seus nem da Railway [F12]. A chave restrita do [4.2](#42-criar-a-chave) também **lê** os registros: quem a teve pôde ver os endereços dos destinatários.
4. **Feche a origem do vazamento:**

| Por onde vazou | O que mais fazer |
|---|---|
| `terraform.tfstate`, `.tfstate.backup` ou arquivo `.tfplan` | O arquivo tem **todos** os segredos em texto: troque também o token da Railway, a senha do seed e a do banco ([8.6](railway-runbook.md#86-rotação-do-token-da-railway) e [8.7](railway-runbook.md#87-rotação-das-senhas) do runbook da Railway) e reveja onde o estado fica ([8.9](railway-runbook.md#89-onde-fica-o-estado-do-terraform)) |
| Commit, Issue ou PR | A rotação é a correção: o histórico não é reescrito (`git push --force` é proibido). Abra uma Issue `security:sensitive`, sem o valor |
| Histórico do shell | Apague a linha de `~/.zsh_history` |
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

| O quê | Quando | Onde |
|---|---|---|
| Consumo do dia perto de 300 | Antes de liberar muitos alunos de uma vez | [1.2](#12-plano-e-limite-diário) |
| Bloqueio de IP ligado sozinho | Cerca de 35 dias depois do primeiro envio; sempre que surgir 401 | [4.3](#43-bloqueio-por-ip) |
| Chave desativada por 90 dias sem uso, ou vencida | E-mail de aviso da Brevo; 401 no log | [8.1](#81-rotação-de-rotina) |
| Linha em `Error` do worker | É o gatilho de alerta da suspensão (ADR-0003 §6.3) | Logs do `helinho-web` |
| Domínio deixou de estar **Authenticated** | Depois de qualquer mudança no DNS da instituição | [2.5](#25-concluir-a-autenticação) |

### LGPD: o que a Brevo guarda

| Dado | Fica na Brevo? | Como limitar |
|---|---|---|
| Endereço do destinatário, assunto, remetente, etiquetas, id da mensagem, eventos e links clicados | Sim, nos registros; **sem prazo por padrão** [F19][F20] | Regra de retenção de 1 mês ([1.4](#14-retenção-dos-registros-na-brevo)); rastreamento anônimo esconde o endereço e os links nos eventos de abertura e clique ([1.3](#13-rastreamento-de-cliques-e-de-abertura)) |
| Conteúdo da mensagem (prévia em HTML: primeiro nome do aluno, matrícula e o link) | Só se as prévias estiverem ligadas [F19] | **Never store previews** ([1.4](#14-retenção-dos-registros-na-brevo)); uma prévia já guardada pode ser apagada à mão no registro [F19] |
| Etiqueta | Sim | Leva só o tipo do trabalho, nunca dado pessoal (ADR-0003 §4.1) |

- **Pedido de exclusão de um titular:** apague os registros daquele endereço. Pelo painel, um a um (**Manually delete the transactional log** [F20]). Pela API, `DELETE /v3/smtp/log/<endereço>` apaga os registros e as prévias daquele destinatário [F21]; exige chave com permissão total em e-mail transacional e ainda não tem procedimento próprio neste runbook. A exclusão pode levar até 72 horas [F20].
- **Os dados saem do Brasil:** passam pela Brevo e pela Railway. Antes de dado real é preciso a resposta da contratante sobre fornecedores (C14, C20; pergunta M12), contrato de tratamento de dados com a Brevo e o ADR de dado real (ADR-0003 §14). Até lá, só dado sintético e o seu próprio e-mail.
- **Sem link de descadastro:** são mensagens transacionais, sem conteúdo promocional (ADR-0003 §8.3). SPF, DKIM e DMARC são o Passo 2; a identificação do remetente e o contato no rodapé são a pergunta M10.

---

## Fatos não confirmados e onde cada um é tratado

| # | Fato não confirmado | Origem | Passo |
|---|---|---|---|
| 1 | O que a API responde quando o limite diário estoura; a hora em que o limite zera | ADR-0003 §17, item 1 | [1.2](#12-plano-e-limite-diário) |
| 2 | Processo e prazo de validação de uma conta nova | este runbook | [1.1](#11-entrar-na-conta-da-contratante) |
| 3 | Onde ver o plano e o consumo do dia no painel | este runbook | [1.2](#12-plano-e-limite-diário) |
| 4 | Se o rastreamento anônimo impede a reescrita do link; se o link do texto puro também é reescrito; se a Brevo ainda só desliga o rastreamento para Enterprise | ADR-0003 §17, item 7 | [1.3](#13-rastreamento-de-cliques-e-de-abertura); [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta), item 3 |
| 5 | Nomes exatos dos registros `CNAME` de DKIM; qual dos dois fluxos de domínio a conta mostra | ADR-0003 §17, item 9 | [2.2](#22-adicionar-o-domínio-na-brevo) |
| 6 | Qual recurso da chave restrita cobre `POST /v3/smtp/email` e as rotas de leitura | este runbook | [4.2](#42-criar-a-chave), [5.2](#52-exportar-a-chave) |
| 7 | Se o bloqueio de IP nasce ligado em conta nova; o erro devolvido; se ele volta sozinho depois de desativado | ADR-0003 §17, item 6 | [4.3](#43-bloqueio-por-ip) |
| 8 | O que o Terraform faz com chave não declarada em `variaveis_web` | este runbook | [5.1](#51-editar-o-terraformtfvars) |
| 9 | Mensagens de erro da aplicação na subida com configuração incompleta | Issue #204 | [5.4](#54-conferir-o-deploy) |
| 10 | Cabeçalho e formato do token do webhook; rótulos dos eventos no painel | ADR-0003 §17, item 5 | [Passo 6](#passo-6--webhook-transacional-opcional) |
| 11 | Nome da variável do segredo do webhook no Terraform | Issue a criar | [Passo 6](#passo-6--webhook-transacional-opcional) |
| 12 | Nomes de tela, de situação, de tabela e de coluna dos fluxos e da fila | Issues a criar | [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta) |
| 13 | Se `htmlContent` e `textContent` saem juntos; se o selo "Sent with Brevo" entra em e-mail enviado pela API | ADR-0003 §17, itens 2 e 8 | [Passo 7](#passo-7--teste-de-fumaça-de-ponta-a-ponta), item 3 |
| 14 | O que acontece com a fila pendente ao trocar para `Fake` | Issue da fila (a criar) | [Desligar o envio em emergência](#desligar-o-envio-em-emergência) |

Quando um desses pontos for resolvido na prática, troque o "Confira:" pelo fato, em PR `docs`.

## Diferenças entre o ADR-0003, o Terraform e o runbook da Railway

Encontradas ao escrever este runbook. Nenhuma foi corrigida aqui: cada uma pede decisão sua ou Issue própria.

| # | Diferença | Onde |
|---|---|---|
| 1 | O ADR-0003 §8.3 trata "rastreamento desligado" como passo obrigatório deste runbook. A documentação não mostra como desligar os cliques fora do plano Enterprise; o que existe é o rastreamento anônimo | [1.3](#13-rastreamento-de-cliques-e-de-abertura) |
| 2 | O ADR-0003 §13 e a decisão D7 contam com `Email__DailyLimit=300` em homologação e com `Brevo__WebhookSecret`. Nenhuma das duas variáveis existe em `infra/railway/variables.tf` (PR #100) | Tabela de variáveis; [5.1](#51-editar-o-terraformtfvars) |
| 3 | O ADR-0003 §13 prevê `App__PublicBaseUrl` derivada do domínio do serviço quando vazia. No Terraform de hoje, vazia não é enviada, e a aplicação recusaria subir com `Brevo` | [5.1](#51-editar-o-terraformtfvars) |
| 4 | O Terraform valida só a chave. Remetente ou URL pública faltando passam no `plan` e derrubam o deploy | [5.4](#54-conferir-o-deploy) |
| 5 | O ADR-0003 §13 manda "conferir se o bloqueio de IP está ligado". A documentação mostra mais: ele se liga sozinho depois de 30 dias sem IP novo, então a conferência é periódica | [4.3](#43-bloqueio-por-ip) |
| 6 | O ADR-0003 não cita a desativação da chave por 90 dias sem uso nem a validade da chave | [4.2](#42-criar-a-chave) |
| 7 | A alternativa da D7 ("sem teto na aplicação, confiando na fila da Brevo") tem um custo que o ADR-0003 §15 não cita: como o link nasce no envio, a redefinição (1 h) vence parada nessa fila | [1.2](#12-plano-e-limite-diário) |
| 8 | O ADR-0003 §17, itens 8, 9 e 10, e o limite da §15 estavam sem fonte direta. Agora têm: selo no gratuito [F1], registros DNS [F3], retenção [F19] e a fila de 1.000 [F1]. O item 12 (`help.brevo.com` responde 403) continua valendo para as páginas HTML; os artigos foram lidos pela API pública da central de ajuda | [Fontes](#fontes) |
| 9 | O runbook da Railway diz que o terminal do Terraform carrega dois segredos e que o serviço web tem 13 variáveis ([4.3](railway-runbook.md#43-exportar-os-segredos-sem-deixá-los-no-histórico), [5.3](railway-runbook.md#53-plan)). Depois do Passo 5 são três segredos e 17 variáveis | [Convenções](#convenções); [5.3](#53-plan-e-apply) |
| 10 | A Issue #206 lista três eventos de webhook; o ADR-0003 §12 trata quatro (inclui `invalid_email`) | [Passo 6](#passo-6--webhook-transacional-opcional) |

## Fontes

Todas consultadas em **19/11/2026**. As páginas HTML de `help.brevo.com` responderam 403 à leitura automática, como o ADR-0003 já registrava; os artigos da central de ajuda foram lidos pela API pública dela (`https://help.brevo.com/api/v2/help_center/en-us/articles/<id>.json`, com o mesmo `<id>` do endereço abaixo). A página HTML de preços não devolveu texto à ferramenta.

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
| F15 | Envio transacional: parâmetros, `removeTrackingPixel`, `contactPixelTrackingConsent`, códigos de erro | https://developers.brevo.com/reference/sendtransacemail e https://developers.brevo.com/changelog/2026/7/20 |
| F16 | Rastreamento anônimo em e-mails transacionais | https://help.brevo.com/hc/en-us/articles/11643306229906 |
| F17 | Cliques medidos com links individualizados (atualizado em 11/11/2026) | https://help.brevo.com/hc/en-us/articles/37113920427922 |
| F18 | Fórum oficial: desligar o rastreamento em transacionais só sob pedido e para Enterprise (10/07/2024 e 18/10/2024); relatos sobre o domínio de rastreamento | https://community.brevo.com/t/no-way-to-disable-by-option-tracking-in-transactional-e-mail/201/28 e https://community.brevo.com/t/why-no-custom-domain-for-click-tracking/710 |
| F19 | Retenção dos registros e das prévias: sem prazo por padrão, regra de 1 a 24 meses, **Never store previews** | https://help.brevo.com/hc/en-us/articles/4415743225746 |
| F20 | Registros transacionais: onde ver, filtros, prévia, exportar, apagar | https://help.brevo.com/hc/en-us/articles/360021533839 |
| F21 | Apagar registros por destinatário pela API; endereço da tela de registros | https://developers.brevo.com/docs/deleting-transactional-logs-based-on-recipient-address-to |
| F22 | Webhooks de saída no painel: menus, autenticação, cabeçalhos, estratégia de envio, teste, estatísticas | https://help.brevo.com/hc/en-us/articles/27824932835474 |
| F23 | Webhooks: eventos e corpo, formas de proteção, criação pela API, novas tentativas | https://developers.brevo.com/docs/transactional-webhooks, https://developers.brevo.com/docs/secured-webhooks, https://developers.brevo.com/reference/create-webhook e https://developers.brevo.com/docs/retry-mechanism |

Documentos do repositório usados como especificação: `docs/architecture/ADR-0003-emails-brevo.md` e `docs/context/pendencias.md` (lidos na branch do PR #185), `infra/railway/variables.tf`, `terraform.tfvars.example`, `main.tf`, `modules/ambiente/main.tf` e `README.md` (lidos na branch do PR #100), `docs/operations/railway-runbook.md` e `docs/operations/ambientes.md`. Os códigos C04, C13, C14, C15, C18 e C20 são itens do Q&A com a contratante, mantido pelo Henrique fora do repositório.
