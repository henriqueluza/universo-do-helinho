# Propostas de contrato da API (P1–P16)

**Status: pendente de aceite do desenvolvedor Unity.**

Aprovadas por Henrique em 09/11/2026 **para o MVP, como PROPOSTA**. Fecham as lacunas de `rotas-api.md` (seções 4 e 5) e as inconsistências entre os exemplos. As Issues de abertura congelam o contrato com elas. Nenhuma foi aprovada pela contratante nem aceita pelo desenvolvedor Unity; uma recusa ou mudança exige ADR novo.

Onde este arquivo e `rotas-api.md` divergirem, vale este arquivo.

| # | Tema | Proposta |
|---|---|---|
| P1 | Corpo do `POST /login` | `{ "login": "...", "password": "..." }`. O campo `login` aceita matrícula, usuário ou e-mail |
| P2 | Sessão | Token opaco, guardado no servidor só como hash, validade de 8 h, **sem refresh** (o app refaz o login). `expires_at` em ISO 8601 UTC |
| P3 | `POST /logout` | Revoga o token no servidor. Responde 204 sem corpo |
| P4 | Erros | Corpo `{ "error": "<código>", "message": "..." }`. 401 `invalid_credentials`, 401 `token_expired`, 403 `forbidden`. Conta não aprovada pelo responsável responde `forbidden` |
| P5 | Sem evento em `GET /live-event` | `200 { "has_event": false }`, não 204 |
| P6 | Missão genérica em `GET /quests/daily` | `quiz_type: ""`, nunca `null` |
| P7 | Datas | Sempre texto ISO 8601 em UTC com sufixo `Z` |
| P8 | Sessão de quiz | O servidor mantém a sessão por aluno e tipo de quiz. `GET /quiz/random` devolve a pergunta corrente da sessão, com `position` e `total`. Responder de novo a mesma pergunta devolve o mesmo resultado, sem somar XP |
| P9 | `POST /devices/push-token` | `{ "token": "...", "platform": "android" }`. Responde 204. Só registra; não há envio no MVP |
| P10 | `POST /user/inventory` | Responde com a seleção gravada, no formato do GET |
| P11 | `GET /user/badges` | Parâmetro `?page=1`; tamanho de página fixo no servidor |
| P12 | Catálogo de customização | Fixo no jogo; não há rota |
| P13 | XP, níveis e conquistas | Fórmula simples, documentada como PROPOSTA; cerca de 10 conquistas fictícias |
| P14 | Identificadores de login | No seed, matrícula, usuário e e-mail são únicos por aluno. A regra definitiva de unicidade continua pendente |
| P15 | Filtro por faixa etária no painel | Presente em Alunos, Questões, Dashboard e Desempenho |
| P16 | Prefixo da API | Todas as rotas ficam sob `/api` (`POST /api/login`, `GET /api/profile`…). A URL base entregue ao jogo já inclui `/api`, então os caminhos relativos do contrato não mudam. O painel fica na raiz do mesmo host |

## Códigos de erro (P4)

| Situação | HTTP | `error` | O que o jogo faz |
|---|---|---|---|
| Credencial errada ou usuário inexistente | 401 | `invalid_credentials` | Pede a credencial de novo |
| Token vencido ou revogado | 401 | `token_expired` | Volta para a tela de login |
| Sem permissão, ou conta não aprovada pelo responsável | 403 | `forbidden` | Mostra aviso; não adianta tentar de novo |
| `quiz_type` ausente ou fora dos cinco tipos em `GET /quiz/random` (PROPOSTA, pendente de aceite da contratante) | 400 | `invalid_quiz_type` | Não deve ocorrer; indica erro do jogo |
| Sem perguntas ativas para o tipo e a faixa do aluno em `GET /quiz/random` (PROPOSTA, pendente de aceite da contratante) | 404 | `no_questions` | Mostra aviso de que o quiz ainda não está disponível |

## Regras do JsonUtility

As oito regras da seção 1 de `rotas-api.md` valem para todas as rotas, inclusive as novas (`/logout`, `/devices/push-token`) e para o corpo de erro.
