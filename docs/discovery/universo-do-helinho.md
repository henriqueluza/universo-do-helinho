# Discovery — universo-do-helinho

**Data:** 09/11/2026 · **Autor das respostas:** Henrique · **Estado:** concluído. Gates 1 e 2 aprovados em 09/11/2026; as decisões posteriores estão no ADR e no PRD, que prevalecem sobre este documento.

Marcadores usados: **Confirmado** (dito por Henrique ou presente nas fontes como decisão encerrada), **PROPOSTA** (sugestão técnica, precisa de aprovação no Gate 2), **Não definido** (sem resposta; é risco).

Esta sessão só produz documentos. Nenhum código é escrito na fábrica; a implementação acontece por Issues + dispatch no repositório do projeto.

---

## 1. Objetivo e problema

Backend e painel administrativo do Universo do Hélinho, plataforma educacional gamificada de uma fundação pública municipal que atende crianças e adolescentes de 6 a 14 anos. O jogo (Unity, Android) é feito por outro desenvolvedor; este projeto entrega:

- a **API REST** que o jogo consome (login, perfil, quiz, missões, estatísticas, conquistas, customização);
- o **painel administrativo** onde a equipe da fundação cadastra alunos, unidades, administradores, questões, eventos e missões, e acompanha desempenho.

**Objetivo imediato (MVP):** demonstrar o sistema rodando **localmente em 10/11/2026**, navegável de ponta a ponta, com dados sintéticos carregados por seed. Prioridade: o que aparece na tela e funciona ao clicar. Profundidade de regra de negócio vem depois.

**Prazo contratual final do projeto inteiro:** 24/01/2027 (inclui integração com o jogo, testes e validação).

## 2. Usuários e volume

| Perfil | Onde atua | Observação |
|---|---|---|
| Aluno (6–14 anos, faixas 6–7, 8–10, 11–12, 13–14) | Jogo Unity, via API | Só entra se a conta foi aprovada pelo responsável |
| Administrador de unidade | Painel | Um por unidade; só vê alunos da própria unidade |
| Master | Painel | Único que cria administradores; vê todas as unidades |

- **Volume:** algumas centenas de alunos (estimativa de Henrique, "se não me engano" — **a confirmar** com a contratante). Crescimento em 6/12/24 meses: **não definido**.
- **Pico:** possibilidade de **todos os alunos simultâneos** durante eventos especiais (live events). Fora disso, uso em horário de atividade nas unidades.
- Aparelhos **compartilhados** (tablets da unidade) com troca de conta — confirmado.

## 3. Dados e sensibilidade

Dados pessoais de **crianças e adolescentes**: nome completo, RG, CPF, endereço, data de nascimento, nome do responsável, unidade, turma (todos obrigatórios no cadastro — confirmado). Mais dados de uso: respostas, XP, progresso, conquistas.

Sensibilidade **alta** (LGPD art. 14 + ECA). No MVP **não entra nenhum dado real**: todo dado pessoal é sintético, gerado por seed.

## 4. Integrações externas

| Integração | Situação | No MVP |
|---|---|---|
| Jogo Unity (cliente da API) | Contrato de rotas recebido; jogo ainda sem camada de rede | API real, respondendo com seed |
| E-mail transacional (credenciais, reset) | Provedor e remetente não fechados | Interface + implementação fake |
| Push | Provedor em aberto (Firebase não é obrigatório) | Só registro do token (`POST /devices/push-token`); envio fake |
| IA para geração de questões | Provedor não definido | Fora do MVP |

Provedores externos ficam atrás de interfaces com implementação fake.

## 5. Mobile, tempo real, assíncrono

- Mobile: o cliente é o jogo Unity (Android; iOS depois; Chromebook via build Android). Este projeto não entrega app.
- Tempo real: não há requisito.
- Assíncrono: não há requisito no MVP (push e e-mail reais ficam para depois).
- Funcionamento sem internet: **não definido** (pendência da contratante); o MVP assume conexão.

## 6. Restrições legais, de segurança e contratuais

Fontes: capítulos de segurança/conformidade da apresentação de produto, Q&A e o contrato (lido apenas para escopo, prazo e obrigações; nada é reproduzido aqui).

