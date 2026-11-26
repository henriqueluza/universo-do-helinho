# O token da Railway vem só da variável de ambiente RAILWAY_TOKEN, lida pelo provider.
# Não use o argumento `token` nem variável do Terraform: o valor nunca entra em arquivo.
provider "railway" {}
