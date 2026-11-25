# Infra da Railway (Terraform)

Root module do Terraform que vai descrever a hospedagem do backend na Railway. Por enquanto é só o esqueleto: versões fixadas, provider configurado e nenhum recurso. Os recursos (projeto, ambiente, serviço, banco, variáveis) e o backend de estado entram em Issues próprias.

## Arquivos

| Arquivo | Conteúdo |
|---|---|
| `versions.tf` | Versão do Terraform e do provider `terraform-community-providers/railway` |
| `providers.tf` | Provider da Railway, sem token em arquivo |
| `variables.tf`, `main.tf`, `outputs.tf` | Vazios até a Issue dos recursos |
| `terraform.tfvars.example` | Modelo de `terraform.tfvars` (o arquivo real é ignorado pelo git) |
| `.terraform.lock.hcl` | Hashes do provider para `linux_amd64`, `darwin_arm64` e `darwin_amd64`; fica versionado |
| `.tflint.hcl` | Configuração do `tflint` (ruleset `terraform`, preset `recommended`) |

## Validar localmente

Nenhum destes comandos fala com a Railway nem precisa de token:

```sh
terraform -chdir=infra/railway fmt -check -recursive
terraform -chdir=infra/railway init -backend=false -lockfile=readonly
terraform -chdir=infra/railway validate
(cd infra/railway && tflint --init && tflint)
```

Requer Terraform `>= 1.16.0, < 1.18.0` e, para o lint, `tflint` `>= 0.66`. O workflow `infra` roda os mesmos comandos em todo PR que toca `infra/**`.

## Token

O provider lê o token da variável de ambiente `RAILWAY_TOKEN`. O valor nunca vai para `.tf`, `.tfvars`, log ou commit, e o CI de validação não o recebe.

## `plan` e `apply`

`terraform plan`, `apply` e `destroy` são executados **só pelo Henrique**. Agentes e o CI de PR ficam em `fmt`, `init -backend=false`, `validate` e `tflint`. O passo a passo completo (token, estado, ordem dos comandos, rollback) virá no runbook `docs/operations/railway-runbook.md`.

## Atualizar o provider

PR `chore` próprio, sem recurso junto: mude a versão em `versions.tf`, rode `terraform init -upgrade -backend=false` e regenere o lockfile com

```sh
terraform -chdir=infra/railway providers lock \
  -platform=linux_amd64 -platform=darwin_arm64 -platform=darwin_amd64
```
