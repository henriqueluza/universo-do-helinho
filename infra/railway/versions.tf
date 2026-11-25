terraform {
  # 1.16 é a versão da máquina do Henrique; 1.17 é a estável atual (usada no CI).
  required_version = ">= 1.16.0, < 1.18.0"

  required_providers {
    railway = {
      source = "terraform-community-providers/railway"
      # Provider 0.x: versão exata. Atualizar é um PR chore próprio (init -upgrade + lock).
      version = "0.6.3"
    }
  }
}
