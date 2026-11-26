locals {
  porta    = 8080
  pg_dados = "/var/lib/postgresql/data"

  # Referências `${{Serviço.VAR}}`, resolvidas pela Railway no deploy: a senha do banco não é escrita aqui.
  pg = { for v in ["PGHOST", "PGPORT", "PGDATABASE", "PGUSER", "PGPASSWORD"] : v => "$${{${railway_service.postgres.name}.${v}}}" }

  # Com domínio próprio, App__PublicBaseUrl vale https://<domínio>, salvo valor explícito em variaveis_web.
  url_dominio  = var.dominio_proprio == "" ? "" : "https://${var.dominio_proprio}"
  url_base_app = var.variaveis_web.App__PublicBaseUrl != "" ? var.variaveis_web.App__PublicBaseUrl : local.url_dominio

  valores_web = merge(var.variaveis_web, var.segredos_web, {
    App__PublicBaseUrl           = local.url_base_app
    ASPNETCORE_ENVIRONMENT       = var.aspnetcore_environment
    PORT                         = tostring(local.porta)
    Proxy__TrustForwardedHeaders = "true"
    # Timeout curto: com o banco fora, cada chamada a /health/ready segura uma conexão até esse limite.
    ConnectionStrings__Default = "Host=${local.pg.PGHOST};Port=${local.pg.PGPORT};Database=${local.pg.PGDATABASE};Username=${local.pg.PGUSER};Password=${local.pg.PGPASSWORD};Timeout=5;Command Timeout=30"
    Seed__Enabled              = var.seed_enabled ? "true" : ""
    HELINHO_SEED_PASSWORD      = var.seed_enabled ? var.seed_password : ""
  })
  # Variável vazia não é enviada (não se sabe se a API aceita valor vazio). Só os nomes perdem a marca
  # de sensível, para o plano mostrar quais variáveis mudam; os valores continuam ocultos.
  nomes_web = nonsensitive([for nome, valor in local.valores_web : nome if valor != ""])
}

resource "railway_project" "this" {
  name           = "helinho-${var.nome}"
  description    = "Universo do Hélinho (${var.nome}), gerenciado por Terraform em infra/railway"
  workspace_id   = var.workspace_id
  private        = true
  has_pr_deploys = false

  default_environment = { name = var.nome }
}

resource "random_password" "postgres" {
  length  = 32
  special = false # só letras e dígitos: não exige escape na connection string
}

# Sem railway_service_domain e sem railway_tcp_proxy: o banco só existe na rede privada do ambiente.
resource "railway_service" "postgres" {
  name         = "Postgres"
  project_id   = railway_project.this.id
  source_image = var.postgres_imagem
  regions      = [{ region = var.regiao }]

  volume = {
    name       = "postgres-dados"
    mount_path = local.pg_dados
  }

  lifecycle {
    prevent_destroy = true # apagar o serviço apaga o volume do banco
  }
}

resource "railway_variable_collection" "postgres" {
  environment_id = railway_project.this.default_environment.id
  service_id     = railway_service.postgres.id

  variables = [
    { name = "POSTGRES_DB", value = "helinho" },
    { name = "POSTGRES_USER", value = "helinho" },
    { name = "POSTGRES_PASSWORD", value = random_password.postgres.result },
    # O ponto de montagem do volume não nasce vazio; o PostgreSQL exige um subdiretório.
    { name = "PGDATA", value = "${local.pg_dados}/pgdata" },
    { name = "PGHOST", value = "$${{RAILWAY_PRIVATE_DOMAIN}}" },
    { name = "PGPORT", value = "5432" },
    { name = "PGDATABASE", value = "$${{POSTGRES_DB}}" },
    { name = "PGUSER", value = "$${{POSTGRES_USER}}" },
    { name = "PGPASSWORD", value = "$${{POSTGRES_PASSWORD}}" },
  ]
}

resource "railway_service" "web" {
  name               = "helinho-web"
  project_id         = railway_project.this.id
  source_repo        = var.repositorio
  source_repo_branch = var.branch
  # O provider não liga nem desliga o auto-deploy: um padrão que nenhum arquivo casa faz a Railway ignorar os pushes.
  watch_patterns             = var.auto_deploy ? [] : ["/.auto-deploy-desligado"]
  predeploy_command          = "tini -- dotnet Helinho.Web.dll migrate"
  predeploy_timeout          = 300
  healthcheck_path           = "/health/ready"
  healthcheck_timeout        = 120
  restart_policy_type        = "ON_FAILURE"
  restart_policy_max_retries = 5
  serverless                 = var.serverless
  regions                    = [{ region = var.regiao }]

  # O banco recebe as variáveis e é reimplantado antes de o web ser criado.
  depends_on = [railway_variable_collection.postgres]
}

resource "railway_service_domain" "web" {
  environment_id = railway_project.this.default_environment.id
  service_id     = railway_service.web.id
  subdomain      = var.subdominio
  target_port    = local.porta
}

# Só existe com `dominio_proprio` preenchido. Mudar o domínio recria o recurso (e os registros DNS mudam).
resource "railway_custom_domain" "web" {
  count          = var.dominio_proprio == "" ? 0 : 1
  environment_id = railway_project.this.default_environment.id
  service_id     = railway_service.web.id
  domain         = var.dominio_proprio
  target_port    = local.porta
}

resource "railway_variable_collection" "web" {
  environment_id = railway_project.this.default_environment.id
  service_id     = railway_service.web.id
  variables      = [for nome in local.nomes_web : { name = nome, value = local.valores_web[nome] }]
}
