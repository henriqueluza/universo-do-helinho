# Custos de hospedagem na Railway

**Consultado em:** 18/11/2026 · **Fontes:** https://railway.com/pricing e https://docs.railway.com/pricing/plans
**Decisão de hospedagem:** `docs/architecture/ADR-0002-hospedagem-railway.md`
**Códigos C04, C13, C14, C15 e C19:** itens do Q&A com a contratante, "Perguntas e Respostas — Universo do Hélinho", mantido pelo Henrique fora do repositório.

Valores em dólar americano, sem impostos, IOF nem câmbio. São **estimativas**: a contratante não informou volume de usuários (C13) e o sistema ainda não rodou na Railway. Meça na primeira semana e corrija a tabela de premissas.

## Resumo

| Cenário | Uso estimado por mês | Fatura no plano Hobby | Fatura no plano Pro |
|---|---|---|---|
| **1. Só homologação** | US$ 4 (entre US$ 2,50 e US$ 8) | US$ 5 (até US$ 8) | US$ 20 |
| **2. Homologação + produção** | US$ 16 (entre US$ 9 e US$ 32) | US$ 16 (entre US$ 9 e US$ 32) | US$ 20 (até US$ 32) |

A fatura é `max(assinatura, uso)`: a assinatura já paga o uso até o valor dela.

Se o workspace da contratante já paga a assinatura por outros projetos, o custo **incremental** deste projeto é só o uso que passar do que estiver incluído e ainda não consumido. Qual plano a conta tem e o que mais roda nela é pergunta para o Igor (C04, C19).

## Como a Railway cobra

**Assinatura por workspace**, que vira crédito de uso do mês (não acumula):

| Plano | Assinatura | Uso incluído | Observações |
|---|---|---|---|
| Hobby | US$ 5/mês | US$ 5 | workspace de um desenvolvedor; logs por 7 dias; imagem de deploy removido guardada por 72 h |
| Pro | US$ 20/mês | US$ 20 | assentos ilimitados sem custo; logs por 30 dias; imagem guardada por 120 h |

**Uso**, medido pelo que o serviço realmente consome, não pelo que está reservado:

| Recurso | Preço |
|---|---|
| Memória | US$ 10 por GB por mês |
| CPU | US$ 20 por vCPU por mês |
| Volume | US$ 0,15 por GB por mês (cobra o espaço usado) |
| Backup de volume | mesmo preço do volume, só sobre o incremento |
| Tráfego de saída (egress) | US$ 0,05 por GB |
| Build | grátis |

Tráfego entre o web e o banco pela rede privada não paga egress.

## Premissas de consumo

Serviços ligados 24 horas por dia, uma réplica cada, sem serverless.

### Homologação (ociosa quase o tempo todo; poucos testadores)

| Item | Premissa (média do mês) | Custo |
|---|---|---|
| `helinho-web`, memória | 0,25 GB | US$ 2,50 |
| `helinho-web`, CPU | 0,02 vCPU | US$ 0,40 |
| `Postgres`, memória | 0,10 GB | US$ 1,00 |
| `Postgres`, CPU | 0,01 vCPU | US$ 0,20 |
| Volume do banco | 0,5 GB usados | US$ 0,08 |
| Egress | 1 GB | US$ 0,05 |
| **Total** | | **US$ 4,23** |

Faixa: de US$ 2,50 (web em 0,15 GB, banco em 0,06 GB) a US$ 7,65 (web em 0,4 GB e 0,05 vCPU, banco em 0,2 GB e 0,02 vCPU, 1 GB de volume, 2 GB de egress).

### Produção (algumas centenas de alunos, pico em eventos)

| Item | Premissa (média do mês) | Custo |
|---|---|---|
| `helinho-web`, memória | 0,5 GB | US$ 5,00 |
| `helinho-web`, CPU | 0,10 vCPU | US$ 2,00 |
| `Postgres`, memória | 0,25 GB | US$ 2,50 |
| `Postgres`, CPU | 0,05 vCPU | US$ 1,00 |
| Volume do banco | 2 GB usados | US$ 0,30 |
| Backups agendados | 2 GB de incremento | US$ 0,30 |
| Egress | 5 GB | US$ 0,25 |
| **Total** | | **US$ 11,35** |

Faixa: de US$ 6 a US$ 24,50 (web em 1 GB e 0,25 vCPU, banco em 0,5 GB e 0,1 vCPU, 5 GB de volume, 5 GB de backup, 20 GB de egress).

"Algumas centenas de alunos" vem do ADR-0001, não da contratante. A produção não está decidida: depende de ADR próprio, da resposta sobre região e fornecedores (C14) e de um teto aprovado (C04).

## O que não entra na conta

- Domínio próprio e DNS (C15, pendente).
- Brevo (Sprint 3) e provedor de IA (Sprint 4).
- HCP Terraform ou bucket de estado: o ADR recomenda estado local, custo zero.
- Segundo ambiente por PR: PR environments ficam desligados.

## Como manter o gasto mínimo

1. **Alerta de gasto por e-mail** no workspace (Workspace → Usage → Set Usage Limits). Sugestão: US$ 10. Precisa de administrador do workspace.
2. **Limite rígido:** ao ser atingido, a Railway desliga **todos** os serviços do workspace, não só os deste projeto. Só o Igor decide se e com que valor.
3. **Limite de CPU e memória por réplica** (configurações do serviço → Deploy → Replica Limits): evita pico inesperado. Não é configurável pelo Terraform; é passo do runbook.
4. **Serverless** (dormir quando ocioso) fica desligado: economizaria no máximo cerca de US$ 3 por mês em homologação e faria a primeira chamada do jogo demorar ou falhar com 502. No plano Pro não economiza nada, porque o uso já cabe na assinatura.
5. **Homologação parada por semanas:** remova o deploy dos dois serviços no painel. Sem deploy ativo só o volume é cobrado (centavos). Para voltar, publique de novo.
6. Confira o uso real em Workspace → Usage depois da primeira semana e atualize as tabelas acima.
