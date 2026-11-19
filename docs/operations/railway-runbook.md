# Runbook — homologação na Railway com Terraform

**Para quem:** Henrique, sozinho, na própria máquina (macOS). **Escrito em:** 18/11/2026.
**Descreve:** o Terraform de `infra/railway/` (PR #110, que chega à `main` pelo PR #100), o ADR-0002 (`docs/architecture/ADR-0002-hospedagem-railway.md`, PR #101) e o `Dockerfile` da raiz.

> **Nada deste runbook foi executado.** Nenhum agente rodou `plan`, `apply`, criou conta, token ou recurso. Os passos de painel foram conferidos na documentação da Railway em 18/11/2026 (seção [Fontes](#fontes); as marcas `[F1]`, `[F2]`… apontam para ela). Onde a documentação não dá o nome do botão, o texto diz "o nome pode variar". Onde um comportamento não pôde ser confirmado, o passo traz **Confira:** com o que fazer em cada caso; o índice desses pontos está em [Fatos não confirmados](#fatos-não-confirmados-e-onde-cada-um-é-tratado).

Só dados sintéticos em homologação. Produção não é ligada por este runbook.

## O caminho feliz em 10 linhas

1. **Igor:** entrar no workspace da contratante na Railway como Admin e saber o plano (Hobby ou Pro) e o teto de gasto. → [Passo 1](#passo-1--o-que-pedir-ao-igor)
2. **Repositório e ferramentas:** PRs #100 (Terraform), #101 (ADR), #104 (`/health/ready`) e o do Data Protection (Issue #103) mergeados na `main`; Terraform 1.16 ou 1.17 e `tflint` 0.66+. → [Passo 2](#passo-2--ferramentas-e-token-da-railway)
3. **Token:** criar um token de **workspace** em `railway.com/account/tokens` e guardar no gerenciador de senhas. → [2.5](#25-criar-o-token-de-workspace)
4. **GitHub App:** instalar a app da Railway na conta do GitHub com acesso a `universo-do-helinho`. → [Passo 3](#passo-3--github-app-da-railway-no-repositório)
5. **Entradas:** copiar `terraform.tfvars.example` para `terraform.tfvars`; `read -rs` de `RAILWAY_TOKEN` e `TF_VAR_seed_password` em um terminal só do Terraform. → [Passo 4](#passo-4--terraformtfvars-e-variáveis-de-ambiente)
6. **Terraform:** `init` → `plan -out` (esperado: `Plan: 7 to add`, nenhum domínio nem TCP proxy no banco, nenhum segredo na tela) → `apply`. → [Passo 5](#passo-5--init-plan-e-apply)
7. **Estado:** backup cifrado de `infra/railway/terraform.tfstate` logo depois do `apply`. → [5.7](#57-backup-cifrado-do-estado)
8. **Primeiro deploy:** no painel, serviço `helinho-web` → `Cmd+K` → **Deploy Latest Commit**; o pre-deploy `migrate` roda e o deploy fica ativo. → [Passo 6](#passo-6--primeiro-deploy-e-conferências)
9. **Conferir:** `/health/ready` responde 200, login no painel com `master@helinho.example`, `POST /api/login` com a matrícula `2026001`. → [6.4](#64-healthready)
10. **Registrar:** preencher `docs/operations/ambientes.md` e passar a URL da API (já com `/api`) e a credencial sintética ao desenvolvedor Unity. → [Passo 7](#passo-7--url-para-o-desenvolvedor-unity-e-registro-do-ambiente)

Operação do dia a dia (publicar, rollback, backup, restore, rotação, destruição): [Passo 8](#passo-8--operação). O que fica para depois: [Passo 9](#passo-9--o-que-fica-para-depois).

## Variável → onde pegar → onde colocar

| Variável | Onde pegar | Onde colocar | Segredo? |
|---|---|---|---|
| `RAILWAY_TOKEN` | Você cria: `railway.com/account/tokens`, com o workspace da contratante selecionado ([2.5](#25-criar-o-token-de-workspace)) | Gerenciador de senhas. No uso: `read -rs` no terminal do Terraform. Nunca em arquivo, `tfvars`, CI ou chat | sim |
| `TF_VAR_seed_password` | Você gera no gerenciador de senhas: 16 ou mais caracteres, com minúscula, maiúscula, dígito e símbolo | Gerenciador de senhas. No uso: `read -rs` no terminal do Terraform. O Terraform a grava na Railway como `HELINHO_SEED_PASSWORD` | sim |
| `workspace_id` | Só se o token enxergar mais de um workspace ([5.5](#55-se-o-apply-falhar-no-meio)) | `infra/railway/terraform.tfvars` | não |
| `ambientes`, `repositorio`, `branch`, `auto_deploy`, `regiao`, `serverless`, `postgres_imagem`, `variaveis_web` | Os padrões de `terraform.tfvars.example` já servem. Exceção provável: o subdomínio padrão `helinho-homologacao` é único em toda a Railway e pode estar ocupado; aí defina `ambientes.homologacao.subdominio` | `infra/railway/terraform.tfvars`, só se for mudar | não |
| `TF_VAR_brevo_api_key` | Painel da Brevo, na Sprint 3 | Não defina agora | sim |
| `TF_VAR_ai_api_key` | Painel do provedor de IA, na Sprint 4 | Não defina agora | sim |
| Senha do banco | O Terraform gera (`random_password`); você nunca digita | Fica em texto no estado do Terraform e na Railway (variável `PGPASSWORD` do serviço `Postgres`) | sim |
| `url_publica`, `url_api` | `terraform output`, depois do `apply` | `docs/operations/ambientes.md`; a `url_api` vai para o desenvolvedor Unity | não |
| Service Instance ID do `Postgres` | Painel: serviço `Postgres` → `Cmd+K` → **Copy Service Instance ID** | Só no terminal da CLI, para o túnel do `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)) | não |

## Convenções

- **Dois terminais, nunca misturados.**
  - **Terminal do Terraform:** onde `RAILWAY_TOKEN` e `TF_VAR_seed_password` estão exportados. Só roda `terraform`, `curl`, `jq` e `age`. **Nunca rode a CLI `railway` nele:** a CLI lê `RAILWAY_TOKEN` como token de **projeto**, e o seu é de workspace [F3].
  - **Terminal da CLI:** aberto à parte, sem `RAILWAY_TOKEN`, autenticado com `railway login`. Só é usado no [Passo 8](#passo-8--operação).
- Todos os comandos partem da **raiz do repositório**.
- `<…>` é marcador: troque pelo seu valor. Nenhum exemplo traz segredo.
- **✔ Confira:** é o resultado esperado. **Se der errado:** é a saída mais provável. **Confira:** (sem o ✔) marca comportamento que ninguém verificou ainda.
- No painel da Railway, `Cmd+K` abre a paleta de comandos (`Ctrl+K` fora do macOS).

---

## Passo 1 — O que pedir ao Igor

O Igor é o contato da contratante para a conta da Railway (C19). Mande as perguntas abaixo de uma vez e anote as respostas na tabela "Conta" de `docs/operations/ambientes.md`.

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

**Pré-requisitos do primeiro `apply`, todos mergeados na `main`:**

| PR | O que traz | Sem ele |
|---|---|---|
| #100 | O Terraform de `infra/railway/` (o PR #110 já foi mergeado dentro da branch dele) | Não há o que aplicar |
| #101 | ADR-0002 e `docs/operations/custos.md` | As decisões D1 a D3 não estão aceitas |
| #104 | A rota `/health/ready` | O healthcheck do serviço aponta para ela: **o primeiro deploy falha no healthcheck** |
| O da Issue #103 | Chaves do Data Protection no PostgreSQL (pacote e migração) | Cada deploy derruba as sessões do painel |

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

```sh
grep -c 'DataProtection.EntityFrameworkCore' Directory.Packages.props
```

**✔ Confira:** imprime `1` (o PR da Issue #103 entrou).

**Se der errado:** imprime `0` → o PR do Data Protection não foi mergeado. Espere por ele. Se você decidir subir a homologação mesmo assim, o sistema funciona, mas **cada deploy derruba as sessões do painel** (todo mundo precisa entrar de novo); registre a escolha em `docs/operations/ambientes.md`.

### 2.2 Terraform na versão fixada

`infra/railway/versions.tf` aceita `>= 1.16.0, < 1.18.0`. O CI usa a 1.17.2, que era a estável em 18/11/2026 [F19].

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

**Não precisa editar nada:** todas as linhas do exemplo estão comentadas e mostram os padrões (só `homologacao`, com seed ligado; `auto_deploy = false`; região `us-east4-eqdc4a`; `postgres:17.12`). Edite só se:

| Situação | O que descomentar |
|---|---|
| O token enxerga mais de um workspace (o erro aparece no `apply`) | `workspace_id = "<id do workspace>"` |
| O subdomínio `helinho-homologacao` já existe na Railway (o erro aparece no `apply`) | `ambientes = { homologacao = { seed_enabled = true, subdominio = "<outro-nome>" } }` |

**Segredo não entra nesse arquivo.** Não acrescente `producao` em `ambientes` ([Passo 9](#passo-9--o-que-fica-para-depois)).

### 4.2 Gerar a senha do seed

No gerenciador de senhas, gere uma senha de **20 caracteres ou mais, com minúscula, maiúscula, dígito e símbolo**, e salve em uma entrada própria (sugestão: "Helinho homologação — senha do seed"). O Terraform recusa senha com menos de 16 caracteres ou sem uma das quatro classes.

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

**✔ Confira:** `Success! The configuration is valid.` e o `tflint` termina sem imprimir achados.

### 5.3 `plan`

O arquivo de plano guarda os segredos em texto. Ele fica **fora do repositório**, em uma pasta só sua, e é apagado depois do `apply`. O `.gitignore` só passa a bloquear `*.tfplan` com a Issue #112; mesmo depois dela, mantenha o plano fora do repositório.

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
4. Em `railway_service.web`: `source_repo = "henriqueluza/universo-do-helinho"`, `source_repo_branch = "main"`, `watch_patterns = ["/.auto-deploy-desligado"]`, `predeploy_command = "tini -- dotnet Helinho.Web.dll migrate"`, `predeploy_timeout = 300`, `healthcheck_path = "/health/ready"`, `healthcheck_timeout = 120`.
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
| `helinho-web` sem nenhum deploy, ou com deploy marcado como ignorado (**Show Skipped** [F5]) | Possível: o watch pattern segurou o deploy inicial | Nada; o primeiro deploy é manual, no [Passo 6](#passo-6--primeiro-deploy-e-conferências) |
| `helinho-web` já com deploy **verde** | Possível: a criação das variáveis reimplanta o serviço | Pule para o [6.3](#63-conferir-o-pre-deploy-migrate) |

### 5.5 Se o `apply` falhar no meio

O `apply` não é atômico: o que foi criado antes do erro **continua criado** e está no estado. Antes de qualquer coisa, veja o que o Terraform conhece:

```sh
terraform -chdir=infra/railway state list
```

Compare com o painel e ache a linha do seu erro:

| Sintoma | O que aconteceu | O que fazer |
|---|---|---|
| Erro ao criar `railway_service.web` citando o repositório ou o GitHub (o texto pode variar) | A Railway não enxerga o repositório ([Passo 3](#passo-3--github-app-da-railway-no-repositório)) | 1) Apague o `helinho-web` órfão, se existir (linha do serviço órfão, abaixo). 2) Refaça o Passo 3. 3) Repita `plan` e `apply`. **Plano B**, se a integração estiver certa e o erro persistir: crie um token de **conta** ("No workspace" em [2.5](#25-criar-o-token-de-workspace)), defina `workspace_id` no `terraform.tfvars`, repita, e ao final **apague esse token** e volte ao de workspace. O token de conta alcança também os seus projetos pessoais: é exceção, não rotina |
| `Unable to redeploy service after variable collection created` | As variáveis **já estão** na Railway; o provider tentou reimplantar um serviço que não tinha deploy anterior | No painel, serviço citado no erro → `Cmd+K` → **Deploy Latest Commit** (web) ou **Redeploy** do último deploy (`Postgres`). Depois repita `plan` e `apply` |
| `serviceManifest is not found` | O provider lê a região no manifesto do último deploy, e esse deploy não tem manifesto (foi ignorado, por exemplo) | Igual à linha de cima: faça existir um deploy de verdade e repita `plan` e `apply` |
| Erro em `railway_service_domain.web` dizendo que o subdomínio já existe | `helinho-homologacao` é único em toda a Railway e alguém já usa | Defina `subdominio` no `terraform.tfvars` ([4.1](#41-criar-o-terraformtfvars)) e repita `plan` e `apply` |
| Erro em `railway_project.this` pedindo workspace | O token enxerga mais de um workspace (acontece com token de conta) | Defina `workspace_id`. **Confira:** a documentação não diz onde o id do workspace aparece no painel; tente `Cmd+K` e procure por "workspace", ou peça ao Igor |
| `Not Authorized`, `401` ou erro de autenticação no primeiro recurso | Token errado, apagado, de projeto, ou colado com espaço | Repita o [4.3](#43-exportar-os-segredos-sem-deixá-los-no-histórico). Nada foi criado |
| Um serviço existe no painel e **não** aparece no `state list` | **Serviço órfão** (limitação L8 do provider): ele só entra no estado no fim da criação | Apague o órfão no painel: configurações do **projeto** → seção de perigo (**Danger**) → apagar o serviço [F4][F15]. Depois repita `plan` e `apply`. No primeiro `apply` o órfão não tem dado nenhum; em qualquer outro momento, **não apague o `Postgres` sem backup** |

Regras para repetir:

- **Sempre** faça um `plan -out` novo; o arquivo de plano antigo não serve mais.
- O `plan` novo deve mostrar só o que **faltou** criar. Se ele quiser **destruir**, **substituir** ou **alterar** `railway_project` ou `railway_service.postgres`, pare ([8.1](#81-publicar-uma-versão-nova) explica por quê).
- Faça o backup do estado ([5.7](#57-backup-cifrado-do-estado)) mesmo depois de um `apply` parcial.
- Na segunda falha igual, não tente uma terceira vez do mesmo jeito: guarde a mensagem de erro (sem segredo) e abra uma Issue.

### 5.6 Conferir o que foi criado

```sh
terraform -chdir=infra/railway output
```

**✔ Confira:** `url_publica` e `url_api` para `homologacao`, e em `ambientes` os ids do projeto, do ambiente e dos dois serviços. Nenhuma saída traz senha.

No painel, projeto `helinho-homologacao`, ambiente `homologacao`:

- **`Postgres`:** nas configurações de rede do serviço **não há** domínio público nem TCP proxy; o volume `postgres-dados` está montado em `/var/lib/postgresql/data`; a aba **Variables** tem 9 variáveis.
- **`helinho-web`:** origem `henriqueluza/universo-do-helinho`, branch `main`; watch path `/.auto-deploy-desligado`; pre-deploy command `tini -- dotnet Helinho.Web.dll migrate`; healthcheck `/health/ready`; aba **Variables** com 13 variáveis.

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

**✔ Confira:** pede a frase-senha e imprime `7`.

Copie o arquivo `.age` para **fora da máquina** ([8.9](#89-onde-fica-o-estado-do-terraform)).

**Se der errado:** `age: command not found` → `brew install age`. Alternativa com `gpg`, se preferir: `gpg --symmetric --cipher-algo AES256 -o <destino>.gpg infra/railway/terraform.tfstate`.

---

## Passo 6 — Primeiro deploy e conferências

Com `auto_deploy = false`, push na `main` não publica. Quem publica é você, pelo painel.

### 6.1 Ver em que estado o `helinho-web` ficou

Painel → projeto `helinho-homologacao` → serviço `helinho-web` → aba **Deployments**.

- Deploy **verde** (ativo) → pule para o [6.3](#63-conferir-o-pre-deploy-migrate).
- Deploy que falhou, nenhum deploy, ou deploy ignorado → siga para o 6.2.

### 6.2 Publicar: Deploy Latest Commit

1. Com o serviço `helinho-web` aberto, aperte `Cmd+K`.
2. Escolha **Deploy Latest Commit**. A Railway implanta o último commit da branch conectada, `main` [F5][F6].
3. Acompanhe na aba **Deployments**: build da imagem (o `Dockerfile` da raiz) → pre-deploy → healthcheck → ativo. O primeiro build leva alguns minutos.

**✔ Confira:** o deploy termina **verde** e é o deploy ativo do serviço.

**Confira (ADR-0002 §16, item 2): o Deploy Latest Commit respeita o watch pattern?** A documentação indica esse comando como saída quando os watch paths seguram um deploy [F5], mas não afirma que ele os ignora.

- Se o comando criar um deploy normal → está certo, siga.
- Se o deploy aparecer como **ignorado** (use **Show Skipped** [F5]) → o contorno do `auto_deploy = false` não funciona. **Plano B:**
  1. No `terraform.tfvars`, defina `auto_deploy = true`; rode `plan` (esperado: 1 mudança, em `railway_service.web`, tirando o watch pattern) e `apply`. Esse `apply` já reimplanta o web.
  2. **Logo em seguida**, nas configurações do serviço `helinho-web`, na parte de origem e auto-deploy, clique em **Disable** para desligar o deploy automático [F5] (o lugar exato do botão pode variar).
  3. Repita o **Deploy Latest Commit**.
  4. Registre a troca em `docs/operations/ambientes.md` e abra uma Issue: o botão Disable é estado só de painel, que o Terraform não enxerga.

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
| `404` | O commit publicado não tem a rota (PR #104) | Mergeie o PR e repita o [6.2](#62-publicar-deploy-latest-commit) |
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
- Depois de um deploy novo você é deslogado → o commit publicado não tem o PR do Data Protection (Issue #103, [2.1](#21-conferir-que-a-main-tem-tudo)).

### 6.6 `POST /api/login` com `curl`

O jogo entra por matrícula, usuário ou e-mail no campo `login`. Use o aluno `2026001` (os alunos 03 e 08 estão com a aprovação do responsável pendente e respondem 403).

```sh
read -rs HELINHO_PASS
```

Cole a senha do seed e aperte Enter. O comando abaixo monta o JSON com o `jq` (a senha tem símbolos e quebraria um JSON montado à mão) e mostra só o tamanho do token:

```sh
jq -n --arg l 2026001 --arg p "$HELINHO_PASS" '{login:$l,password:$p}' | curl -sS -X POST "$BASE/api/login" -H 'Content-Type: application/json' --data-binary @- | jq '{user_id, expires_at, token_chars: (.token | length)}'
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

**✔ Confira:** as primeiras respostas são `401` e as últimas são `429` (o limite é de 10 por minuto por IP, e o login do 6.6 conta se foi feito no mesmo minuto). Isso prova que a borda da Railway **sobrescreveu** o `X-Real-IP` forjado e a aplicação contou as 12 chamadas como um IP só.

**Se der errado:** doze `401` → o cabeçalho forjado chegou à aplicação, e o limite de taxa pode ser burlado. **Não ponha dado real nesse ambiente**, abra uma Issue com a label `security:sensitive` e avise o revisor de segurança. Homologação com dado sintético pode continuar.

### 6.8 No próximo push na `main`: o deploy foi ignorado?

Só dá para conferir depois do próximo merge na `main` (ADR-0002 §7.5 e §16, item 1).

**✔ Confira:** na aba **Deployments** do `helinho-web`, com **Show Skipped** ligado [F5], o push aparece como **ignorado**, e o deploy ativo continua sendo o anterior.

**Se der errado:** o push publicou sozinho → o watch pattern não segurou. Clique em **Disable** no auto-deploy do serviço [F5], registre em `ambientes.md` e abra uma Issue.

---

## Passo 7 — URL para o desenvolvedor Unity e registro do ambiente

### 7.1 Pegar a URL base da API

```sh
terraform -chdir=infra/railway output -json url_api | jq -r .homologacao
```

**✔ Confira:** imprime `https://<subdomínio>.up.railway.app/api`, **já com `/api`** e sem barra no fim. É essa a URL base do jogo: `POST <url>/login`, `GET <url>/profile` e as demais rotas de `docs/context/rotas-api.md`.

### 7.2 Preencher `docs/operations/ambientes.md`

Abra o arquivo e preencha os campos marcados com `<a preencher>`: URL do painel, URL da API, data do `apply`, plano da conta e, se usou, o plano B do [6.2](#62-publicar-deploy-latest-commit). **Não escreva senha, token nem id de workspace** nesse arquivo. Commit em PR `docs` próprio.

**✔ Confira:** `git diff docs/operations/ambientes.md` mostra só URL, datas e texto; nenhuma senha.

### 7.3 Passar a credencial ao desenvolvedor Unity

- **URL base:** a do 7.1.
- **Login de teste:** matrícula `2026001` (ou `2026002`, `2026004`…; evite `2026003` e `2026008`, que respondem 403 de propósito).
- **Senha:** a do seed, enviada por **item compartilhado do gerenciador de senhas** ou outro canal cifrado combinado. Nunca por e-mail, chat aberto, Issue ou PR.

**Atenção:** todos os usuários do seed têm a **mesma senha**. Ao entregar a senha do aluno você entrega também a do `master` de homologação. Isso só é aceitável porque o ambiente tem apenas dado sintético. Se alguém que recebeu a senha deixar o projeto, troque-a ([8.7](#87-rotação-das-senhas)).

**✔ Confira:** o desenvolvedor Unity confirma um `POST /login` com resposta 200 a partir da máquina dele.

---

## Passo 8 — Operação

### 8.1 Publicar uma versão nova

1. Mergeie o PR na `main` (revisado e com CI verde).
2. Painel → `helinho-web` → `Cmd+K` → **Deploy Latest Commit** [F5].
3. Repita as conferências [6.3](#63-conferir-o-pre-deploy-migrate) e [6.4](#64-healthready).

Três coisas para lembrar:

- **Todo `terraform apply` com mudança é um deploy** do serviço alterado: o provider reimplanta quando muda o serviço ou as variáveis dele. O do `Postgres` tem indisponibilidade curta, porque serviço com volume não troca de versão sem parar [F12]. Rode `apply` em horário combinado.
- Mudou algo no painel à mão? O Terraform não sabe. Rode `terraform plan` e traga a mudança para o código, ou desfaça no painel.
- Em qualquer `plan`: **nunca** aceite `destroy` ou `replace` em `railway_project.this` ou `railway_service.postgres`. O `prevent_destroy` do `Postgres` faz o `plan` falhar em vez de apagar o banco; se falhar por isso, pare e entenda por quê (mudar `workspace_id`, o nome do ambiente ou o `project_id` recria tudo).
- **O `prevent_destroy` não protege o volume.** No provider 0.6.3, remover (ou trocar) o bloco `volume` do serviço `Postgres` aparece no `plan` como **alteração no lugar** (`~ update in-place`), não como destruição, e **apaga o volume com os dados**. Por isso, em todo `plan`, confira que `railway_service.postgres` **não aparece como alterado**. Se a alteração for intencional (trocar `postgres_imagem`, por exemplo), faça antes um `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)) e um backup de volume ([8.3](#83-backup-de-volume-da-railway)), e leia linha por linha o que muda dentro de `volume`: se o bloco `volume` aparecer no diff, **não aplique**.

### 8.2 Rollback para o deploy anterior

1. Painel → `helinho-web` → aba **Deployments**.
2. No deploy anterior que estava bom, clique nos **três pontos** no fim da linha → **Rollback** → confirme [F6].

**✔ Confira:** o deploy escolhido volta a ser o ativo e `curl` em `/health/ready` responde 200.

O que o rollback faz e não faz:

- Restaura a **imagem** e as **variáveis** daquele deploy [F6].
- **Não desfaz migração.** O banco fica no esquema novo. Por isso toda migração precisa ser compatível com a versão anterior (expand/contract).
- Só existe enquanto a imagem está guardada: **72 horas no Hobby, 120 no Pro** depois de o deploy ser removido [F17]. Passado o prazo a opção **Rollback** some [F6]; resta **Redeploy**, no mesmo menu, que reconstrói a partir do código [F6].
- Como as variáveis voltam ao valor antigo, o Terraform pode ver diferença: rode `terraform plan` depois e decida se reaplica.

**Se der errado:** a versão antiga não sobe por causa do esquema novo → o caminho é para a frente: corrija por PR e publique ([8.1](#81-publicar-uma-versão-nova)). Se os dados estiverem estragados, restaure o banco ([8.5](#85-restore-do-banco)).

### 8.3 Backup de volume da Railway

É o backup rápido, dentro da plataforma. Serve antes de migração arriscada e antes de rotação de senha.

1. Painel → serviço `Postgres` → aba **Backups** [F11].
2. Crie um backup manual. A documentação diz que o backup manual existe, mas não dá o nome do botão (o nome pode variar) [F11].

**✔ Confira:** o backup aparece na lista da aba **Backups**, com a data e a hora de agora.

Limites, todos da documentação [F11]:

- Só restaura **no mesmo projeto e ambiente**. Não protege contra apagar o projeto.
- Backup manual é limitado a 50% do tamanho total do volume.
- Apagar o conteúdo do volume ("wipe") apaga **todos** os backups dele.
- É incremental e cobrado como volume.
- A agenda (diária, semanal, mensal) é um clique nessa mesma aba ([Passo 9](#passo-9--o-que-fica-para-depois)).

### 8.4 `pg_dump` para fora da plataforma

É o backup que sobrevive à perda do projeto e à perda do volume. **Faça um antes de qualquer `apply` cujo plano toque o serviço `Postgres`** ([8.1](#81-publicar-uma-versão-nova)), antes de rotação de senha e antes de destruir. O banco não tem domínio público nem TCP proxy, então a conexão passa por um **túnel SSH** até o contêiner do `Postgres`.

**Caminho escolhido:** `ssh` nativo com redirecionamento de porta, que a documentação da Railway descreve [F13]. A conexão é aberta de dentro do contêiner, e o redirecionamento só alcança o próprio contêiner e a rede privada do projeto [F13].

**Por que não `railway connect Postgres --tunnel-only`** (o comando citado no ADR-0002 §11): o código da CLI lê as credenciais da variável `DATABASE_URL` (ou `DATABASE_PUBLIC_URL`) do serviço [F14], e o Terraform deste repositório define `PGHOST`, `PGPORT`, `PGUSER`, `PGPASSWORD` e `PGDATABASE`, **não** `DATABASE_URL`. O comando deve falhar por variável ausente. Além disso, ele imprime a senha do banco no terminal [F14]. Se um dia o serviço ganhar `DATABASE_URL` (Issue própria em `infra/`), ele passa a ser uma opção.

**Por que não um TCP proxy temporário:** expõe o banco à internet e cobra tráfego de saída (ADR-0002 §14). Não crie, nem "só por cinco minutos".

**Riscos do túnel:** quem tem chave SSH registrada e acesso ao workspace vira administrador do banco; a porta local fica aberta na sua máquina enquanto o túnel existir (só em `127.0.0.1`); o arquivo de dump contém hashes de senha e, depois da Issue #103, as chaves do Data Protection. Feche o túnel ao terminar e cifre o dump.

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

1. Painel → serviço `Postgres` → `Cmd+K` → **Copy Service Instance ID**. Atenção: **Copy Service ID** é outro valor e **não** serve como destino de SSH [F13].
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

#### Fazer o dump

Em **outro** terminal (também sem `RAILWAY_TOKEN`):

```sh
"$(brew --prefix libpq)/bin/pg_dump" -h 127.0.0.1 -p 5433 -U helinho -d helinho -Fc -f "$HOME/.helinho-infra/helinho-homologacao-$(date +%Y%m%d-%H%M).dump"
```

**Confira:** a imagem oficial do PostgreSQL costuma aceitar conexão local sem senha, então o comando pode nem pedir. **Se pedir senha:** painel → `Postgres` → aba **Variables** → `PGPASSWORD` (o gesto para revelar o valor pode variar [F21]); copie e cole no prompt do `pg_dump`. Não ponha a senha na linha de comando.

```sh
"$(brew --prefix libpq)/bin/pg_restore" --list "$HOME/.helinho-infra/<arquivo>.dump" | head -20
```

**✔ Confira:** o `pg_restore --list` imprime o sumário do dump, com as tabelas do sistema (por exemplo as do Identity e `__EFMigrationsHistory`). Arquivo vazio ou erro de formato = dump inválido; repita.

Feche o túnel com `Ctrl+C` no primeiro terminal. Cifre o dump e apague o original:

```sh
age -p -o "$HOME/.helinho-infra/<arquivo>.dump.age" "$HOME/.helinho-infra/<arquivo>.dump"
```

```sh
rm "$HOME/.helinho-infra/<arquivo>.dump"
```

Guarde o `.age` fora da máquina, junto do backup do estado ([8.9](#89-onde-fica-o-estado-do-terraform)).

#### Plano B: dump dentro do contêiner e cópia por `scp`

Use só se o túnel não abrir. Os três comandos usam recursos documentados [F13]. Troque `<id do projeto>` pelo `projeto_id` de `terraform output ambientes`.

```sh
railway ssh --project <id do projeto> --environment homologacao --service Postgres -- pg_dump -U helinho -d helinho -Fc -f /tmp/helinho.dump
```

```sh
scp "$INSTANCIA@ssh.railway.com:/tmp/helinho.dump" "$HOME/.helinho-infra/helinho-homologacao-$(date +%Y%m%d-%H%M).dump"
```

```sh
railway ssh --project <id do projeto> --environment homologacao --service Postgres -- rm /tmp/helinho.dump
```

**✔ Confira:** o mesmo `pg_restore --list` de cima. Não esqueça o terceiro comando: o dump não pode ficar no contêiner.

### 8.5 Restore do banco

Dois caminhos. Antes de qualquer um, faça um backup de volume do estado atual ([8.3](#83-backup-de-volume-da-railway)), para poder voltar atrás.

#### A. Restaurar um backup de volume (mesmo projeto e ambiente)

1. Painel → `Postgres` → aba **Backups** → ache o backup pela data → **Restore** [F11].
2. A Railway prepara a mudança como alteração pendente: clique em **Details**, no topo do projeto, para revisar, e em **Deploy** para concluir [F11][F20].
3. O serviço é reimplantado com um **volume novo** montado no mesmo caminho; o volume antigo continua existindo, desmontado [F11].

**✔ Confira:** o `Postgres` volta com deploy verde; faça **Redeploy** do `helinho-web` e repita [6.4](#64-healthready) e [6.5](#65-login-no-painel).

**Confira (não confirmado): o Terraform depois do restore.** O volume novo recebe como nome a data do backup, e o código declara o volume `postgres-dados` [F11]. Rode `terraform plan` **sem aplicar**. Se ele mostrar **qualquer** mudança em `railway_service.postgres`, inclusive só o nome do volume, **não aplique**: alteração no bloco `volume` pode apagar o volume restaurado ([8.1](#81-publicar-uma-versão-nova)). Guarde a saída e abra uma Issue para alinhar o código ao que está na Railway.

#### B. `pg_restore` de um dump (qualquer banco vazio ou o próprio)

1. Decifre o dump:

```sh
age -d -o "$HOME/.helinho-infra/<arquivo>.dump" "$HOME/.helinho-infra/<arquivo>.dump.age"
```

2. Abra o túnel como no [8.4](#84-pg_dump-para-fora-da-plataforma).
3. Restaure por cima, apagando os objetos existentes:

```sh
"$(brew --prefix libpq)/bin/pg_restore" -h 127.0.0.1 -p 5433 -U helinho -d helinho --clean --if-exists --no-owner "$HOME/.helinho-infra/<arquivo>.dump"
```

4. Feche o túnel, apague o dump decifrado e faça **Redeploy** do `helinho-web`.

**✔ Confira:** o `pg_restore` termina sem linhas `error:`; login no painel funciona ([6.5](#65-login-no-painel)).

**Se der errado:** erros de objeto em uso → remova o deploy do `helinho-web` antes (três pontos → **Remove** [F6]), restaure e publique de novo. O restore de um dump **antigo** em cima de um esquema mais novo deixa o banco no esquema do dump: publique o commit correspondente ou deixe o `migrate` do próximo deploy trazer o esquema para a frente.

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
3. Faça um backup de volume ([8.3](#83-backup-de-volume-da-railway)).
4. Abra o túnel do [8.4](#84-pg_dump-para-fora-da-plataforma) e zere o esquema. **Isso apaga todos os dados de homologação:**

```sh
"$(brew --prefix libpq)/bin/psql" -h 127.0.0.1 -p 5433 -U helinho -d helinho -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'
```

5. Feche o túnel e faça **Deploy Latest Commit** no `helinho-web`: o `migrate` recria as tabelas e o seed recria os usuários com a senha nova.
6. Repita [6.5](#65-login-no-painel) e [6.6](#66-post-apilogin-com-curl) com a senha nova; repasse-a a quem usa ([7.3](#73-passar-a-credencial-ao-desenvolvedor-unity)); faça o backup do estado ([5.7](#57-backup-cifrado-do-estado)).

**✔ Confira:** a senha antiga responde `401` e a nova entra.

Isso só é aceitável porque o dado é sintético. Nunca faça em ambiente com dado real.

#### Senha do banco

A senha é gerada pelo Terraform. Trocar tem uma armadilha: a imagem oficial do PostgreSQL só lê `POSTGRES_PASSWORD` **na criação do volume**. Mudar a variável não muda a senha do usuário que já existe. **Este procedimento nunca foi executado**; ele tem alguns minutos de indisponibilidade.

1. Backup de volume ([8.3](#83-backup-de-volume-da-railway)) e `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)).
2. Gere a senha nova no Terraform:

```sh
terraform -chdir=infra/railway plan -replace='module.ambiente["homologacao"].random_password.postgres' -out="$HOME/.helinho-infra/rotacao.tfplan"
```

**✔ Confira:** o plano substitui `random_password.postgres` e altera `railway_variable_collection.postgres`. `railway_service.postgres` e `railway_project.this` **não aparecem** no plano, nem como alteração.

3. Aplique (`terraform -chdir=infra/railway apply "$HOME/.helinho-infra/rotacao.tfplan"`) e apague o arquivo de plano. O `Postgres` reinicia, mas a senha **de dentro do banco continua a antiga**, e o web que está no ar segue funcionando com ela. Um deploy novo do web falharia agora, no pre-deploy, porque já receberia a senha nova.
4. Leia a senha nova no painel (`Postgres` → **Variables** → `POSTGRES_PASSWORD`) e copie.
5. Abra o túnel do [8.4](#84-pg_dump-para-fora-da-plataforma) e troque a senha dentro do banco. O `\password` pede a senha duas vezes sem mostrar na tela:

```sh
"$(brew --prefix libpq)/bin/psql" -h 127.0.0.1 -p 5433 -U helinho -d helinho -c '\password helinho'
```

6. Feche o túnel e faça **Deploy Latest Commit** no `helinho-web`. Entre o passo 5 e o fim deste deploy, o painel e a API ficam fora do ar.
7. Repita [6.4](#64-healthready) e [6.5](#65-login-no-painel); faça o backup do estado ([5.7](#57-backup-cifrado-do-estado)).

**Se der errado**, ou se você preferir não arriscar: em homologação, destruir e recriar o ambiente ([8.8](#88-destruir-o-ambiente)) gera senha nova do banco sem armadilha.

### 8.8 Destruir o ambiente

Destruir apaga o banco e o volume. Antes: `pg_dump` ([8.4](#84-pg_dump-para-fora-da-plataforma)) se algum dado importar, e backup do estado ([5.7](#57-backup-cifrado-do-estado)).

O banco também pode ser apagado **sem** você pedir uma destruição: uma alteração no bloco `volume` do serviço `Postgres` apaga o volume, e o `prevent_destroy` não impede ([8.1](#81-publicar-uma-versão-nova)).

**Só quer parar de gastar por umas semanas?** Não destrua: remova o deploy dos dois serviços (três pontos do deploy → **Remove** [F6]). Sem deploy ativo, só o volume é cobrado (`docs/operations/custos.md`). Para voltar, publique de novo.

#### A. Pelo Terraform (preferido: o estado fica coerente)

O serviço `Postgres` tem `prevent_destroy`. Destruir de propósito exige tirar essa proteção em um commit, numa branch que **não** vai para a `main`.

1. Crie a branch:

```sh
git switch -c chore/destruir-homologacao
```

2. Em `infra/railway/modules/ambiente/main.tf`, apague o bloco `lifecycle { prevent_destroy = true }` do recurso `railway_service.postgres` e faça o commit (`chore(infra): remove prevent_destroy para destruir a homologação`).
3. Planeje a destruição:

```sh
terraform -chdir=infra/railway plan -destroy -out="$HOME/.helinho-infra/destruir.tfplan"
```

**✔ Confira:** `Plan: 0 to add, 0 to change, 7 to destroy.`, todos sob `module.ambiente["homologacao"]`.

4. Destrua:

```sh
terraform -chdir=infra/railway apply "$HOME/.helinho-infra/destruir.tfplan"
```

**✔ Confira:** `Apply complete! Resources: 0 added, 0 changed, 7 destroyed.`; o projeto `helinho-homologacao` sumiu do painel; `terraform -chdir=infra/railway state list` não imprime nada.

5. Apague o arquivo de plano, volte para a `main` (`git switch main`) e apague a branch local. A proteção volta sozinha, porque o commit não foi mergeado.

**Se der errado:**

- `Instance cannot be destroyed` → o `prevent_destroy` ainda está no arquivo.
- O `destroy` para no meio (o provider reimplanta o serviço ao apagar uma coleção de variáveis, e isso pode falhar) → faça um `plan -destroy` novo e aplique de novo; se insistir, use o caminho B.
- Erro de permissão ao apagar projeto ou serviço → o seu papel não é Admin [F16].

#### B. Pelo painel (se o Terraform não conseguir)

1. Painel → configurações do projeto `helinho-homologacao` → aba **Danger** → **Delete Project** [F15]. Isso apaga todos os serviços, ambientes e deploys do projeto [F15].
2. O estado local ficou apontando para recursos que não existem. Guarde-o cifrado ([5.7](#57-backup-cifrado-do-estado)) e tire-o do caminho:

```sh
mv infra/railway/terraform.tfstate "$HOME/.helinho-infra/tfstate-destruido-$(date +%Y%m%d).json"
```

3. Apague esse arquivo depois de cifrá-lo: ele contém as senhas antigas.

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
| Cópia automática anterior | `infra/railway/terraform.tfstate.backup` | Mesmo cuidado: também tem segredo |
| Arquivos de plano | `~/.helinho-infra/*.tfplan` | Têm os segredos em texto. O `.gitignore` só bloqueia `*.tfplan` a partir da Issue #112: por isso ficam fora do repositório e são apagados depois do `apply` |
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

## Passo 9 — O que fica para depois

Nada disto é feito agora. Cada item tem dono e condição.

| Item | Por que fica para depois | Como será feito |
|---|---|---|
| **Domínio próprio e DNS** | A contratante não definiu (C15) | `railway_custom_domain` no Terraform, em Issue própria; os registros DNS de verificação saem como output. HSTS com `includeSubDomains` e `preload` só então |
| **Ambiente de produção** | Exige ADR próprio para dado real, resposta sobre região e fornecedores (C14, Issue #39), teto de gasto (C04), estado separado e `Seed__Enabled=false` (ADR-0002 §5.2) | Acrescentar `producao` em `ambientes` cria um **segundo projeto** (`helinho-producao`). **Não acrescente agora.** **Confira (ADR-0002 §16, item 5):** nunca crie um segundo ambiente **dentro** do projeto de homologação, nem pelo painel: o provider só lê serviço e volume do ambiente padrão, e o comportamento com dois ambientes não foi testado |
| **Brevo (Sprint 3)** | E-mail real ainda não existe; `Email__Provider = "Fake"` | `TF_VAR_brevo_api_key` + `variaveis_web` (`Email__Provider`, `Brevo__SenderEmail`, `Brevo__SenderName`, `App__PublicBaseUrl`); runbook próprio. A chave passa a ficar em texto no estado |
| **IA (Sprint 4)** | `Ai__Provider = "Fake"` | `TF_VAR_ai_api_key` + `Ai__Provider`, `Ai__Model`; runbook próprio. A chave passa a ficar em texto no estado |
| **`DATABASE_URL` no serviço `Postgres`** | O Terraform não define; sem ela `railway connect` não funciona ([8.4](#84-pg_dump-para-fora-da-plataforma)) | Issue própria em `infra/`, se você quiser o comando da CLI |
| **Deploy contínuo** (`auto_deploy = true`) | Decisão D3: manual por enquanto | Trocar a variável e ativar **Wait for CI** nas configurações do serviço, que o provider não alcança [F5] |
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
| 1 | Conectar o repositório no primeiro `apply` dispara um deploy? Ele respeita o watch pattern? | ADR §16.1; handoff 107, surpresa 2 | [5.4](#54-apply), [6.1](#61-ver-em-que-estado-o-helinho-web-ficou), [6.8](#68-no-próximo-push-na-main-o-deploy-foi-ignorado) |
| 2 | "Deploy Latest Commit" ignora o watch pattern? | ADR §16.2; handoff 107, surpresa 8 | [6.2](#62-publicar-deploy-latest-commit) |
| 3 | O pre-deploy roda em shell ou em forma exec? | ADR §16.3 | [6.3](#63-conferir-o-pre-deploy-migrate) |
| 4 | O healthcheck traz `X-Forwarded-Proto`? | ADR §16.4 | [6.2](#62-publicar-deploy-latest-commit) |
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
| 15 | Todo `apply` com mudança é um deploy | handoff 107, surpresa 7 | [8.1](#81-publicar-uma-versão-nova) |
| 16 | Trocar `seed_password` regrava a senha dos usuários? | handoff 107, surpresa 9 | [8.7](#87-rotação-das-senhas): conferido no código, **não regrava** |
| 17 | A borda sobrescreve `X-Real-IP`; cookie `Secure` e HSTS em ambiente real | ADR §8; handoff 97 | [6.7](#67-conferir-o-comportamento-atrás-do-proxy) |
| 18 | Token de workspace consegue conectar repositório privado de conta pessoal? | este runbook | [Passo 3](#passo-3--github-app-da-railway-no-repositório), [5.5](#55-se-o-apply-falhar-no-meio) |
| 19 | Onde aparecem os logs do pre-deploy no painel | este runbook | [6.3](#63-conferir-o-pre-deploy-migrate) |
| 20 | O túnel `ssh -L` até o contêiner do `Postgres` funciona como a documentação descreve | este runbook | [8.4](#84-pg_dump-para-fora-da-plataforma) |
| 21 | Nome do volume depois de restaurar um backup, visto pelo Terraform | este runbook | [8.5](#85-restore-do-banco) |
| 22 | Janela de 48 h do volume quando o projeto inteiro é apagado | este runbook | [8.8](#88-destruir-o-ambiente) |
| 23 | Onde o id do workspace aparece no painel; qual papel pode criar token de workspace | este runbook | [5.5](#55-se-o-apply-falhar-no-meio), [2.5](#25-criar-o-token-de-workspace) |
| 24 | Alterar o bloco `volume` do `Postgres` apaga o volume no lugar, sem o `prevent_destroy` impedir | segunda revisão do PR #110 (Issue #112) | [8.1](#81-publicar-uma-versão-nova), [8.5](#85-restore-do-banco), [8.8](#88-destruir-o-ambiente) |

Quando um desses pontos for resolvido na prática, troque o "Confira:" pelo fato, em PR `docs`.

## Fontes

Todas consultadas em **18/11/2026**. A interface da Railway muda com frequência: se o painel não bater com o texto, vale o painel, e o runbook é corrigido.

| # | Assunto | Fonte |
|---|---|---|
| F1 | Tipos de token, página de criação, seletor de workspace, cabeçalhos | https://docs.railway.com/integrations/api |
| F2 | Autenticação do provider (`RAILWAY_TOKEN`, token de conta ou de workspace); `projectDelete` no `destroy` | https://registry.terraform.io/providers/terraform-community-providers/railway/0.6.3/docs e o código da tag `v0.6.3` em https://github.com/terraform-community-providers/terraform-provider-railway (`docs/index.md`, `internal/provider/resource_project.graphql`) |
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

Documentos do repositório usados como especificação: `docs/architecture/ADR-0002-hospedagem-railway.md`, `infra/railway/README.md`, `docs/handoff/107.md`, `docs/handoff/94.md`, `docs/handoff/95.md`, `docs/handoff/97.md`, `docs/operations/custos.md` e o `README.md` da raiz.
