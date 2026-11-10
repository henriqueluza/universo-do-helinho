---
paths:
  - "**/*.rs"
---

# Rust
- Guia completo: `engineering-standards` → references/stacks/rust.md.
- Sem `unwrap()`/`expect()` fora de testes e inicialização; erros tipados (`thiserror` em libs, `anyhow` só em binários).
- `unsafe` proibido sem ADR.
- `cargo fmt`, `cargo clippy -- -D warnings` e `cargo test` antes do PR.
