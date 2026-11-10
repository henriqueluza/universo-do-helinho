# PRD — universo-do-helinho (MVP de demonstração)

**Data:** 09/11/2026 · **Status:** aprovado (Gate 2, Henrique, 09/11/2026)
**Base:** `docs/discovery/universo-do-helinho.md` e `docs/architecture/ADR-0001-stack.md`

## 1. Objetivo

Demonstrar em **10/11/2026**, rodando localmente, o backend e o painel administrativo do Universo do Hélinho: navegável de ponta a ponta, com dados sintéticos carregados por seed. Prioridade para o que aparece na tela e funciona ao clicar; profundidade de regra de negócio vem depois.

O MVP é a primeira entrega de um contrato que termina em 24/01/2027. O que fica para depois está na seção 6.

## 2. Métricas de sucesso

| Métrica | Alvo |
|---|---|
| Tempo de `git clone` limpo + `scripts/demo.sh` até o sistema no ar | Menos de 5 minutos |
| Telas do painel navegáveis sem erro 500 e sem "em construção" | Todas as da seção 4.B |
| Rotas da API com teste de contrato do JsonUtility verde | 14 de 14 |
| Segregação por unidade | Comprovada por teste no painel e na API |
| Login e logout | Funcionando no painel e na API; token revogado no servidor |
| CI do GitHub Actions em `main` | Verde |

## 3. Usuários

| Perfil | Canal | O que faz no MVP |
|---|---|---|
| Aluno (6–14 anos; faixas 6–7, 8–10, 11–12, 13–14) | Jogo Unity, pela API | Entra, vê perfil e progresso, responde quiz, vê missões, estatísticas, conquistas e customização |
| Administrador de unidade | Painel | Gerencia alunos da própria unidade, questões, eventos e missões; acompanha desempenho |
| Master | Painel | Tudo do administrador em todas as unidades; cria unidades e administradores |

## 4. Escopo do MVP (lista fechada)

### A. Login e autenticação
- A1. Seed com 1 master, 2 administradores de unidades diferentes e ~10 alunos fictícios em 2 unidades e nas 4 faixas etárias. Todo dado pessoal é sintético.
- A2. Painel: entrar e sair; recuperação de senha como tela sem envio real.
- A3. API: `POST /api/login` com matrícula, usuário ou e-mail + senha; só passa aluno aprovado pelo responsável.
- A4. API: `POST /api/logout` invalida a sessão no servidor.
- A5. Token com validade; erros com código no corpo: `invalid_credentials`, `token_expired`, `forbidden`.
- A6. Segregação por unidade: administrador só vê e altera alunos da própria unidade; master vê tudo. Vale no painel e na API.

### B. Painel administrativo
- B1. Layout com menu lateral; nome do usuário, papel e unidade visíveis.
- B2. Dashboard com números sintéticos: alunos ativos, respostas no período, acerto médio por tipo de quiz, atividade recente. **Filtro por faixa etária.**
- B3. Alunos: listagem com filtro por unidade, turma e faixa etária; cadastro com RG, CPF, endereço, nascimento, responsável, nome completo, unidade e turma (todos obrigatórios); edição; status de aprovação do responsável.
- B4. Unidades (só master): listar e criar.
- B5. Administradores (só master): listar e criar, um por unidade.
- B6. Questões: listagem por tipo de quiz e faixa etária; criação e edição com 4 alternativas e explicação; status rascunho/aprovada. Só questão aprovada chega ao jogo.
- B7. Eventos (live event): listar e criar com título, subtítulo, tipo de quiz e término.
- B8. Missões diárias: listar e editar título, meta, recompensa e tipo de quiz.
- B9. Desempenho do aluno: XP, nível, acertos por tipo de quiz, conquistas. **Filtro por faixa etária.**

### C. API Unity (sob `/api`, com dados do seed)
- C1. `GET /profile`
- C2. `GET /live-event`
- C3. `GET /quizzes/progress`
- C4. `GET /quiz/random?quiz_type=` com sessão de quiz controlada no servidor (posição e total)
- C5. `POST /quiz/{question_id}/answer`, idempotente: repetir não soma XP de novo
- C6. `GET /quests/daily` e `GET /quests/daily-bonus`
- C7. `GET /user/stats`
- C8. `GET /user/badges` (paginada; ~10 conquistas fictícias)
- C9. `GET /user/inventory` e `POST /user/inventory`
- C10. `POST /devices/push-token` (só registra; não envia)
- C11. Todas as rotas respeitam as 8 regras do JsonUtility, verificadas por teste de contrato.
- C12. OpenAPI em `/openapi/v1.json` com UI navegável (Scalar).

### D. Demonstração
- D1. `scripts/demo.sh`: sobe o PostgreSQL, aplica a migração, roda o seed e inicia o sistema.
- D2. `docs/demo/roteiro-apresentacao.md`: subir em 3 comandos, credenciais de cada perfil, sequência de telas, chamadas à API.
- D3. README com instruções de desenvolvimento local.

