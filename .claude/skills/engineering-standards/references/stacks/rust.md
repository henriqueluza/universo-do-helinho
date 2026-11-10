# Rust (axum + tokio, workspace de crates)

Guia para agentes que implementam Issues em serviços Rust. Leia junto com `../clean-architecture.md`, `../solid.md` e `../design-patterns.md`. Versões: use o toolchain estável atual e as versões estáveis atuais de `axum`, `tokio`, `sqlx`, `serde` — confirme APIs via Context7 (a API do axum muda entre versões maiores).

## 1. Detecção

- `Cargo.toml` na raiz; `[workspace]` com `members` → workspace de crates.
- `Cargo.lock` versionado (serviços/binários sempre versionam).
- `rust-toolchain.toml` → toolchain fixado pelo projeto; respeite.
- Dependências: `axum`, `tokio`, `sqlx` ou `diesel`/`sea-orm` em `Cargo.toml`.
- Confirmar versões: `rustc --version`, `cargo --version`, `cargo tree -d` (duplicatas), `cargo tree -i axum`.

## 2. Níveis de arquitetura

`simple` — serviço pequeno, um crate:

```
src/
  main.rs          # bootstrap, router, estado
  config.rs
  error.rs         # AppError + IntoResponse
  routes/
    orders.rs      # handlers
  db/
    orders.rs      # queries sqlx
  models.rs
migrations/
tests/             # testes de integração
```

`clean` — regras de negócio relevantes; o compilador impõe as camadas via crates:

```
Cargo.toml                 # [workspace]
crates/
  domain/                  # entidades, VOs, erros, traits de repositório — sem tokio/axum/sqlx
  application/             # casos de uso; depende só de domain
  infrastructure/          # impl sqlx dos repositórios, clients HTTP; depende de domain
  api/                     # axum: handlers, DTOs serde, AppError; depende de application
  server/                  # bin: config, composition root, main
migrations/
```

`clean+ddd` — agregados com invariantes garantidas por construtores privados e typestate; eventos de domínio. Só se o ADR pedir.

## 3. Mapeamento Clean Architecture → Rust/axum

| Conceito | Onde fica |
|---|---|
| Entidades / value objects | `domain` — structs com campos privados + construtores que validam; newtypes para IDs e valores |
| Casos de uso | `application` — struct genérica sobre as portas, ou funções que recebem `&impl Trait` |
| Portas | `domain` — `trait OrderRepository` (async via `async fn` em trait ou `#[async_trait]` se o projeto já usa) |
| Adapters | `infrastructure` — `PgOrderRepository { pool: PgPool }` |
| Handlers | `api` — funções `async` com extractors (`State`, `Json`, `Path`) |
| DTOs | `api` — structs `serde` separadas das entidades, com `From`/`TryFrom` |
| Composition root | `server/main.rs` — monta pool, repositórios, casos de uso, `AppState` |

## 4. Impondo a regra de dependência

- O `Cargo.toml` de cada crate **é** a regra: `domain` não declara `axum`, `sqlx` nem `tokio`; `application` depende só de `domain`. Dependência proibida = não compila.
- Use visibilidade: `pub(crate)` por padrão, exponha só a API pública do crate. Campos de entidades privados.
- Verifique no CI que o domínio continua limpo:

```bash
# falha se domain passar a depender de crates de infraestrutura
! cargo tree -p domain -e normal | grep -E '^\S*(axum|sqlx|tokio|reqwest) '
```

## 5. SOLID idiomático

Rust não tem herança; SOLID se aplica via traits, composição e ownership.

- **S** — Handler extrai, chama caso de uso, converte resposta. Queries ficam em `infrastructure`. Módulos coesos; arquivo crescendo demais é sinal de dividir.
- **O** — Comportamento novo = nova implementação de trait. `enum` + `match` exaustivo é idiomático e preferível quando o conjunto de variantes é fechado e conhecido (o compilador aponta casos faltantes).
- **L** — Toda implementação de um trait respeita o contrato documentado (mesmos erros, sem `panic!` onde o contrato retorna `Result`).
- **I** — Traits pequenos (`OrderReader`, `OrderWriter`); bounds genéricos pedem só o necessário.
- **D** — Casos de uso dependem de traits. Prefira genéricos (dispatch estático) e use `Arc<dyn Trait>` quando precisar trocar em runtime ou guardar em `AppState`. Ownership ajuda: o caso de uso recebe o que precisa por valor/`Arc`, sem estado global.

