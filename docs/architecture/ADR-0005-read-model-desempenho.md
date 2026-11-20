# ADR-0005 — Taxa de acerto no read model de Desempenho

**Data:** 20/11/2026 · **Status:** proposto (aguarda o Henrique) · **Decisor:** Henrique
**Issue:** #214 · **Base:** ADR-0001 (seção 3, concessão 1; T1)

**Nível de arquitetura:** `clean`. Este ADR não muda o nível.

---

## 1. Contexto

O critério do #153 pede ".table com .meter para o acerto" na lista de Desempenho. Ele ficou fora do PR #163 porque `PerformanceRow` só trazia XP e nível, sem respostas e acertos. A revisão do #163 pediu um ADR para mudar o formato de consulta já entregue, em vez de alterá-lo de carona.

## 2. Decisão

`PerformanceRow` (`Helinho.Application/Painel/StudentPerformance.cs`) ganha `int Answered, int Correct` no fim do record posicional.

- **Cálculo:** duas subconsultas escalares correlacionadas sobre `QuizAnswers ⨝ QuizSessions` por `StudentId`, dentro da projeção paginada de `StudentPerformanceQueries.ListAsync`. São 2 comandos SQL por página (contagem e página), sem N+1.
- **Segregação:** a raiz da consulta é `db.Students`, então o filtro global por unidade vale (T1). `QuizAnswers` e `QuizSessions` não têm filtro próprio e herdam o recorte pelo aluno.
- **Apresentação:** o percentual é calculado na página por `AlunoModel.CorrectPercent`, o mesmo helper da ficha do aluno. Sem respostas, a coluna mostra "—".
- **Contrato congelado:** a regra do `CLAUDE.md` congela entidades, migração única, DTOs e rotas da API do jogo, e veda migração, DTO ou pacote novo fora das Issues de abertura. Este ADR não a viola: `PerformanceRow` é read model interno do painel (consumido só pela página Razor `Desempenho/Index`), não DTO nem rota de `/api`. Não há entidade, coluna, migração nem pacote novo; só muda a projeção da consulta e o formato do record. O ADR existe porque a revisão do #163 exigiu registro para alterar consulta já entregue, não porque o contrato seja alterado.

## 3. Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Segunda consulta agrupada por aluno | Um round-trip a mais e junção em memória, sem ganho sobre as subconsultas na mesma projeção |
| Coluna desnormalizada em `Student` | Exige migração, vedada pelo contrato congelado, e cria um segundo lugar para manter coerente com as respostas |
| Calcular por linha na página | N+1: uma consulta por aluno listado |

## 4. Consequências

**Positivas**
- A lista mostra o acerto sem tabela nova, migração ou pacote.
- A segregação por unidade é herdada do filtro global de `Student`, sem código extra.
- Lista e ficha usam o mesmo cálculo de percentual.

**Negativas e riscos**
- O custo das subconsultas cresce com o volume de `QuizAnswers`. Índices existentes (`src/Helinho.Infrastructure/Persistence/Configurations/QuizConfiguration.cs`): `QuizSessions (StudentId, QuizType)`, cujo prefixo `StudentId` atende a correlação, e `QuizAnswers (SessionId, QuestionId)` único, cujo prefixo `SessionId` atende a junção. Não há índice só em `QuizAnswers.SessionId`; o composto cobre. Para algumas centenas de alunos e página limitada, é suficiente. Se o volume crescer, índice novo exige Issue própria com migração.
- O acerto agrega todos os tipos de quiz, sem recorte por período. Filtrar por período ou tipo seria outra mudança de consulta, com ADR próprio se alterar este formato.
- `Application` continua acoplada ao EF Core pela concessão 1 do ADR-0001; a consulta usa `IAppDbContext` diretamente.