- **LGPD com dados de menores:** consentimento do responsável antes de a conta funcionar; coleta mínima; finalidade pedagógica; a contratada atua como operadora.
- **Separação por unidade:** dados de uma unidade nunca acessíveis por administrador de outra. Aplicada **no painel e na API**.
- **Sem exposição de desempenho individual** de uma criança a outras crianças.
- **Somente dados sintéticos ou anonimizados** em desenvolvimento, teste e demonstração; nunca copiar base real para ambiente pessoal.
- **Segredos:** nada de token, chave ou senha no código; variáveis de ambiente ou cofre. Menor privilégio. Não compartilhar credenciais.
- **Logout invalida a sessão no servidor** (tablet compartilhado).
- **HTTPS obrigatório** no ambiente publicado (o Android bloqueia texto puro); local roda em HTTP só na máquina do desenvolvedor.
- **Rastreabilidade** das ações relevantes é exigência do produto; auditoria completa está fora do MVP.
- **Uso de IA e repositórios externos:** o contrato restringe colocar código, documentos e dados da contratante em ferramentas públicas de IA e repositórios não autorizados, e exige revisão e teste humano de todo código assistido por IA. Ver riscos R2 e R3.
- **Retenção, região de hospedagem e fornecedores permitidos:** **não definido** pela contratante.

## 7. Orçamento e custeio

- Orçamento mensal e teto absoluto de infraestrutura: **não definido**. Única orientação: "gastos mínimos".
- Quem paga: a contratante (conta de hospedagem já com pagamento configurado, segundo o Q&A).
- Duração: contrato até 24/01/2027; operação posterior **não definida**.
- Sem impacto no MVP (roda local, custo zero). Precisa fechar antes da fase de deploy.

## 8. Stack (decidida por Henrique)

.NET 10, ASP.NET Core, Razor Pages + Bootstrap no painel, EF Core + PostgreSQL, ASP.NET Core Identity, Testcontainers nos testes, `compose.yaml` com PostgreSQL local.

**Deploy:** planejado para a fase final. No ADR, a seção "Hospedagem e ambientes" registra apenas isso. Nada de Railway, Dockerfile de produção, CD, ambientes, domínio, backup, monitoramento, Brevo ou push real agora. Issues de pipeline de staging e workflow de produção podem ser criadas, fora deste sprint. Permitido agora: compose local, CI com build + testes, Testcontainers. Deploy não é bloqueio nem pendência.

## 9. Plataforma e repositório

- **GitHub** `henriqueluza/universo-do-helinho` (privado), PRs e GitHub Actions. **Merge é sempre do Henrique.**
- Repositório ainda não existe; será criado do zero pelo `project-bootstrap`.
- **Clone local: `~/Trabalho/Universo-do-Helinho/repo`** (e **não** `~/Trabalho/universo-do-helinho`). O bootstrap precisa usar esse caminho.
- A pasta `~/Trabalho/Universo-do-Helinho`, fora de `repo/`, é **só leitura** e nunca vai para o GitHub. Nenhum arquivo dela é copiado para o repositório sem aprovação explícita de Henrique.
- **Autorização:** o GitHub pessoal vale **só para a demo**; depois o código migra para o repositório oficial indicado pela contratante. A migração é etapa futura, fora deste sprint.

## 10. Escopo do MVP

### A. Login e autenticação
- Identity com seed: 1 master, 2 administradores de unidades diferentes, ~10 alunos fictícios em 2 unidades e nas 4 faixas etárias.
- Painel: login, logout; recuperação de senha como tela sem envio real.
- API Unity: `POST /login` (matrícula, usuário ou e-mail + senha); `POST /logout` invalidando a sessão no servidor; token com validade; erros com código no corpo (`invalid_credentials`, `token_expired`, `forbidden`).
- Segregação por unidade: admin só vê alunos da própria unidade; master vê tudo.

### B. Painel administrativo
- Dashboard com números sintéticos: alunos ativos, respostas no período, acerto médio por tipo de quiz, atividade recente.
- Alunos: listagem com filtro por unidade/turma/faixa etária; cadastro (RG, CPF, endereço, nascimento, responsável, nome completo, unidade, turma); edição; status de aprovação do responsável.
- Unidades e administradores (só master): listar e criar.
- Questões: listagem por tipo de quiz e faixa etária; criação/edição com 4 alternativas e explicação; status rascunho/aprovada.
- Eventos (live event): listar e criar com título, subtítulo, tipo de quiz e término.
- Missões diárias: listar e editar título, meta, recompensa, tipo de quiz.
- Desempenho do aluno: XP, nível, acertos por tipo de quiz, conquistas.
- Layout com menu lateral; nome do usuário, papel e unidade visíveis.

