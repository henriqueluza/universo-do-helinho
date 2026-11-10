# Terraform — AWS, Azure e GCP

Infraestrutura como código para projetos que rodam em hyperscaler. Leia junto com `ci-cd.md` (pipeline), `environments.md` (staging/produção e aprovações) e `observability.md`. Versões do Terraform, dos providers e das ferramentas: use as estáveis atuais e **confirme na documentação atual via Context7**.

**Regra central da casa:** agentes escrevem código Terraform e rodam `plan`. **`apply` só acontece no CI**, depois do merge humano: automático em staging e, em produção, só por disparo manual humano (`workflow_dispatch`).

## 1. Estrutura do repositório

```
infra/
  modules/                 # módulos reutilizáveis, sem estado próprio
    app-service/           # main.tf, variables.tf, outputs.tf, versions.tf, README.md
    postgres/
    storage/
  envs/
    staging/               # root module: backend próprio, chama os módulos
      main.tf  backend.tf  versions.tf  variables.tf  terraform.tfvars  (sem segredos)
    production/
      main.tf  backend.tf  versions.tf  variables.tf  terraform.tfvars
  .tflint.hcl
```

**Diretórios por ambiente, não workspaces.**

| | Diretório por ambiente | Workspaces |
|---|---|---|
| Estado | backend e credenciais separados por ambiente | mesmo backend e credencial para todos |
| Erro humano | `plan` em staging não alcança produção | `terraform workspace select` errado aplica em produção |
| Diferenças entre ambientes | explícitas no código (tamanho, réplicas) | escondidas em `terraform.workspace` e condicionais |
| Permissões no CI | papel de staging ≠ papel de produção | um papel com acesso a tudo |
| Revisão | o diff mostra qual ambiente muda | ambíguo |

Duplicação entre `staging/` e `production/` é aceitável: são poucas linhas chamando módulos. A lógica fica nos módulos.

## 2. Remote state com lock

Nunca use estado local. Cada ambiente tem seu próprio backend, e o bucket/conta de estado tem versionamento ligado.

| Provedor | Backend | Lock |
|---|---|---|
| AWS | `s3` | lock nativo do S3 (`use_lockfile`) em versões recentes; DynamoDB em versões antigas — **confirme na documentação atual qual sua versão suporta** |
| Azure | `azurerm` (Storage Account + container) | lease de blob (automático) |
| GCP | `gcs` | lock nativo do GCS (automático) |

```hcl
# infra/envs/staging/backend.tf (AWS)
terraform {
  backend "s3" {
    bucket       = "acme-tfstate-staging"
    key          = "app/terraform.tfstate"
    region       = "us-east-1"
    encrypt      = true
    use_lockfile = true # confirme suporte na versão em uso
  }
}
```

O bucket de estado, a tabela de lock e os papéis de OIDC são criados uma vez, por um humano, num diretório `infra/bootstrap/` com estado próprio. Agentes não mexem nele.

## 3. Versões fixadas

- `required_version` e `required_providers` com restrição de versão em todo root module e módulo.
- `.terraform.lock.hcl` **commitado** em cada `envs/<ambiente>`. Atualizar provider é um PR `chore` próprio (`terraform init -upgrade`), nunca junto com recurso novo.

```hcl
terraform {
  required_version = "~> 1.x"          # fixe na minor atual — confirme via Context7
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> X.Y" }
  }
}
```

## 4. Qualidade no PR (somente leitura)

| Etapa | Comando | Falha o PR? |
|---|---|---|
| Formato | `terraform fmt -check -recursive infra` | sim |
| Validação | `terraform init -backend=false && terraform validate` (por ambiente) | sim |
| Lint | `tflint --recursive` (com plugin do provedor) | sim |
| Segurança | `trivy config infra` (ou `checkov -d infra`) | sim em HIGH/CRITICAL |
| Plano | `terraform plan -lock=false -out=plan` com credencial **de leitura** | sim se falhar; resultado comentado no PR |

O comentário do `plan` no PR é o que o revisor humano lê. Plano com `destroy` ou `replace` de recurso com dados = PR `needs-human`, sempre.

## 5. Apply: só no CI, só depois do merge

```yaml
# trecho — job de apply (detalhes do pipeline em ci-cd.md)
apply-staging:
  if: github.ref == 'refs/heads/main'
  environment: staging          # automático
  permissions: { id-token: write, contents: read }
# arquivo separado: terraform-production.yml, on: workflow_dispatch (humano)
apply-production:
  environment: production       # restringe a main; required reviewers se o plano permitir
  permissions: { id-token: write, contents: read }
```

- `apply` local é proibido, inclusive para humanos fora de emergência documentada.
- O job de produção aplica o **mesmo commit** já aplicado em staging.

