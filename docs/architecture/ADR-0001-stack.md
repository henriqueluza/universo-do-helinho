# ADR-0001 — Stack e arquitetura do universo-do-helinho

**Data:** 09/11/2026 · **Status:** aceito (Gate 1 aprovado por Henrique em 09/11/2026) · **Decisor:** Henrique
**Base:** `docs/discovery/universo-do-helinho.md`

**Nível de arquitetura: `clean`** (um nível para a solução inteira; justificativa na seção 3).

---

## 1. Contexto

Backend e painel administrativo de uma plataforma educacional gamificada para crianças e adolescentes de 6 a 14 anos. O jogo (Unity, Android) é de outro desenvolvedor e consome a API. Duas pressões opostas:

- **MVP demonstrável localmente em 10/11/2026**, um dia depois deste ADR, com dados sintéticos.
- **Contrato até 24/01/2027**, com regras de negócio ainda por fechar (XP, níveis, missões, conquistas), integrações futuras (e-mail, push, IA) e dados pessoais de menores.

A stack foi decidida por Henrique no discovery e coincide com a stack padrão do portfólio (C# .NET + PostgreSQL). Não há desvio a justificar.

### Architecture Board

**Produto.** O problema é bem definido e o contrato de rotas do jogo dá um alvo concreto. O escopo do MVP é largo para um dia (14 rotas, ~10 telas); por isso há ordem de corte: painel primeiro, depois API Unity (Login → Home → Quiz → resto). Várias regras são PROPOSTA (P1–P15) e não foram aceitas pelo desenvolvedor Unity; o contrato da API é congelado com elas e muda só por ADR.

**Dados.** Modelo relacional pequeno: unidades, usuários (master, administradores, alunos), questões e alternativas, sessões de quiz e respostas, eventos, missões diárias e progresso, conquistas, inventário, sessões de API, tokens de push. Volume baixo (algumas centenas de alunos; pico de todos simultâneos em eventos). **Consistência forte**: a resposta de quiz soma XP uma única vez (idempotência) e o logout revoga na hora. Um PostgreSQL único resolve; não há caso para consistência eventual, cache distribuído ou fila.

> **Nota de 19/11/2026:** o trecho "não há caso para […] fila" é revisto pelo [ADR-0003](ADR-0003-emails-brevo.md) (seção 5.0, proposto): e-mail com repetição e, na Sprint 4, trabalhos de IA pedem fila, que fica em uma tabela no próprio PostgreSQL, sem Redis e sem broker.

**Infraestrutura.** O MVP roda local: PostgreSQL em `compose.yaml`, custo zero. Hospedagem, cenários de custo por carga, ambientes, autenticação do CI no provedor, segredos de produção e rollback são decididos em ADR próprio na fase final (seção 6). O pico de "todos simultâneos" são centenas de requisições leves; cabe em uma instância pequena e será verificado por teste de carga antes de publicar.

**Segurança/Compliance.** Dados pessoais de menores (LGPD art. 14, ECA), segregação por unidade, tablets compartilhados. Threat model na seção 7. No MVP só existem dados sintéticos.

**Manutenibilidade.** Um mantenedor (Henrique) com agentes de IA implementando por Issue; o código será entregue à contratante e migrado para o repositório oficial dela. Isso pede stack convencional, um único deployável e fronteiras verificadas por teste, sem bibliotecas de licença incerta.

## 2. Decisão

| Item | Decisão |
|---|---|
| Linguagem e runtime | C# / .NET 10 (LTS) |
| Web | ASP.NET Core: **um único host** com Razor Pages (painel) e Minimal APIs (API Unity) |
| UI do painel | Razor Pages + Bootstrap, servido como arquivo estático do próprio projeto (sem CDN, sem build de frontend) |
| Persistência | EF Core + PostgreSQL (Npgsql); migrações do EF |
| Identidade | ASP.NET Core Identity, um único cadastro de usuários com papéis `Master`, `Admin`, `Student` |
| Autenticação do painel | Cookie do Identity; só `Master` e `Admin` entram |
| Autenticação da API | Token opaco em `Authorization: Bearer`, guardado no servidor só como hash, validade de 8 h, sem refresh, revogado no logout (P2, P3) |
| JSON da API | System.Text.Json com `snake_case`, enums como texto, datas ISO 8601 UTC; DTOs explícitos, um por rota |
| OpenAPI | `/openapi/v1.json` gerado pelo ASP.NET Core + Scalar como UI |
| Provedores externos | E-mail, push e IA atrás de portas em `Application`, com implementação fake em `Infrastructure` |
| Testes | xUnit, `WebApplicationFactory`, Testcontainers (PostgreSQL real), NetArchTest.Rules |
| Local | `compose.yaml` com PostgreSQL; `scripts/demo.sh` sobe tudo e roda o seed |
| CI | GitHub Actions: restore, format, build com warnings como erro, testes, pacotes vulneráveis |

Versões exatas dos pacotes são confirmadas no Context7 e fixadas na Issue 1 (central package management).

### Prefixo das rotas

O contrato do jogo usa caminhos relativos (`/login`, `/profile`…) e o painel também tem login. Para não colidirem no mesmo host:

- **API Unity sob `/api`**: `POST /api/login`, `GET /api/profile` etc. A URL base entregue ao desenvolvedor Unity já inclui `/api`, então os caminhos do contrato não mudam para ele.
- **Painel na raiz**, com páginas em português (`/entrar`, `/alunos`, `/questoes`…).

Isso é uma decisão nova deste ADR e entra em `propostas-contrato-api.md` como **P16**, pendente de aceite do desenvolvedor Unity.

### Usuários

Master, administradores e alunos são usuários do Identity (senha com o hash do Identity, bloqueio por tentativas). Os dados cadastrais do aluno ficam em entidade própria do domínio, ligada ao usuário. Login do aluno aceita matrícula, usuário ou e-mail (P1) e só passa se o responsável aprovou.

## 3. Nível de arquitetura: `clean`

Pela tabela de proporcionalidade de `engineering-standards`:

- **Sinais de `clean`:** regras de negócio reais (sessão de quiz com idempotência, XP e níveis, missões, segregação por unidade aplicada em dois canais), integrações externas (e-mail, push, IA), vida útil além do contrato e entrega do código a terceiros.
- **Por que não `simple`:** o painel sozinho é CRUD, mas as mesmas regras servem o painel e a API. Em `simple` elas acabariam duplicadas em PageModels e endpoints, e subir de nível no meio de um contrato com dez frentes paralelas custaria mais que começar em `clean`.
- **Por que não `clean+ddd`:** não há dinheiro, as transições de estado são poucas e auditoria completa está fora do MVP. Agregados e eventos de domínio seriam abstração para o futuro.

Para caber no MVP de um dia, o `clean` é **enxuto**, com três concessões registradas:

1. **`Application` enxerga o EF Core por uma porta `IAppDbContext`** (com `DbSet<>`), em vez de um repositório por agregado. O domínio continua sem nenhum pacote externo. Custo aceito: os casos de uso dependem da API do EF; trocar de ORM exigiria reescrevê-los. Ganho: as telas de CRUD não carregam uma camada de repositórios que só repassaria o ORM.
2. **Sem MediatR/CQRS.** Um handler por caso de uso, injetado direto.
3. **Projetos de teste agrupados** (unidade, integração, arquitetura) em vez de um por camada.

## 4. Estrutura de pastas

```
src/
  Helinho.Domain/            # entidades, value objects, regras; zero pacotes externos
  Helinho.Application/       # casos de uso, portas, DTOs; referencia Domain
    Abstractions/            # IAppDbContext, ICurrentUser, IClock, IEmailSender, IPushSender
    <Area>/                  # um diretório por área (Students, Quiz, Quests…)
  Helinho.Infrastructure/    # EF Core, migrações, Identity, seed, fakes dos provedores
  Helinho.Web/               # composition root
    Api/<Area>/              # Minimal APIs: um arquivo de endpoints por área
    Pages/<Area>/            # Razor Pages do painel
    wwwroot/                 # Bootstrap e tokens de tema
tests/
  Helinho.UnitTests/         # domínio e casos de uso, sem I/O
  Helinho.IntegrationTests/  # API e painel com PostgreSQL em container
  Helinho.ArchitectureTests/ # NetArchTest.Rules
docs/  scripts/  compose.yaml
```

**Teste de arquitetura (NetArchTest.Rules):**
- `Domain` não depende de `Application`, `Infrastructure`, `Web`, EF Core nem ASP.NET Core.
- `Application` não depende de `Infrastructure` nem de `Web`.
- `Web` não usa o `DbContext` concreto nem Npgsql; fala com `Application`.

**Comandos de qualidade:**
```bash
dotnet format --verify-no-changes
dotnet build --no-restore -warnaserror
dotnet test --no-build
dotnet list package --vulnerable --include-transitive
```

### Regras para o trabalho paralelo

A abertura é fatiada em uma Issue serial e três paralelas. Depois delas, cada frente só **adiciona arquivos**.

| Fatia | Conteúdo | Depende de |
|---|---|---|
| **1a** (serial) | Esqueleto da solução, CI, `compose.yaml`, **todas as dependências** (pacotes), teste de arquitetura e os arquivos compartilhados com pontos de extensão | — |
| **1b** | Entidades do domínio, configuração do EF, **migração inicial única**, `IAppDbContext` | 1a |
| **1c** | DTOs e rotas da API Unity (contratos congelados), OpenAPI | 1a |
| **1d** | Layout base do painel com menu completo, Identity mínima (entrar, sair, políticas por papel) | 1a |

1b, 1c e 1d rodam em paralelo, cada uma com a própria `area:*`.

**Dependência das frentes:** só das fatias que usam. Painel → 1b e 1d. API Unity → 1b e 1c. Identidade/sessão → 1b, 1c e 1d (toca os dois canais). QA → 1b, 1c e 1d. Documentação/demo → 1a.

**Pontos de extensão criados pela 1a**, para que 1b, 1c e 1d não editem o mesmo arquivo:

- `Program.cs` chama um método de extensão por fatia e por área (`AddDomainPersistence`, `AddApiContracts`, `AddPanel`, `MapApi<Área>`…), cada um em arquivo próprio já existente e vazio; cada fatia preenche só o seu.
- `AppDbContext` nasce como classe `partial` derivada do contexto do Identity, com o tipo de usuário já declarado; a 1b acrescenta as entidades em arquivo próprio e é a única a gerar migração.
- O registro de DI e o mapeamento de rotas de cada área ficam em arquivos por área, não em uma lista central.

**Regras que continuam valendo:** frente nenhuma gera migração; mudança de schema ou de contrato depois de 1b/1c é Issue própria com ADR; dependência nova depois da 1a é Issue própria.

**Limite conhecido:** a 1d entrega as páginas de entrar e sair, a configuração do cookie e as políticas por papel, mas o login de ponta a ponta com usuários do seed só é comprovado na frente de Identidade/sessão, que depende de 1b e 1d.

## 5. Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Nível `simple` (um projeto, pastas por feature) | Mais rápido no primeiro dia, mas duplica regra entre painel e API e obriga a subir de nível no meio do contrato |
| Nível `clean+ddd` | Sem dinheiro nem transições complexas; cerimônia sem problema presente |
| Dois hosts (API e painel separados) | Dois deploys e dois processos para um mantenedor e orçamento mínimo; a separação lógica já existe por pasta e esquema de autenticação |
| JWT para a API | Logout precisa invalidar no servidor (tablet compartilhado); JWT exigiria lista de revogação, que é o mesmo estado no servidor com mais superfície |
| Repositório por agregado | Em telas de CRUD só repassaria o EF; concessão 1 da seção 3 |
| SPA (React) no painel | Fora da stack decidida; build de frontend a mais sem ganho para um painel administrativo |
| EF InMemory ou SQLite nos testes | Comportamento diferente do PostgreSQL; a casa usa Testcontainers |

## 6. Hospedagem e ambientes

Deploy planejado para a fase final.

## 7. Segurança

**Ativos:** dados cadastrais de menores (nome, RG, CPF, endereço, nascimento, responsável); credenciais e sessões; banco de questões; integridade de XP e progresso.

**Superfícies:** login do painel e da API; rotas autenticadas da API; formulários do painel; seed e credenciais de demonstração; CI e dependências.

| # | Ameaça | Mitigação |
|---|---|---|
| T1 | Administrador de uma unidade lê ou altera aluno de outra (IDOR) | Filtro por unidade aplicado em `Application` a partir de `ICurrentUser`, reforçado por filtro global de consulta no EF; teste de integração por rota e por página |
| T2 | Sessão do aluno anterior reutilizada em tablet compartilhado | Token opaco com hash no servidor, validade de 8 h, `POST /logout` revoga; `token_expired` distinguível de `invalid_credentials` |
| T3 | Força bruta no login (matrícula é previsível) | Bloqueio por tentativas do Identity, limite de taxa nas rotas de login, mesma resposta para usuário inexistente e senha errada |
| T4 | Vazamento de dado pessoal por log, erro ou resposta da API | DTOs explícitos (a API do jogo não devolve dado cadastral), ProblemDetails sem stack trace, logs estruturados sem CPF, RG, senha ou token |
| T5 | Dado real ou segredo no repositório ou em prompt de IA | Seed 100% sintético, segredos só por variável de ambiente, varredura de segredos antes de cada PR |
| T6 | XP inflado por reenvio de resposta | Resposta de quiz idempotente por sessão e pergunta (P8); XP calculado só no servidor |

Também valem: antiforgery nos formulários do painel, cookies `HttpOnly` e `SameSite`, autorização por papel em toda página e rota, HTTPS obrigatório quando houver ambiente publicado.

**Fica para antes de receber dado real** (ADR próprio): proteção em repouso de CPF e RG, política de retenção e exclusão, registro de consentimento do responsável com histórico, auditoria completa, MFA para master.

### Risco R3 — cláusula contratual de IA

O contrato condiciona o uso de ferramentas de IA e exige revisão e teste humano de todo código gerado ou assistido. Tratamento neste projeto:

- todo PR é revisado e mergeado por Henrique; nenhum agente faz merge;
- o seed e os testes usam só dados sintéticos; nenhum dado real, credencial ou documento sigiloso entra no repositório nem em prompts;
- do material da contratante, só dois arquivos aprovados por Henrique vão para `docs/context/`;
- o GitHub pessoal vale só para a demonstração; a migração para o repositório oficial da contratante é Issue de backlog (risco R2).

## 8. Consequências

**Positivas**
- Um processo, um banco e um comando para a demonstração.
- Regras de negócio em um lugar, servindo painel e API; fronteiras verificadas no CI.
- Contratos congelados na abertura (1b e 1c) permitem dez frentes em paralelo sem conflito de arquivo.
- Trocar fakes por provedores reais é adicionar um adapter, sem tocar em caso de uso.

**Negativas e riscos**
- A abertura (1a, depois 1b/1c/1d em paralelo) é o gargalo: nenhuma frente começa antes de as fatias que ela usa estarem mergeadas. A 1a precisa acertar os pontos de extensão de primeira; se errar, 1b, 1c e 1d conflitam no `Program.cs`.
- Uma migração inicial congelada torna cara qualquer mudança de schema durante o MVP.
- `Application` acoplada ao EF Core (concessão 1).
- O contrato da API é congelado sobre P1–P16 sem aceite do desenvolvedor Unity; uma recusa vira ADR e retrabalho.
- `clean` custa mais arquivos por tela que `simple`; com a demonstração em um dia, parte do escopo pode ficar para depois (ordem de corte do discovery).
- Painel e API escalam juntos; aceitável para o volume previsto.