### C. API Unity com dados do seed
Todas as rotas do contrato de rotas, respeitando as 8 regras do JsonUtility, mais `POST /logout` e `POST /devices/push-token`. Sessão de quiz controlada no servidor (posição/total, idempotência da resposta). OpenAPI em `/openapi/v1.json` com UI navegável (Scalar ou Swagger UI).

| Método | Caminho | Origem |
|---|---|---|
| POST | `/login` | contrato |
| POST | `/logout` | lacuna 4.2 do contrato |
| GET | `/profile` | contrato |
| GET | `/live-event` | contrato |
| GET | `/quizzes/progress` | contrato |
| GET | `/quiz/random?quiz_type=` | contrato |
| POST | `/quiz/{question_id}/answer` | contrato |
| GET | `/quests/daily` | contrato |
| GET | `/quests/daily-bonus` | contrato |
| GET | `/user/stats` | contrato |
| GET | `/user/badges` | contrato |
| GET | `/user/inventory` | contrato |
| POST | `/user/inventory` | contrato |
| POST | `/devices/push-token` | lacuna 5.1 do contrato |

### D. Roteiro de demonstração
`docs/demo/roteiro-apresentacao.md` (subir em 3 comandos, credenciais de cada perfil, sequência de telas, chamadas à API) e `scripts/demo.sh` que sobe tudo e roda o seed. Ambos no repositório do projeto.

### Fora do MVP
Importação em lote, geração por IA, e-mail real, push real, paginação sofisticada, cálculo definitivo de XP/níveis/conquistas (fórmula simples, documentada como PROPOSTA), MFA, auditoria completa, 182 conquistas (usar ~10 fictícias), qualquer coisa de deploy.

### Definição de pronto
- `git clone` limpo + `scripts/demo.sh` → sistema no ar em menos de 5 minutos.
- Todas as telas do item B navegáveis, sem erro 500 e sem "em construção".
- Login/logout no painel e na API; segregação por unidade comprovada por teste.
- Testes de contrato do JsonUtility verdes para todas as rotas.
- CI do GitHub Actions verde em `main`.

### Ordem de corte se não couber até 10/11 (decidida por Henrique: **painel primeiro**)
1. Issue 1 (esqueleto e contratos congelados).
2. Domínio + seed e Identidade/sessão.
3. Painel completo (item B) com login.
4. API Unity na ordem Login/Logout → Home/Perfil/Progresso → Quiz → Missões/Stats/Badges/Inventário/Push-token.
5. QA e documentação/demo acompanham o que estiver pronto; `demo.sh` e o roteiro têm de existir de qualquer forma.

## 11. Regras do JsonUtility (valem para toda rota da API Unity)

1. **Objeto na raiz**, nunca array solto; todo array dentro de um campo nomeado.
2. **Sem chave dinâmica** (sem dicionário); lista de objetos com o identificador como campo.
3. **Nomes de campo exatos**, `snake_case`, inclusive maiúsculas/minúsculas.
4. **Campo ausente não é distinguível de zero**; ausência significativa vem com booleano explícito ou 204 sem corpo.
5. **Data e hora como texto** ISO 8601 ou timestamp Unix.
6. **Enum como texto.**
7. **Uma forma por rota**, sem polimorfismo.
8. **Sem array de array**; aninhamento é objeto dentro de array.

Corolários do contrato: valores crus (o jogo formata); cor nunca vem no payload; adicionar campo não quebra build antigo, remover ou renomear quebra. A regra 8 consta no contrato de rotas e no Q&A; o documento de premissas lista só as sete primeiras. Vale o contrato de rotas.

Tipos de quiz (`quiz_type`): `ambiental`, `conhecimento`, `esportivo`, `cultural`, `diversidade`.

## 12. Regras de negócio confirmadas

- Login do aluno por matrícula, usuário **ou** e-mail, condicionado à aprovação do responsável.
- Master (da equipe da fundação) cria administradores; cada unidade tem um administrador.
- Administrador só trabalha com alunos da própria unidade; master abrange todas.
- Campos cadastrais obrigatórios: RG, CPF, endereço, data de nascimento, nome do responsável, nome completo, unidade, turma. Turma é campo, não módulo de gestão de turmas.
- Faixas etárias: 6–7, 8–10, 11–12, 13–14. Não há aluno fora delas.
- Aparelhos compartilhados com troca de conta; logout invalida o token no servidor.
- Questão só fica disponível no jogo depois de aprovação humana (status rascunho/aprovada).
- Um evento destaca **um** tipo de quiz existente; não cria categoria.
- `next_level_xp` é o limiar do próximo nível, não o que falta. `correct_average_questions` é calculado na API.
- Dados fictícios autorizados para desenvolvimento e demonstração.