```rust
pub struct PlaceOrder<R: OrderRepository> { repo: R }

impl<R: OrderRepository> PlaceOrder<R> {
    pub async fn execute(&self, cmd: NewOrder) -> Result<Order, DomainError> {
        let order = Order::new(cmd.customer_id, cmd.items)?; // invariantes no construtor
        self.repo.save(&order).await?;
        Ok(order)
    }
}
```

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| Newtype (`struct OrderId(Uuid)`) | IDs e valores que não podem ser trocados por engano; validação no construtor | Tipo interno sem risco de confusão |
| Builder | Struct com muitos campos opcionais (configs, requests) | 2–3 campos — use construtor |
| Typestate | Transições de estado que devem ser impossíveis de pular (rascunho → pago) | Estados que mudam em runtime vindos de dados — use `enum` |
| `thiserror` em libs/domínio | Erros tipados que o chamador precisa distinguir | — |
| `anyhow` no binário | Erros de bootstrap/scripts onde só se reporta | Em `domain`/`application` — perde tipagem |
| Repository trait | Nível `clean`; testar caso de uso sem banco | Nível `simple` — funções sqlx diretas bastam |
| Extractors customizados | Auth/tenant reutilizados em muitos handlers | Lógica de negócio dentro do extractor |

Anti-padrões comuns:
- `.unwrap()`/`.expect()` em caminho de request (aceito só em testes e invariantes provadas, com comentário).
- Bloquear o runtime: I/O síncrono ou CPU pesado em handler async — use `tokio::task::spawn_blocking`.
- Segurar `MutexGuard` (std) através de `.await`.
- `clone()` espalhado para calar o borrow checker em vez de ajustar ownership/`Arc`.
- Entidades com todos os campos `pub` (invariantes violáveis por qualquer código).
- Um crate só com tudo, num projeto que o ADR definiu como `clean`.

## 7. Erros, validação, configuração e logging

- Um `enum AppError` em `api` com `impl IntoResponse`, convertendo `DomainError` via `From`; respostas de erro com corpo consistente e sem detalhes internos.
- Validação: parse, don't validate — `TryFrom<Dto> for Command` falha cedo; depois disso os tipos garantem validade.
- Config via variáveis de ambiente (ex.: crate `config` ou `envy` + `serde`), carregada uma vez no `server`. Segredos com `secrecy::SecretString` para não aparecerem em `Debug`/logs.
- Logging/tracing com `tracing` + `tracing-subscriber` (JSON em produção) e `tower-http::trace::TraceLayer`. Nunca logue tokens/dados pessoais; cuidado com `#[derive(Debug)]` em structs com segredos.

## 8. Testes

- **Unitários**: `#[cfg(test)] mod tests` no próprio arquivo — domínio e casos de uso com repositórios fake (struct em memória implementando o trait).
- **Integração**: `tests/` do crate `api`/`server`; dispare o router com `tower::ServiceExt::oneshot` sem subir porta.
- **Banco**: `#[sqlx::test]` (cria banco isolado por teste e aplica migrations) ou Testcontainers (`testcontainers` + módulo postgres).

```rust
#[tokio::test]
async fn create_order_returns_201() {
    let app = build_router(test_state());
    let req = Request::post("/orders")
        .header("content-type", "application/json")
        .body(Body::from(r#"{"items":[{"sku":"A1","qty":2}]}"#))
        .unwrap();
    let res = app.oneshot(req).await.unwrap();
    assert_eq!(res.status(), StatusCode::CREATED);
}
```

## 9. Comandos de qualidade

```bash
cargo fmt --all -- --check
cargo clippy --workspace --all-targets --all-features -- -D warnings
cargo test --workspace --all-features
cargo deny check        # se o projeto tem deny.toml (licenças/advisories)
```

Steps para o job `validate`:

```yaml
      - uses: dtolnay/rust-toolchain@stable
        with:
          components: rustfmt, clippy
      - uses: Swatinem/rust-cache@v2
      - run: cargo fmt --all -- --check
      - run: cargo clippy --workspace --all-targets --all-features -- -D warnings
      - run: cargo test --workspace --all-features
```

Se o projeto usa macros `sqlx::query!` com verificação offline, rode `cargo sqlx prepare --workspace` localmente e versione `.sqlx/`; no CI defina `SQLX_OFFLINE=true`.

## 10. Checklist de PR

- [ ] Crate `domain` sem dependência de axum/sqlx/tokio
- [ ] Nenhum `unwrap()`/`expect()` em caminho de request
- [ ] Nenhuma operação bloqueante em contexto async
- [ ] Erros tipados com `thiserror`; `AppError` mapeia para HTTP em um lugar
- [ ] Newtypes/construtores validando invariantes; campos de entidade privados
- [ ] Segredos em `SecretString`; nada sensível em `Debug`/logs
- [ ] Migration nova para toda mudança de schema
- [ ] Testes unitários do caso de uso + teste de integração do handler
- [ ] `cargo fmt`, `clippy -D warnings` e `cargo test` passando
- [ ] `Cargo.lock` atualizado e sem dependências duplicadas desnecessárias
