# Ambientes

Registro do que está publicado e de como acessar. Quem mantém é o Henrique, depois de cada `terraform apply`, rotação ou destruição.

**Regras deste arquivo:** nenhuma senha, token, chave, id de workspace ou dado pessoal. Só URL, nomes, datas e onde cada segredo está guardado. Os campos `<a preencher>` são preenchidos no [Passo 7 do runbook](railway-runbook.md#passo-7--url-para-o-desenvolvedor-unity-e-registro-do-ambiente).

| | Homologação | Produção |
|---|---|---|
| Situação | `<a preencher: no ar desde DD/MM/AAAA>` | **Desligada.** Não existe na Railway |
| Para quê | Validar o que foi mergeado; integração com o jogo Unity; demonstração | Usuários reais |
| Dados | Só sintéticos (seed) | Reais; depende de ADR próprio |

## Homologação

### Acesso

| Item | Valor |
|---|---|
| Painel | `<a preencher: https://<subdomínio>.up.railway.app>` (saída `url_publica` do Terraform) |
| Tela de login do painel | `<URL do painel>/entrar` |
| API do jogo (URL base, já com `/api`) | `<a preencher: https://<subdomínio>.up.railway.app/api>` (saída `url_api`) |
| Saúde | `<URL do painel>/health` (processo vivo) e `<URL do painel>/health/ready` (banco) |
| Contrato da API | [`docs/context/rotas-api.md`](../context/rotas-api.md) |

### O que está publicado

| Item | Valor |
|---|---|
| Provedor | Railway, workspace da contratante |
| Projeto e ambiente na Railway | `helinho-homologacao`, ambiente `homologacao` |
| Serviços | `helinho-web` (repositório, branch `main`, `Dockerfile` da raiz) e `Postgres` (imagem `postgres:17.12` com volume `postgres-dados`) |
| Região | `us-east4-eqdc4a` (US East). Os dados ficam nos Estados Unidos |
| Modo da aplicação | `ASPNETCORE_ENVIRONMENT=Staging` |
| Banco | Só na rede privada do projeto: sem domínio público e sem TCP proxy |
| Seed | Ligado (`Seed__Enabled=true`); roda no pre-deploy `migrate` |
| E-mail e IA | `Email__Provider=Fake`, `Ai__Provider=Fake` |
| Sessões do painel | `<a preencher: persistem entre deploys (decisão D4, opção A, com o PR da Issue #103) ou caem a cada deploy (D4 aberta ou opção D)>` |
| Publicação | Manual: painel da Railway → `helinho-web` → `Cmd+K` → **Deploy Latest Commit** (`auto_deploy = false`) |
| Infraestrutura como código | `infra/railway/` (Terraform); `plan` e `apply` só pelo Henrique |
| Estado do Terraform | Local, na máquina do Henrique, fora do git, com backup cifrado ([runbook, 8.9](railway-runbook.md#89-onde-fica-o-estado-do-terraform)) |
| Decisão de hospedagem | [`ADR-0002`](../architecture/ADR-0002-hospedagem-railway.md) |
| Custos estimados | [`custos.md`](custos.md) |

### Usuários sintéticos

Criados pelo seed (`PeopleSeeder`). Todos fictícios, com e-mail no domínio reservado `helinho.example`.

| Perfil | Login | Onde entra | Observação |
|---|---|---|---|
| Master | `master@helinho.example` | Painel | Vê todas as unidades |
| Administrador Norte | `admin.norte@helinho.example` | Painel | Unidade Norte (fictícia) |
| Administrador Sul | `admin.sul@helinho.example` | Painel | Unidade Sul (fictícia) |
| Aluno | `aluno01` a `aluno10`, matrículas `2026001` a `2026010` | API do jogo (`POST /api/login`, por matrícula, usuário ou e-mail) | Os alunos 03 e 08 estão com a aprovação do responsável pendente e recebem `403` de propósito |

**Senha:** uma só para todos os usuários do seed. **Não está neste arquivo nem em nenhum outro do repositório.**

| Onde está a senha | Quem acessa |
|---|---|
| Gerenciador de senhas do Henrique, entrada "Helinho homologação — senha do seed" | Henrique |
| Variável `HELINHO_SEED_PASSWORD` do serviço `helinho-web`, no painel da Railway | Quem tem acesso ao projeto na Railway |
| Estado do Terraform (`seed_password`) | Henrique |

Como todos os usuários têm a mesma senha, quem recebe a credencial de um aluno recebe também a do master **de homologação**. Isso só é aceitável porque o ambiente tem apenas dado sintético. Para trocar a senha: [runbook, 8.7](railway-runbook.md#87-rotação-das-senhas).

### Para o desenvolvedor Unity

- **URL base:** a da linha "API do jogo" acima, já com `/api`. Exemplo de rota: `POST <URL base>/login`.
- **Login de teste:** matrícula `2026001` (evite `2026003` e `2026008`).
- **Senha:** recebida do Henrique por item compartilhado do gerenciador de senhas. Não peça nem envie por e-mail, chat aberto, Issue ou PR.
- **O token vale 8 horas** e não tem renovação: ao vencer, o jogo faz login de novo.
- O ambiente pode ser zerado ou recriado sem aviso longo: progresso e XP dos alunos sintéticos não são preservados.

### Conta e limites

| Item | Valor |
|---|---|
| Plano do workspace (Hobby ou Pro) | `<a preencher>` |
| Papel do Henrique no workspace | `<a preencher>` |
| Teto de gasto combinado com a contratante | `<a preencher>` |
| Alerta de gasto por e-mail | `<a preencher: valor e data, ou "não configurado">` |
| Limite rígido de gasto do workspace | `<a preencher: "não configurado" ou valor decidido pelo Igor>` |
| Agenda de backup do volume do `Postgres` | `<a preencher: "não configurada" ou diária/semanal/mensal>` |
| Auto-deploy | Desligado pelo watch pattern do Terraform. `<a preencher se usou o plano B: botão Disable no painel>` |

### Registro de mudanças

Uma linha por `apply`, rotação, restore ou destruição. Sem segredo.

| Data | O que mudou | Quem |
|---|---|---|
| `<a preencher>` | Primeiro `apply` e primeiro deploy | Henrique |

## Produção

**Desligada.** O código do Terraform já aceita a chave `producao` em `ambientes`, mas ela não está definida, e nenhum recurso de produção existe na Railway. Não há URL, banco nem credencial de produção.

Antes de ligar (ADR-0002 §5.2):

1. ADR próprio para receber dado real de menor (ADR-0001 §7).
2. Resposta da contratante sobre região e fornecedores permitidos (C14, Issue #39): os dados ficariam nos Estados Unidos.
3. Teto de gasto aprovado (C04).
4. Estado do Terraform separado por ambiente, em backend remoto com lock.
5. `Seed__Enabled` desligado. O Terraform recusa o seed em `producao`.
6. Domínio próprio e DNS (C15).

Quando existir, a produção será um **segundo projeto** na Railway (`helinho-producao`), nunca um segundo ambiente dentro do projeto de homologação.
