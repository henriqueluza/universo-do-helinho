# Runbook — domínio próprio do serviço web na Railway

**Para quem:** Henrique, na própria máquina. **Escrito em:** 22/11/2026 (Issue #220).
**Complementa:** `docs/operations/railway-runbook.md` (o runbook principal; o Passo 9 dele deixava o domínio próprio para depois). As convenções são as mesmas: comandos a partir da raiz do repositório, no **terminal do Terraform**; **✔ Confira:** é o resultado esperado; **Se der errado:** é a saída mais provável; **Confira:** (sem o ✔) marca o que ninguém verificou.

> **Nada deste runbook foi executado.** Nenhum agente rodou `plan` ou `apply`, criou domínio ou registro DNS. O schema do provider foi lido com `terraform providers schema -json` e no código da tag `v0.6.3`; o comportamento da Railway vem da documentação consultada em 22/11/2026 ([Fontes](#fontes), marcas `[D1]`…). O que não tem fonte está em [Fatos não confirmados](#fatos-não-confirmados).

Todos os exemplos usam `paineladm.example.org`. Troque pelo domínio real **só no seu `terraform.tfvars`**, que o git ignora; o domínio real não entra no repositório.

## Antes de começar

| Pergunta | Resposta |
|---|---|
| Subdomínio ou domínio raiz? | **Prefira subdomínio** (`paineladm.example.org`): um `CNAME` comum funciona em qualquer provedor de DNS. Domínio raiz (`example.org`) só funciona em provedor com *CNAME flattening* ou `ALIAS` dinâmico; a Railway cita Cloudflare, DNSimple, Namecheap e bunny.net como compatíveis, e AWS Route 53, Azure DNS, Hostinger, GoDaddy, NameSilo, Hurricane Electric e SquareSpace como **não** compatíveis. A saída indicada é mover os nameservers para a Cloudflare [D1] |
| Quantos domínios o plano permite? | Trial: 1. Hobby: 2 por serviço. Pro: 20 por serviço [D1] |
| Quem mexe no DNS? | Quem administra a zona do domínio (Igor ou a contratante). Você vai precisar criar **dois** registros |
| Curinga (`*.example.org`)? | Não é aceito: exige registros que o provider não expõe |

## Ordem em relação ao primeiro deploy

**Caminho A (recomendado): depois do Passo 6 do runbook principal.** Faça o primeiro `apply` e o primeiro deploy sem domínio, exatamente como o runbook descreve (`Plan: 7 to add`, 17 variáveis), confira `/health/ready` no endereço `*.up.railway.app` e só então siga os passos abaixo. Motivo: preencher o domínio acrescenta `App__PublicBaseUrl` às variáveis do web, e mudar as variáveis **reimplanta** o serviço (README do Terraform, L4). Com um deploy anterior no ar, essa reimplantação é um deploy comum.

**Caminho B: junto com o primeiro `apply`.** Preencha o domínio no 4.1 do runbook principal. Mudam três conferências do 5.3 de lá: o resumo é `Plan: 8 to add` (entra `railway_custom_domain.web[0]`); são **14** nomes de variável (entra `App__PublicBaseUrl`); e o `grep -c -e railway_tcp_proxy -e railway_custom_domain` deixa de imprimir `0`. O domínio próprio só responde depois do primeiro deploy (Passo 6 de lá). Ganha-se tempo de propagação do DNS; perde-se a correspondência exata com o runbook principal.

O que **não** fazer: preencher o domínio entre o primeiro `apply` e o primeiro deploy. A reimplantação sem deploy anterior é o ponto fraco conhecido do provider (runbook principal, 5.5: `Unable to redeploy service…`).

## Passo 1 — Preencher o domínio

Em `infra/railway/terraform.tfvars`, descomente `ambientes` e preencha `dominio_proprio`:

```hcl
ambientes = {
  homologacao = { seed_enabled = true, dominio_proprio = "paineladm.example.org" }
}
```

Se você já tinha definido `subdominio`, mantenha-o na mesma linha. Só o nome do host, em minúsculas: sem `https://`, sem barra, sem porta.

`App__PublicBaseUrl` passa a valer `https://paineladm.example.org` sem você definir nada. Só preencha `variaveis_web.App__PublicBaseUrl` se quiser outro valor (o explícito vence).

## Passo 2 — `plan`

Com `RAILWAY_TOKEN` e `TF_VAR_seed_password` exportados como no 4.3 do runbook principal:

```sh
terraform -chdir=infra/railway plan -out="$HOME/.helinho-infra/homologacao.tfplan"
```

**✔ Confira (caminho A):**

1. Resumo **`Plan: 1 to add, 1 to change, 0 to destroy.`**
2. A criar: `module.ambiente["homologacao"].railway_custom_domain.web[0]`, com `domain = "paineladm.example.org"` e `target_port = 8080`.
3. A alterar no lugar: `module.ambiente["homologacao"].railway_variable_collection.web` (os valores aparecem como sensíveis).
4. Em `Changes to Outputs`: `dns_dominio_proprio` (conhecido só depois do `apply`) e, em `ambientes`, `url_base_app = "https://paineladm.example.org"` e `App__PublicBaseUrl` na lista `variaveis_web`.

**Se der errado:**

- `dominio_proprio aceita só o nome do host em minúsculas…` → tire `https://`, a barra final, a porta ou as maiúsculas.
- Aparece `destroy` ou `replace` de qualquer recurso, ou alteração no `Postgres` → **não aplique**; compare o `terraform.tfvars` com o que estava aplicado.

## Passo 3 — `apply`

Este `apply` reimplanta o `helinho-web` (a troca de variáveis dispara um deploy). Rode em horário combinado, como todo `apply` com mudança.

```sh
terraform -chdir=infra/railway apply "$HOME/.helinho-infra/homologacao.tfplan"
```

```sh
rm "$HOME/.helinho-infra/homologacao.tfplan"
```

**✔ Confira:** `Apply complete! Resources: 1 added, 1 changed, 0 destroyed.` No painel, o `helinho-web` tem um deploy novo que fica verde.

Refaça o backup cifrado do estado (5.7 do runbook principal): o estado mudou.

**Se der errado:**

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| `Unable to create custom domain` | O domínio já está em outro serviço ou projeto da Railway, ou o plano atingiu o limite de domínios (**Confira:** o texto do erro não foi observado) | Leia a mensagem; libere o domínio onde ele está ou escolha outro; repita `plan` e `apply` |
| `Unable to redeploy service after variable collection updated` | As variáveis **já foram gravadas**; a Railway recusou a reimplantação | No painel, `helinho-web` → `Cmd+K` → **Deploy Latest Commit**; depois repita `plan` e `apply` |

## Passo 4 — Criar os registros no DNS

```sh
terraform -chdir=infra/railway output dns_dominio_proprio
```

A saída tem **dois** registros por ambiente. Formato (os valores abaixo são ilustrativos; use os da sua saída):

```hcl
{
  "homologacao" = [
    { "nome" = "painel", "tipo" = "CNAME", "valor" = "abc123.up.railway.app", "zona" = "example.org" },
    { "nome" = "_railway-verify.painel", "tipo" = "TXT", "valor" = "railway-verify=…", "zona" = "example.org" },
  ]
}
```

Cadastre os dois no provedor de DNS da zona, copiando `nome` e `valor` como vieram:

| Registro | Para que serve | Sem ele |
|---|---|---|
| `CNAME` | Aponta o host para a Railway | O domínio não resolve |
| `TXT` | Prova a posse do domínio. É público e não é segredo | O domínio não é verificado e responde `404`, mesmo com o `CNAME` certo [D1] |

Os dois são obrigatórios [D1]. `nome` é relativo à `zona`: se o seu provedor pede o nome completo, junte os dois (`paineladm.example.org`). **Confira:** os mesmos registros aparecem no painel (`helinho-web` → **Settings** → **Public Networking** → o domínio); se o painel mostrar nome ou valor diferente da saída, vale o painel, e isso vira Issue.

Na Cloudflare, crie o `CNAME` como **DNS only** (nuvem cinza) até o domínio ser verificado. Se depois ligar o proxy (nuvem laranja), o modo SSL/TLS tem de ser **Full**; **Full (Strict) não funciona** [D1].

## Passo 5 — Conferir a propagação

Troque pelos nomes da sua saída:

```sh
dig +short CNAME paineladm.example.org
```

```sh
dig +short TXT _railway-verify.paineladm.example.org
```

**✔ Confira:** o primeiro imprime o `valor` do `CNAME` (com um ponto no fim) e o segundo, o `valor` do `TXT` entre aspas. Repita com `@1.1.1.1` e `@8.8.8.8` depois de `dig` para ver resolvedores públicos.

**Se der errado:** saída vazia → o registro ainda não propagou ou foi criado com o nome errado (nome duplicado, como `paineladm.example.org.example.org`, é o erro comum). A propagação pode levar até 72 horas [D1]; em geral são minutos.

## Passo 6 — Conferir a verificação e o certificado

A Railway emite o certificado sozinha (Let's Encrypt, 90 dias, renovação automática) [D1]. A emissão costuma sair em até uma hora depois de o DNS estar certo, e pode levar até 72 horas [D1][D2].

No painel, em **Public Networking**, o domínio ganha uma marca verde quando é verificado [D1]. No terminal:

```sh
curl -sS -o /dev/null -w '%{http_code}\n' https://paineladm.example.org/health/ready
```

**✔ Confira:** imprime `200`.

```sh
echo | openssl s_client -connect paineladm.example.org:443 -servername paineladm.example.org 2>/dev/null | openssl x509 -noout -subject -issuer -dates
```

**✔ Confira:** `subject` com o seu domínio, `issuer` da Let's Encrypt e `notAfter` cerca de 90 dias à frente.

Depois repita, com `BASE=https://paineladm.example.org`, as conferências 6.5 a 6.7 do runbook principal (login no painel, `POST /api/login`, cookie `Secure` e HSTS). O cookie de sessão vale por host: quem entrou pelo endereço `*.up.railway.app` entra de novo pelo domínio próprio.

### Se o certificado não sair

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| `404` no domínio, `dig` do `CNAME` certo | `TXT` ausente ou errado [D1] | Corrija o `TXT` (Passo 4) |
| Erro de certificado no `curl` ou no navegador, menos de uma hora depois do DNS | Emissão em andamento | Espere pelo menos uma hora antes de investigar [D2] |
| Continua sem certificado | Registros errados ou não propagados; registro `CAA` que não inclui a Let's Encrypt; DNSSEC mal configurado; proxy da Cloudflare ligado [D2] | `dig +short CAA example.org`: se houver `CAA`, inclua `letsencrypt.org` (sem nenhum `CAA`, não é esse o problema). Confira o DNSSEC em dnsviz.net. Na Cloudflare, desligue o proxy, espere a marca verde e religue [D2] |
| `ERR_TOO_MANY_REDIRECTS` com Cloudflare | Modo SSL/TLS diferente de Full [D1] | Mude para **Full** |
| Mais de 72 horas sem certificado | — | Suporte da Railway [D2] |

**Não apague e recrie o domínio para "destravar".** Os limites da Let's Encrypt podem bloquear a emissão por 7 dias [D2]. No Terraform, trocar o valor de `dominio_proprio` (ou esvaziar e preencher de novo) é apagar e recriar.

## Passo 7 — Registrar

1. Em `docs/operations/ambientes.md`, registre a URL do domínio próprio ao lado da `url_publica`.
2. Para o desenvolvedor Unity, a base da API passa a ser `https://paineladm.example.org/api`. As saídas `url_publica` e `url_api` continuam com o endereço `*.up.railway.app`, que segue funcionando.
3. HSTS com `includeSubDomains` e `preload` continua fora (Passo 9 do runbook principal): é mudança de aplicação, em Issue própria.

## Remover o domínio próprio

Volte `dominio_proprio` para `""` (ou apague o campo), rode `plan` (`1 to change, 1 to destroy`: sai o `railway_custom_domain.web[0]` e a variável `App__PublicBaseUrl`) e `apply`. O serviço web é reimplantado. Depois apague os dois registros no DNS. Não apague o domínio pelo painel: o provider passa a falhar na leitura (`Unable to find custom domain`).

## Fatos não confirmados

| # | Fato | Onde aparece |
|---|---|---|
| 1 | Formato exato de `nome` nos dois registros (rótulo relativo à zona? `@` no domínio raiz? prefixo do `TXT`). A documentação não mostra o nome do `TXT`; o exemplo `_railway-verify.painel` é ilustrativo | Passos 4 e 5 |
| 2 | O provider 0.6.3 lê só o primeiro registro DNS devolvido pela Railway e não devolve o tipo; `CNAME` vem da documentação | Passo 4 |
| 3 | Texto do erro quando o domínio já está em uso ou o plano atingiu o limite | Passo 3 |
| 4 | Criar o domínio próprio não afeta o domínio `*.up.railway.app` (a documentação não diz nada; o provider só chama a criação) | Passo 7 |
| 5 | Tempo real de verificação e de emissão do certificado neste workspace | Passo 6 |
| 6 | Provedor de DNS da contratante e se ele aceita `CNAME`/`ALIAS` no domínio raiz | Antes de começar |

Quando um desses pontos for resolvido na prática, troque o "Confira:" pelo fato, em PR `docs`.

## Fontes

Consultadas em **22/11/2026**.

| # | Assunto | Fonte |
|---|---|---|
| D1 | Domínio próprio: `CNAME` e `TXT` obrigatórios, `404` sem o `TXT`, domínio raiz e provedores, limites por plano, prazos, Let's Encrypt, Cloudflare | https://docs.railway.com/networking/domains/working-with-domains |
| D2 | Certificado que não sai: prazos, causas (`CAA`, DNSSEC, proxy), não apagar e recriar | https://docs.railway.com/networking/troubleshooting/ssl |
| D3 | `railway_custom_domain` no provider 0.6.3: atributos, o que recria, leitura | `terraform providers schema -json` e `internal/provider/resource_custom_domain.go` na tag `v0.6.3` de https://github.com/terraform-community-providers/terraform-provider-railway |
