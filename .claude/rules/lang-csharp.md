---
paths:
  - "**/*.cs"
  - "**/*.csproj"
---

# C# / .NET
- Guia completo: `engineering-standards` → references/stacks/dotnet.md.
- Nullable reference types habilitado; sem `!` para silenciar o compilador sem justificativa.
- `async` de ponta a ponta; nunca `.Result`/`.Wait()`; propague `CancellationToken`.
- Configuração via Options pattern (`IOptions<T>`), nunca leitura direta de `Environment` no domínio.
- EF Core fica em Infrastructure; entidades de domínio sem atributos de persistência (use Fluent API).
- `dotnet format` e `dotnet test` antes do PR.
