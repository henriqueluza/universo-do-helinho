# Stack: Go (net/http, chi ou similar)

> Versões: use a versão estável atual do Go (declarada em `go.mod`) e versões estáveis atuais das libs — confirme via Context7. Prefira a biblioteca padrão; adicione dependência só com motivo claro.

**Aviso importante:** Go não é Java. Não crie camadas Java-like (`interfaces/`, `impl/`, `dto/`, `services/` com uma interface por struct), nem `IUserRepository` com 15 métodos "para o caso de precisar". Os princípios (dependência apontando para o domínio, regras isoladas de I/O) valem; a forma é a idiomática de Go: pacotes pequenos por responsabilidade, interfaces pequenas definidas por quem consome, composição.

## 1. Detecção

- Arquivos: `go.mod`, `go.sum`, `go.work` (multi-módulo), `cmd/<app>/main.go`, `.golangci.yml`.
- Router: `github.com/go-chi/chi` no `go.mod`, ou só `net/http` (o `ServeMux` padrão já faz roteamento por método e path params).
- Confirmar versão: `go version`, `go env GOVERSION`, linha `go` e `toolchain` do `go.mod`, `go list -m all`.

## 2. Níveis de arquitetura

O ADR do projeto define o nível. Não suba de nível sem ADR novo.

`simple` — poucos pacotes, organizados por feature:

```
cmd/api/main.go           # wiring e start do servidor
internal/
  order/
    handler.go            # HTTP
    service.go            # regras
    store.go              # SQL (database/sql, pgx ou sqlc)
  platform/db/db.go
go.mod
```

`clean` — domínio no centro, adapters por fora, tudo sob `internal/`:

```
cmd/api/main.go           # composition root: cria adapters e injeta
internal/
  order/                  # domínio + casos de uso do contexto "order"
    order.go              # entidade, regras, erros de domínio
    service.go            # casos de uso; declara as interfaces que consome
  postgres/               # adapter: implementa as interfaces de order
    order_store.go
  httpapi/                # adapter de entrada: handlers, DTOs, rotas
    order_handler.go
  payment/stripe/         # adapter de gateway externo
```

`clean+ddd` — um pacote por bounded context em `internal/`, com agregados e eventos de domínio como tipos simples; mesma regra de dependência.

## 3. Mapeamento Clean Architecture → construtos

| Conceito | Onde fica em Go |
|---|---|
| Entidades / value objects | Structs no pacote de domínio (`internal/order`); construtor `New...` valida invariantes; campos não exportados quando a invariante importa |
| Casos de uso | Métodos de um `Service` no pacote de domínio |
| Portas | Interfaces pequenas declaradas **no pacote que consome** (`order.Store`), não no adapter |
| Adapters | Pacotes `postgres`, `stripe`, `httpapi` — dependem do domínio, nunca o contrário |
| Entrada HTTP | Handlers em `httpapi`; convertem request → chamada de serviço → response |
| Composition root | `cmd/<app>/main.go` (ou `run()` chamado por ele): monta tudo à mão; não use container de DI |

## 4. Impondo a regra de dependência

1. `internal/` impede import por módulos externos.
2. `depguard` no `golangci-lint` bloqueia imports proibidos no domínio:

```yaml
# .golangci.yml (trecho)
linters:
  enable: [depguard]
linters-settings:
  depguard:
    rules:
      domain:
        files: ["**/internal/order/**"]
        deny:
          - pkg: "database/sql"
            desc: "domínio não acessa banco; use a interface Store"
          - pkg: "net/http"
            desc: "domínio não conhece HTTP"
```

Para regras por camada mais ricas, use `go-arch-lint` com `.go-arch-lint.yml`. Confirme a sintaxe atual do `.golangci.yml` via Context7 (o formato muda entre versões maiores).

## 5. SOLID idiomático

- **SRP**: um pacote, uma responsabilidade; handler não monta SQL. Violação típica: `utils`/`common`/`helpers` com tudo.
- **OCP**: estenda por composição e novas implementações de interface; embedding para reaproveitar comportamento.
- **LSP**: implementações de interface devem respeitar o contrato documentado (ex.: retornar `ErrNotFound` do domínio, não `sql.ErrNoRows`).
- **ISP**: "quanto maior a interface, mais fraca a abstração" — interfaces de 1–3 métodos, declaradas pelo consumidor.
- **DIP**: o serviço recebe interfaces no construtor; "aceite interfaces, retorne structs".

```go
// internal/order/service.go
type Store interface {
	Save(ctx context.Context, o Order) error
}

type Service struct {
	store Store
	now   func() time.Time
}

func NewService(s Store, now func() time.Time) *Service {
	return &Service{store: s, now: now}
}
```

## 6. Padrões recomendados e quando NÃO usar