## 13. Propostas de contrato

Lacunas do contrato de rotas, congeladas na abertura do projeto. Aprovadas por Henrique para o MVP como PROPOSTA; nada aqui foi aprovado pela contratante nem aceito pelo desenvolvedor Unity. A lista vigente, com a P16 acrescentada pelo ADR, está em `docs/context/propostas-contrato-api.md`.

| # | Tema | PROPOSTA |
|---|---|---|
| P1 | Corpo do `POST /login` | `{ "login": "...", "password": "..." }`; `login` aceita matrícula, usuário ou e-mail |
| P2 | Sessão | Token opaco, guardado só como hash no servidor, validade de 8 h, **sem refresh** (o app refaz o login); `expires_at` em ISO 8601 UTC |
| P3 | `POST /logout` | Revoga o token; responde 204 sem corpo |
| P4 | Erros | Corpo `{ "error": "<código>", "message": "..." }`; 401 `invalid_credentials`, 401 `token_expired`, 403 `forbidden`; conta não aprovada pelo responsável responde `forbidden` |
| P5 | Sem evento | `200 { "has_event": false }` (não 204) |
| P6 | Missão genérica | `quiz_type: ""`, nunca `null` |
| P7 | Datas | Sempre ISO 8601 UTC com sufixo `Z` |
| P8 | Sessão de quiz | Servidor mantém a sessão por aluno + tipo de quiz; `GET /quiz/random` devolve a pergunta corrente da sessão; responder de novo a mesma pergunta devolve o mesmo resultado sem somar XP |
| P9 | `POST /devices/push-token` | `{ "token": "...", "platform": "android" }`; 204 |
| P10 | `POST /user/inventory` | Responde com a seleção gravada, no formato do GET |
| P11 | `GET /user/badges` | `?page=1`, tamanho de página fixo no servidor |
| P12 | Catálogo de customização | Fixo no jogo; sem rota |
| P13 | XP, níveis, conquistas | Fórmula simples e documentada como PROPOSTA; ~10 conquistas fictícias |
| P14 | Identificadores de login | No seed, matrícula, usuário e e-mail únicos por aluno; a regra definitiva de unicidade (e-mail de responsável com mais de um aluno) continua pendente |
| P15 | Filtro por faixa etária | Presente também no Dashboard e em Desempenho, não só em Alunos (o escopo contratado trata a faixa etária como filtro principal das telas de acompanhamento) |

## 14. Plano de paralelização (para o bootstrap)

> A "Issue 1" abaixo foi fatiada no ADR em 1a (serial) e 1b, 1c, 1d (paralelas); cada frente depende só das fatias que usa. Vale o ADR, seção 4.

**Issue 1 — serial e curta.** Esqueleto da solução, projetos, EF Core, compose, CI, layout base do painel com menu, Identity mínima. Ela **define e congela os contratos** (entidades do domínio, DTOs da API, rotas das páginas), cria os arquivos compartilhados e instala todas as dependências.

**Frentes paralelas**, cada uma com a própria label `area:*`, dependendo só da Issue 1:

| # | Frente | Label sugerida |
|---|---|---|
| 1 | Domínio + migrações + seed sintético | `area:dominio` |
| 2 | Identidade/sessão (login triplo, logout, tokens, códigos de erro, segregação) | `area:identidade` |
| 3 | API Unity: Home/Perfil/Progresso | `area:api-home` |
| 4 | API Unity: Quiz | `area:api-quiz` |
| 5 | API Unity: Missões/Stats/Badges/Inventário/Push-token | `area:api-perfil` |
| 6 | Painel: Dashboard + Alunos | `area:painel-alunos` |
| 7 | Painel: Unidades/Administradores + Questões | `area:painel-conteudo` |
| 8 | Painel: Eventos + Missões + Desempenho | `area:painel-engajamento` |
| 9 | QA: contrato JsonUtility, segregação, logout, smoke test das páginas | `area:qa` |
| 10 | Documentação/Demo: roteiro, `demo.sh`, README, OpenAPI UI | `area:docs-demo` |

- Cada Issue = um PR de até 400 linhas. Frente que não couber vira várias Issues da mesma área, **em série**.
- Mudança de contrato depois da Issue 1 **exige ADR**.
- As labels `area:*` servem à trava por área do dispatch da fábrica: uma Issue por área de cada vez.

## 15. Fontes lidas e proposta para `docs/context/`

