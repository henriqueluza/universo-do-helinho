# Python / FastAPI / SQLAlchemy / Pydantic

Guia para agentes que implementam Issues em serviços FastAPI. Leia junto com `../clean-architecture.md`, `../solid.md` e `../design-patterns.md`. Versões: use a versão estável atual de Python, FastAPI, SQLAlchemy (API 2.x) e Pydantic (v2) — confirme APIs via Context7.

## 1. Detecção

- `pyproject.toml` (ou `requirements*.txt`) com `fastapi` → esta stack.
- Gerenciador: `uv.lock` → uv; `poetry.lock` → Poetry; só `requirements.txt` → pip. **Use o gerenciador que já existe.**
- `alembic.ini` + `alembic/` → migrations com Alembic.
- Confirmar versões: `python --version`, `uv pip list` / `poetry show` / `pip show fastapi sqlalchemy pydantic`.

## 2. Níveis de arquitetura

`simple` — CRUD, poucas regras, um banco:

```
app/
  main.py              # cria FastAPI, inclui routers
  config.py            # Settings (pydantic-settings)
  db.py                # engine, sessionmaker, get_session
  orders/
    router.py          # endpoints
    schemas.py         # Pydantic in/out
    models.py          # SQLAlchemy ORM
    service.py         # regras simples + acesso ao banco
tests/
```

`clean` — regras de negócio relevantes, várias integrações, necessidade de testar domínio sem banco:

```
src/app/
  domain/              # entidades, value objects, exceções de domínio — Python puro
  application/         # casos de uso, portas (Protocol), DTOs de entrada/saída
  infrastructure/      # SQLAlchemy models + repositórios, clients HTTP, UoW
  api/                 # routers, schemas Pydantic, dependências (Depends), handlers de erro
  main.py              # composition root
tests/{unit,integration}/
```

`clean+ddd` — agregados com invariantes, eventos de domínio publicados pelo UoW após commit. Use só se o ADR pedir.

## 3. Mapeamento Clean Architecture → FastAPI

| Conceito | Onde fica |
|---|---|
| Entidades / value objects | `domain/` — `@dataclass` (frozen para VOs); **sem** import de FastAPI, SQLAlchemy ou Pydantic |
| Casos de uso | `application/` — classe ou função com dependências injetadas, um por ação |
| Portas | `application/ports.py` — `typing.Protocol` (`OrderRepository`, `PaymentGateway`) |
| Adapters | `infrastructure/` — `SqlAlchemyOrderRepository`, clients `httpx` |
| Schemas HTTP | `api/schemas.py` — Pydantic; converta para/desde entidades no limite |
| Controllers | `api/routers/` — finos: validam, chamam caso de uso, mapeiam resposta |
| DI / composition root | `Depends` em `api/dependencies.py` monta session → repo → caso de uso |

No nível `simple`, models ORM e schemas Pydantic podem ser usados diretamente — não crie entidades duplicadas sem regra que justifique.

## 4. Impondo a regra de dependência

Use **import-linter** (`.importlinter` ou `[tool.importlinter]` no `pyproject.toml`):

```toml
[tool.importlinter]
root_package = "app"

[[tool.importlinter.contracts]]
name = "Camadas clean"
type = "layers"
layers = ["app.api", "app.infrastructure", "app.application", "app.domain"]

[[tool.importlinter.contracts]]
name = "Domínio sem frameworks"
type = "forbidden"
source_modules = ["app.domain", "app.application"]
forbidden_modules = ["fastapi", "sqlalchemy", "pydantic"]
```

Rode `lint-imports` no CI. Nota: `infrastructure` acima de `application` na lista permite que infra importe as portas, que é o desejado.

## 5. SOLID idiomático

- **S** — Router não contém regra de negócio nem query. Violação típica: endpoint de 60 linhas que valida, consulta, calcula e envia e-mail.
- **O** — Novas formas de pagamento/notificação = nova implementação de um `Protocol`, registrada no composition root; não um `if provider == ...` crescente.
- **L** — Repositório fake de teste e repositório SQLAlchemy devem levantar as mesmas exceções de domínio (ex.: `OrderNotFound`), não `NoResultFound` num e `None` no outro.
- **I** — `Protocol`s pequenos e específicos do uso; casos de uso dependem só dos métodos que chamam.
- **D** — Caso de uso recebe portas no construtor; `Depends` faz a montagem:

