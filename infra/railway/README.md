# Infra da Railway (Terraform)

Root module do Terraform que descreve a hospedagem do backend na Railway, conforme o ADR-0002 (`docs/architecture/ADR-0002-hospedagem-railway.md`, PR #101). Um `apply` cria, por ambiente: o projeto, o PostgreSQL 17 com volume, o serviço web ligado ao repositório, as variáveis dos dois serviços e o domínio `*.up.railway.app`. O domínio próprio é opcional (seção [Domínio próprio](#domínio-próprio)).

**`terraform plan`, `apply` e `destroy` são executados só pelo Henrique.** Agentes e o CI ficam em `fmt`, `init -backend=false`, `validate` e `tflint`, que não falam com a Railway e não precisam de token. O passo a passo de operação (token, estado, primeiro `apply`, rollback, backup) vai para o runbook `docs/operations/railway-runbook.md`, em Issue própria.

## Arquivos

| Arquivo | Conteúdo |
|---|---|
| `versions.tf` | Versão do Terraform e dos providers `terraform-community-providers/railway` (exata) e `hashicorp/random` |
| `providers.tf` | Provider da Railway, sem token em arquivo |
| `variables.tf` | Entradas, com as validações (seed, senha, provedores) |
| `main.tf` | Uma instância do módulo `ambiente` por chave de `var.ambientes` |
| `outputs.tf` | URLs e ids por ambiente; nenhuma saída com segredo |
| `modules/ambiente/` | Os recursos de um ambiente: projeto, `Postgres`, `helinho-web`, variáveis e domínio |
| `tests/validacoes.tftest.hcl` | `terraform test` das validações, com providers simulados |
| `terraform.tfvars.example` | Modelo de `terraform.tfvars` (o arquivo real é ignorado pelo git) |
| `.terraform.lock.hcl` | Hashes dos providers para `linux_amd64`, `darwin_arm64` e `darwin_amd64`; fica versionado |
| `.tflint.hcl` | Configuração do `tflint` (ruleset `terraform`, preset `recommended`) |

## O que é criado, por ambiente

| Recurso | O que é |
|---|---|
| `railway_project.this` | Projeto `helinho-<ambiente>`, privado, sem PR deploys, com o ambiente padrão renomeado para `<ambiente>` |
| `random_password.postgres` | Senha do banco (32 letras e dígitos), gerada no `apply`; fica só no estado e na Railway |
| `railway_service.postgres` | Serviço `Postgres`, imagem `postgres:17.<minor>`, volume `postgres-dados` em `/var/lib/postgresql/data`. Sem domínio público e sem TCP proxy: só rede privada |
| `railway_variable_collection.postgres` | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `PGDATA` e os espelhos `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`, `PGPASSWORD` |
| `railway_service.web` | Serviço `helinho-web`: repositório GitHub, branch `main`, Dockerfile da raiz; pre-deploy `tini -- dotnet Helinho.Web.dll migrate` (300 s); healthcheck `/health/ready` (120 s); restart `ON_FAILURE` com 5 tentativas |
| `railway_service_domain.web` | Domínio `<subdomínio>.up.railway.app` apontando para a porta 8080 |
| `railway_custom_domain.web` | **Só com `dominio_proprio` preenchido:** o domínio próprio do serviço web, na porta 8080 |
| `railway_variable_collection.web` | Variáveis do serviço web (tabela abaixo) |

O Dockerfile não é indicado por atributo: havendo `Dockerfile` na raiz, a Railway sempre usa ele (ADR-0002 §4.2). Não existe `railway.toml`.

## Validar localmente

```sh
terraform -chdir=infra/railway fmt -check -recursive
terraform -chdir=infra/railway init -backend=false -lockfile=readonly
terraform -chdir=infra/railway validate
(cd infra/railway && tflint --init && tflint --recursive)
terraform -chdir=infra/railway test
```

O `terraform test` roda `tests/validacoes.tftest.hcl` com os dois providers simulados (`mock_provider`): só planeja em memória, sem token e sem falar com a Railway. É ele que exercita as validações de `variables.tf` e o caso com produção ligada.

Requer Terraform `>= 1.16.0, < 1.18.0` e, para o lint, `tflint` `>= 0.66`. O `--recursive` inclui `modules/ambiente`.

O `validate` não recebe valores de variável: as validações de `variables.tf` só rodam no `plan`. Por construção, a configuração é a mesma com produção ligada ou desligada, porque `producao` é só mais uma chave do mesmo `for_each`.

## Variáveis de entrada

Tudo tem padrão, menos `seed_password`. Valores sem segredo vão em `terraform.tfvars` (copie de `terraform.tfvars.example`); segredo vai em variável de ambiente, só no shell do Terraform.

| Variável | Obrigatória? | Sensível? | De onde vem | Padrão |
|---|---|---|---|---|
| `RAILWAY_TOKEN` (ambiente, não é variável do Terraform) | sim | sim | token de **workspace** criado no painel da Railway | — |
| `seed_password` | sim, enquanto algum ambiente tiver `seed_enabled` | sim | `TF_VAR_seed_password`; você escolhe (16+ caracteres, com minúscula, maiúscula, dígito e símbolo) | sem padrão |
| `workspace_id` | só se o token enxergar mais de um workspace | não | painel da Railway | `null` |
| `ambientes` | não | não | `terraform.tfvars` | só `homologacao`, com seed ligado |
| `repositorio` | não | não | `terraform.tfvars` | este repositório |
| `branch` | não | não | `terraform.tfvars` | `main` |
| `auto_deploy` | não | não | `terraform.tfvars` | `false` |
| `regiao` | não | não | `terraform.tfvars` | `us-east4-eqdc4a` |
| `serverless` | não | não | `terraform.tfvars` | `false` |
| `postgres_imagem` | não | não | `terraform.tfvars` | `postgres:17.12` |
| `variaveis_web` | não | não | `terraform.tfvars` | padrões do README da raiz para `Api__Auth__*`; `Fake` nos provedores; o resto vazio |
| `brevo_api_key` | só com `Email__Provider = "Brevo"` (Sprint 3) | sim | `TF_VAR_brevo_api_key`; painel da Brevo | vazio |
| `ai_api_key` | só com `Ai__Provider` diferente de `Fake` (Sprint 4) | sim | `TF_VAR_ai_api_key`; painel do provedor de IA | vazio |

A senha do banco não é variável: o Terraform gera.

### Variáveis que o serviço web recebe

| Variável na Railway | Valor | Origem |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` em `homologacao`, `Production` em `producao` | fixo pelo nome do ambiente (`main.tf`); não há como pedir `Development` |
| `PORT` | `8080` | fixo; é a porta do domínio e do healthcheck |
| `Proxy__TrustForwardedHeaders` | `true` | fixo |
| `ConnectionStrings__Default` | `Host=${{Postgres.PGHOST}};Port=…;Database=…;Username=…;Password=${{Postgres.PGPASSWORD}};Timeout=5;Command Timeout=30` | montada no módulo por referência; a Railway resolve no deploy. O valor resolvido contém a senha do banco |
| `Seed__Enabled` | `true` | só quando `seed_enabled`; só é aceito em `homologacao` |
| `HELINHO_SEED_PASSWORD` | `seed_password` | idem |
| `Api__Auth__TokenLifetime`, `…MaxFailedAttempts`, `…LockoutDuration`, `…LoginRateLimit`, `…LoginRateWindow` | `08:00:00`, `5`, `00:15:00`, `10`, `00:01:00` | `variaveis_web` |
| `Email__Provider`, `Ai__Provider` | `Fake` | `variaveis_web` |
| `Brevo__SenderEmail`, `Brevo__SenderName`, `Ai__Model` | não enviadas enquanto vazias | `variaveis_web` |
| `App__PublicBaseUrl` | `https://<dominio_proprio>` quando o ambiente tem domínio próprio; não enviada sem domínio | `variaveis_web`, se preenchida (vence o domínio); senão `ambientes.<nome>.dominio_proprio` |
| `Brevo__ApiKey`, `Ai__ApiKey` | não enviadas enquanto vazias | `brevo_api_key`, `ai_api_key` |

`variaveis_web` é um objeto fechado: só aceita as chaves declaradas em `variables.tf`. Variável nova entra por PR. De propósito, **não** são definidas `ASPNETCORE_FORWARDEDHEADERS_ENABLED` nem `AllowedHosts` (o healthcheck da Railway chega com `Host: healthcheck.railway.app`).

O provider marca todo valor de variável como sensível: o `plan` mostra os nomes e esconde os valores, inclusive os que não são segredo.

### Validações

O `plan` falha, antes de falar com a Railway, quando:

- algum ambiente diferente de `homologacao` tem `seed_enabled = true`;
- o nome do ambiente não é `homologacao` nem `producao`;
- `dominio_proprio` não é só um nome de host em minúsculas (tem `https://`, barra, porta, curinga, maiúscula ou um rótulo só);
- o seed está ligado e `seed_password` está ausente ou é fraca;
- `Email__Provider` não é `Fake` nem `Brevo`, ou é `Brevo` sem `brevo_api_key`;
- `Ai__Provider` é diferente de `Fake` sem `ai_api_key`;
- `postgres_imagem` não é `postgres:17` ou `postgres:17.<minor>`.

`null` explícito vale o padrão da variável (`nullable = false`) e passa pelas mesmas validações. Só `workspace_id` e `seed_password` aceitam `null`, que neles quer dizer "não definido".

## Saídas

| Saída | Conteúdo |
|---|---|
| `url_publica` | `https://<subdomínio>.up.railway.app`, por ambiente |
| `url_api` | a URL pública com `/api`, por ambiente |
| `dns_dominio_proprio` | por ambiente: os registros a cadastrar no DNS (`tipo`, `nome`, `zona`, `valor`); lista vazia sem domínio próprio |
| `ambientes` | por ambiente: `projeto_id`, `ambiente_id`, nome e id dos serviços, os nomes das variáveis enviadas ao web e `url_base_app` (o valor de `App__PublicBaseUrl`; vazio = não enviada) |

Nenhuma saída carrega segredo. A senha do banco se lê no painel (variável `PGPASSWORD` do serviço `Postgres`) quando o runbook precisar dela.

## Domínio próprio

Opcional e desligado por padrão. Em `terraform.tfvars`:

```hcl
ambientes = {
  homologacao = { seed_enabled = true, dominio_proprio = "painel.example.org" }
}
```

Com o campo preenchido, o módulo cria `railway_custom_domain.web` e `App__PublicBaseUrl` passa a valer `https://painel.example.org`. Para outro valor, defina `variaveis_web.App__PublicBaseUrl`: o valor explícito vence. Hoje nenhum código da aplicação lê essa variável (ela é da Sprint 3). O domínio `*.up.railway.app` continua existindo, e `url_publica` e `url_api` continuam apontando para ele.

Depois do `apply`, `terraform output dns_dominio_proprio` traz os dois registros que a Railway exige: o `CNAME` do host e o `TXT` de verificação de posse. O `TXT` vai para o DNS público e não é segredo. `nome` é relativo à `zona`. O passo a passo (ordem em relação ao primeiro deploy, DNS, `dig`, certificado) está em `docs/operations/railway-dominio.md`.

Schema do `railway_custom_domain` no provider 0.6.3, conferido com `terraform providers schema -json` e no código da tag `v0.6.3` (`internal/provider/resource_custom_domain.go`):

| Atributo | Tipo | Observação |
|---|---|---|
| `domain`, `environment_id`, `service_id` | obrigatórios | mudar qualquer um **recria** o recurso; os registros DNS mudam junto |
| `target_port` | opcional | atualizado no lugar; aqui é sempre 8080 |
| `host_label`, `zone`, `dns_record_value` | computados | o registro que aponta o host para a Railway. O provider não devolve o tipo: a documentação da Railway diz `CNAME` |
| `verification_host_label`, `verification_record_value` | computados | o registro `TXT` de verificação |
| `id`, `project_id` | computados | |

Acrescentar o domínio a um ambiente que já existe muda `railway_variable_collection.web` (entra `App__PublicBaseUrl`): pelo L4, esse `apply` reimplanta o serviço web. A aplicação não restringe o host (`AllowedHosts` é `*`) e nada precisa mudar nela.

## Deploy contínuo (`auto_deploy`)

O provider não tem atributo para ligar ou desligar o auto-deploy. Com `auto_deploy = false` (padrão), o serviço web recebe o watch pattern `/.auto-deploy-desligado`, que nenhum arquivo do repositório casa: a Railway ignora os pushes e a publicação é manual, pelo painel (`Cmd+K` → **Deploy Latest Commit**). Não crie arquivo com esse nome na raiz do repositório.

Com `auto_deploy = true` a lista fica vazia e todo push em `main` implanta. Ative também **Wait for CI** nas configurações do serviço, que o provider não alcança.

## Produção

`producao` existe no código e nasce desligada: o padrão de `ambientes` só tem `homologacao`. Acrescentar a chave `producao` cria um segundo projeto Railway (`helinho-producao`) com o mesmo módulo. Antes disso o ADR-0002 §5.2 exige ADR próprio para dado real, resposta sobre região e fornecedores, teto de gasto e estado do Terraform separado por ambiente. O seed é recusado em produção.

## Limitações do provider comunitário e contornos

Provider `terraform-community-providers/railway` 0.6.3, conferido no código da tag `v0.6.3` e com `terraform providers schema -json`.

| # | Limitação | Contorno |
|---|---|---|
| L1 | Não cria PostgreSQL gerenciado nem por template | Serviço de imagem `postgres:17.<minor>` com volume, declarado aqui |
| L2 | Não tem atributo de auto-deploy; conectar o repositório cria o gatilho da branch | Watch pattern que nunca casa (seção acima). Plano B: botão **Disable** de auto-deploy no painel |
| L3 | `railway_service` não tem `environment_id`: configuração e volume são lidos e gravados só no ambiente padrão do projeto | Um projeto por ambiente, com o ambiente padrão renomeado. `railway_environment` não é usado |
| L4 | Criar ou mudar `railway_variable_collection`, ou mudar o `railway_service`, **reimplanta** o serviço | Todo `apply` com mudança é um deploy; rode em horário combinado |
| L5 | Só aceita token de conta ou de workspace (`Authorization: Bearer`) | Token de workspace em `RAILWAY_TOKEN`. A CLI da Railway usa esse mesmo nome para token de projeto: não rode a CLI no shell do Terraform |
| L6 | Tamanho do volume é somente leitura | Vale o padrão do plano da conta |
| L7 | Versão 0.x, com remoções entre versões | Versão exata em `versions.tf` e lockfile versionado |
| L8 | O serviço só entra no estado no fim da criação. Se o `apply` falhar no meio (volume, conexão do repositório, leitura), o serviço fica criado na Railway e fora do estado | Apague o serviço órfão no painel antes de repetir o `apply` |
| L9 | Mudar `workspace_id` ou o nome do ambiente recria o projeto; mudar `project_id` recria os serviços | O `prevent_destroy` do serviço `Postgres` faz o `plan` falhar em vez de apagar o banco |
| L10 | `subdomain` é obrigatório e único em toda a Railway | Padrão `helinho-<ambiente>`; se já existir, defina `ambientes.<nome>.subdominio` |
| L11 | Não configura limite de CPU e memória, agenda de backup do volume, alerta de gasto nem Wait for CI | Cliques no painel, descritos no runbook |
| L12 | `railway_custom_domain` só lê o primeiro registro DNS que a Railway devolve e falha na leitura (`Unable to find custom domain`) se o domínio for apagado pelo painel | Não apague o domínio pelo painel; para tirá-lo, esvazie `dominio_proprio` e aplique. Curinga (`*.`) é recusado pela validação, porque exige registros que o provider não expõe |

O serviço `Postgres` tem `prevent_destroy`. Destruir a homologação de propósito exige remover essa linha em um commit; o runbook descreve.

**O `prevent_destroy` não protege o volume contra edição do serviço.** No provider 0.6.3 o volume é um bloco do `railway_service`, não um recurso próprio: remover o bloco `volume` do serviço `Postgres` é uma atualização no lugar, e nela o provider apaga o volume (e os dados do banco) sem recriar o serviço. No `plan` isso aparece como atualização no lugar do serviço, com o `volume` removido, e não como destruição. Nunca aplique um plano que remova esse bloco.

## Antes do primeiro `apply`

O healthcheck do serviço web aponta para `/health/ready`, rota criada pelo PR #104. **Esse PR precisa estar na `main` antes do primeiro `apply`:** sem ele a rota não existe (a `main` só tem `/health`), o healthcheck não recebe 200 dentro dos 120 s e todo deploy do `helinho-web` falha.

O pre-deploy `tini -- dotnet Helinho.Web.dll migrate` depende do Dockerfile (PR #105) e do comando `migrate` (PR #102), que já estão na `main`.

## Estado

O estado (`terraform.tfstate`) guarda **em texto**:

- a senha do PostgreSQL (`random_password.postgres` e a variável `POSTGRES_PASSWORD`);
- a senha do seed (`HELINHO_SEED_PASSWORD`);
- as chaves da Brevo e do provedor de IA (`Brevo__ApiKey`, `Ai__ApiKey`), quando definidas.

Ele é local, fica fora do git (`.gitignore`) e precisa de backup cifrado depois de cada `apply` (ADR-0002 §10.3, decisão D2). Quem lê o arquivo lê todos esses segredos.

O plano salvo com `terraform plan -out=<arquivo>` guarda os mesmos valores. O `.gitignore` bloqueia `*.tfplan` e `tfplan`: use um desses nomes e apague o arquivo depois do `apply`.

## Atualizar um provider

PR `chore` próprio, sem recurso junto: mude a versão em `versions.tf` (e em `modules/ambiente/versions.tf`), rode `terraform init -upgrade -backend=false` e regenere o lockfile com

```sh
terraform -chdir=infra/railway providers lock \
  -platform=linux_amd64 -platform=darwin_arm64 -platform=darwin_amd64
```
