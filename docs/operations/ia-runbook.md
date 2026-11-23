# Runbook — geração de rascunhos de questões por IA

**Para quem:** Henrique, sozinho, no navegador e na própria máquina (macOS). **Escrito em:** 22/11/2026 (Issue #253).
**Descreve:** o ADR-0004 (`docs/architecture/ADR-0004-ia-geracao-questoes.md`, aceito em 22/11/2026), o Terraform de `infra/railway/` na versão do PR #233 (ainda não mesclado) e o código do núcleo da IA: porta, opções, gerador desligado, gerador fake e prompt v1 já estão na branch padrão (Issues #224, #227, #231, #232); validador, analisador e adaptador da Anthropic ainda estão fora dela (Issues #236, #246, #249, #250, #251). Formato e terminal do Terraform: o mesmo do [runbook da Railway](railway-runbook.md) e do [runbook da Brevo](brevo-runbook.md).

> **Nada deste runbook foi executado.** Nenhum agente criou conta, chave ou limite de gasto, nem chamou a API da Anthropic, nem rodou `terraform plan` ou `apply`. Os passos do console da Anthropic **não foram conferidos na tela**: vêm do ADR-0004 (fontes lidas em 19/11/2026) e estão marcados **Confira**. O console muda; se a tela não bater com o texto, vale a tela.

Só dados sintéticos fora da produção. Nenhum exemplo traz chave, e-mail ou domínio reais.

## Resumo

**O que existe hoje.** O gerador fake e o adaptador do Claude Haiku (Anthropic) estão escritos e testados com cliente falso, atrás de `Ai__Provider`. O Terraform (PR #233) já tem as variáveis. A aplicação sobe com a IA `Off` (padrão do código), `Fake` ou `Anthropic`.

**O que falta.**
1. **A chave da Anthropic e o aval da contratante** (ADR-0004 D8, o Marcos). Chegam depois. Hoje a homologação sobe em `Fake` (sugestões fictícias) e a produção em `Off`.
2. **O resto do produto:** migração, casos de uso e telas. Ver [O que ainda não existe](#o-que-ainda-não-existe). Ligar `Anthropic` agora **não produz nenhuma questão**: não há tela nem fila que chame o gerador.

**A ordem das ações, quando a chave chegar:**

1. Conferir os [pré-requisitos](#1-antes-de-ligar-o-provedor-real) (aval da contratante, conta, privacidade).
2. [Criar a chave e o limite de gasto](#2-console-da-anthropic-chave-limite-e-modelo) no console, e anotar o identificador exato do modelo no dia.
3. [Ligar pelo Terraform](#3-ligar-pelo-terraform) (`variaveis_web` + `TF_VAR_ai_api_key`, `plan` e `apply` por você).
4. [Conferir](#4-conferir-que-funcionou) a subida, o log e, quando a UI existir, um pedido de teste.
5. Operar: [cotas e custo](#5-cotas-e-custo), [desligar](#6-desligar-trocar-e-revogar-a-chave), avisos.

## Variável → onde pegar → onde colocar

| Variável | Onde pegar | Onde colocar | Segredo? | Padrão |
|---|---|---|---|---|
| `Ai__Provider` | Não vem da Anthropic: `Off`, `Fake` ou `Anthropic` | `infra/railway/terraform.tfvars`, em `variaveis_web` | não | `Fake` em `homologacao`, `Off` em `producao` |
| `Ai__ApiKey` | Console da Anthropic, ao criar a chave ([2.1](#21-criar-a-chave)) | Gerenciador de senhas; no uso, `read -rs TF_VAR_ai_api_key` (variável `ai_api_key`). Nunca em `tfvars`, arquivo, chat, Issue ou PR | **sim** | vazio |
| `Ai__Model` | Documentação de modelos da Anthropic, no dia ([2.3](#23-identificador-do-modelo)) | `terraform.tfvars`, em `variaveis_web` | não | vazio |
| `Ai__TimeoutSeconds` | Não vem do provedor | `variaveis_web` | não | `90` (5 a 300) |
| `Ai__DailyQuotaPerAdmin` | Não vem do provedor | `variaveis_web` | não | `60` |
| `Ai__DailyGlobalLimit` | Não vem do provedor | `variaveis_web` | não | `300` |

`Ai:Fake:DelayMilliseconds` e `Ai:Fake:Failure` são chaves **só de desenvolvimento e teste**; não entram no Terraform e não se usam em ambiente publicado.

---

## 1. Antes de ligar o provedor real

Ligar o provedor real exige **todos** os itens abaixo (ADR-0004 §14). Sem eles, o sistema continua em `Fake` ou `Off`.

- [ ] **Aval da contratante (D8).** O ADR-0004 §16 tem as sete perguntas para levar ao Marcos. Respostas que mudam o que você faz aqui: IA-4 (qual provedor, de quem é a conta, quem paga, teto mensal), IA-6 (os enunciados do banco podem ir ao provedor como lista do que não repetir) e IA-7 (o contrato cobre IA no produto). Detalhe em `docs/context/pendencias-ia.md`.
- [ ] **Conta em nome da contratante**, com o e-mail institucional como dono (é para ele que o provedor manda os avisos de cobrança e de limite). Combine quem lê essa caixa.
- [ ] **Regras de conteúdo do prompt validadas** pela equipe pedagógica (IA-5), e a amostra de 100 questões prevista no ADR-0004 §6.5 combinada. Pode ser depois da primeira chamada de teste, mas **antes de abrir a produção a usuários**.
- [ ] **Limite de gasto configurado no console antes da primeira chamada** ([2.2](#22-limite-de-gasto)).
- [ ] Homologação no ar pelo runbook da Railway.

**Privacidade, para você saber o que sai do sistema.** Vão ao provedor: tipo de quiz, faixa etária, quantidade, o assunto digitado e até 80 enunciados de questões do mesmo tipo e faixa. **Nenhum dado de aluno, de administrador ou de unidade vai ao provedor**: o desenho do ADR-0004 §11 prevê isso pelo tipo da requisição, pelo caso de uso e por testes. Na branch padrão já existem o teste da forma da requisição (`QuestionDraftRequestTests`) e o do envelope montado sobre o seed (`AiPromptPrivacyTests`); o caso de uso ainda não existe. O campo "assunto" é texto livre: quem digita é orientado a não escrever nome de pessoa, mas o sistema só recusa e-mail e sequências de 6 ou mais dígitos. Se alguém digitou um nome, ele saiu do sistema; trate como incidente de privacidade e avise a contratante. O provedor processa fora do Brasil (global por padrão) e guarda entradas e saídas por até 30 dias; o conteúdo sinalizado pelos sistemas de segurança dele pode ficar até 2 anos (as pontuações de classificação, até 7 anos); não usa dados da API para treinar por padrão (ADR-0004 §6.1). **Confira** esses prazos com a Anthropic antes de afirmá-los à contratante (ADR-0004 §18, item 4).

## 2. Console da Anthropic: chave, limite e modelo

O console é `https://platform.claude.com`. **Confira** os nomes de menu e botão na tela; o ADR só registra os endereços e as regras abaixo.

### 2.1 Criar a chave

1. Entre no console com a conta da contratante e abra a área de **API keys** (**Confira:** o ADR-0004 não registra o caminho exato do menu; costuma ser Settings → API keys).
2. Crie uma chave com nome que diga o uso, por exemplo `helinho-homologacao` (uma por ambiente: revogar uma não derruba a outra). Se o console oferecer **workspace**, use um workspace só do projeto, para o limite de gasto valer só para ele.
3. **Copie o valor na hora, direto para o gerenciador de senhas.** **Confira:** o console costuma mostrar o valor uma única vez; se for o caso, perdeu, crie outra.
4. Não cole a chave em arquivo, `tfvars`, chat, Issue ou PR. O formato da chave não é repetido aqui de propósito.

A aplicação exige que a chave tenha só caracteres ASCII visíveis, sem espaço nem quebra de linha (ela vai num cabeçalho HTTP). Chave colada com espaço no fim faz a aplicação **não subir**.

### 2.2 Limite de gasto

Faça **antes da primeira chamada** (ADR-0004 §14).

1. Abra `https://platform.claude.com/settings/spend-limits` (endereço do ADR-0004 §6.1, F8). **Confira:** o caminho pode ter mudado.
2. Defina o limite mensal da organização. Sugestão do ADR: **US$ 10 por mês**. Defina também o do workspace, se usar um.
3. Quando o limite é atingido, o provedor recusa as chamadas: o adaptador devolve o motivo `Misconfigured` (veja [a tabela do passo 4](#43-o-que-cada-motivo-de-falha-significa)). **Esse limite é o único teto firme de custo**: a cota do sistema pode ser ultrapassada em um pedido por concorrência (ADR-0004 §13).
4. Todo ambiente começa numa camada de uso do provedor com teto próprio (a Start tem US$ 500 por mês, segundo o ADR; **confira** a camada da conta). O seu limite deve ficar bem abaixo.

### 2.3 Identificador do modelo

O modelo vem **sempre** de `Ai__Model`; o código não tem nome de modelo.

- **Exemplo de 22/11/2026:** `claude-haiku-5-5` (Claude Haiku 5.5, lançado em 07/10/2026, aposentadoria "não antes de 07/10/2027", ADR-0004 §6.1). Esse é um valor **datado**, não uma instrução.
- **No dia de ligar:** abra a página de modelos da Anthropic (`https://platform.claude.com/docs/en/about-claude/models/overview`), confira o identificador exato do Haiku atual e use-o letra por letra.
- **Nunca use apelido `latest`** nem identificador que termine em `latest`: o Terraform recusa no `plan`, e o modelo trocaria sozinho sob a aplicação. Modelos são aposentados; a troca é mudança de configuração, anotada.
- O modelo que respondeu fica gravado em cada pedido (quando a tela e a migração existirem), então dá para saber depois qual rodou.

## 3. Ligar pelo Terraform

> **Confira contra o código mesclado.** As variáveis, as validações e a saída `provedor_ia` desta seção vêm do PR #233 (branch `issue-229-terraform-email-ia`), ainda **fora da branch padrão**. Antes do `plan`, abra `infra/railway/variables.tf` e `infra/railway/README.md` na branch padrão; se divergirem deste texto, vale o código mesclado.

Terminal do Terraform, na raiz do repositório, como no [runbook da Railway, 4.3](railway-runbook.md#43-exportar-os-segredos-sem-deixá-los-no-histórico). **Você** roda `plan` e `apply`; nenhum agente roda.

1. Em `infra/railway/terraform.tfvars` (fora do git), no bloco `variaveis_web`:

   ```hcl
   variaveis_web = {
     Ai__Provider = "Anthropic"
     Ai__Model    = "claude-haiku-5-5" # exemplo de 22/11/2026; confira o identificador no dia
   }
   ```

   Se o bloco já tem outras chaves (e-mail, por exemplo), acrescente estas duas sem apagar as outras.

2. Leia a chave sem deixá-la no histórico:

   ```sh
   read -rs TF_VAR_ai_api_key && export TF_VAR_ai_api_key
   ```

   Cole a chave e tecle Enter (nada aparece na tela).

3. `terraform plan` e leia o resultado. O `plan` recusa: `Ai__Provider` fora de `Off`/`Fake`/`Anthropic`; `Anthropic` sem chave ou sem `Ai__Model`; modelo terminado em `latest`; prazo e cotas fora da faixa. A chave não aparece na saída.
4. `terraform apply`. **O `apply` reimplanta o serviço `helinho-web`.** Depois, backup cifrado do estado ([5.7 do runbook da Railway](railway-runbook.md#57-backup-cifrado-do-estado)).
5. `terraform output ambientes`.

**Atenção: o valor explícito de `Ai__Provider` em `variaveis_web` vale para todos os ambientes do mesmo estado.** Escrever `Anthropic` liga a IA real na homologação **e na produção**. Hoje só existe `homologacao`; quando a produção entrar, decida isso antes (ADR-0004 D9: produção em `Off` até a chave e o aval).

A chave pode ficar exportada no terminal enquanto ele estiver aberto; com `Off` ou `Fake` ela é enviada à Railway e ignorada pela aplicação.

## 4. Conferir que funcionou

### 4.1 Hoje (só o núcleo existe)

1. `terraform output ambientes`: `provedor_ia = "Anthropic"`, e `Ai__ApiKey` e `Ai__Model` na lista de variáveis do web. **A chave não aparece na saída**, só o nome da variável.
2. Painel da Railway → `helinho-web` → aba **Deployments**: o deploy novo fica ativo. Se a aplicação **não sobe**, abra os **Deploy Logs**: a mensagem de configuração começa pelo nome da variável (`Ai:ApiKey`, `Ai:Model`, `Ai:Provider`, `Ai:TimeoutSeconds`, `Ai:DailyQuotaPerAdmin`, `Ai:DailyGlobalLimit`) e **nunca traz o valor**. Corrija a variável e rode `apply` de novo.
3. `/health/ready` responde 200.
4. Não deve existir a linha de aviso `Ai:Provider=Anthropic ignorado em Development: a geração usa o Fake.` Ela só aparece em `Development`, nunca num ambiente publicado; se aparecer, o ambiente está em `Development` e **não está chamando o provedor**.

Com isso você sabe que a configuração foi aceita. **Ainda não dá para fazer uma geração de verdade** (não há tela nem caso de uso). A primeira chamada real acontece quando a UI entrar.

### 4.2 Quando a UI entrar (a completar)

Esta parte será escrita quando as telas de `/questoes/ia` existirem (ADR-0004 §15). O roteiro previsto: com um administrador de teste, pedir **5** questões de um tipo e faixa, sem assunto; ver o estado "gerando" virar cartões de revisão; conferir que nenhuma sugestão aparece no jogo sem aprovação; conferir no log a linha de conclusão ([4.4](#44-o-que-olhar-no-log)); conferir no console da Anthropic que o gasto subiu poucos centavos.

### 4.3 O que cada motivo de falha significa

O adaptador classifica toda falha num motivo fechado (`QuestionDraftFailure`, já na branch padrão). A classificação por código HTTP vem do adaptador, ainda fora da branch padrão (branch `issue-250-ia-registro-anthropic`): **confira contra o código mesclado**. Na fila, o motivo vira `ai_timeout`, `ai_unavailable` etc. (ADR-0004 §10.2). O que cada um pede de quem opera:

| Motivo | Quando acontece | O que fazer |
|---|---|---|
| `Timeout` | Passou `Ai__TimeoutSeconds` (90 s) contando tentativas e esperas | Pedir de novo. Se for constante com 20 questões, pedir menos por vez; só depois pensar em subir o prazo (máximo 300) |
| `RateLimited` | O provedor respondeu 429 de limite de taxa e as 2 repetições não resolveram | Esperar alguns minutos e pedir de novo. Se repetir, ver a camada de uso da conta no console |
| `ProviderUnavailable` | 5xx ou 529 do provedor, erro de conexão, ou 429 de **teto de gasto da camada** (sem `retry-after`) | Ver a página de status do provedor (**Confira** o endereço). Se o teto de gasto da camada estourou, ver o console |
| `Refused` | O modelo recusou (`stop_reason = refusal`). A chamada é cobrada. Repetir a mesma requisição costuma recusar de novo | Tentar outro assunto. Recusa frequente sem assunto estranho: avisar o desenvolvedor |
| `InvalidOutput` | Saída cortada (`max_tokens`), fora do formato ou fora das regras | Pedir de novo; se for frequente, avisar o desenvolvedor (ajuste de prompt ou de tamanho) |
| `Misconfigured` | 401, 403, 404 e **qualquer outro 4xx (400, 402, 413, 422)**: chave inválida, revogada ou sem permissão, modelo inexistente ou aposentado, **conta sem crédito (402)** e **limite de gasto do console atingido (400)** | Ver [diagnóstico](#diagnóstico-de-misconfigured). Pedir de novo **não adianta** |
| `Disabled` | `Ai__Provider` é `Off` ou vazio | É o esperado com a IA desligada; as telas devem dar 404 (quando existirem) |

A tela mostra mensagens amigáveis por motivo (ADR-0004 §15). O motivo exato só está no log e na linha da fila.

#### Diagnóstico de `Misconfigured`

O motivo é o mesmo para causas diferentes e o log só traz o `request-id`. Percorra na ordem:

1. **Console da Anthropic → uso e faturamento:** limite de gasto atingido ou crédito esgotado? Se sim, suba o limite (com aval de quem paga) ou espere o mês virar.
2. **A chave** ainda existe e está ativa no console? Se foi revogada, [troque](#62-trocar-ou-revogar-a-chave).
3. **O modelo** em `Ai__Model` ainda existe? Compare com a página de modelos ([2.3](#23-identificador-do-modelo)). Modelo aposentado dá 404.
4. Nada disso: guarde o `request-id` do log e abra um chamado no suporte da Anthropic.

### 4.4 O que olhar no log

Logs do deploy do `helinho-web` na Railway (aba **Deployments** → **Deploy Logs**; ou **Observability**, como no [runbook da Railway](railway-runbook.md)). O adaptador escreve **uma linha por geração** e uma por repetição. Os textos abaixo são os da branch `issue-250-ia-registro-anthropic`, ainda fora da branch padrão; **confira contra o código mesclado**:

| Linha (texto da aplicação) | Nível | Quer dizer |
|---|---|---|
| `Geração por IA concluída: tentativas=…, tokens de entrada=…, de saída=…, duração=… ms, request-id=…` | Information | Chamada bem-sucedida e o que ela custou em tokens |
| `Geração por IA será repetida: motivo=…, tentativa=…` | Information | 429 ou 5xx; o adaptador tenta de novo (no máximo 2 repetições) |
| `Geração por IA falhou: motivo=…, tentativas=…, request-id=…` | Warning | Falhou; `motivo` é um da tabela acima |

**O log nunca traz** chave, prompt, assunto, enunciados, texto gerado nem a mensagem de erro do provedor (que poderia trazer cabeçalho ou corpo). Se você achar qualquer um desses no log, é defeito de segurança: **revogue a chave** ([6.2](#62-trocar-ou-revogar-a-chave)) e abra uma Issue sem colar o trecho. O `request-id` é o que se entrega ao suporte da Anthropic.

## 5. Cotas e custo

Quatro controles do nosso lado (ADR-0004 §13): três limites configuráveis e o máximo por pedido, fixo no código. Acima deles fica o limite de gasto do console:

| Limite | Variável | Padrão | O que faz |
|---|---|---|---|
| Por administrador | `Ai__DailyQuotaPerAdmin` | 60 | Sugestões pedidas por pessoa em 24 h |
| Global | `Ai__DailyGlobalLimit` | 300 | Sugestões pedidas, somando todos, em 24 h |
| Prazo por chamada | `Ai__TimeoutSeconds` | 90 | Teto de cada geração, tentativas incluídas (5 a 300). O botão "Sugerir" do criador usa o menor entre esse valor e 20 s |
| Máximo por pedido | fixo no código | 20 | A tela oferece 5, 10 e 20 |

Regras (ADR-0004 §13): a unidade é a **sugestão pedida**, não a requisição (um pedido de 20 gasta 20); a janela é **deslizante de 24 horas**, apesar do nome "Daily"; **todo pedido conta, inclusive o que falhou**. Pedido acima da cota é recusado antes de criar o job, com a mensagem do saldo.

**Como mudar:** em `terraform.tfvars`, no `variaveis_web` (por exemplo `Ai__DailyQuotaPerAdmin = "40"`), `plan` e `apply`. O `apply` reimplanta o web. Valores fora da faixa fazem o `plan` falhar. Os valores padrão são **PROPOSTA** (IA-1) até a contratante responder.

**Estimativa do ADR (premissas não medidas, ADR-0004 §6.5 e §13):**

- Haiku 5.5: US$ 0,10 por milhão de tokens de entrada e US$ 0,50 por milhão de saída.
- ≈ **US$ 0,51 por mil questões aprovadas** (faixa de US$ 0,30 a US$ 0,80), contando 30% de descarte.
- Pior caso com as cotas padrão (300 por dia, todo dia): ≈ 9.000 sugestões por mês ≈ **US$ 3,20 por mês**. O limite sugerido de US$ 10 cobre isso com folga.
- Sem impostos, IOF nem câmbio. Esforço e raciocínio do modelo ainda não foram medidos: só a primeira semana com a chave mostra o custo real. Compare o gasto no console com essa estimativa e avise se passar do dobro.

Custo do provedor não entra em `docs/operations/custos.md` (que é da Railway); anote lá quando a conta existir.

## 6. Desligar, trocar e revogar a chave

### 6.1 Desligar (emergência ou fim de uso)

1. `terraform.tfvars`: `Ai__Provider = "Off"` em `variaveis_web`. **`Off` não exige chave**, então não precisa exportar `TF_VAR_ai_api_key`.
2. `terraform plan` e `terraform apply` (reimplanta o web).
3. Conferir: `terraform output ambientes` com `provedor_ia = "Off"`.

Com `Off`, os botões e avisos de IA somem, as rotas de IA respondem 404 (quando as telas existirem), nenhum trabalho de IA roda e a porta recusa com `Disabled` sem tocar a rede. Sugestões já guardadas continuam no banco. Para voltar à demonstração sem provedor real: `Ai__Provider = "Fake"` (aparece o aviso "Modo de demonstração: sugestões fictícias").

**Desligar pelo Terraform leva alguns minutos (reimplantação).** Se houver gasto descontrolado agora, **revogue a chave no console primeiro** (a chamada passa a falhar com `Misconfigured`) e depois desligue pelo Terraform.

### 6.2 Trocar ou revogar a chave

- **Troca de rotina** (ou chave vazou): crie a chave nova no console ([2.1](#21-criar-a-chave)); `read -rs TF_VAR_ai_api_key && export TF_VAR_ai_api_key` com o valor novo; `plan` e `apply`; confira a geração; **só então apague a chave antiga no console.** Se **vazou**, inverta: apague a antiga primeiro (a IA falha com `Misconfigured` até o `apply`), depois troque.
- **Rotacione também qualquer chave que tenha aparecido em log, chat, Issue, PR ou captura de tela**, mesmo que por um instante: trate como vazada.
- **Se vazou:** além de apagar, olhe o gasto do período no console. O que for gasto por terceiros é incidente a registrar e a levar à contratante.
- O valor antigo continua no estado do Terraform (`terraform.tfstate`) e nos backups dele. Por isso o estado fica cifrado e fora do git ([runbook da Railway](railway-runbook.md)).
- Sem uso, o provedor pode desativar chaves antigas. **Confira** a política atual no console.

## 7. Avisos

- **Não defina `ANTHROPIC_LOG=debug` em produção** (nem em homologação com texto real). **Confira** na documentação do SDK C# (`https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp`) o que esse nível registra; o risco é gravar requisições com cabeçalhos e texto no log. O adaptador por si só nunca grava nada disso (item 4.4).
- **`ANTHROPIC_BASE_URL` não é usado** pelo adaptador. O endereço da API é fixo no código, em HTTPS. Também não valem `ANTHROPIC_API_KEY` nem `ANTHROPIC_AUTH_TOKEN`: endereço e chave são passados na construção do cliente (handoff 249). Há teste para `ANTHROPIC_BASE_URL` e `ANTHROPIC_AUTH_TOKEN` (`AnthropicQuestionDraftGeneratorTests`); para `ANTHROPIC_API_KEY`, **ainda não há teste**. Não existe `Ai__BaseUrl`. A chave só vale vinda de `Ai__ApiKey`.
- **Em `Development` o provedor efetivo nunca é o real:** `Anthropic` vira `Fake` com aviso no log. Teste local não chama a API mesmo com chave no ambiente.
- **O cliente HTTP do adaptador não segue redirecionamento**, para que uma resposta 307 não reenvie a chave a outro endereço (`AllowAutoRedirect = false` em `Composition/Areas/Ia.cs`, coberto por `AiProviderTests`, na branch `issue-250-ia-registro-anthropic`).
- **Não cadastre chave real em máquina de desenvolvimento nem em CI.** Nenhum teste chama a API; se um teste pedir chave real, é defeito.

## O que ainda não existe

O runbook descreve o que **vale hoje**. Para não prometer o que o código não faz, esta é a lista do que **falta** (ADR-0004 §19; os números são as Issues do plano, quando existirem):

| Peça | Estado em 22/11/2026 | Efeito no runbook |
|---|---|---|
| Porta, opções `Ai__*`, gerador desligado, gerador `Fake` e prompt v1 (#224, #227, #231, #232) | Na branch padrão | Valem no próximo deploy |
| Validador e analisador da saída (#236, #246) | Fora da branch padrão | Sem eles o adaptador não entra |
| Adaptador da Anthropic, repetição, registro na porta (#249, #251, #250) | Escritos, fora da branch padrão; **nunca falaram com a API real** | A primeira chamada real é o teste de fumaça deste runbook, quando a UI existir |
| Variáveis `Ai__*` no Terraform (PR #233) | Aprovado, **não mesclado** | O passo 3 só funciona depois do merge |
| **Migração** (`AiGenerationJobs`, `AiQuestionSuggestions`, proveniência em `Question`) | Não existe | Sem tabelas, não há registro de uso, cota nem sugestões |
| **Casos de uso** (`RequestAiDrafts`, tratador na fila, aprovar, descartar, editar, "Sugerir") | Não existem | Cotas ainda **não são cobradas**; os limites da seção 5 são só configuração |
| **Fila** (`QueueJobs`) e worker (ADR-0003, #223 e #228) | Não existem | Sem a fila, nada chama o gerador |
| **Telas `/questoes/ia`**, selo "IA", filtro de origem, botão "Gerar com IA", filtro 404 com `Off` | Em construção | A conferência de ponta a ponta ([4.2](#42-quando-a-ui-entrar-a-completar)) e a mensagem de cada motivo na tela serão escritas quando a UI entrar |
| Purga das sugestões descartadas (30 dias) e retenção do "assunto" (IA-2) | Não existe | Sem prazo de apagamento hoje |

**Quando a UI entrar, este runbook deve ser atualizado** em: 4.2 (roteiro de verificação com cliques), a tabela de motivos (texto exato das mensagens e onde ver a linha da fila) e a seção 5 (onde ver a cota restante e o uso por pedido).

## Diferenças entre o ADR-0004, o Terraform e o código (22/11/2026)

O runbook segue o **código** onde ele difere do ADR, e diz isso aqui.

1. **Limite de gasto próprio do console (HTTP 400) e falta de crédito (402).** O ADR-0004 §6.2 manda o limite de gasto virar `ProviderUnavailable`. No código, só o 429 de teto de gasto da camada vira `ProviderUnavailable`; o 400 do limite próprio e o 402 caem em `Misconfigured` (qualquer 4xx fora de 401/403/404/429). O diagnóstico da seção 4.3 cobre isso. O ajuste está na Issue #256; quando ela entrar, a tabela da seção 4.3 muda.
2. **Faixas de cota.** O código só exige cotas maiores que zero. O Terraform é mais estrito (`Ai__DailyQuotaPerAdmin` de 1 a 10000 e `Ai__DailyGlobalLimit` de 1 a 100000). O Terraform manda no ambiente; as duas coisas não conflitam.
3. **Atraso do fake.** O ADR sugere 1500 ms na demonstração; o código padrão é 0 (o ADR e o handoff da #232 dizem que quem liga é o ambiente ou `scripts/demo.sh`). `Ai:Fake:DelayMilliseconds` não está no Terraform.
4. **`Timeout` no fake.** Não lança `Timeout`: espera até o prazo de quem chamou. Só importa para quem testa localmente.
5. **SDK.** O ADR §6.4 diz "SDK oficial" (D4) e o código usa o pacote `Anthropic` 13.10.0 (`Directory.Packages.props` e handoff 249); a tentativa do SDK é desligada (`MaxRetries = 0`, em `AnthropicQuestionDraftGenerator.cs`; handoffs 249 e 251) e a repetição é nossa, só para 429 e 5xx. Tudo isso está na branch `issue-250-ia-registro-anthropic`. Não há diferença para quem opera.
6. **Terraform e o ADR §14.** Os dois ajustes que o ADR pedia ao Terraform (chave só com `Anthropic`; `Fake`/`Off` sem chave) estão no PR #233, junto com a exigência de `Ai__Model` e a recusa de `latest`.

## Fontes

- ADR-0004, seções 6.1 (fatos da API na data), 11, 13, 14, 16 e 18; `docs/context/pendencias-ia.md`.
- `infra/railway/README.md` do PR #233 (branch `issue-229-terraform-email-ia`): variáveis, padrões, validações, `terraform output ambientes`.
- Handoffs 227, 232, 249, 250 e 251 (branch `issue-250-ia-registro-anthropic`): nomes das opções, motivos de falha, repetição, log, `Development`.
- Branch padrão, conferida em 22/11/2026: `src/Helinho.Application/Ai/AiOptions.cs` (nomes, padrões e mensagens das opções) e `src/Helinho.Web/Composition/Areas/Ia.cs` (aviso em `Development`).
- Documentação da Anthropic citada pelo ADR-0004, lida em 19/11/2026 e **não reconferida** para este runbook: `https://platform.claude.com/docs/en/about-claude/models/overview`, `.../api/errors`, `.../api/rate-limits`, `.../cli-sdks-libraries/sdks/csharp`.
