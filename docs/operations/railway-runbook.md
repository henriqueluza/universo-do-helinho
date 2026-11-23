# Runbook — homologação na Railway com Terraform

**Para quem:** Henrique, sozinho, na própria máquina (macOS). **Escrito em:** 18/11/2026. **Revisto em:** 19/11/2026, depois da leitura "no papel" (Issue #117), e em 22/11/2026 para a decisão D3 (Issue #226).
**Descreve:** o Terraform de `infra/railway/` (PRs #110, #113 e #116, que chegam à `main` pelo PR #100), o ADR-0002 (`docs/architecture/ADR-0002-hospedagem-railway.md`, PR #101) e o `Dockerfile` da raiz.

> **Nada deste runbook foi executado.** Nenhum agente rodou `plan`, `apply`, criou conta, token ou recurso. Os passos de painel foram conferidos na documentação da Railway em 18 e 19/11/2026 (seção [Fontes](#fontes); as marcas `[F1]`, `[F2]`… apontam para ela). Onde a documentação não dá o nome do botão, o texto diz "o nome pode variar". Onde um comportamento não pôde ser confirmado, o passo traz **Confira:** com o que fazer em cada caso; o índice desses pontos está em [Fatos não confirmados](#fatos-não-confirmados-e-onde-cada-um-é-tratado).

Só dados sintéticos em homologação. Produção não é ligada por este runbook.

## Decisões do Henrique em 22/11/2026 que este runbook segue

- **D1 do ADR-0002: só homologação por enquanto.** Depois de subir tudo, o ambiente fica parado para o desenvolvedor Unity testar com estabilidade. Produção é etapa posterior ([`ambientes.md`](ambientes.md#o-que-falta-para-ligar-a-produção)).
- **D3: auto-deploy ligado em homologação** (`auto_deploy = true` no `terraform.tfvars`). Cada merge na `main` publica sozinho. O padrão da variável no Terraform continua `false`; quem liga é o `terraform.tfvars`.
- **Risco aceito para homologação:** com auto-deploy, a opção **Wait for CI** da Railway **não espera build nem testes**, porque o workflow `validate` só roda em `pull_request`; no push para a `main` só roda o `security`. O Henrique aceitou isso em homologação (dado sintético) em 22/11/2026 ([registro na Issue #225](https://github.com/henriqueluza/universo-do-helinho/issues/225#issuecomment-7089221462); ADR-0002, D3). A Issue #225 (rodar o `validate` também no commit que chega à `main`) continua aberta e é **pré-requisito de produção**. O que segura um commit ruim hoje é a proteção da `main` (PR, `validate`, `semgrep`, `gitleaks` e `pr-size` antes do merge). Detalhes: [Wait for CI](#58-ligar-wait-for-ci).
- **Senha do seed:** valor genérico, só para testes, definido por ele em `TF_VAR_seed_password` na máquina dele. Nunca em arquivo versionado. A Issue #115 (senha separada para alunos) fica de lado.
- **Domínio próprio do painel:** entra depois, pela variável `dominio_proprio` (PR #221, na branch `issue-99-terraform-esqueleto`; passo a passo em `docs/operations/railway-dominio.md` quando o #100 chegar à `main`). Os exemplos usam `paineladm.example.org`. O domínio real nunca entra no repositório.

## O caminho feliz em 10 linhas

1. **Igor:** entrar no workspace da contratante na Railway como Admin e saber o plano (Hobby ou Pro) e o teto de gasto. → [Passo 1](#passo-1--o-que-pedir-ao-igor)
2. **Repositório e ferramentas:** PRs #100 (Terraform), #101 (ADR) e #104 (`/health/ready`) mergeados na `main`; o do Data Protection (Issue #103) só se a decisão D4 for a opção A; Terraform 1.16 ou 1.17 e `tflint` 0.66+. → [Passo 2](#passo-2--ferramentas-e-token-da-railway)
3. **Token:** criar um token de **workspace** em `railway.com/account/tokens` e guardar no gerenciador de senhas. → [2.5](#25-criar-o-token-de-workspace)
4. **GitHub App:** instalar a app da Railway na conta do GitHub com acesso a `universo-do-helinho`. → [Passo 3](#passo-3--github-app-da-railway-no-repositório)
5. **Entradas:** copiar `terraform.tfvars.example` para `terraform.tfvars` e descomentar `auto_deploy = true`; `read -rs` de `RAILWAY_TOKEN` e `TF_VAR_seed_password` em um terminal só do Terraform. → [Passo 4](#passo-4--terraformtfvars-e-variáveis-de-ambiente)
6. **Terraform:** `init` → `plan -out` (esperado: `Plan: 7 to add`, nenhum domínio nem TCP proxy no banco, nenhum segredo na tela) → conferir a proteção da `main` → `apply`. → [Passo 5](#passo-5--init-plan-e-apply)
7. **Estado:** backup cifrado de `infra/railway/terraform.tfstate` logo depois do `apply`. → [5.7](#57-backup-cifrado-do-estado)
8. **Wait for CI e primeiro deploy:** logo depois do `apply`, ligar **Wait for CI** no painel ([5.8](#58-ligar-wait-for-ci)). Com `auto_deploy = true` a Railway já publica sozinha; confira que o pre-deploy `migrate` rodou e o deploy ficou ativo (se não houver deploy, `Cmd+K` → **Deploy Latest Commit**). → [Passo 6](#passo-6--primeiro-deploy-e-conferências)
9. **Conferir:** `/health/ready` responde 200, login no painel com `master@helinho.example`, `POST /api/login` com a matrícula `2026001`. → [6.4](#64-healthready)
10. **Registrar:** preencher `docs/operations/ambientes.md` e passar a URL da API (já com `/api`) e a credencial sintética ao desenvolvedor Unity. → [Passo 7](#passo-7--url-para-o-desenvolvedor-unity-e-registro-do-ambiente)

Operação do dia a dia (publicar, rollback, backup, restore, rotação, destruição): [Passo 8](#passo-8--operação). Estabilidade para o teste do jogo: [Deixar a homologação parada](#deixar-a-homologação-parada-para-o-teste-do-jogo). O que fica para depois: [Passo 9](#passo-9--o-que-fica-para-depois).

## Variável → onde pegar → onde colocar

| Variável | Onde pegar | Onde colocar | Segredo? |
|---|---|---|---|
| `RAILWAY_TOKEN` | Você cria: `railway.com/account/tokens`, com o workspace da contratante selecionado ([2.5](#25-criar-o-token-de-workspace)) | Gerenciador de senhas. No uso: `read -rs` no terminal do Terraform. Nunca em arquivo, `tfvars`, CI ou chat | sim |
| `TF_VAR_seed_password` | Você gera no gerenciador de senhas: mínimo de 16 caracteres (exigido pelo Terraform), recomendado 20 ou mais, com minúscula, maiúscula, dígito e símbolo | Gerenciador de senhas. No uso: `read -rs` no terminal do Terraform. O Terraform a grava na Railway como `HELINHO_SEED_PASSWORD` | sim |
| `workspace_id` | Só se o token enxergar mais de um workspace, o que acontece com token de conta. Vem de `railway whoami --json`, no terminal da CLI ([5.5](#55-se-o-apply-falhar-no-meio)) | `infra/railway/terraform.tfvars` | não |
| `auto_deploy` | Decisão D3 do Henrique (22/11/2026): `true` em homologação | `infra/railway/terraform.tfvars`: descomente `auto_deploy = true` ([4.1](#41-criar-o-terraformtfvars)) | não |
| `ambientes`, `repositorio`, `branch`, `regiao`, `serverless`, `postgres_imagem`, `variaveis_web` | Os padrões de `terraform.tfvars.example` já servem. Exceção provável: o subdomínio padrão `helinho-homologacao` é único em toda a Railway e pode estar ocupado; aí defina `ambientes.homologacao.subdominio` | `infra/railway/terraform.tfvars`, só se for mudar | não |
| `TF_VAR_brevo_api_key` | Painel da Brevo, na Sprint 3 | Não defina agora | sim |
| `TF_VAR_ai_api_key` | Painel do provedor de IA, na Sprint 4 | Não defina agora | sim |
| Senha do banco | O Terraform gera (`random_password`); você nunca digita | Fica em texto no estado do Terraform e na Railway (variável `PGPASSWORD` do serviço `Postgres`) | sim |
| `url_publica`, `url_api` | `terraform output`, depois do `apply` | `docs/operations/ambientes.md`; a `url_api` vai para o desenvolvedor Unity | não |
| Service Instance ID do `Postgres` | Painel: serviço `Postgres` → `Cmd+K` → **Copy Service Instance ID** | Só no terminal da CLI, para o túnel do `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)) | não |

## Convenções

- **Dois terminais, nunca misturados.**
  - **Terminal do Terraform:** onde `RAILWAY_TOKEN` e `TF_VAR_seed_password` estão exportados. Só roda `terraform`, `curl`, `jq` e `age`. **Nunca rode a CLI `railway` nele:** a CLI lê `RAILWAY_TOKEN` como token de **projeto**, e o seu é de workspace [F3].
  - **Terminal da CLI:** aberto à parte, sem `RAILWAY_TOKEN`, autenticado com `railway login`. É usado no [Passo 8](#passo-8--operação) e, se você precisar do id do workspace, no [5.5](#55-se-o-apply-falhar-no-meio).
- Todos os comandos partem da **raiz do repositório**.
- `<…>` é marcador: troque pelo seu valor. Nenhum exemplo traz segredo.
- **✔ Confira:** é o resultado esperado. **Se der errado:** é a saída mais provável. **Confira:** (sem o ✔) marca comportamento que ninguém verificou ainda.
- No painel da Railway, `Cmd+K` abre a paleta de comandos (`Ctrl+K` fora do macOS).

---

## Passo 1 — O que pedir ao Igor

O Igor é o contato da contratante para a conta da Railway (C19). Mande as perguntas abaixo de uma vez e anote as respostas na tabela "Conta e limites" de `docs/operations/ambientes.md`.

1. **Convite para o workspace da contratante**, com o seu usuário da Railway, no papel **Admin**. O convite é feito em: engrenagem ao lado do nome do workspace → aba **People** [F16].
   - Por que Admin: o papel Member cria projeto, serviço, variável e volume, mas **não apaga** projeto nem serviço e não vê cobrança [F16]. Sem Admin você não consegue limpar um serviço órfão ([5.5](#55-se-o-apply-falhar-no-meio)), destruir o ambiente ([8.8](#88-destruir-o-ambiente)) nem configurar o alerta de gasto [F18].
2. **Qual é o plano do workspace: Hobby ou Pro?** (ADR-0002 §16, item 7.)
   - **Confira:** a documentação diz que só Pro e Enterprise aceitam membros no workspace [F17]. Se for **Hobby**, o Igor não vai conseguir convidar você. Pare e decida com ele entre: subir o workspace para Pro (US$ 20/mês, que já cobre o uso estimado da homologação, ver `docs/operations/custos.md`), ou ele mesmo criar o token de workspace e passá-lo a você pelo gerenciador de senhas. No segundo caso, a conexão com o GitHub ([Passo 3](#passo-3--github-app-da-railway-no-repositório)) passa a depender da conta dele.
   - O plano também muda o prazo do rollback (72 h no Hobby, 120 h no Pro [F17]) e a retenção de logs (7 dias no Hobby, 30 no Pro [F9]).
3. **Quem paga e qual é o teto.** A estimativa é de cerca de US$ 4 de uso por mês: fatura de US$ 5 no Hobby ou US$ 20 no Pro (`docs/operations/custos.md`). Peça um número de teto por escrito (C04).
4. **O que mais roda nesse workspace** e se já existe **limite rígido de gasto**. O limite rígido desliga **todos** os serviços do workspace quando é atingido [F18]; ligar ou não é decisão do Igor.
5. **Alerta de gasto por e-mail** (sugestão de `custos.md`: US$ 10). É um clique de Admin do workspace ([Passo 9](#passo-9--o-que-fica-para-depois)).

**✔ Confira:** ao entrar em `https://railway.com`, o seletor de workspace (canto superior esquerdo) mostra o workspace da contratante, e você tem por escrito o plano e o teto.

**Se der errado:** o convite não chega ou o workspace não aparece → confirme com o Igor o e-mail ou o usuário do GitHub que ele convidou; a Railway associa o convite à conta com que você entra.

---

## Passo 2 — Ferramentas e token da Railway

### 2.1 Conferir que a `main` tem tudo

**Pré-requisitos do primeiro `apply`, mergeados na `main`:**

| PR | O que traz | Obrigatório? | Sem ele |
|---|---|---|---|
| #100 | O Terraform de `infra/railway/` (os PRs #110, #113 e #116 já foram mergeados dentro da branch dele) | sim | Não há o que aplicar |
| #101 | ADR-0002 e `docs/operations/custos.md` | sim | As decisões D1 a D3 não estão aceitas |
| #104 | A rota `/health/ready` | sim | O healthcheck do serviço aponta para ela: **o primeiro deploy falha no healthcheck** |
| O da Issue #103 | Chaves do Data Protection no PostgreSQL (pacote e migração) | **só se a decisão D4 do ADR-0002 for a opção A** | Cada deploy derruba as sessões do painel |

Já estão na `main`: #102 (`migrate`), #105 (`Dockerfile`) e #106 (proxy).

```sh
git switch main
```

```sh
git pull --ff-only
```

```sh
ls infra/railway/versions.tf infra/railway/modules/ambiente/main.tf docs/architecture/ADR-0002-hospedagem-railway.md Dockerfile
```

**✔ Confira:** os quatro caminhos são listados, sem `No such file or directory`.

```sh
grep -c '"/health/ready"' src/Helinho.Web/Composition/HealthEndpoint.cs
```

**✔ Confira:** imprime `1`.

**Se der errado:** imprime `0` ou falta arquivo → algum PR ainda não foi mergeado. **Pare aqui.** Sem a rota `/health/ready` (PR #104) o healthcheck da Railway nunca recebe 2xx e todo deploy falha depois de 120 segundos.

**Data Protection: depende da decisão D4** (ADR-0002 §9 e §12), que é sua e pode ainda estar aberta. O PR da Issue #103 implementa a opção A e pode ser fechado sem merge se você escolher outra.

| Decisão D4 | O que fazer aqui |
|---|---|
| **A** (tabela no PostgreSQL com o pacote oficial) | Espere o PR da Issue #103 entrar e confira com o `grep` abaixo |
| **B** ou **C** (repositório próprio, ou volume no web) | A persistência chega por outro PR. O `grep` abaixo imprime `0` para sempre e não quer dizer nada; confira pelo PR da opção escolhida |
| **D** (não persistir) | Siga. Cada deploy derruba as sessões do painel (todo mundo entra de novo), e isso passa a ser o comportamento esperado |
| Ainda aberta | Você pode subir a homologação sem esperar: o sistema funciona, só desloga o painel a cada deploy. Registre em `docs/operations/ambientes.md`, na linha "Sessões do painel" |

Só para a opção A:

```sh
grep -c 'DataProtection.EntityFrameworkCore' Directory.Packages.props
```

**✔ Confira:** imprime `1` (o PR da Issue #103 entrou).

**Se der errado:** imprime `0` com a D4 na opção A → o PR ainda não foi mergeado. Espere por ele, ou siga sabendo que cada deploy desloga o painel até ele entrar (a migração nova dele roda sozinha no pre-deploy do deploy seguinte).

### 2.2 Terraform na versão fixada

`infra/railway/versions.tf` aceita `>= 1.16.0, < 1.18.0`. O workflow `infra` do CI, quando existir, usará a 1.17.2, que era a estável em 18/11/2026 [F19]; em 19/11/2026 ele ainda não estava na branch do PR #100.

```sh
terraform version
```

**✔ Confira:** a primeira linha é `Terraform v1.16.x` ou `Terraform v1.17.x`.

**Se der errado:**

- `command not found` → instale [F19]:

  ```sh
  brew tap hashicorp/tap
  ```

  ```sh
  brew install hashicorp/tap/terraform
  ```

- Versão 1.18 ou maior (o Homebrew instala sempre a mais nova) → o `init` falha com `Unsupported Terraform Core version`. Baixe o binário da 1.17.2 em `https://releases.hashicorp.com/terraform/1.17.2/`, confira o SHA-256 publicado na mesma página e ponha o binário à frente no `PATH`. Não mude `versions.tf` para aceitar a versão nova: isso é um PR `chore` próprio.

### 2.3 `tflint` e utilitários

`infra/railway/.tflint.hcl` exige `tflint` 0.66 ou maior.

```sh
brew install tflint jq age
```

```sh
tflint --version
```

**✔ Confira:** `TFLint version 0.66.x` ou maior. `jq --version` e `age --version` respondem.

**Se der errado:** versão menor que 0.66 → `brew upgrade tflint`.

### 2.4 O conflito do nome `RAILWAY_TOKEN`

Leia antes de criar o token.

- A Railway tem três tipos de token [F1]: de **conta** (tudo o que o seu usuário alcança, em todos os workspaces), de **workspace** (só aquele workspace) e de **projeto** (um ambiente de um projeto; usa outro cabeçalho HTTP).
- O provider do Terraform lê a variável de ambiente `RAILWAY_TOKEN` e exige token de **conta ou de workspace** [F2]. Token de projeto não funciona, e nem poderia criar o projeto.
- A CLI `railway` usa o **mesmo nome** `RAILWAY_TOKEN` para token de **projeto**, e `RAILWAY_API_TOKEN` para conta ou workspace [F3].

Consequência: `RAILWAY_TOKEN` é exportado **só** no terminal do Terraform, e a CLI nunca roda nele.

### 2.5 Criar o token de workspace

1. Entre em `https://railway.com/account/tokens` [F1].
2. No formulário de novo token, dê um nome que diga para que serve (por exemplo `terraform-helinho-homologacao`).
3. No seletor de workspace do formulário, escolha **o workspace da contratante**. É isso que faz o token ser de workspace. A opção "No workspace" cria um token de **conta**, que enxerga também os seus projetos pessoais: não use [F1].
4. Crie o token (o nome do botão pode variar).
5. Copie o valor **direto para o gerenciador de senhas**, em uma entrada própria (sugestão: "Railway — token Terraform helinho homologação"). Não cole em chat, e-mail, nota, arquivo ou no histórico do shell.

**✔ Confira:** a lista de tokens da página mostra o token novo, associado ao workspace da contratante, e o valor está salvo no gerenciador.

**Confira:** a documentação não diz se o valor é mostrado só uma vez [F1]. Trate como se fosse: se não conseguir ver o valor de novo, apague o token e crie outro.

**Se der errado:** o workspace da contratante não aparece no seletor → você ainda não é membro dele (volte ao [Passo 1](#passo-1--o-que-pedir-ao-igor)) ou o seu papel não permite criar token. A documentação não diz qual papel é exigido [F16]; peça Admin.

**Escopo e validade:** o token de workspace alcança **todos** os projetos daquele workspace, não só o Helinho [F1]. É o menor escopo que o provider aceita. Guarde como guardaria a senha da conta, e troque conforme [8.6](#86-rotação-do-token-da-railway).

---

## Passo 3 — GitHub App da Railway no repositório

O serviço `helinho-web` é ligado ao repositório `henriqueluza/universo-do-helinho`, que é **privado**. Sem a integração, a criação do serviço falha no meio do `apply` e deixa um serviço órfão ([5.5](#55-se-o-apply-falhar-no-meio)).

1. **Ligue a sua conta da Railway ao GitHub.** A documentação exige isso ("You must link your Railway account to GitHub") sem dar o caminho no painel [F4]. Se você entra na Railway com o GitHub, já está ligado. Se não, procure a conexão com o GitHub nas configurações da conta (o nome da seção pode variar).
2. **Instale a app.** Abra `https://github.com/apps/railway-app/installations/new` (é o link que a documentação da Railway indica [F4]).
3. Escolha a conta **`henriqueluza`** (a dona do repositório).
4. Em "Repository access", escolha **Only select repositories** e marque só **`universo-do-helinho`**. Não dê acesso a todos os repositórios.
5. Confirme a instalação (botão **Install** ou **Save**; o nome pode variar).

**✔ Confira:** em `https://github.com/settings/installations`, a app **Railway App** aparece com acesso a `universo-do-helinho`. No painel da Railway, dentro do workspace da contratante, o botão de novo projeto com a opção de repositório do GitHub lista `henriqueluza/universo-do-helinho` (clique em **Refresh** se a lista estiver velha [F5]). **Feche sem criar nada:** quem cria o projeto é o Terraform.

**Se der errado:**

- O repositório não aparece na lista → em `https://github.com/settings/installations` → **Configure** na Railway App: veja se há pedido de permissão pendente para aceitar e se o repositório está marcado. Depois, **Refresh** na Railway [F5].
- Continua sem aparecer → desinstale e reinstale a app; é o último recurso que a documentação indica [F5].

**Confira (não confirmado em fonte):** o Terraform conecta o repositório usando um token de **workspace**, que não é ligado a um usuário. A documentação só diz que o auto-deploy exige "pelo menos um membro do projeto com conta do GitHub conectada" e acesso de contribuidor ao repositório [F5]. Se, no `apply`, a criação de `railway_service.web` falhar com erro que cita o repositório ou o GitHub, siga a primeira linha da tabela de [5.5](#55-se-o-apply-falhar-no-meio).

---

## Passo 4 — `terraform.tfvars` e variáveis de ambiente

### 4.1 Criar o `terraform.tfvars`

```sh
cp infra/railway/terraform.tfvars.example infra/railway/terraform.tfvars
```

```sh
git check-ignore -v infra/railway/terraform.tfvars
```

**✔ Confira:** o segundo comando imprime a regra `*.tfvars` do `.gitignore`. `git status --short` não mostra o arquivo.

**Se der errado:** o `git check-ignore` não imprime nada → o `.gitignore` do PR #110 não está na sua `main`. Pare e volte ao [2.1](#21-conferir-que-a-main-tem-tudo).

**Uma edição é obrigatória.** Todas as linhas do exemplo vêm comentadas e mostram os padrões (só `homologacao`, com seed ligado; `auto_deploy = false`; região `us-east4-eqdc4a`; `postgres:17.12`). Pela decisão D3 do Henrique (22/11/2026), em homologação o auto-deploy fica **ligado**: abra o arquivo e descomente a linha, trocando o valor:

```hcl
auto_deploy = true
```

O padrão da variável no Terraform continua `false` (é o previsto para produção); quem liga é o seu `terraform.tfvars`, que o git ignora. Edite o resto só se:

| Situação | O que descomentar |
|---|---|
| O token enxerga mais de um workspace (o erro aparece no `apply`) | `workspace_id = "<id do workspace>"` |
| O subdomínio `helinho-homologacao` já existe na Railway (o erro aparece no `apply`) | `ambientes = { homologacao = { seed_enabled = true, subdominio = "<outro-nome>" } }` |

**Confira:** `grep -n '^auto_deploy' infra/railway/terraform.tfvars` imprime `auto_deploy = true`. Sem essa linha o `plan` do [5.3](#53-plan) mostra o watch pattern de "deploy desligado" e a homologação nasce em publicação manual.

**Domínio próprio:** não preencha agora. Ele entra depois, pelo `dominio_proprio` dentro de `ambientes` (exemplo `paineladm.example.org`, sem `https://` e sem barra), pelo passo a passo de `docs/operations/railway-dominio.md`, **depois** do primeiro deploy.

**Segredo não entra nesse arquivo.** Não acrescente `producao` em `ambientes` ([Passo 9](#passo-9--o-que-fica-para-depois)).

### 4.2 Gerar a senha do seed

No gerenciador de senhas, gere uma senha com **minúscula, maiúscula, dígito e símbolo**. O **mínimo é 16 caracteres**, exigido pelo Terraform, que recusa senha menor ou sem uma das quatro classes; o **recomendado é 20 ou mais**. Salve em uma entrada própria (sugestão: "Helinho homologação — senha do seed").

Ela vale para **todos** os usuários sintéticos (master, administradores e alunos). Não reutilize senha de outro lugar.

### 4.3 Exportar os segredos sem deixá-los no histórico

Abra um terminal novo: ele é o **terminal do Terraform**. O `read -rs` lê sem mostrar na tela e sem gravar no histórico do shell (funciona em `zsh` e `bash`). Depois de cada comando abaixo o cursor fica parado, **sem prompt**: cole o valor e aperte Enter.

```sh
read -rs RAILWAY_TOKEN && export RAILWAY_TOKEN
```

```sh
read -rs TF_VAR_seed_password && export TF_VAR_seed_password
```

```sh
echo "token: ${#RAILWAY_TOKEN} caracteres | senha do seed: ${#TF_VAR_seed_password} caracteres"
```

**✔ Confira:** o `echo` mostra só **tamanhos**: o do token maior que zero e o da senha maior ou igual a 16. Nenhum valor aparece na tela.

**Se der errado:**

- Tamanho `0` → o valor não foi colado; repita o `read`.
- Você digitou `export RAILWAY_TOKEN=<valor>` por engano → o valor ficou no histórico. Apague a linha do histórico (`~/.zsh_history`) e **troque o token** ([8.6](#86-rotação-do-token-da-railway)).

Regras desse terminal: não rode `railway`, não rode `env` nem `set` (imprimem os valores), não compartilhe a tela. Ao terminar o dia:

```sh
unset RAILWAY_TOKEN TF_VAR_seed_password
```

---

## Passo 5 — `init`, `plan` e `apply`

Tudo no terminal do Terraform. O estado fica em `infra/railway/terraform.tfstate`, fora do git ([8.9](#89-onde-fica-o-estado-do-terraform)).

### 5.1 `init`

```sh
terraform -chdir=infra/railway init
```

**✔ Confira:** `Terraform has been successfully initialized!`; os providers instalados são `terraform-community-providers/railway v0.6.3` e `hashicorp/random v3.9.x`; `git status --short infra/railway` continua vazio (o `.terraform.lock.hcl` não mudou).

**Se der errado:**

- `Unsupported Terraform Core version` → versão errada do Terraform ([2.2](#22-terraform-na-versão-fixada)).
- O lockfile aparece modificado no `git status` → a sua plataforma não está entre as três do lockfile (`linux_amd64`, `darwin_arm64`, `darwin_amd64`). Desfaça com `git checkout -- infra/railway/.terraform.lock.hcl` e abra uma Issue; não commite o lockfile alterado à mão.

### 5.2 Validar antes de planejar

```sh
terraform -chdir=infra/railway validate
```

```sh
(cd infra/railway && tflint --init && tflint --recursive)
```

```sh
terraform -chdir=infra/railway test
```

**✔ Confira:** `Success! The configuration is valid.`; o `tflint` termina sem imprimir achados; e o `test` termina com `Success!` e `0 failed`. O `terraform test` usa providers simulados: só planeja em memória e não fala com a Railway, mesmo com `RAILWAY_TOKEN` exportado (`infra/railway/README.md`).

### 5.3 `plan`

O arquivo de plano guarda os segredos em texto. Ele fica **fora do repositório**, em uma pasta só sua, e é apagado depois do `apply`. O `.gitignore` já bloqueia `*.tfplan` e `tfplan` (PR #116), mas isso só impede o commit: o arquivo continuaria em claro dentro do repositório. Por isso o plano vai para `~/.helinho-infra/`, sempre com nome terminado em `.tfplan`.

```sh
mkdir -p ~/.helinho-infra && chmod 700 ~/.helinho-infra
```

```sh
terraform -chdir=infra/railway plan -out="$HOME/.helinho-infra/homologacao.tfplan"
```

**✔ Confira, na saída do `plan`:**

1. A última linha do resumo é **`Plan: 7 to add, 0 to change, 0 to destroy.`**
2. Os sete recursos são, todos sob `module.ambiente["homologacao"]`: `railway_project.this`, `random_password.postgres`, `railway_service.postgres`, `railway_variable_collection.postgres`, `railway_service.web`, `railway_service_domain.web`, `railway_variable_collection.web`.
3. **Não existe** `railway_tcp_proxy`, e o **único** `railway_service_domain` é o `.web`. O banco não tem endereço público.
4. Em `railway_service.web`: `source_repo = "henriqueluza/universo-do-helinho"`, `source_repo_branch = "main"`, `watch_patterns = []` (com `auto_deploy = true`; se aparecer `["/.auto-deploy-desligado"]`, o `terraform.tfvars` está sem a linha do [4.1](#41-criar-o-terraformtfvars)), `predeploy_command = "tini -- dotnet Helinho.Web.dll migrate"`, `predeploy_timeout = 300`, `healthcheck_path = "/health/ready"`, `healthcheck_timeout = 120`.
5. Em `railway_service.postgres`: `source_image = "postgres:17.12"` e o volume `postgres-dados` em `/var/lib/postgresql/data`.
6. `railway_variable_collection.web` e, em `Changes to Outputs`, a saída `ambientes` (`variaveis_web`) listam **13 nomes**: `ASPNETCORE_ENVIRONMENT`, `PORT`, `Proxy__TrustForwardedHeaders`, `ConnectionStrings__Default`, `Seed__Enabled`, `HELINHO_SEED_PASSWORD`, os cinco `Api__Auth__*`, `Email__Provider` e `Ai__Provider`. Nenhuma variável de Brevo ou de IA além dessas (as vazias não são enviadas; ADR-0002 §16, item 6: não se sabe se a API da Railway aceita valor vazio).
7. **Nenhum segredo na tela:** todos os valores de variável aparecem como `(sensitive value)`, inclusive os que não são segredo.

Duas conferências por comando, que não imprimem o segredo:

```sh
terraform -chdir=infra/railway show "$HOME/.helinho-infra/homologacao.tfplan" | grep -cF -f <(printf '%s\n' "$TF_VAR_seed_password")
```

**✔ Confira:** imprime `0` (a senha do seed não aparece no plano legível).

```sh
terraform -chdir=infra/railway show "$HOME/.helinho-infra/homologacao.tfplan" | grep -c -e railway_tcp_proxy -e railway_custom_domain
```

**✔ Confira:** imprime `0`.

**Se der errado:**

- `Com o seed ligado, defina TF_VAR_seed_password com 16 ou mais caracteres…` → a senha não foi exportada ou é fraca ([4.2](#42-gerar-a-senha-do-seed), [4.3](#43-exportar-os-segredos-sem-deixá-los-no-histórico)).
- `O seed sintético só pode ser ligado em homologacao.` ou `Ambientes aceitos: homologacao e producao.` → você mexeu em `ambientes`; volte ao padrão.
- O número não é 7 → **não aplique**. Compare com a lista do item 2 e descubra o que sobrou ou faltou no `terraform.tfvars`.

O primeiro `plan`, sem estado, provavelmente não fala com a Railway: um token errado só aparece no `apply`, que falha no primeiro recurso sem criar nada.

### 5.4 `apply`

**Antes do `apply`, confira a proteção da `main`.** Com `auto_deploy = true`, o próprio `apply` pode publicar, e a proteção da `main` é a única barreira antes de cada deploy; o ADR-0002 §7.5 deixa a você conferi-la antes do primeiro `apply`. O ruleset `protect-main` deve exigir PR e os checks `validate`, `semgrep`, `gitleaks` e `pr-size` antes do merge. Veja em `gh api repos/henriqueluza/universo-do-helinho/rulesets` ou em **Settings → Rules** do repositório.

**✔ Confira:** o ruleset está ativo na `main`, exige PR e lista os quatro checks.

**Se der errado:** o ruleset não exige os checks, ou a `main` aceita push direto → **não rode o `apply`** com `auto_deploy = true`. Avise o Henrique.

Abra o painel da Railway em outra janela para acompanhar. O `apply` cria, nesta ordem: projeto → senha → serviço `Postgres` → variáveis do `Postgres` (reimplanta) → serviço `helinho-web` → domínio e variáveis do web (reimplanta).

```sh
terraform -chdir=infra/railway apply "$HOME/.helinho-infra/homologacao.tfplan"
```

**✔ Confira:** `Apply complete! Resources: 7 added, 0 changed, 0 destroyed.` e, em `Outputs`, `url_publica` e `url_api` com `https://<subdomínio>.up.railway.app`.

Apague o arquivo de plano, que contém os segredos:

```sh
rm "$HOME/.helinho-infra/homologacao.tfplan"
```

**Confira (handoff 107, nunca executado): o que você deve ver no painel durante o `apply`.**

| O que aparece | É esperado? | O que fazer |
|---|---|---|
| `Postgres` com um deploy **vermelho** e, logo depois, um **verde** | Sim. A imagem é conectada antes de as variáveis existirem; o primeiro deploy falha por falta de `POSTGRES_PASSWORD` e o segundo, disparado pelas variáveis, sobe | Nada |
| `Postgres` só com deploy vermelho depois de o `apply` terminar | Não | Abra os logs do deploy. Se a mensagem for de `POSTGRES_PASSWORD` ausente, confira a aba **Variables** (devem ser 9 variáveis) e faça **Redeploy** no menu de três pontos do deploy [F6] |
| `helinho-web` com um deploy que falhou no pre-deploy, sem connection string | Possível (ADR-0002 §16, item 1): conectar o repositório pode disparar um deploy antes de as variáveis existirem | Nada; siga para o [Passo 6](#passo-6--primeiro-deploy-e-conferências) |
| `helinho-web` sem nenhum deploy | Possível, mesmo com auto-deploy ligado: o gatilho só vale para o próximo push | Nada agora; o primeiro deploy é feito no [Passo 6](#passo-6--primeiro-deploy-e-conferências) |
| `helinho-web` com deploy marcado como ignorado (**Show Skipped** [F5]) | Não esperado com `auto_deploy = true` (o watch pattern fica vazio) | Confira o `terraform.tfvars` ([4.1](#41-criar-o-terraformtfvars)) e o `plan` |
| `helinho-web` já com deploy **verde** | Possível: a criação das variáveis reimplanta o serviço | Pule para o [6.3](#63-conferir-o-pre-deploy-migrate) |

### 5.5 Se o `apply` falhar no meio

O `apply` não é atômico: o que foi criado antes do erro **continua criado** e está no estado. Antes de qualquer coisa, veja o que o Terraform conhece:

```sh
terraform -chdir=infra/railway state list
```

Compare com o painel e ache a linha do seu erro:

| Sintoma | O que aconteceu | O que fazer |
|---|---|---|
| Erro ao criar `railway_service.web` citando o repositório ou o GitHub (o texto pode variar) | A Railway não enxerga o repositório ([Passo 3](#passo-3--github-app-da-railway-no-repositório)) | 1) Apague o `helinho-web` órfão, se existir (linha do serviço órfão, abaixo). 2) Refaça o Passo 3. 3) Repita `plan` e `apply`. **Plano B**, se a integração estiver certa e o erro persistir: crie um token de **conta** ("No workspace" em [2.5](#25-criar-o-token-de-workspace)), pegue o id do workspace (logo abaixo desta tabela), defina `workspace_id` no `terraform.tfvars`, repita, e ao final **apague esse token** e volte ao de workspace. O token de conta alcança também os seus projetos pessoais: é exceção, não rotina |
| `Unable to redeploy service after variable collection created` | As variáveis **já estão** na Railway; o provider tentou reimplantar um serviço que não tinha deploy anterior | No painel, serviço citado no erro → `Cmd+K` → **Deploy Latest Commit** (web) ou **Redeploy** do último deploy (`Postgres`). Depois repita `plan` e `apply` |
| `serviceManifest is not found` | O provider lê a região no manifesto do último deploy, e esse deploy não tem manifesto (foi ignorado, por exemplo) | Igual à linha de cima: faça existir um deploy de verdade e repita `plan` e `apply` |
| Erro em `railway_service_domain.web` dizendo que o subdomínio já existe | `helinho-homologacao` é único em toda a Railway e alguém já usa | Defina `subdominio` no `terraform.tfvars` ([4.1](#41-criar-o-terraformtfvars)) e repita `plan` e `apply` |
| Erro em `railway_project.this` pedindo workspace | O token enxerga mais de um workspace (acontece com token de conta) | Pegue o id do workspace (logo abaixo desta tabela) e defina `workspace_id` no `terraform.tfvars` |
| `Not Authorized`, `401` ou erro de autenticação no primeiro recurso | Token errado, apagado, de projeto, ou colado com espaço | Repita o [4.3](#43-exportar-os-segredos-sem-deixá-los-no-histórico). Nada foi criado |
| Um **serviço** existe no painel e **não** aparece no `state list` | **Serviço órfão** (limitação L8 do provider): ele só entra no estado no fim da criação | Siga "Apagar só o serviço órfão", abaixo. **Não repita o `apply` com o órfão presente** |
| O **projeto** `helinho-homologacao` existe no painel e o `state list` **não imprime nada** | O `apply` foi interrompido (queda de rede, `Ctrl+C`, terminal fechado) depois de criar o projeto e antes de gravar o estado | Siga "Projeto criado, mas fora do estado", abaixo. **Não repita o `apply`:** ele criaria um segundo projeto com o mesmo nome |

**Pegar o id do workspace** (só para o plano B com token de conta). O painel copia id de projeto, de serviço e de ambiente com `Cmd+K`, mas não o de workspace [F1]. A CLI mostra [F22]. Em um **terminal da CLI** (novo, sem `RAILWAY_TOKEN`; a preparação está no [8.4](#84-pg_dump-para-fora-da-plataforma)), depois de `brew install railway` e `railway login`:

```sh
railway whoami --json | jq -r '.workspaces[] | "\(.id)  \(.name)"'
```

**✔ Confira:** uma linha por workspace, com id e nome. Copie o id da linha do workspace da contratante para `workspace_id`, no `terraform.tfvars`. O id não é segredo, mas não vai para `ambientes.md`.

**Se der errado:** a lista vem vazia ou sem o workspace da contratante → você não é membro dele ([Passo 1](#passo-1--o-que-pedir-ao-igor)). Peça o id ao Igor (ele roda o mesmo comando).

**Apagar só o serviço órfão.** A opção fica nas configurações do **projeto**, na seção de perigo (**Danger**) [F4][F15]. **É a mesma página do botão `Delete Project`: você vai apagar um serviço, não o projeto.**

1. Confira o alvo antes de clicar: o projeto aberto é `helinho-homologacao`, e o serviço que você vai apagar é exatamente o que **falta** no `state list` (`helinho-web` ou `Postgres`).
2. Na seção de perigo, procure a opção de apagar **serviço** e escolha o órfão pelo nome. A documentação diz que serviços podem ser apagados nessa página, mas não dá o rótulo do botão (o nome pode variar) [F15]. **Não clique em `Delete Project`.**
3. No primeiro `apply` o órfão não tem dado nenhum. Em qualquer outro momento, **não apague o `Postgres` sem `pg_dump`** ([8.4](#84-pg_dump-para-fora-da-plataforma)).

**✔ Confira:** o serviço sumiu do painel, o projeto `helinho-homologacao` continua lá com os demais serviços, e o `state list` não mudou. Só então repita `plan` e `apply`.

**Se der errado:** repetir o `apply` com o órfão presente faz o provider tentar criar outro serviço com o mesmo nome: ou falha por nome repetido, ou deixa dois serviços iguais no projeto. Se já aconteceu, apague o que **não** está no estado (compare o id do serviço no painel, `Cmd+K`, com `servico_web_id` e `servico_postgres_id` de `terraform output ambientes`).

**Projeto criado, mas fora do estado.**

- **Primeiro `apply`, projeto recém-criado e sem dado:** apague o projeto no painel (configurações do projeto → aba **Danger** → **Delete Project** [F15]). Antes de clicar, confira que o nome é `helinho-homologacao`, que ele foi criado agora e que não há serviço com dado dentro dele. Confira que sumiu da lista de projetos e só então repita `plan` e `apply`.
- **Projeto que já tem dado:** não apague. Traga-o para o estado. O provider aceita importar o projeto pelo id [F2], que aparece nas configurações do projeto, aba **General** [F15]:

  ```sh
  terraform -chdir=infra/railway import 'module.ambiente["homologacao"].railway_project.this' <id do projeto>
  ```

  **Confira (nunca executado):** depois do `import`, rode `plan`. Ele deve querer criar só o que falta. Se quiser destruir ou substituir qualquer coisa, não aplique e abra uma Issue.

Regras para repetir:

- **Sempre** faça um `plan -out` novo; o arquivo de plano antigo não serve mais.
- O `plan` novo deve mostrar só o que **faltou** criar. Se ele quiser **destruir**, **substituir** ou **alterar** `railway_project` ou `railway_service.postgres`, pare ([8.1](#81-o-que-dispara-um-deploy-e-como-conferir) explica por quê).
- Faça o backup do estado ([5.7](#57-backup-cifrado-do-estado)) mesmo depois de um `apply` parcial.
- Na segunda falha igual, não tente uma terceira vez do mesmo jeito: guarde a mensagem de erro (sem segredo) e abra uma Issue.

### 5.6 Conferir o que foi criado

```sh
terraform -chdir=infra/railway output
```

**✔ Confira:** `url_publica` e `url_api` para `homologacao`, e em `ambientes` os ids do projeto, do ambiente e dos dois serviços. Nenhuma saída traz senha.

No painel, projeto `helinho-homologacao`, ambiente `homologacao`:

- **`Postgres`:** nas configurações de rede do serviço **não há** domínio público nem TCP proxy; o volume `postgres-dados` está montado em `/var/lib/postgresql/data`; a aba **Variables** tem 9 variáveis.
- **`helinho-web`:** origem `henriqueluza/universo-do-helinho`, branch `main`; **sem** watch path (com `auto_deploy = true`; um watch path `/.auto-deploy-desligado` quer dizer auto-deploy desligado); pre-deploy command `tini -- dotnet Helinho.Web.dll migrate`; healthcheck `/health/ready`; aba **Variables** com 13 variáveis.

**Se der errado:** o `Postgres` tem domínio público ou TCP proxy → alguém clicou no painel. Remova na hora e rode `terraform plan`; o banco só pode existir na rede privada.

### 5.7 Backup cifrado do estado

O estado guarda em texto a senha do banco e a senha do seed e, depois das Sprints 3 e 4, as chaves da Brevo e do provedor de IA. Faça o backup **depois de cada `apply`**, inclusive os que falharam no meio.

```sh
age -p -o "$HOME/.helinho-infra/tfstate-$(date +%Y%m%d-%H%M).age" infra/railway/terraform.tfstate
```

O `age` pede uma frase-senha duas vezes. Use uma frase própria, guardada no gerenciador de senhas (sugestão de entrada: "Helinho — frase do backup do estado").

```sh
ls -l ~/.helinho-infra/
```

**✔ Confira:** existe um arquivo `tfstate-<data>-<hora>.age` com tamanho maior que zero. Teste a volta, trocando `<arquivo>`:

```sh
age -d "$HOME/.helinho-infra/<arquivo>.age" | jq '.resources | length'
```

**✔ Confira:** pede a frase-senha e imprime `7` depois de um `apply` completo. Depois de um `apply` parcial imprime menos: o mesmo número de linhas do `terraform -chdir=infra/railway state list`.

Copie o arquivo `.age` para **fora da máquina** ([8.9](#89-onde-fica-o-estado-do-terraform)).

**Se der errado:** `age: command not found` → `brew install age`. Alternativa com `gpg`, se preferir: `gpg --symmetric --cipher-algo AES256 -o <destino>.gpg infra/railway/terraform.tfstate`.

### 5.8 Ligar Wait for CI

Faça **logo depois do primeiro `apply`** (ADR-0002 §7.5). O provider não alcança esse botão: é clique no painel.

1. Painel → projeto `helinho-homologacao` → serviço `helinho-web` → **Settings**, na parte de origem e auto-deploy → ligue **Wait for CI** [F5]. **Confira:** o lugar exato e o nome do botão podem variar; se não achar, procure na documentação [F5] antes de improvisar.
2. Confira de novo a proteção da `main` no GitHub, a mesma do [5.4](#54-apply) (quem confirma é uma pessoa, o ADR-0002 §7.5 deixa isso a você): o ruleset `protect-main` deve exigir PR e os checks `validate`, `semgrep`, `gitleaks` e `pr-size` antes do merge. Em `gh api repos/henriqueluza/universo-do-helinho/rulesets` ou em **Settings → Rules** do repositório.

> **Aviso, risco aceito só para homologação.** Ligar **Wait for CI não faz a Railway esperar build nem testes** do commit publicado. O workflow `validate` (format, build e testes) só roda em `pull_request`; no push para a `main` roda apenas o `security` (gitleaks e semgrep). Um merge com o `validate` verde no PR publica sozinho, sem nenhuma verificação nova. **Confira (ADR-0002 §16, item 10):** não se sabe como a Railway se comporta com Wait for CI quando o único workflow do push é o `security`: pode publicar na hora, esperar só ele ou segurar o deploy.
>
> O Henrique aceitou esse risco em 22/11/2026, para homologação com dado sintético. A Issue #225 (rodar o `validate` também no commit que chega à `main`) é **pré-requisito de produção**.

**✔ Confira:** o botão fica ligado, e o ruleset da `main` lista os quatro checks.

**Se der errado:** o ruleset não exige os checks, ou a `main` aceita push direto → **não deixe o auto-deploy ligado**: é a única barreira. Desligue-o ([Deixar a homologação parada](#deixar-a-homologação-parada-para-o-teste-do-jogo)) e avise o Henrique.

---

## Passo 6 — Primeiro deploy e conferências

Com `auto_deploy = true` (decisão D3, 22/11/2026), **todo push na `main` publica sozinho**. O primeiro deploy pode ter saído sozinho do `apply`; o [6.1](#61-ver-em-que-estado-o-helinho-web-ficou) mostra como saber.

> **Aviso:** o Wait for CI **não espera build nem testes** (veja o [5.8](#58-ligar-wait-for-ci)). Cada merge na `main` vira deploy em homologação sem ninguém acompanhar. Se um commit quebrado for publicado, faça o rollback do [8.2](#82-rollback-para-o-deploy-anterior) (Rollback ou Redeploy do deploy anterior, pelo painel da Railway).

### 6.1 Ver em que estado o `helinho-web` ficou

Painel → projeto `helinho-homologacao` → serviço `helinho-web` → aba **Deployments**.

- Deploy **verde** (ativo) → pule para o [6.3](#63-conferir-o-pre-deploy-migrate).
- Deploy que falhou, ou nenhum deploy → siga para o 6.2.

### 6.2 Se não há deploy bom: Deploy Latest Commit

1. Com o serviço `helinho-web` aberto, aperte `Cmd+K`.
2. Escolha **Deploy Latest Commit**. A Railway implanta o último commit da branch conectada, `main` [F5][F6].
3. Acompanhe na aba **Deployments**: build da imagem (o `Dockerfile` da raiz) → pre-deploy → healthcheck → ativo. O primeiro build leva alguns minutos.

**✔ Confira:** o deploy termina **verde** e é o deploy ativo do serviço.

Este é o caminho usado no primeiro deploy, quando o deploy automático não aconteceu e para publicar de novo depois de um restore ou de uma rotação de senha ([8.5](#85-restore-do-banco), [8.7](#87-rotação-das-senhas)). O dia a dia é o [8.1](#81-o-que-dispara-um-deploy-e-como-conferir). Com `auto_deploy = false` a publicação é sempre manual por este comando (e o ADR-0002 §16, item 2, deixa em aberto se ele respeita o watch pattern: se o deploy aparecer como **ignorado** em **Show Skipped** [F5], o plano B é o botão **Disable** do auto-deploy nas configurações do serviço, que é estado só de painel e deve ser registrado em `ambientes.md`).

**Se der errado:**

- **Falha no build.** Abra o deploy → aba **Build Logs** [F9]. A imagem nunca foi construída em `linux/amd64` (só em `arm64`, handoff 94); se a falha for de arquitetura ou de pacote (`tini`), guarde o log e abra uma Issue. Nada do que este runbook faz conserta o `Dockerfile`.
- **Falha no pre-deploy.** Veja o 6.3.
- **Falha no healthcheck** (`Healthcheck failed`, depois de 120 s). Abra os logs do deploy. Causas prováveis: a rota `/health/ready` não existe no commit publicado (PR #104); o banco não está no ar (veja o `Postgres`); a aplicação não escuta na porta 8080 (a variável `PORT=8080` sumiu). **Confira (ADR-0002 §16, item 4):** não se sabe se a requisição do healthcheck traz `X-Forwarded-Proto`. A aplicação deixa `/health` fora do redirecionamento para HTTPS justamente por isso. Se o log mostrar resposta **307** para `/health/ready`, essa exceção não está valendo: abra uma Issue de `fix`.

### 6.3 Conferir o pre-deploy `migrate`

O pre-deploy roda `tini -- dotnet Helinho.Web.dll migrate` em um contêiner separado, na rede privada, com as variáveis do serviço e **sem volume** [F7]. Ele aplica as migrações e, como `Seed__Enabled=true`, roda o seed sintético. Se sair com código diferente de zero, o deploy não prossegue e a versão anterior continua no ar [F7].

1. Clique no deploy, na aba **Deployments**.
2. Abra os logs do deploy (**Deploy Logs**). A documentação não nomeia uma aba só do pre-deploy; a equipe da Railway diz que os logs do pre-deploy que falhou ficam nos logs do deploy [F8]. Se não achar ali, use **Observability**, no topo do projeto, que junta os logs do ambiente [F9].

**✔ Confira:** o deploy passou pela etapa de pre-deploy sem erro e ficou ativo. Nos logs pode haver linhas do EF Core sobre migrações aplicadas (o texto exato pode variar). A prova definitiva de que migração e seed rodaram é o login funcionar ([6.5](#65-login-no-painel) e [6.6](#66-post-apilogin-com-curl)).

**Ruído esperado:** a linha `Cannot load library libgssapi_krb5.so.2` no começo do log é só aviso do driver do PostgreSQL (handoff 94).

**Confira (ADR-0002 §16, item 3): o pre-deploy roda em shell ou em forma exec?** A documentação não diz [F7]. O comando foi escrito para funcionar nos dois casos (não usa expansão de shell, e a aplicação só procura a palavra `migrate` nos argumentos).

- Erro como `tini: not found`, `executable file not found` ou `No such file or directory` → a Railway não está resolvendo o `tini` pelo `PATH`. Troque, **por PR** em `infra/railway/modules/ambiente/main.tf`, o comando para `/usr/bin/tini -- dotnet Helinho.Web.dll migrate`. Não edite o pre-deploy no painel: o próximo `apply` desfaz.
- O pre-deploy **não termina** e estoura os 300 s → o comando não foi reconhecido e a aplicação subiu como servidor web dentro do pre-deploy. Guarde o log e abra uma Issue.

**Se der errado:**

- `Defina a variável de ambiente HELINHO_SEED_PASSWORD…` → a variável não chegou ao serviço; confira a aba **Variables** e o `terraform plan`.
- Erro de conexão com o banco (`Failed to connect`, `password authentication failed`) → veja se o `Postgres` está com deploy verde e se `ConnectionStrings__Default` existe no web. A senha é resolvida pela Railway por referência; não a digite no painel.

### 6.4 `/health/ready`

No terminal do Terraform (ou em qualquer terminal com `terraform` e `jq`):

```sh
BASE=$(terraform -chdir=infra/railway output -json url_publica | jq -r .homologacao)
```

```sh
curl -sS -o /dev/null -w '%{http_code}\n' "$BASE/health/ready"
```

**✔ Confira:** imprime `200`. O corpo (`curl -sS "$BASE/health/ready"`) é `{"status":"ok"}`.

**Se der errado:**

| Resposta | Causa provável | O que fazer |
|---|---|---|
| `503`, corpo `{"status":"unavailable"}` | A aplicação está no ar e não alcança o banco | Veja o deploy do `Postgres`; depois, **Redeploy** do web |
| `404` | O commit publicado não tem a rota (PR #104) | Mergeie o PR (o merge publica sozinho) e repita a conferência |
| `502` ou página "Application failed to respond" | Não há deploy ativo, ou a aplicação não escuta em 8080 | Volte ao [6.1](#61-ver-em-que-estado-o-helinho-web-ficou) |
| `307` em laço (`curl: (47) Maximum redirects`) em outras rotas | `Proxy__TrustForwardedHeaders` não chegou ao serviço | Confira a aba **Variables** e o `terraform plan` |

O healthcheck da Railway só é consultado **durante o deploy**; depois que o deploy fica ativo ninguém monitora a rota [F10]. Monitoramento contínuo não existe nesta sprint.

### 6.5 Login no painel

1. Abra no navegador a URL que este comando imprime:

```sh
echo "$BASE/entrar"
```

2. Usuário: `master@helinho.example`. Senha: a do seed, do gerenciador de senhas.

**✔ Confira:** entra no painel, com menu lateral, nome, papel "master" e o Dashboard com dados sintéticos (2 unidades, 10 alunos). O endereço é `https://` e o navegador mostra o cadeado.

**Se der errado:**

- "Usuário ou senha inválidos" → o seed não rodou (volte ao [6.3](#63-conferir-o-pre-deploy-migrate)) ou a senha colada não é a do `TF_VAR_seed_password`.
- A conta bloqueou por tentativas erradas → espere 15 minutos.
- Depois de um deploy novo você é deslogado → o commit publicado não persiste as chaves do Data Protection. É esperado enquanto a decisão D4 estiver aberta ou se ela for a opção D ([2.1](#21-conferir-que-a-main-tem-tudo)).

### 6.6 `POST /api/login` com `curl`

O jogo entra por matrícula, usuário ou e-mail no campo `login`. Use o aluno `2026001` (os alunos 03 e 08 estão com a aprovação do responsável pendente e respondem 403).

```sh
read -rs HELINHO_PASS && export HELINHO_PASS
```

Cole a senha do seed e aperte Enter. O comando abaixo monta o JSON com o `jq` (a senha tem símbolos e quebraria um JSON montado à mão) e mostra só o tamanho do token. A senha **não entra na linha de comando**: o `jq` a lê da variável de ambiente (`env.HELINHO_PASS`), então ela não aparece na lista de processos nem no histórico.

```sh
jq -n --arg l 2026001 '{login:$l,password:env.HELINHO_PASS}' | curl -sS -X POST "$BASE/api/login" -H 'Content-Type: application/json' --data-binary @- | jq '{user_id, expires_at, token_chars: (.token | length)}'
```

**✔ Confira:** um objeto com `user_id`, `expires_at` cerca de 8 horas à frente (em UTC) e `token_chars` maior que zero.

**Se der errado:**

| Resposta | Causa |
|---|---|
| `401` com `invalid_credentials` | Senha errada, ou o seed não rodou |
| `403` com `forbidden` | Aluno não aprovado (03 ou 08), ou login de administrador ou master: a API do jogo só aceita aluno |
| `429` | Mais de 10 tentativas por minuto do mesmo IP; espere um minuto |
| `jq: error` ou HTML no lugar de JSON | `$BASE` vazio ou errado; refaça o [6.4](#64-healthready) |

```sh
unset HELINHO_PASS
```

### 6.7 Conferir o comportamento atrás do proxy

Estes pontos nunca foram verificados em ambiente real (ADR-0002 §8, handoff 97).

```sh
curl -sS -D - -o /dev/null "$BASE/entrar" | grep -i -e '^HTTP' -e strict-transport-security -e set-cookie
```

**✔ Confira:** status `200`; `strict-transport-security: max-age=31536000`; e todo `set-cookie` traz o atributo `secure`.

Agora o limite de taxa do login, com um IP forjado diferente a cada chamada. O login `nao-existe` não é de ninguém, então nenhuma conta é bloqueada.

```sh
for i in $(seq 1 12); do curl -sS -o /dev/null -w '%{http_code} ' -X POST "$BASE/api/login" -H 'Content-Type: application/json' -H "X-Real-IP: 203.0.113.$i" -d '{"login":"nao-existe","password":"x"}'; done; echo
```

**✔ Confira:** as primeiras respostas são `401` e as últimas são `429` (o limite é de 10 por minuto por IP, e o login do 6.6 conta se foi feito no mesmo minuto). Isso prova que o `X-Real-IP` forjado **não decide** em qual balde a chamada cai: a borda da Railway o sobrescreveu.

**Se der errado:** doze `401` → o cabeçalho forjado chegou à aplicação, e o limite de taxa pode ser burlado. **Não ponha dado real nesse ambiente**, abra uma Issue com a label `security:sensitive` e avise o revisor de segurança. Homologação com dado sintético pode continuar.

O teste acima ainda **não prova que o limite é por cliente**: o resultado seria o mesmo se todas as chamadas do mundo caíssem em um balde só, o do IP do proxy. Para separar os dois casos, **em menos de um minuto** depois do laço (enquanto a sua rede ainda recebe `429`), troque de rede (o 4G do celular como roteador, por exemplo) e faça **uma** chamada:

```sh
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$BASE/api/login" -H 'Content-Type: application/json' -d '{"login":"nao-existe","password":"x"}'
```

**✔ Confira:** `401`. A outra rede tem o seu próprio balde: o limite é por IP de cliente.

**Se der errado:**

- `429` na outra rede → todos os clientes dividem o mesmo balde (a aplicação está vendo o IP do proxy, não o do cliente). Dez tentativas de qualquer pessoa bloqueiam o login de todo mundo por um minuto. Abra uma Issue com a label `security:sensitive`; homologação pode continuar.
- Você demorou mais de um minuto para trocar de rede → a janela zerou e o `401` não prova nada. Repita o laço e a troca.

### 6.8 No primeiro merge na `main`: o deploy saiu sozinho?

Só dá para conferir depois do próximo merge na `main` (ADR-0002 §7.5 e §16, item 1).

1. Mergeie um PR qualquer, já revisado.
2. Painel → `helinho-web` → aba **Deployments**: um deploy novo deve aparecer sozinho, com o título do commit.

**✔ Confira:** o deploy novo passa por build, pre-deploy e healthcheck e fica ativo; repita o [6.4](#64-healthready).

**Se der errado:** o push não gerou deploy → confira, em **Show Skipped** [F5], se ele foi marcado como ignorado. Se foi, o watch pattern ainda é o de "desligado": reveja o `auto_deploy = true` ([4.1](#41-criar-o-terraformtfvars)) e rode `terraform plan`. Se nem aparece como ignorado, confira a GitHub App ([Passo 3](#passo-3--github-app-da-railway-no-repositório)) e o botão **Wait for CI** ([5.8](#58-ligar-wait-for-ci)) e abra uma Issue.

---

## Passo 7 — URL para o desenvolvedor Unity e registro do ambiente

### 7.1 Pegar a URL base da API

```sh
terraform -chdir=infra/railway output -json url_api | jq -r .homologacao
```

**✔ Confira:** imprime `https://<subdomínio>.up.railway.app/api`, **já com `/api`** e sem barra no fim. É essa a URL base do jogo: `POST <url>/login`, `GET <url>/profile` e as demais rotas de `docs/context/rotas-api.md`.

### 7.2 Preencher `docs/operations/ambientes.md`

Abra o arquivo e preencha os campos marcados com `<a preencher>`: URL do painel, URL da API, data do `apply`, plano da conta e o estado do auto-deploy (ligado, ou o plano B do botão **Disable**, se foi usado). **Não escreva senha, token nem id de workspace** nesse arquivo. Commit em PR `docs` próprio.

**✔ Confira:** `git diff docs/operations/ambientes.md` mostra só URL, datas e texto; nenhuma senha.

### 7.3 Passar a credencial ao desenvolvedor Unity

- **URL base:** a do 7.1.
- **Login de teste:** matrícula `2026001` (ou `2026002`, `2026004`…; evite `2026003` e `2026008`, que respondem 403 de propósito).
- **Senha:** a do seed, enviada por **item compartilhado do gerenciador de senhas** ou outro canal cifrado combinado. Nunca por e-mail, chat aberto, Issue ou PR.

**Atenção:** todos os usuários do seed têm a **mesma senha**. Ao entregar a senha do aluno você entrega também a do `master` de homologação. Isso só é aceitável porque o ambiente tem apenas dado sintético. Se alguém que recebeu a senha deixar o projeto, troque-a ([8.7](#87-rotação-das-senhas)).

**✔ Confira:** o desenvolvedor Unity confirma um `POST /login` com resposta 200 a partir da máquina dele.

---

## Passo 8 — Operação

### 8.1 O que dispara um deploy e como conferir

Com `auto_deploy = true` (D3, decisão do Henrique em 22/11/2026), a publicação é automática. Disparam um deploy do `helinho-web`:

- **Merge (push) na `main`.** Qualquer commit, inclusive só de documentação: o watch pattern está vazio, então nada é filtrado.
- **`terraform apply` com mudança** no serviço ou nas variáveis dele (veja o primeiro item abaixo).
- **Ação manual no painel:** **Deploy Latest Commit** ou **Redeploy** [F6].

O que acontece em cada deploy: build da imagem (`Dockerfile` da raiz) → **pre-deploy `migrate`** (aplica as migrações pendentes e, com `Seed__Enabled=true`, roda o seed sintético, em contêiner separado e sem volume [F7]) → healthcheck em `/health/ready` → deploy ativo. Se o `migrate` sair com código diferente de zero ou o healthcheck falhar, o deploy não prossegue e **a versão anterior continua no ar** [F7]. Por isso toda migração precisa ser compatível com a versão anterior (expand/contract): ela já rodou quando o healthcheck falha, e o rollback não a desfaz.

> **Aviso:** o Wait for CI **não espera build nem testes** (Issue #225 aberta; [5.8](#58-ligar-wait-for-ci)). O Henrique aceitou isso em homologação em 22/11/2026. A única barreira antes do deploy é a proteção da `main`.

Como ver o resultado, depois de cada merge:

1. Painel → `helinho-web` → aba **Deployments**: o deploy novo aparece sozinho. Acompanhe até **verde** (ativo) ou falha.
2. Repita as conferências [6.3](#63-conferir-o-pre-deploy-migrate) e [6.4](#64-healthready).
3. Se ficou **vermelho**, a versão anterior segue ativa; corrija por PR. Se ficou verde mas **quebrado** (por exemplo `/health/ready` ok e o painel com erro), faça o rollback do [8.2](#82-rollback-para-o-deploy-anterior).

Como desligar o auto-deploy: [Deixar a homologação parada](#deixar-a-homologação-parada-para-o-teste-do-jogo). O caminho de **publicação manual** (**Deploy Latest Commit**, `Cmd+K`) é o usado com `auto_deploy = false`, o padrão da variável e o previsto para produção.

Quatro coisas para lembrar sobre o `terraform apply`:

- **Todo `terraform apply` com mudança é um deploy** do serviço alterado: o provider reimplanta quando muda o serviço ou as variáveis dele. O do `Postgres` tem indisponibilidade curta, porque serviço com volume não troca de versão sem parar [F12]. Rode `apply` em horário combinado.
- Mudou algo no painel à mão? O Terraform não sabe. Rode `terraform plan` e traga a mudança para o código, ou desfaça no painel.
- Em qualquer `plan`: **nunca** aceite `destroy` ou `replace` em `railway_project.this` ou `railway_service.postgres`. O `prevent_destroy` do `Postgres` faz o `plan` falhar em vez de apagar o banco; se falhar por isso, pare e entenda por quê (mudar `workspace_id`, o nome do ambiente ou o `project_id` recria tudo).
- **O `prevent_destroy` não protege o volume.** No provider 0.6.3, remover (ou trocar) o bloco `volume` do serviço `Postgres` aparece no `plan` como **alteração no lugar** (`~ update in-place`), não como destruição, e **apaga o volume com os dados**. Por isso, em todo `plan`, confira que `railway_service.postgres` **não aparece como alterado**. Se a alteração for intencional (trocar `postgres_imagem`, por exemplo), faça antes um `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)) e um backup de volume ([8.3](#83-backup-de-volume-da-railway)), e leia linha por linha o que muda dentro de `volume`: se o bloco `volume` aparecer no diff, **não aplique**.

### 8.2 Rollback para o deploy anterior

**Quando usar:** um commit quebrado foi publicado (o auto-deploy publica sem testar, [5.8](#58-ligar-wait-for-ci)). O passo mais rápido é **redeploy do deploy anterior pelo painel da Railway**, abaixo. Depois, corrija por PR (reverter o commit é um PR comum); o merge desse PR publica de novo. **Confira:** a documentação não diz se o Rollback pausa o auto-deploy; o próximo merge publica de qualquer forma, então não mergeie nada enquanto o rollback valer.

1. Painel → `helinho-web` → aba **Deployments**.
2. No deploy anterior que estava bom, clique nos **três pontos** no fim da linha → **Rollback** → confirme [F6].

**✔ Confira:** o deploy escolhido volta a ser o ativo e `curl` em `/health/ready` responde 200.

O que o rollback faz e não faz:

- Restaura a **imagem** e as **variáveis** daquele deploy [F6].
- **Não desfaz migração.** O banco fica no esquema novo. Por isso toda migração precisa ser compatível com a versão anterior (expand/contract).
- Só existe enquanto a imagem está guardada: **72 horas no Hobby, 120 no Pro** depois de o deploy ser removido [F17]. Passado o prazo a opção **Rollback** some [F6]; resta **Redeploy**, no mesmo menu, que reconstrói a partir do código [F6].
- Como as variáveis voltam ao valor antigo, o Terraform pode ver diferença: rode `terraform plan` depois e decida se reaplica.

**Se der errado:** a versão antiga não sobe por causa do esquema novo → o caminho é para a frente: corrija por PR e publique (o merge dispara o deploy, [8.1](#81-o-que-dispara-um-deploy-e-como-conferir)). Se os dados estiverem estragados, restaure o banco ([8.5](#85-restore-do-banco)).

### 8.3 Backup de volume da Railway

É o backup rápido, dentro da plataforma. Serve antes de migração arriscada e antes de rotação de senha.

1. Painel → serviço `Postgres` → aba **Backups** [F11].
2. Crie um backup manual. A documentação diz que o backup manual existe, mas não dá o nome do botão (o nome pode variar) [F11].

**✔ Confira:** o backup aparece na lista da aba **Backups**, com a data e a hora de agora.

**Confira (não confirmado):** a documentação não diz em quais planos a aba **Backups** existe [F11][F17]. **Se ela não aparecer no seu plano**, o `pg_dump` do [8.4](#84-pg_dump-para-fora-da-plataforma) deixa de ser o segundo nível e passa a ser a **salvaguarda obrigatória**: faça-o em todo lugar onde este runbook pede "backup de volume" ([8.5](#85-restore-do-banco) e [8.7](#87-rotação-das-senhas)), e o caminho A do restore não existe para você.

Limites, todos da documentação [F11]:

- Só restaura **no mesmo projeto e ambiente**. Não protege contra apagar o projeto.
- Backup manual é limitado a 50% do tamanho total do volume.
- Apagar o conteúdo do volume ("wipe") apaga **todos** os backups dele.
- É incremental e cobrado como volume.
- A agenda (diária, semanal, mensal) é um clique nessa mesma aba ([Passo 9](#passo-9--o-que-fica-para-depois)).

### 8.4 `pg_dump` para fora da plataforma

É o backup que sobrevive à perda do projeto e à perda do volume. **Faça um antes de qualquer `apply` cujo plano toque o serviço `Postgres`** ([8.1](#81-o-que-dispara-um-deploy-e-como-conferir)), antes de rotação de senha e antes de destruir. O banco não tem domínio público nem TCP proxy, então a conexão passa por um **túnel SSH** até o contêiner do `Postgres`.

**Caminho escolhido:** `ssh` nativo com redirecionamento de porta, que a documentação da Railway descreve [F13]. A conexão é aberta de dentro do contêiner, e o redirecionamento só alcança o próprio contêiner e a rede privada do projeto [F13].

**Por que não `railway connect Postgres --tunnel-only`:** o ADR-0002 §11 já descarta esse comando neste desenho, pelos mesmos motivos. O código da CLI lê as credenciais da variável `DATABASE_URL` (ou `DATABASE_PUBLIC_URL`) do serviço [F14], e o Terraform deste repositório define `PGHOST`, `PGPORT`, `PGUSER`, `PGPASSWORD` e `PGDATABASE`, **não** `DATABASE_URL`: o comando falharia por variável ausente. Além disso, ele imprime a senha do banco no terminal [F14].

**Por que não um TCP proxy temporário:** expõe o banco à internet e cobra tráfego de saída (ADR-0002 §14). Não crie, nem "só por cinco minutos".

**Riscos do túnel:** quem tem chave SSH registrada e acesso ao workspace vira administrador do banco; a porta local fica aberta na sua máquina enquanto o túnel existir (só em `127.0.0.1`); o arquivo de dump contém hashes de senha e, se a decisão D4 for a opção A, as chaves do Data Protection. Feche o túnel ao terminar e cifre o dump.

#### Preparar (uma vez)

Em um **terminal da CLI** (novo, sem `RAILWAY_TOKEN`):

```sh
echo "${RAILWAY_TOKEN:+RAILWAY_TOKEN esta definida: feche este terminal}"
```

**✔ Confira:** não imprime nada.

```sh
brew install railway libpq
```

```sh
railway login
```

**✔ Confira:** o navegador abre, você autoriza, e `railway whoami` mostra o seu usuário [F3].

```sh
"$(brew --prefix libpq)/bin/pg_dump" --version
```

**✔ Confira:** `pg_dump (PostgreSQL) 17.x` ou maior. O cliente não pode ser mais antigo que o servidor (17).

Registre a sua chave SSH pública na Railway (escolha uma das chaves de `~/.ssh` quando a CLI perguntar) [F13]:

```sh
railway ssh keys add
```

**✔ Confira:** `railway ssh keys` lista a chave.

**Se der errado:** você não tem chave em `~/.ssh` → crie uma com `ssh-keygen -t ed25519` e repita.

#### Abrir o túnel

1. Painel → projeto **`helinho-homologacao`** (confira o nome no topo da página) → serviço `Postgres` → `Cmd+K` → **Copy Service Instance ID**. Atenção: **Copy Service ID** é outro valor e **não** serve como destino de SSH [F13].
2. No terminal da CLI, guarde o id (não é segredo):

```sh
read -r INSTANCIA
```

3. Abra o túnel. A porta local 5433 passa a apontar para a 5432 de dentro do contêiner do `Postgres`:

```sh
ssh -N -L 5433:127.0.0.1:5432 "$INSTANCIA@ssh.railway.com"
```

**✔ Confira:** o comando **fica parado**, sem mensagem de erro. É o túnel aberto; deixe esse terminal assim.

**Se der errado:**

- `Permission denied (publickey)` → a chave não está registrada (`railway ssh keys`) ou o `ssh` está oferecendo outra chave; acrescente `-i ~/.ssh/<sua-chave>`.
- Erro dizendo que o destino não existe → você copiou o Service ID no lugar do Service **Instance** ID.
- **Confira (não executado):** se o `ssh` recusar o redirecionamento de porta, use o plano B no fim desta seção.

#### Conferir que o túnel aponta para a homologação

Todo comando deste runbook que **apaga ou sobrescreve dados** pelo túnel ([8.5](#85-restore-do-banco), caminho B, e [8.7](#87-rotação-das-senhas)) começa por esta conferência. Em **outro** terminal (também sem `RAILWAY_TOKEN`), com o túnel aberto:

```sh
"$(brew --prefix libpq)/bin/psql" -h 127.0.0.1 -p 5433 -U helinho -d helinho -Atc "select current_database(), count(*) filter (where \"Email\" not like '%@helinho.example') from \"AspNetUsers\""
```

**✔ Confira:** imprime `helinho|0`: o banco é o `helinho` e **nenhum** usuário tem e-mail fora do domínio sintético `helinho.example`.

**Se der errado:**

- O número depois da barra **não é `0`** → há usuário que o seed não criou. Ou este não é o banco de homologação, ou alguém cadastrou contas pelo painel. **Pare:** não rode nada destrutivo antes de entender de onde vêm essas contas.
- `relation "AspNetUsers" does not exist` → o banco está vazio (o `migrate` nunca rodou nele). Para um restore isso é esperado; confira no painel que o Service Instance ID veio do `Postgres` do projeto `helinho-homologacao`.
- Pede senha → veja a nota sobre senha, logo abaixo.

Para o `pg_dump`, que só lê, a conferência é opcional.

#### Fazer o dump

No mesmo terminal da conferência (sem `RAILWAY_TOKEN`). O `umask 077` faz todo arquivo criado neste terminal nascer legível só por você:

```sh
umask 077
```

```sh
DUMP="$HOME/.helinho-infra/helinho-homologacao-$(date +%Y%m%d-%H%M).dump"
```

```sh
"$(brew --prefix libpq)/bin/pg_dump" -h 127.0.0.1 -p 5433 -U helinho -d helinho -Fc -f "$DUMP"
```

**Confira:** a imagem oficial do PostgreSQL costuma aceitar conexão local sem senha, então o comando pode nem pedir. **Se pedir senha:** painel → `Postgres` → aba **Variables** → `PGPASSWORD` (o gesto para revelar o valor pode variar [F21]); copie e cole no prompt do `pg_dump`. Não ponha a senha na linha de comando.

```sh
"$(brew --prefix libpq)/bin/pg_restore" --list "$DUMP" | grep -c 'TABLE DATA'
```

**✔ Confira:** imprime um número maior que zero (uma linha por tabela com dados; as do Identity e `__EFMigrationsHistory` estão entre elas). **Anote o número.** `0` ou erro de formato = dump inválido; repita.

Feche o túnel com `Ctrl+C` no primeiro terminal. Cifre o dump:

```sh
age -p -o "$DUMP.age" "$DUMP"
```

**Antes de apagar o original**, prove que o arquivo cifrado existe e abre com a frase que você digitou:

```sh
age -d "$DUMP.age" | "$(brew --prefix libpq)/bin/pg_restore" --list | grep -c 'TABLE DATA'
```

**✔ Confira:** pede a frase-senha e imprime **o mesmo número** de antes.

**Se der errado:** o `age -p` reclamou que as duas frases não conferem, o arquivo `.age` não existe, ou a conferência não imprime o mesmo número → **não apague o dump.** Apague o `.age` defeituoso (`rm "$DUMP.age"`) e cifre de novo.

Só depois da conferência:

```sh
rm "$DUMP"
```

Guarde o `.age` fora da máquina, junto do backup do estado ([8.9](#89-onde-fica-o-estado-do-terraform)).

#### Plano B: dump dentro do contêiner e cópia por `scp`

Use só se o túnel não abrir. Os comandos usam recursos documentados [F13]. Troque `<id do projeto>` pelo `projeto_id` de `terraform output ambientes`.

O terminal em que você leu `INSTANCIA` era o do túnel, que falhou ou foi fechado. **Neste terminal, leia de novo** (cole o Service Instance ID do `Postgres`):

```sh
read -r INSTANCIA
```

```sh
umask 077
```

```sh
DUMP="$HOME/.helinho-infra/helinho-homologacao-$(date +%Y%m%d-%H%M).dump"
```

1. Gere o dump dentro do contêiner:

```sh
railway ssh --project <id do projeto> --environment homologacao --service Postgres -- pg_dump -U helinho -d helinho -Fc -f /tmp/helinho.dump
```

2. Copie para a sua máquina:

```sh
scp "$INSTANCIA@ssh.railway.com:/tmp/helinho.dump" "$DUMP"
```

3. Apague o dump do contêiner. **Rode este comando mesmo se o `scp` falhar:** o dump em claro não pode ficar no contêiner.

```sh
railway ssh --project <id do projeto> --environment homologacao --service Postgres -- rm -f /tmp/helinho.dump
```

```sh
railway ssh --project <id do projeto> --environment homologacao --service Postgres -- ls /tmp/helinho.dump
```

**✔ Confira:** o último comando responde `No such file or directory`.

4. Daqui em diante é igual ao caminho normal: `pg_restore --list "$DUMP"`, `age -p`, a conferência do arquivo cifrado e só então o `rm "$DUMP"`.

### 8.5 Restore do banco

Dois caminhos. Antes de qualquer um, faça um backup de volume do estado atual ([8.3](#83-backup-de-volume-da-railway)) ou, se a aba **Backups** não existir no seu plano, um `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)), para poder voltar atrás. Nos dois, confira no painel que o projeto aberto é o **`helinho-homologacao`** antes de restaurar.

#### A. Restaurar um backup de volume (mesmo projeto e ambiente)

1. Painel → `Postgres` → aba **Backups** → ache o backup pela data → **Restore** [F11].
2. A Railway prepara a mudança como alteração pendente: clique em **Details**, no topo do projeto, para revisar, e em **Deploy** para concluir [F11][F20].
3. O serviço é reimplantado com um **volume novo** montado no mesmo caminho; o volume antigo continua existindo, desmontado [F11].

**✔ Confira:** o `Postgres` volta com deploy verde; faça **Redeploy** do `helinho-web` e repita [6.4](#64-healthready) e [6.5](#65-login-no-painel).

**Confira (não confirmado): o Terraform depois do restore.** O volume novo recebe como nome a data do backup, e o código declara o volume `postgres-dados` [F11]. Rode `terraform plan` **sem aplicar**. Se ele mostrar **qualquer** mudança em `railway_service.postgres`, inclusive só o nome do volume, **não aplique**: alteração no bloco `volume` pode apagar o volume restaurado ([8.1](#81-o-que-dispara-um-deploy-e-como-conferir)). Guarde a saída e abra uma Issue para alinhar o código ao que está na Railway.

#### B. `pg_restore` de um dump (qualquer banco vazio ou o próprio)

O `--clean` **apaga os objetos existentes** antes de recriá-los. Entre o passo 1 e o passo 7 o painel e a API ficam fora do ar.

1. **Tire o `helinho-web` do ar primeiro:** painel → `helinho-web` → aba **Deployments** → três pontos do deploy ativo → **Remove** [F6]. Com a aplicação conectada, o `pg_restore --clean` tende a ficar **parado**, esperando as travas das tabelas em uso, em vez de dar erro.

**✔ Confira:** o serviço `helinho-web` fica sem deploy ativo e `curl` em `/health/ready` deixa de responder `200`.

2. Decifre o dump (o `umask 077` deixa o arquivo decifrado legível só por você):

```sh
umask 077
```

```sh
age -d -o "$HOME/.helinho-infra/<arquivo>.dump" "$HOME/.helinho-infra/<arquivo>.dump.age"
```

3. Abra o túnel como no [8.4](#84-pg_dump-para-fora-da-plataforma), conferindo no painel que o Service Instance ID é o do `Postgres` do projeto **`helinho-homologacao`**.
4. **Confira o alvo** com o comando de "Conferir que o túnel aponta para a homologação" ([8.4](#84-pg_dump-para-fora-da-plataforma)). Só siga com `helinho|0` (ou com o banco vazio, se o restore é em banco novo).
5. Restaure por cima:

```sh
"$(brew --prefix libpq)/bin/pg_restore" -h 127.0.0.1 -p 5433 -U helinho -d helinho --clean --if-exists --no-owner "$HOME/.helinho-infra/<arquivo>.dump"
```

**✔ Confira:** o `pg_restore` termina sozinho, sem linhas `error:`.

6. Feche o túnel. Confira que o cifrado continua lá e apague o dump decifrado:

```sh
ls -l "$HOME/.helinho-infra/<arquivo>.dump.age"
```

```sh
rm "$HOME/.helinho-infra/<arquivo>.dump"
```

7. Publique de novo: `helinho-web` → `Cmd+K` → **Deploy Latest Commit**.

**✔ Confira:** deploy verde; `/health/ready` responde `200` ([6.4](#64-healthready)); login no painel funciona ([6.5](#65-login-no-painel)).

**Se der errado:**

- O `pg_restore` fica parado → ainda há conexão da aplicação: confira que o deploy do `helinho-web` foi mesmo removido, interrompa com `Ctrl+C` e repita o passo 5.
- O dump é **mais antigo** que o código publicado → o banco fica no esquema do dump, e o `migrate` do deploy do passo 7 traz o esquema para a frente.

Homologação só tem dado sintético: se o restore complicar, destruir e recriar ([8.8](#88-destruir-o-ambiente)) é um caminho legítimo.

### 8.6 Rotação do token da Railway

Quando: suspeita de vazamento (o valor apareceu em tela compartilhada, histórico, log ou chat), troca de máquina, saída de alguém com acesso, e antes de ligar produção.

1. Crie um token novo, como no [2.5](#25-criar-o-token-de-workspace).
2. No terminal do Terraform:

```sh
read -rs RAILWAY_TOKEN && export RAILWAY_TOKEN
```

3. Prove que o token novo funciona:

```sh
terraform -chdir=infra/railway plan
```

**✔ Confira:** `No changes. Your infrastructure matches the configuration.`

4. Em `https://railway.com/account/tokens`, **apague o token antigo** (o nome do botão pode variar) [F1].
5. Atualize a entrada do gerenciador de senhas.

**Se der errado:** o `plan` falha com erro de autenticação → o token novo é de outro workspace ou de projeto; **não apague o antigo** antes de o `plan` passar.

### 8.7 Rotação das senhas

#### Senha do seed (usuários sintéticos)

**Fato conferido no código** (`PeopleSeeder.EnsureUserAsync`): o seed **não regrava** a senha de usuário que já existe. Trocar `TF_VAR_seed_password` e reimplantar muda só a variável; os usuários continuam com a senha antiga. Para a senha nova valer, os usuários precisam ser recriados, e o jeito simples em homologação é zerar o banco:

1. Gere a senha nova no gerenciador ([4.2](#42-gerar-a-senha-do-seed)).
2. No terminal do Terraform, exporte a nova (`read -rs TF_VAR_seed_password && export TF_VAR_seed_password`), rode `plan` (esperado: 1 mudança, em `railway_variable_collection.web`) e `apply`. O `apply` reimplanta o web.
3. Faça um backup de volume ([8.3](#83-backup-de-volume-da-railway)) ou, se a aba não existir no seu plano, um `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)).
4. Abra o túnel do [8.4](#84-pg_dump-para-fora-da-plataforma), conferindo no painel que o Service Instance ID é o do `Postgres` do projeto **`helinho-homologacao`**, e **confira o alvo** com o comando de "Conferir que o túnel aponta para a homologação". Só siga com `helinho|0`.
5. Zere o esquema. **Isso apaga todos os dados do banco em que o túnel está ligado, sem pedir confirmação:**

```sh
"$(brew --prefix libpq)/bin/psql" -h 127.0.0.1 -p 5433 -U helinho -d helinho -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'
```

6. Feche o túnel e faça **Deploy Latest Commit** no `helinho-web`: o `migrate` recria as tabelas e o seed recria os usuários com a senha nova.
7. Repita [6.5](#65-login-no-painel) e [6.6](#66-post-apilogin-com-curl) com a senha nova; repasse-a a quem usa ([7.3](#73-passar-a-credencial-ao-desenvolvedor-unity)); faça o backup do estado ([5.7](#57-backup-cifrado-do-estado)).

**✔ Confira:** a senha antiga responde `401` e a nova entra.

Isso só é aceitável porque o dado é sintético. Nunca faça em ambiente com dado real.

#### Senha do banco

A senha é gerada pelo Terraform. Trocar tem uma armadilha: a imagem oficial do PostgreSQL só lê `POSTGRES_PASSWORD` **na criação do volume**. Mudar a variável não muda a senha do usuário que já existe. **Este procedimento nunca foi executado**; ele tem alguns minutos de indisponibilidade.

1. Backup de volume ([8.3](#83-backup-de-volume-da-railway)) e `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)).
2. Gere a senha nova no Terraform:

```sh
terraform -chdir=infra/railway plan -replace='module.ambiente["homologacao"].random_password.postgres' -out="$HOME/.helinho-infra/rotacao.tfplan"
```

**✔ Confira:** o plano substitui `random_password.postgres` e altera `railway_variable_collection.postgres`. `railway_service.postgres` e `railway_project.this` **não aparecem** no plano, nem como alteração, e todos os endereços começam por `module.ambiente["homologacao"]`. O `apply` do passo 3 usa esse plano salvo e **não pede confirmação**.

3. Aplique (`terraform -chdir=infra/railway apply "$HOME/.helinho-infra/rotacao.tfplan"`) e apague o arquivo de plano. O `Postgres` reinicia, mas a senha **de dentro do banco continua a antiga**. Um deploy novo do web falharia agora, no pre-deploy, porque já receberia a senha nova.

   **Confira (não confirmado):** o esperado é que o web que está no ar siga funcionando com a senha antiga, porque o Terraform não muda nenhuma variável **dele** (a connection string só faz referência à do `Postgres`). Não se sabe se a Railway reimplanta o web sozinha quando muda uma variável referenciada. Olhe a aba **Deployments** do `helinho-web`: se aparecer um deploy novo que **falhou no pre-deploy**, é esse caso; a versão anterior continua no ar [F7] e o procedimento segue igual, a partir do passo 4. Se o web cair, siga também: ele volta no passo 6.
4. Leia a senha nova no painel (`Postgres` → **Variables** → `POSTGRES_PASSWORD`) e copie.
5. Abra o túnel do [8.4](#84-pg_dump-para-fora-da-plataforma), conferindo no painel que o Service Instance ID é o do `Postgres` do projeto **`helinho-homologacao`**. Confira o alvo com o comando de "Conferir que o túnel aponta para a homologação" (só siga com `helinho|0`) e troque a senha dentro do banco. O `\password` pede a senha duas vezes sem mostrar na tela:

```sh
"$(brew --prefix libpq)/bin/psql" -h 127.0.0.1 -p 5433 -U helinho -d helinho -c '\password helinho'
```

6. Feche o túnel e faça **Deploy Latest Commit** no `helinho-web`. Entre o passo 5 e o fim deste deploy, o painel e a API ficam fora do ar.
7. Repita [6.4](#64-healthready) e [6.5](#65-login-no-painel); faça o backup do estado ([5.7](#57-backup-cifrado-do-estado)).

**Se der errado**, ou se você preferir não arriscar: em homologação, destruir e recriar o ambiente ([8.8](#88-destruir-o-ambiente)) gera senha nova do banco sem armadilha.

### 8.8 Destruir o ambiente

Destruir apaga o banco e o volume. Antes: `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)) se algum dado importar, e backup do estado ([5.7](#57-backup-cifrado-do-estado)).

O banco também pode ser apagado **sem** você pedir uma destruição: uma alteração no bloco `volume` do serviço `Postgres` apaga o volume, e o `prevent_destroy` não impede ([8.1](#81-o-que-dispara-um-deploy-e-como-conferir)).

**Só quer parar de gastar por umas semanas?** Não destrua: remova o deploy dos dois serviços (três pontos do deploy → **Remove** [F6]). Sem deploy ativo, só o volume é cobrado (`docs/operations/custos.md`). Para voltar, publique de novo.

#### Antes de qualquer um dos dois caminhos: conferir o alvo

Os dois caminhos apagam **tudo o que está no estado**, sem volta. No terminal do Terraform:

```sh
terraform -chdir=infra/railway output -json ambientes | jq -c 'keys'
```

**✔ Confira:** imprime exatamente `["homologacao"]`.

```sh
terraform -chdir=infra/railway state list
```

**✔ Confira:** sete linhas, **todas** começando por `module.ambiente["homologacao"]`.

**Se der errado:** aparece `producao`, ou qualquer endereço fora de `module.ambiente["homologacao"]` → **pare.** Este procedimento destruiria também o que não é homologação; ele só vale para um estado que contém apenas a homologação.

Confira também que o backup cifrado do estado de **agora** existe e abre ([5.7](#57-backup-cifrado-do-estado)): depois da destruição ele é a única cópia das senhas e dos ids antigos.

#### A. Pelo Terraform (preferido: o estado fica coerente)

O serviço `Postgres` tem `prevent_destroy`. Destruir de propósito exige tirar essa proteção. Isso é feito em um **commit, numa branch local que nunca vai para a `main` nem para o GitHub**: com a mudança commitada, o `git switch main` do passo 6 devolve o `prevent_destroy` sem deixar nada pendente, e a remoção não fica esquecida como edição solta no arquivo.

1. Comece na `main`, sem nada pendente, e crie a branch:

```sh
git status --short
```

**✔ Confira:** não imprime nada.

```sh
git switch -c chore/destruir-homologacao
```

2. Em `infra/railway/modules/ambiente/main.tf`, apague o bloco `lifecycle { prevent_destroy = true }` do recurso `railway_service.postgres` (as três linhas) e faça o commit:

```sh
git commit -am "chore(infra): remove prevent_destroy para destruir a homologação"
```

```sh
grep -c prevent_destroy infra/railway/modules/ambiente/main.tf
```

**✔ Confira:** imprime `0`. **Não faça `git push` dessa branch.**

3. Planeje a destruição:

```sh
terraform -chdir=infra/railway plan -destroy -out="$HOME/.helinho-infra/destruir.tfplan"
```

**✔ Confira:** `Plan: 0 to add, 0 to change, 7 to destroy.`, e os sete endereços começam por `module.ambiente["homologacao"]`. Qualquer outro número ou endereço: **não aplique**.

4. Destrua. **Atenção:** aplicar um plano salvo **não pede confirmação**. Ao apertar Enter, o banco, o volume e o projeto são apagados.

```sh
terraform -chdir=infra/railway apply "$HOME/.helinho-infra/destruir.tfplan"
```

**✔ Confira:** `Apply complete! Resources: 0 added, 0 changed, 7 destroyed.`; o projeto `helinho-homologacao` sumiu do painel.

5. Confira que o estado ficou vazio:

```sh
terraform -chdir=infra/railway state list
```

**✔ Confira:** não imprime nada.

6. Volte para a `main` e apague a branch local. O `-D` é necessário porque o commit, de propósito, não foi mergeado:

```sh
git switch main
```

```sh
git branch -D chore/destruir-homologacao
```

```sh
grep -c prevent_destroy infra/railway/modules/ambiente/main.tf
```

**✔ Confira:** imprime `1`: a proteção voltou.

7. Limpe o que ficou em claro, seguindo "Nada fica em claro", abaixo.

**Se der errado:**

- `Instance cannot be destroyed` → o `prevent_destroy` ainda está no arquivo (o `grep` do passo 2 não imprimiu `0`).
- O `destroy` para no meio (o provider reimplanta o serviço ao apagar uma coleção de variáveis, e isso pode falhar) → **não apague o estado**. Faça um `plan -destroy` novo e aplique de novo; se insistir, use o caminho B.
- Erro de permissão ao apagar projeto ou serviço → o seu papel não é Admin [F16].
- O `grep` do passo 6 imprime `0` → você ainda está na branch errada ou editou o arquivo na `main`: `git status` e `git checkout -- infra/railway/modules/ambiente/main.tf`.

#### B. Pelo painel (se o Terraform não conseguir)

1. Confira o alvo: o id do projeto que o Terraform conhece precisa ser o do projeto aberto no painel (configurações do projeto → aba **General** [F15]), e o nome precisa ser `helinho-homologacao`.

```sh
terraform -chdir=infra/railway output -json ambientes | jq -r '.homologacao.projeto_id'
```

**✔ Confira:** o id impresso é igual ao da aba **General** do projeto que você vai apagar.

2. Painel → configurações do projeto `helinho-homologacao` → aba **Danger** → **Delete Project** [F15]. Isso apaga todos os serviços, ambientes e deploys do projeto [F15].

**✔ Confira:** o projeto sumiu da lista de projetos do workspace.

3. O estado local ficou apontando para recursos que não existem, e continua com as senhas antigas. Cifre-o:

```sh
age -p -o "$HOME/.helinho-infra/tfstate-destruido-$(date +%Y%m%d-%H%M).age" infra/railway/terraform.tfstate
```

4. Prove que o cifrado abre, trocando `<arquivo>`:

```sh
age -d "$HOME/.helinho-infra/<arquivo>.age" | jq '.resources | length'
```

**✔ Confira:** pede a frase-senha e imprime um número (`7`, se o estado estava completo).

5. Só então apague os arquivos em claro, seguindo "Nada fica em claro", abaixo.

#### Nada fica em claro

Vale para os dois caminhos, **só depois** de: o projeto ter sumido do painel, e o backup cifrado do estado ter sido aberto com sucesso. No caminho A, também só depois de o `state list` não imprimir nada.

O `terraform.tfstate.backup` é a cópia automática do estado **anterior**: depois de destruir, é ele que guarda a senha do banco e a do seed em texto. Apague os dois arquivos de estado:

```sh
rm -f infra/railway/terraform.tfstate infra/railway/terraform.tfstate.backup
```

Apague os planos salvos, que também têm os segredos em texto:

```sh
find "$HOME/.helinho-infra" -name '*.tfplan' -delete
```

Confira que não sobrou nada em claro:

```sh
find infra/railway -maxdepth 1 -name 'terraform.tfstate*'
```

```sh
find "$HOME/.helinho-infra" -type f ! -name '*.age'
```

**✔ Confira:** os dois comandos não imprimem nada. Em `~/.helinho-infra/` só ficam arquivos `.age`.

**Se der errado:** o segundo `find` lista um `.dump` ou outro arquivo → é um dump ou um estado decifrado que ficou para trás. Confira que existe o `.age` correspondente e apague o arquivo em claro.

**Depois de destruir (os dois caminhos):**

- **Confira:** a documentação diz que um volume apagado fica na fila por 48 horas e pode ser restaurado pelo link enviado por e-mail; depois disso é definitivo [F12]. Não foi confirmado se isso vale quando o volume some junto com o projeto inteiro. Não conte com essa janela: o seu backup é o `pg_dump`.
- O subdomínio `helinho-homologacao` é liberado; se outra pessoa o pegar antes de você recriar, use `subdominio` ([4.1](#41-criar-o-terraformtfvars)).
- Para recriar: [Passo 5](#passo-5--init-plan-e-apply) de novo. A senha do banco será outra; a URL só é a mesma se o subdomínio ainda estiver livre.
- Atualize `docs/operations/ambientes.md`.

### 8.9 Onde fica o estado do Terraform

Decisão D2 do ADR-0002 (§10.3): estado **local**, fora do git, com backup cifrado. É exceção à regra da casa, válida enquanto houver um único operador e só homologação.

| O quê | Onde | Observação |
|---|---|---|
| Estado em uso | `infra/railway/terraform.tfstate` | Texto, com a senha do banco e a do seed e, depois das Sprints 3 e 4, as chaves da Brevo e de IA. O `.gitignore` cobre `*.tfstate` e `*.tfstate.*` |
| Cópia automática anterior | `infra/railway/terraform.tfstate.backup` | Mesmo cuidado: também tem segredo. Depois de destruir, é ela que guarda as senhas antigas ([8.8](#88-destruir-o-ambiente), "Nada fica em claro") |
| Arquivos de plano | `~/.helinho-infra/*.tfplan` | Têm os segredos em texto. O `.gitignore` bloqueia `*.tfplan` e `tfplan` (PR #116), mas o arquivo continuaria em claro no disco: por isso ficam fora do repositório e são apagados depois do `apply` |
| Backups cifrados | `~/.helinho-infra/tfstate-<data>.age` | Um por `apply` ([5.7](#57-backup-cifrado-do-estado)) |
| Cópia fora da máquina | Um lugar que só você acessa: anexo cifrado no gerenciador de senhas ou disco externo cifrado | Sempre o arquivo `.age`, nunca o estado em texto. Não use drive compartilhado sem combinar com a contratante |
| Frase-senha do `age` | Gerenciador de senhas | Sem ela o backup não abre |

Cuidados:

- Não deixe o repositório em pasta sincronizada com nuvem (iCloud Drive, Dropbox): o estado em texto subiria junto.
- Não há lock: nunca rode dois `apply` ao mesmo tempo, e ninguém mais roda `apply`.

**Recuperar o estado de um backup:**

```sh
age -d -o infra/railway/terraform.tfstate "$HOME/.helinho-infra/<arquivo>.age"
```

```sh
terraform -chdir=infra/railway plan
```

**✔ Confira:** `No changes.` (ou só as mudanças feitas depois daquele backup).

**Perdeu o estado e não tem backup:** os recursos continuam na Railway, mas o Terraform não os conhece, e um `apply` tentaria criar tudo de novo. Em homologação, o caminho curto é apagar o projeto pelo painel ([8.8](#88-destruir-o-ambiente), caminho B) e recriar. A senha do banco gerada antes está perdida junto com o estado, mas continua legível no painel (variável `PGPASSWORD`).

Quando houver produção, segundo operador ou `apply` em CI, o estado vai para backend remoto com lock, em ADR próprio.

---

## Deixar a homologação parada para o teste do jogo

Decisão do Henrique em 22/11/2026: depois de subir tudo, a homologação fica parada para o desenvolvedor Unity testar com estabilidade. Com o auto-deploy ligado, "parada" depende de quem mergeia.

**O que não mexer enquanto o jogo é testado:**

- **Não mergeie na `main`** sem avisar o desenvolvedor Unity: cada merge publica e reinicia o `helinho-web` (e o pre-deploy pode rodar migração).
- **Não rode o `apply` do Terraform com mudança** (é um deploy; o do `Postgres` tem indisponibilidade curta).
- **Não zere o banco, não troque a senha do seed** ([8.7](#87-rotação-das-senhas)) **nem destrua o ambiente** ([8.8](#88-destruir-o-ambiente)): o desenvolvedor perde o progresso e a credencial.
- **Não mude nada pelo painel** (variáveis, rede, domínios): o Terraform não vê e o próximo `apply` desfaz.
- O domínio próprio do painel entra **depois** (`dominio_proprio`, exemplo `paineladm.example.org`); preencher reimplanta o serviço.

**Congelar de vez (estabilidade total):** desligue o auto-deploy. Com ele desligado, merges não publicam e a publicação passa a ser manual ([6.2](#62-se-não-há-deploy-bom-deploy-latest-commit)).

1. No `infra/railway/terraform.tfvars`, troque para `auto_deploy = false` (ou comente a linha: o padrão é `false`).
2. Exporte os segredos no terminal do Terraform ([4.3](#43-exportar-os-segredos-sem-deixá-los-no-histórico)) e rode o `plan`:

```sh
terraform -chdir=infra/railway plan -out="$HOME/.helinho-infra/homologacao.tfplan"
```

**✔ Confira:** `Plan: 0 to add, 1 to change, 0 to destroy.`, a mudança em `railway_service.web` e só o `watch_patterns` indo de `[]` para `["/.auto-deploy-desligado"]`. Qualquer outra mudança, ou alteração no `Postgres`: **não aplique**.

3. Aplique o plano, como no [5.4](#54-apply), e apague o arquivo do plano (`rm "$HOME/.helinho-infra/homologacao.tfplan"`).

Esse `apply` é uma mudança no serviço e, pela regra do [8.1](#81-o-que-dispara-um-deploy-e-como-conferir), reimplanta o web uma vez: faça-o em horário combinado. Depois, faça um backup do estado ([5.7](#57-backup-cifrado-do-estado)).

**Confira (ADR-0002 §16, item 1):** o contorno do watch pattern nunca foi validado em ambiente real. No primeiro merge depois do congelamento, a aba **Deployments**, com **Show Skipped** ligado [F5], deve mostrar o push como **ignorado**, e o deploy ativo continua o mesmo. Se publicou sozinho, use o plano B: botão **Disable** do auto-deploy nas configurações do serviço [F5] (o lugar exato pode variar), e registre em `ambientes.md`.

Para voltar ao auto-deploy: `auto_deploy = true`, `plan` (esperado: 1 mudança, `watch_patterns` de volta para `[]`) e o mesmo `apply`. Se usou o **Disable** do painel, religue-o também no painel.

---

## Passo 9 — O que fica para depois

Nada disto é feito agora. Cada item tem dono e condição.

| Item | Por que fica para depois | Como será feito |
|---|---|---|
| **Domínio próprio e DNS** | A contratante não definiu (C15). O Henrique decidiu em 22/11/2026 que entra depois do primeiro deploy | Variável `dominio_proprio` dentro de `ambientes` (PR #221, na branch `issue-99-terraform-esqueleto`), exemplo `paineladm.example.org`; os registros DNS saem em `terraform output dns_dominio_proprio`; passo a passo em `docs/operations/railway-dominio.md`. HSTS com `includeSubDomains` e `preload` só então |
| **Ambiente de produção** | Decisão D1 do Henrique (22/11/2026): só homologação por enquanto. Pré-requisitos de produção: Issue #225 (`validate` no commit que chega à `main`), Issue #222 (primeiro master sem o seed) e `auto_deploy` por ambiente (ADR-0002 §7.5). Lista em [`ambientes.md`](ambientes.md#o-que-falta-para-ligar-a-produção). Exige também ADR próprio para dado real, resposta sobre região e fornecedores (C14, Issue #39), teto de gasto (C04), estado separado e `Seed__Enabled=false` (ADR-0002 §5.2) | Acrescentar `producao` em `ambientes` cria um **segundo projeto** (`helinho-producao`). **Não acrescente agora.** **Confira (ADR-0002 §16, item 5):** nunca crie um segundo ambiente **dentro** do projeto de homologação, nem pelo painel: o provider só lê serviço e volume do ambiente padrão, e o comportamento com dois ambientes não foi testado |
| **Brevo (Sprint 3)** | E-mail real ainda não existe; `Email__Provider = "Fake"` | `TF_VAR_brevo_api_key` + `variaveis_web` (`Email__Provider`, `Brevo__SenderEmail`, `Brevo__SenderName`, `App__PublicBaseUrl`); runbook próprio. A chave passa a ficar em texto no estado |
| **IA (Sprint 4)** | `Ai__Provider = "Fake"` | `TF_VAR_ai_api_key` + `Ai__Provider`, `Ai__Model`; runbook próprio. A chave passa a ficar em texto no estado |
| **`railway connect` para o banco** | O ADR-0002 §11 descarta o comando neste desenho: ele exige `DATABASE_URL` no serviço `Postgres`, que o Terraform não define, e imprime a senha ([8.4](#84-pg_dump-para-fora-da-plataforma)) | Só com ADR e Issue em `infra/`, se um dia fizer falta |
| **Senha própria para o aluno de teste** | Hoje todos os usuários do seed têm a mesma senha (genérica, só para testes, definida pelo Henrique em `TF_VAR_seed_password`); risco aberto no ADR-0002 §10.4. A Issue #115 fica de lado | Issue #115, no código do seed |
| **Chaves do Data Protection** | Depende da decisão D4 ([2.1](#21-conferir-que-a-main-tem-tudo)) | Opção A: PR da Issue #103 |
| **Wait for CI que espera build e testes** | D3 foi escolhida (auto-deploy ligado em homologação), mas o Wait for CI não espera build nem testes: o `validate` só roda em `pull_request` | Issue #225 (editar workflow exige autorização do Henrique). Pré-requisito de produção; aceito sem ela em homologação |
| **Estado remoto com lock** | Um operador, um ambiente | ADR próprio, junto com produção |

**Cliques só de painel** (o provider 0.6.3 não configura; faça quando a homologação estiver estável e registre em `ambientes.md`):

1. **Agenda de backup do volume:** `Postgres` → aba **Backups** → escolha a agenda. Diário fica 6 dias, semanal 27, mensal 89 [F11]. Sugestão para homologação: diário.
2. **Alerta de gasto por e-mail:** página de uso do workspace (confira no seletor do canto superior esquerdo que é o workspace da contratante) → **Set Usage Limits** → alerta por e-mail (sugestão de `custos.md`: US$ 10) [F18]. Exige Admin do workspace. **Não defina o limite rígido** sem o Igor: ele tira do ar todos os serviços do workspace, e o mínimo é US$ 10 [F18].
3. **Limite de CPU e memória por réplica:** configurações do serviço → **Deploy** → **Replica Limits** [F18]. Limite baixo demais derruba o serviço; meça uma semana antes.

**Medir na primeira semana** (ADR-0002 §16, item 9): o consumo real de CPU e memória não é conhecido; os números de `docs/operations/custos.md` são premissas. Depois de sete dias, veja o uso na página de uso do workspace e corrija a tabela de premissas em PR `docs`.

---

## Fatos não confirmados e onde cada um é tratado

| # | Fato não confirmado | Origem | Passo |
|---|---|---|---|
| 1 | Conectar o repositório no primeiro `apply` dispara um deploy? Ele respeita o watch pattern? | ADR §16.1; handoff 107, surpresa 2 | [5.4](#54-apply), [6.1](#61-ver-em-que-estado-o-helinho-web-ficou), [6.8](#68-no-primeiro-merge-na-main-o-deploy-saiu-sozinho) |
| 2 | "Deploy Latest Commit" ignora o watch pattern? | ADR §16.2; handoff 107, surpresa 8 | [6.2](#62-se-não-há-deploy-bom-deploy-latest-commit) |
| 3 | O pre-deploy substitui o `ENTRYPOINT` da imagem e roda em forma exec? | ADR §16.3 | [6.3](#63-conferir-o-pre-deploy-migrate) |
| 4 | O healthcheck traz `X-Forwarded-Proto`? | ADR §16.4 | [6.2](#62-se-não-há-deploy-bom-deploy-latest-commit) |
| 5 | Comportamento do provider com um segundo ambiente no mesmo projeto | ADR §16.5 | [Passo 9](#passo-9--o-que-fica-para-depois) |
| 6 | A API da Railway aceita variável com valor vazio? | ADR §16.6 | [5.3](#53-plan), item 6 |
| 7 | Plano da conta da contratante e o que mais roda no workspace | ADR §16.7 | [Passo 1](#passo-1--o-que-pedir-ao-igor) |
| 8 | Versão do PostgreSQL do template da Railway | ADR §16.8 | Não se aplica: o banco é a imagem `postgres:17.12`, conferida no [5.3](#53-plan), item 5 |
| 9 | Consumo real de CPU e memória | ADR §16.9 | [Passo 9](#passo-9--o-que-fica-para-depois) |
| 10 | O `Postgres` nasce sem variáveis: um deploy vermelho e depois um verde | handoff 107, surpresa 1 | [5.4](#54-apply) |
| 11 | Reimplantar sem deploy anterior (`Unable to redeploy service…`) | handoff 107, surpresa 3 | [5.5](#55-se-o-apply-falhar-no-meio) |
| 12 | Leitura das regiões sem manifesto (`serviceManifest is not found`) | handoff 107, surpresa 4 | [5.5](#55-se-o-apply-falhar-no-meio) |
| 13 | Serviço órfão quando a criação falha no meio | handoff 107, surpresa 5 | [5.5](#55-se-o-apply-falhar-no-meio) |
| 14 | Subdomínio ocupado | handoff 107, surpresa 6 | [5.5](#55-se-o-apply-falhar-no-meio) |
| 15 | Todo `apply` com mudança é um deploy | handoff 107, surpresa 7 | [8.1](#81-o-que-dispara-um-deploy-e-como-conferir) |
| 16 | Trocar `seed_password` regrava a senha dos usuários? | handoff 107, surpresa 9 | [8.7](#87-rotação-das-senhas): conferido no código, **não regrava** |
| 17 | A borda sobrescreve `X-Real-IP`; o limite de taxa separa os clientes por IP; cookie `Secure` e HSTS em ambiente real | ADR §8; handoff 97 | [6.7](#67-conferir-o-comportamento-atrás-do-proxy) |
| 18 | Token de workspace consegue conectar repositório privado de conta pessoal? | este runbook | [Passo 3](#passo-3--github-app-da-railway-no-repositório), [5.5](#55-se-o-apply-falhar-no-meio) |
| 19 | Onde aparecem os logs do pre-deploy no painel | este runbook | [6.3](#63-conferir-o-pre-deploy-migrate) |
| 20 | O túnel `ssh -L` até o contêiner do `Postgres` funciona como a documentação descreve | este runbook | [8.4](#84-pg_dump-para-fora-da-plataforma) |
| 21 | Nome do volume depois de restaurar um backup, visto pelo Terraform | este runbook | [8.5](#85-restore-do-banco) |
| 22 | Janela de 48 h do volume quando o projeto inteiro é apagado | este runbook | [8.8](#88-destruir-o-ambiente) |
| 23 | Qual papel pode criar token de workspace | este runbook | [2.5](#25-criar-o-token-de-workspace). O id do workspace deixou de ser dúvida: `railway whoami --json` ([5.5](#55-se-o-apply-falhar-no-meio)) |
| 24 | Alterar o bloco `volume` do `Postgres` apaga o volume no lugar, sem o `prevent_destroy` impedir | `infra/railway/README.md` (lido no código do provider; nunca executado) | [8.1](#81-o-que-dispara-um-deploy-e-como-conferir), [8.5](#85-restore-do-banco), [8.8](#88-destruir-o-ambiente) |
| 25 | A aba **Backups** existe no plano Hobby? | este runbook | [8.3](#83-backup-de-volume-da-railway) |
| 26 | A Railway reimplanta o web quando muda uma variável do `Postgres` que ele referencia? | este runbook | [8.7](#87-rotação-das-senhas) |
| 27 | Rótulo do botão que apaga um serviço na seção de perigo do projeto; `terraform import` do projeto | este runbook | [5.5](#55-se-o-apply-falhar-no-meio) |
| 28 | Wait for CI com o único workflow do push sendo o `security`: publica na hora, espera só ele ou segura o deploy? | ADR §16.10 | [5.8](#58-ligar-wait-for-ci) |
| 29 | Local e nome exatos do botão **Wait for CI**; se o Rollback pausa o auto-deploy | este runbook | [5.8](#58-ligar-wait-for-ci), [8.2](#82-rollback-para-o-deploy-anterior) |

Quando um desses pontos for resolvido na prática, troque o "Confira:" pelo fato, em PR `docs`.

## Fontes

Consultadas em **18/11/2026**; F4, F15 e F22 foram lidas de novo ou acrescentadas em **19/11/2026**. A interface da Railway muda com frequência: se o painel não bater com o texto, vale o painel, e o runbook é corrigido.

| # | Assunto | Fonte |
|---|---|---|
| F1 | Tipos de token, página de criação, seletor de workspace, cabeçalhos | https://docs.railway.com/integrations/api |
| F2 | Autenticação do provider (`RAILWAY_TOKEN`, token de conta ou de workspace); `projectDelete` no `destroy`; `terraform import` de projeto e de serviço pelo id | https://registry.terraform.io/providers/terraform-community-providers/railway/0.6.3/docs e o código da tag `v0.6.3` em https://github.com/terraform-community-providers/terraform-provider-railway (`docs/index.md`, `docs/resources/service.md`, `examples/resources/railway_project/import.sh`, `internal/provider/resource_project.graphql`) |
| F3 | CLI: instalação, `railway login`, `RAILWAY_TOKEN` (projeto) e `RAILWAY_API_TOKEN` (conta ou workspace) | https://docs.railway.com/cli |
| F4 | Serviço a partir de repositório do GitHub, link da GitHub App, apagar serviço | https://docs.railway.com/services |
| F5 | Auto-deploy: botão Disable, Deploy Latest Commit, Show Skipped, Wait for CI, diagnóstico da GitHub App | https://docs.railway.com/deployments/github-autodeploys |
| F6 | Rollback, Redeploy, Remove, Deploy Latest Commit | https://docs.railway.com/deployments/deployment-actions |
| F7 | Pre-deploy command: contêiner separado, sem volume, falha interrompe o deploy | https://docs.railway.com/deployments/pre-deploy-command |
| F8 | Logs do pre-deploy que falhou ficam nos logs do deploy (resposta de funcionário da Railway, 17/09/2025) | https://station.railway.com/questions/predeploy-failed-but-no-error-logs-1461d570 |
| F9 | Logs: Build Logs, logs do deploy, Observability, retenção por plano | https://docs.railway.com/observability/logs |
| F10 | Healthcheck: só durante o deploy, host `healthcheck.railway.app`, porta `PORT` | https://docs.railway.com/deployments/healthchecks |
| F11 | Backups de volume: aba Backups, agendas e retenção, Restore, limites | https://docs.railway.com/volumes/backups |
| F12 | Volumes: indisponibilidade no redeploy, exclusão em 48 horas | https://docs.railway.com/volumes/reference |
| F13 | `railway ssh`: chaves, Service Instance ID, redirecionamento de porta, `scp` | https://docs.railway.com/cli/ssh |
| F14 | `railway connect`: opções e o que o comando lê (`DATABASE_URL`) | https://docs.railway.com/cli/connect e https://github.com/railwayapp/cli/blob/master/src/commands/connect.rs |
| F15 | Projeto: aba Danger, Delete Project, id do projeto | https://docs.railway.com/projects |
| F16 | Workspace: convite na aba People, papéis Admin, Member e Deployer | https://docs.railway.com/projects/workspaces |
| F17 | Planos: preço, uso incluído, retenção de imagem para rollback, membros | https://docs.railway.com/pricing/plans |
| F18 | Controle de custo: Set Usage Limits, alerta e limite rígido, Replica Limits | https://docs.railway.com/pricing/cost-control |
| F19 | Terraform: versão estável 1.17.2 e instalação pelo Homebrew | https://developer.hashicorp.com/terraform/install |
| F20 | Alterações pendentes no painel (Details e Deploy) | https://docs.railway.com/deployments/staged-changes |
| F21 | Variáveis de serviço: aba Variables, variáveis seladas | https://docs.railway.com/variables |
| F22 | `railway whoami --json`: lista os workspaces com id e nome | https://docs.railway.com/cli/whoami |

Documentos do repositório usados como especificação: `docs/architecture/ADR-0002-hospedagem-railway.md`, `infra/railway/README.md`, `docs/handoff/107.md`, `docs/handoff/94.md`, `docs/handoff/95.md`, `docs/handoff/97.md`, `docs/operations/custos.md` e o `README.md` da raiz.