### E. Fundação
- E1. Esqueleto da solução no nível `clean`, CI com build e testes, `compose.yaml`, teste de arquitetura.
- E2. Sistema visual do painel: `DESIGN.md` e tokens.
- E3. `docs/context/` com o contrato de rotas, as premissas de integração (sem o link de compartilhamento) e `propostas-contrato-api.md`.

## 5. Regras de negócio

**Confirmadas**
- Login do aluno por matrícula, usuário ou e-mail, condicionado à aprovação do responsável.
- O master é da equipe da fundação e é o único que cria administradores; cada unidade tem um administrador.
- Administrador só trabalha com alunos da própria unidade.
- Campos cadastrais obrigatórios: RG, CPF, endereço, data de nascimento, nome do responsável, nome completo, unidade, turma. Turma é um campo, não um módulo.
- Faixas etárias: 6–7, 8–10, 11–12, 13–14. Não há aluno fora delas.
- Aparelhos compartilhados com troca de conta; o logout invalida o token no servidor.
- Questão só fica disponível no jogo depois de aprovação humana.
- Um evento destaca um único tipo de quiz existente.
- Cinco tipos de quiz: `ambiental`, `conhecimento`, `esportivo`, `cultural`, `diversidade`.
- `next_level_xp` é o limiar do próximo nível; `correct_average_questions` é calculado na API.
- Errar não retira progresso já conquistado.
- Desempenho individual de um aluno não é exibido a outros alunos.

**Propostas (P1–P16), aprovadas por Henrique para o MVP e pendentes de aceite do desenvolvedor Unity**

Lista completa em `docs/context/propostas-contrato-api.md`. O contrato da API é congelado com elas; mudar depois exige ADR. XP, níveis e conquistas usam fórmula simples, marcada como PROPOSTA no código e nos documentos.

## 6. Fora de escopo

**Fora do MVP, dentro do contrato (backlog):**
- Importação de questões em lote.
- Geração de questões por IA.
- Envio real de e-mail (credenciais e reset de senha).
- Envio real de push e seus gatilhos.
- Cálculo definitivo de XP, níveis, missões e conquistas; catálogo completo de conquistas.
- Regras e conteúdo definitivos de eventos.
- Hospedagem, domínio, ambientes, CD, backup e monitoramento. Deploy é da fase final; as Issues de pipeline de staging e workflow de produção existem, mas ficam fora do sprint do MVP e nenhuma funcionalidade depende delas.
- Testes de integração em conjunto com o desenvolvedor do jogo.
- Migração do código para o repositório oficial da contratante.
- Proteção em repouso de CPF e RG, retenção e exclusão, registro de consentimento com histórico.

**Fora do MVP, sem compromisso:**
- MFA, auditoria completa, paginação sofisticada.
- Gestão de turmas como módulo.
- Funcionamento sem internet.
- Rota de catálogo de customização (fica fixo no jogo).

**Fora deste projeto:**
- O jogo Unity.
- Perfis e funções do produto amplo que não estão no contrato: educador, validação presencial, moderação de conteúdo enviado por criança, acesso de responsáveis, rankings.

## 7. Épicos

Na ordem de prioridade. Se não couber tudo até 10/11, corta-se de baixo para cima a partir do épico 5.

| # | Épico | Itens | Áreas |
|---|---|---|---|
| 1 | **Fundação e contratos** | E1–E3; fatias 1a, 1b, 1c, 1d do ADR | `area:esqueleto`, `area:contrato-dominio`, `area:contrato-api`, `area:painel-base`, `area:design` |
| 2 | **Domínio, seed e identidade** | A1–A6 | `area:dominio`, `area:identidade` |
| 3 | **Painel administrativo** | B1–B9 | `area:painel-alunos`, `area:painel-conteudo`, `area:painel-engajamento` |
| 4 | **API Unity: Home, Perfil e Progresso** | C1–C3 | `area:api-home` |
| 5 | **API Unity: Quiz** | C4–C5 | `area:api-quiz` |
| 6 | **API Unity: Missões, Stats, Badges, Inventário, Push-token** | C6–C10 | `area:api-perfil` |
| 7 | **Qualidade e demonstração** | C11–C12, D1–D3; testes de segregação, logout e smoke das páginas | `area:qa`, `area:docs-demo` |
| 8 | **Deploy (fora do sprint do MVP)** | Pipeline de staging; workflow de produção | `area:deploy` |

Cada Issue é um PR de até 400 linhas; frente que não couber vira várias Issues da mesma área, em série. Dependências entre fatias e frentes seguem o ADR (seção 4).

## 8. Riscos

Os do discovery (seção 17) continuam valendo. Os que mais pesam no MVP:

- **Prazo de um dia** para a abertura mais dez frentes, com merge manual. Mitigação: ordem de corte dos épicos.
- **Contrato da API congelado sobre propostas** sem aceite do desenvolvedor Unity. Mitigação: enviar `propostas-contrato-api.md` a ele; recusa vira ADR.
- **Cláusula contratual de IA (R3):** todo PR revisado e mergeado por Henrique; só dados sintéticos.