| Padrão | Use quando | Evite quando |
|---|---|---|
| Interface pequena no consumidor | Precisa trocar/mockar uma dependência | Só existe uma implementação e não há teste que precise do fake — use o tipo concreto |
| Functional options (`WithTimeout(...)`) | Construtor com muitas opções opcionais (clientes, servidores) | 1–3 parâmetros obrigatórios — use parâmetros normais ou struct `Config` |
| Middleware (`func(http.Handler) http.Handler`) | Cross-cutting: log, auth, recover, request id | Regra de negócio |
| Strategy via interface/func | Regras que variam (frete, taxa) | Só uma variação |
| Worker pool / errgroup | Concorrência limitada com cancelamento por `context` | Trabalho sequencial simples |
| sqlc (SQL → código tipado) | Projetos com SQL relevante | ORM pesado "para abstrair o banco" |

Anti-padrões comuns:
- Interfaces declaradas junto da implementação "para o caso de precisar" (`type UserService interface` + `userServiceImpl`).
- Pacotes `models`, `utils`, `common` que todo mundo importa (ciclos e acoplamento).
- Ignorar erro (`_ = f()`) ou `panic` para controle de fluxo.
- Goroutine sem dono: sem `context`, sem forma de parar, sem esperar término (vazamento).
- Variáveis globais mutáveis e `init()` com efeitos colaterais (conexão a banco no `init`).
- Container de DI/reflection mágica — o wiring manual em `main` é o idiomático.

## 7. Erros, validação, configuração e logging

- Erros são valores: retorne `error`, embrulhe com contexto `fmt.Errorf("save order %s: %w", id, err)`, compare com `errors.Is`/`errors.As`. Erros de domínio como sentinelas (`var ErrEmptyOrder = errors.New(...)`) ou tipos.
- Traduza erros para HTTP só no handler (mapa `ErrNotFound → 404`).
- `context.Context` como primeiro parâmetro em toda operação de I/O; respeite cancelamento e timeouts (`http.Server` com `ReadHeaderTimeout`).
- Validação de entrada no handler/DTO; invariantes no construtor de domínio.
- Config: lida uma vez no `main` a partir de variáveis de ambiente para uma struct `Config`, validada no startup; nenhum segredo no código ou no repositório.
- Logging: `log/slog` estruturado (`slog.Info("order placed", "order_id", id)`), handler JSON em produção; sem PII/tokens.

## 8. Testes

- Ferramentas: `testing` padrão, table-driven tests, `httptest` para handlers, `go test -race`, Testcontainers for Go para banco real; `testify` opcional (o padrão basta).
- Pirâmide: testes do domínio com fakes escritos à mão (struct que implementa a interface pequena) → integração dos adapters com banco em container → poucos testes E2E da API.
- Prefira fakes simples a geradores de mock.

```go
func TestNewOrder_SemItens(t *testing.T) {
	_, err := order.New("cust-1", nil, time.Now())
	if !errors.Is(err, order.ErrEmptyOrder) {
		t.Fatalf("esperava ErrEmptyOrder, veio %v", err)
	}
}
```

## 9. Comandos de qualidade

```bash
gofmt -l . | (! grep .)        # falha se houver arquivo não formatado
go vet ./...
golangci-lint run
go test -race -cover ./...
govulncheck ./...
```

```yaml
      - uses: actions/setup-go@v5
        with:
          go-version-file: go.mod
      - run: test -z "$(gofmt -l .)"
      - run: go vet ./...
      - uses: golangci/golangci-lint-action@v6
      - run: go test -race -cover ./...
      - run: go run golang.org/x/vuln/cmd/govulncheck@latest ./...
```

(Fixe as versões das actions e do govulncheck conforme a política de supply chain do projeto.)

## 10. Checklist de PR (Go)

- [ ] Nível de arquitetura do ADR respeitado; domínio não importa `database/sql`, `net/http` nem adapters (depguard verde)
- [ ] Interfaces pequenas, declaradas pelo consumidor; nada de interface "por precaução"
- [ ] Wiring explícito em `cmd/.../main.go`; sem globais mutáveis nem `init()` com efeitos
- [ ] Todo erro tratado e embrulhado com `%w`; sem `panic` para fluxo normal
- [ ] `context.Context` propagado em I/O; timeouts configurados
- [ ] Goroutines com dono, cancelamento e espera (`errgroup`/`WaitGroup`)
- [ ] Logs com `slog` estruturado, sem dados sensíveis
- [ ] Config por variáveis de ambiente validada no startup; nenhum segredo no repo
- [ ] Testes table-driven + integração com banco real para queries novas; `-race` verde
- [ ] `gofmt`, `go vet`, `golangci-lint` e `govulncheck` verdes
