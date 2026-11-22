# Roteiro de apresentação

> Todos os dados da demonstração são **sintéticos** (seed). Nenhuma pessoa real aparece aqui. Deploy é da fase final do projeto: a demo roda só localmente.

## 1. Subir em 2 comandos

Pré-requisitos: .NET 10 SDK, Docker e `curl`.

```bash
bash scripts/demo.sh          # 1. sobe o PostgreSQL, aplica a migração, roda o seed e inicia o sistema
open http://localhost:5018    # 2. painel (a API fica em /api; a UI da API em /scalar/v1)
```

Não é preciso copiar o `.env`: o `demo.sh` guarda as senhas em `.demo/` (ignorada pelo git). A senha do banco fica em `.demo/db-password` e a do seed em `.demo/seed-password`. Se `HELINHO_SEED_PASSWORD` estiver definida, ela vale e é gravada no arquivo; senão o script reaproveita o arquivo ou gera uma senha na primeira execução. **Toda execução mostra a senha em uso** no terminal. Ela não é escrita em nenhum documento.

## 2. Logins

| Perfil | Login | Unidade |
|---|---|---|
| Master | `master@helinho.example` | todas |
| Administrador Norte | `admin.norte@helinho.example` | Norte |
| Administrador Sul | `admin.sul@helinho.example` | Sul |
| Aluno | `aluno01` … `aluno10` (matrículas `2026001` … `2026010`) | Norte e Sul |

Senha: a exibida pelo `demo.sh`. O aluno entra na API por matrícula, usuário ou e-mail.

## 3. Painel: sequência de telas

Use uma janela anônima por perfil para alternar sem sair.

1. **Entrar** como master. Mostre menu lateral, nome, papel e unidade.
2. **Dashboard**: alunos ativos, respostas no período, acerto médio por tipo de quiz, atividade recente. Aplique o **filtro por faixa etária** (6–7, 8–10, 11–12, 13–14).
3. **Unidades** (só master): listar e criar.
4. **Administradores** (só master): listar e criar, um por unidade.
5. **Alunos**: filtros por unidade, turma e faixa etária; abrir o cadastro e a edição; status de aprovação do responsável.
6. **Desempenho do aluno**: lista com XP, nível e taxa de acerto (barra); ficha com acertos por tipo de quiz e conquistas; filtro por faixa etária.
7. **Questões**: por tipo de quiz e faixa etária; criar com 4 alternativas e explicação; rascunho × aprovada (só aprovada chega ao jogo).
8. **Eventos**: listar e criar (título, subtítulo, tipo de quiz, término).
9. **Missões diárias**: editar título, meta, recompensa e tipo de quiz.
10. **Sair**, e mostrar a tela de recuperação de senha (sem envio real).

### Segregação por unidade

Mesma tela, três perfis:

1. Entre como `admin.norte@helinho.example` e abra **Alunos**: só os alunos da unidade Norte.
2. Em outra janela anônima, entre como `admin.sul@helinho.example` e abra **Alunos**: só os da unidade Sul.
3. Entre como master e abra **Alunos**: todos, com o filtro por unidade.
4. Como administrador, tente abrir o detalhe de um aluno da outra unidade (pela URL): o acesso é negado.

## 4. API com `curl`

Base: `http://localhost:5018/api`. Informe a senha sem gravá-la no histórico:

```bash
BASE=http://localhost:5018/api
read -rs HELINHO_PASS   # cole a senha exibida pelo demo.sh

# login (campo "login": matrícula, usuário ou e-mail)
TOKEN=$(curl -s -X POST "$BASE/login" -H 'Content-Type: application/json' \
  -d "{\"login\":\"2026001\",\"password\":\"$HELINHO_PASS\"}" | jq -r .token)

# profile
curl -s "$BASE/profile" -H "Authorization: Bearer $TOKEN"

# quiz: sorteia uma questão do tipo e responde
curl -s "$BASE/quiz/random?quiz_type=ambiental" -H "Authorization: Bearer $TOKEN"
curl -s -X POST "$BASE/quiz/<question_id>/answer" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"option_id":"b"}'

# logout (204) e prova de que o token foi revogado (401 token_expired)
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$BASE/logout" -H "Authorization: Bearer $TOKEN"
curl -s "$BASE/profile" -H "Authorization: Bearer $TOKEN"
```

Troque `<question_id>` pelo `question_id` devolvido por `/quiz/random`. Repetir a mesma resposta não soma XP de novo (idempotência).

Outras rotas para mostrar: `/live-event`, `/quizzes/progress`, `/quests/daily`, `/quests/daily-bonus`, `/user/stats`, `/user/badges`, `/user/inventory`.

## 5. API pelo Scalar

1. Abra `http://localhost:5018/scalar/v1` (o contrato OpenAPI está em `/openapi/v1.json`).
2. Execute `POST /login` com o corpo `{ "login": "2026001", "password": "<senha do demo.sh>" }`.
3. Cole o `token` em **Authentication → Bearer** e execute `GET /profile` e `GET /quiz/random`.

Contrato completo e regras do JsonUtility: `docs/context/rotas-api.md` e `docs/context/propostas-contrato-api.md`.