```python
class PlaceOrder:
    def __init__(self, orders: OrderRepository, payments: PaymentGateway) -> None:
        self._orders, self._payments = orders, payments

def get_place_order(session: Session = Depends(get_session)) -> PlaceOrder:
    return PlaceOrder(SqlAlchemyOrderRepository(session), StripeGateway(settings))

@router.post("/orders", status_code=201)
def create(body: OrderIn, uc: PlaceOrder = Depends(get_place_order)) -> OrderOut:
    return OrderOut.from_entity(uc.execute(body.to_command()))
```

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| `Depends` para DI | Sempre — sessão, settings, usuário autenticado, casos de uso | Para lógica de negócio (Depends é só montagem) |
| Repository | Nível `clean`, ou query complexa reutilizada | Nível `simple` com CRUD trivial — use a `Session` no service |
| Unit of Work | Caso de uso grava em mais de um repositório numa transação | Operação de leitura ou gravação única |
| Schemas separados in/out | Sempre (`OrderIn`, `OrderOut`) — evita vazar campos internos | — |
| Service layer | Nível `simple`, regras moderadas | Virar "god service" com 30 métodos — quebre por caso de uso |
| Strategy via `Protocol` | Múltiplos provedores intercambiáveis | Só existe uma implementação e não há plano de outra |
| Background tasks / fila | Trabalho lento pós-resposta (`BackgroundTasks` para leve; fila dedicada para crítico) | Algo que precisa de garantia de entrega — não use `BackgroundTasks` |

Anti-padrões comuns:
- Model SQLAlchemy retornado direto como resposta (vaza campos, causa lazy-load fora da sessão).
- Chamada bloqueante (driver síncrono, `requests`) dentro de `async def` — use `def` (roda em threadpool) ou cliente async.
- Sessão global/módulo em vez de `Depends(get_session)` por request.
- `except Exception: pass` ou retornar 200 com `{"error": ...}`.
- Lógica em validators do Pydantic que acessa banco.
- Migrations editadas depois de aplicadas — crie nova revisão.

## 7. Erros, validação, configuração e logging

- Validação de formato/tipo: Pydantic nos schemas. Invariantes de negócio: domínio (exceções próprias, ex. `InsufficientBalance`).
- Mapeie exceções de domínio para HTTP em um ponto só (`app.add_exception_handler(DomainError, handler)`), retornando corpo consistente (ex.: RFC 9457 problem details).
- Config com `pydantic-settings` (`class Settings(BaseSettings)`), lendo variáveis de ambiente; `.env` só em dev e no `.gitignore`. Segredos como `SecretStr`.
- Logging com `logging` padrão (ou `structlog` se o projeto já usa), formato JSON em produção, com request id. Nunca logue tokens, senhas, dados de cartão ou documentos pessoais.

## 8. Testes

- **Unitário** (`pytest`): domínio e casos de uso com fakes das portas — rápidos, sem I/O.
- **API**: `TestClient` (sync) ou `httpx.AsyncClient` com `ASGITransport` (async); substitua dependências com `app.dependency_overrides`.
- **Integração**: repositórios contra banco real via **Testcontainers** (`testcontainers[postgres]`) com Alembic aplicado; nunca SQLite fingindo ser Postgres quando houver SQL específico.

```python
def test_create_order(client: TestClient) -> None:
    app.dependency_overrides[get_place_order] = lambda: PlaceOrder(FakeOrders(), FakePayments())
    resp = client.post("/orders", json={"items": [{"sku": "A1", "qty": 2}]})
    assert resp.status_code == 201
    assert resp.json()["status"] == "pending"
    app.dependency_overrides.clear()
```

## 9. Comandos de qualidade

```bash
ruff format --check .
ruff check .
mypy src            # ou pyright, conforme o projeto
lint-imports        # se nível clean
pytest -q --cov
```

Steps para o job `validate` (ajuste para Poetry/pip se o projeto não usa uv):

```yaml
      - uses: astral-sh/setup-uv@v5
      - run: uv sync --frozen
      - run: uv run ruff format --check .
      - run: uv run ruff check .
      - run: uv run mypy src
      - run: uv run lint-imports
      - run: uv run pytest -q --cov
```

## 10. Checklist de PR

- [ ] Routers finos: sem query nem regra de negócio
- [ ] Schemas de entrada e saída separados; nenhum model ORM na resposta
- [ ] Nenhuma chamada bloqueante dentro de `async def`
- [ ] Exceções de domínio mapeadas no handler central, não espalhadas
- [ ] Transações multi-repositório via Unit of Work
- [ ] Nova migration Alembic para toda mudança de schema (e reversível)
- [ ] Config via `Settings`; nenhum segredo no código
- [ ] `lint-imports` passando (nível clean)
- [ ] Testes unitários do caso de uso + teste de API do endpoint alterado
- [ ] `ruff`, `mypy` e `pytest` passando