## 6. Autenticação do CI: OIDC, sem chaves estáticas

| Provedor | Mecanismo | O que restringir |
|---|---|---|
| AWS | IAM Role com trust no provedor OIDC `token.actions.githubusercontent.com` (`aws-actions/configure-aws-credentials`) | `sub` = `repo:<owner>/<repo>:environment:<env>` |
| Azure | App registration / managed identity com **federated credential** (`azure/login`) | subject por environment |
| GCP | **Workload Identity Federation** + service account (`google-github-actions/auth`) | `attribute.repository` e environment |

Papéis separados: `plan` (leitura, usado em PRs) e `apply-<env>` (escrita, só no environment). Nunca `AdministratorAccess`/`Owner` no papel de apply sem justificativa no ADR.

## 7. Proteção de dados e higiene

- `lifecycle { prevent_destroy = true }` em todo recurso que guarda dados: bancos, buckets, chaves KMS, volumes.
- Backups/PITR ligados nos bancos gerenciados de produção; deletion protection do provedor também ligada.
- **Tags/labels padrão** via `default_tags` (AWS) ou locals aplicados em todo recurso: `project`, `environment`, `owner`, `managed-by = terraform`, `cost-center`.
- **Naming:** `<projeto>-<ambiente>-<componente>` (ex.: `reservas-stg-api`), respeitando limites de cada provedor (storage account no Azure: só minúsculas e números).

## 8. Segredos

- Nunca em `.tfvars` commitado, `default` de variável ou output sem `sensitive = true`.
- O Terraform cria o **contêiner** do segredo (Secrets Manager / Key Vault / Secret Manager); o **valor** é inserido por humano ou gerado (`random_password`) e lido pela aplicação em runtime.
- Lembre que o estado guarda valores em texto: bucket de estado criptografado e com acesso restrito.

## 9. Equivalência de serviços

| Necessidade | AWS | Azure | GCP |
|---|---|---|---|
| Container gerenciado (API) | ECS Fargate / App Runner | Container Apps | Cloud Run |
| Postgres gerenciado | RDS / Aurora PostgreSQL | Database for PostgreSQL Flexible Server | Cloud SQL for PostgreSQL |
| Storage de objetos | S3 | Blob Storage | Cloud Storage |
| Secret manager | Secrets Manager / SSM Parameter Store | Key Vault | Secret Manager |
| Fila | SQS | Service Bus / Storage Queues | Pub/Sub / Cloud Tasks |
| CDN | CloudFront | Front Door | Cloud CDN |
| Registro de imagens | ECR | Container Registry | Artifact Registry |
| Logs/métricas | CloudWatch | Monitor / Log Analytics | Cloud Logging / Monitoring |

Preferência da casa para APIs: o serviço de container mais gerenciado da tabela (App Runner/Fargate, Container Apps, Cloud Run). Kubernetes só com ADR.

## 10. PRs de infra pequenos

- Um módulo ou recurso por PR quando possível (ex.: PR 1 módulo `postgres`; PR 2 uso em staging; PR 3 uso em produção).
- Mudança de staging e de produção podem ir no mesmo PR só se o diff de produção for idêntico ao de staging.
- Refactors de estado (`moved {}` blocks) em PR próprio, sem mudança de recurso junto.

## O que agentes podem e não podem fazer

| Pode | Não pode |
|---|---|
| Escrever/alterar módulos e root modules | `terraform apply` / `destroy` |
| `fmt`, `validate`, `tflint`, `trivy`/`checkov` | `terraform import` |
| `plan` com credencial de leitura | `terraform state mv` / `rm` / `push` / `force-unlock` |
| Usar blocos `moved {}`/`removed {}` em PR, para revisão | Mexer em `infra/bootstrap/` (estado, papéis OIDC) |
| Comentar o plano no PR | Criar credenciais estáticas ou colocar segredos em código |

## Checklist de PR

- [ ] Um propósito por PR (um módulo/recurso ou um ambiente); dentro do orçamento de linhas.
- [ ] `fmt`, `validate`, `tflint` e `trivy config`/`checkov` verdes.
- [ ] `plan` anexado/comentado; nenhum `destroy`/`replace` inesperado.
- [ ] `prevent_destroy` em recursos com dados; deletion protection ligada em produção.
- [ ] Tags/labels padrão e naming `<projeto>-<ambiente>-<componente>`.
- [ ] Nenhum segredo em `.tfvars`, defaults ou outputs não sensíveis.
- [ ] Versões fixadas; `.terraform.lock.hcl` atualizado só em PR de `chore`.
- [ ] Permissões mínimas nos papéis/identidades criados.
- [ ] Mudança de produção idêntica à já validada em staging.
