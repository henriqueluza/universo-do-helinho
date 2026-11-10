---
paths:
  - "**/*.go"
---

# Go
- Guia completo: `engineering-standards` → references/stacks/go.md.
- Seja idiomático: interfaces pequenas definidas no pacote consumidor; não importe camadas estilo Java.
- Erros sempre tratados e embrulhados com contexto (`fmt.Errorf("...: %w", err)`); sem `panic` em fluxo normal.
- `context.Context` como primeiro parâmetro em I/O.
- `gofmt`, `go vet`, `golangci-lint` e `go test -race ./...` antes do PR.