Os agentes que implementam as Issues só enxergam o repositório. Lista para aprovação no Gate 2:

| Arquivo | O que define | Vai para `docs/context/`? |
|---|---|---|
| `Rotas API - Helinho(1).md` | Contrato de rotas do jogo: 8 regras do JsonUtility, princípios, tipos de quiz, payloads de exemplo, lacunas de autenticação, push, sessão de quiz e catálogo | **Sim, como está.** Conferido: sem dado pessoal real (o nome nos exemplos é de personagem ilustrativo, o token é amostra truncada) e sem valor comercial |
| `Premissas Integração - App + Backend (Universo Helinho).md` | Respostas do desenvolvedor Unity: plataformas, versão, restrições de serialização, divisão de responsabilidades de sessão, aparelho compartilhado, identificador do app Android | **Sim, com um ajuste:** remover o link de compartilhamento do Google Docs (item 6). Fora isso, sem nome de pessoa, dado pessoal ou valor comercial |
| `Perguntas_e_Respostas_Universo_do_Helinho.md` | Regras de negócio confirmadas, pendências e status de cada pergunta | **Não.** Cita pessoas reais e detalhes operacionais de contas e acessos. As regras confirmadas vão para o PRD, sem nomes (seção 12 deste documento) |
| `Universo_Helinho_Apresentacao_de_Produto.pdf` | Visão ampla do produto, perfis, regras de operação, princípios de segurança e conformidade | **Não.** Material de proposta da contratante; o que importa está nas seções 6 e 12 |
| `Contrato_..._assinado.pdf` | Escopo contratado, prazo, obrigações de sigilo e proteção de dados | **Nunca.** Sigiloso. Lido só para escopo e prazo |

Nomes sugeridos no repositório: `docs/context/rotas-api.md` e `docs/context/premissas-integracao.md`.

A apresentação descreve um produto maior que o contratado (educador, validação presencial, consentimento com histórico, moderação, rankings). Isso é contexto, **não escopo** deste projeto.

## 16. Escopo contratado que fica para depois do MVP

Para o bootstrap criar como backlog, fora deste sprint: envio de credenciais por e-mail e reset de senha reais; importação de questões em lote; geração de questões por IA com aprovação humana; regras e conteúdo de eventos; recebimento definitivo de progresso/pontuação; gatilhos de push; hospedagem, domínio e e-mail transacional; testes de integração em conjunto com o desenvolvedor do jogo; migração do código para o repositório oficial da contratante.

## 17. Riscos e pontos não definidos

| # | Risco | Tratamento |
|---|---|---|
| R1 | **Prazo:** a demo é em 10/11 e o discovery fechou em 09/11. Issue 1 serial + 10 frentes, com merge manual, dificilmente cabem inteiras em um dia | Ordem de corte da seção 10 (painel primeiro); escopo restante segue depois da demo |
| R2 | **Repositório:** GitHub pessoal autorizado só para a demo; o contrato exige o repositório oficial da contratante | Issue de migração no backlog; tarefa `needs-human` para Henrique combinar a data |
| R3 | **Uso de agentes de IA:** o contrato condiciona o uso de IA e exige revisão e teste humano do código gerado | Todo PR é revisado e mergeado por Henrique; nenhum dado real, credencial ou documento sigiloso entra no repositório ou nos prompts |
| R4 | **Volume e pico:** "algumas centenas", todos simultâneos em eventos, sem confirmação da contratante | Confirmar antes do deploy; teste de carga entra na fase final |
| R5 | **Orçamento de infraestrutura não definido** | Fechar teto antes da fase de deploy |
| R6 | **Contratos congelados sobre propostas** (seção 13) não aceitas pelo desenvolvedor Unity | Gate 2 aprova; enviar P1–P12 ao Unity; mudança depois exige ADR |
| R7 | **Regras de XP, níveis, missões e conquistas** não aprovadas pela contratante | Fórmula simples marcada como PROPOSTA no código e nos documentos |
| R8 | **LGPD:** fluxo de consentimento, retenção, região e fornecedores não definidos | Só dados sintéticos até definição; tarefa `needs-human` |
| R9 | **Sem internet / timeout / reenvio** não definidos | MVP assume conexão; idempotência da resposta de quiz cobre o reenvio |
| R10 | **Lista oficial de unidades** não fornecida | Seed usa duas unidades fictícias |

## 18. Próximo passo

Gate 3: `sprint-planning` autoriza as Issues. A implementação acontece por Issue, com revisão e merge de Henrique.

