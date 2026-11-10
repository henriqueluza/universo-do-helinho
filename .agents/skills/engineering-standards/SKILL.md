---
name: engineering-standards
description: Padrões de engenharia da software house (Clean Architecture, SOLID, design patterns, testes) com guias por stack — .NET, Spring, Go, NestJS, React/Next.js, React Native/Expo, Flutter, FastAPI, Rust, Laravel. Use ANTES de implementar uma Issue, ao propor arquitetura em um ADR, e ao revisar um PR quanto a design e qualidade de código.
---

# Engineering Standards

Objetivo: código que um humano consegue manter daqui a 2 anos. Clean Architecture e patterns são ferramentas, não metas — a quantidade de estrutura é proporcional ao problema.

## Procedimento (siga na ordem)

1. **Identifique a stack** pelos manifestos na raiz do repositório e leia o guia correspondente:

   | Manifesto | Guia |
   |---|---|
   | `*.csproj`, `*.sln` | [stacks/dotnet.md](references/stacks/dotnet.md) |
   | `pom.xml`, `build.gradle(.kts)` com Spring | [stacks/spring.md](references/stacks/spring.md) |
   | `go.mod` | [stacks/go.md](references/stacks/go.md) |
   | `package.json` com `@nestjs/core` | [stacks/nestjs.md](references/stacks/nestjs.md) |
   | `package.json` com `next` | [stacks/react-nextjs.md](references/stacks/react-nextjs.md) |
   | `package.json` com `expo` ou `react-native` | [stacks/react-native-expo.md](references/stacks/react-native-expo.md) |
   | `pubspec.yaml` | [stacks/flutter.md](references/stacks/flutter.md) |
   | `pyproject.toml`/`requirements.txt` com `fastapi` | [stacks/fastapi.md](references/stacks/fastapi.md) |
   | `Cargo.toml` | [stacks/rust.md](references/stacks/rust.md) |
   | `composer.json` com `laravel/framework` | [stacks/laravel.md](references/stacks/laravel.md) |

   Monorepo: leia o guia de cada pacote que o diff toca. Stack fora da tabela: aplique os guias transversais abaixo e registre no PR que não há guia específico.

2. **Leia o nível de arquitetura no ADR** (`docs/architecture/*/ADR-*.md`, campo "Nível de arquitetura"):
   - `simple` — camadas enxutas/MVC. Para CRUD, sites institucionais, protótipos.
   - `clean` — domain / application / infrastructure / presentation, regra de dependência apontando para dentro.
   - `clean+ddd` — clean + DDD tático (agregados, value objects, eventos de domínio). Só para domínio rico (pagamentos, regras financeiras, jogo com regras complexas).
   Sem ADR ou sem o campo: siga a estrutura que já existe no código. Nunca migre o projeto de nível por conta própria — isso é decisão de ADR.

3. **Consulte os guias transversais quando a decisão aparecer**:
   - Camadas, portas/adapters, onde colocar cada coisa → [clean-architecture.md](references/clean-architecture.md)
   - Classe/módulo crescendo, muitos `if` por tipo, dependência concreta difícil de testar → [solid.md](references/solid.md)
   - Pensando em introduzir um pattern → [design-patterns.md](references/design-patterns.md) (tem a coluna "quando NÃO usar")
   - O que testar e em que nível → [testing.md](references/testing.md)
   - Tamanho e fatiamento de PRs, dependências entre Issues → [small-prs.md](references/small-prs.md) — **leia sempre antes de começar uma Issue e ao criar Issues**

4. **APIs de bibliotecas**: não confie na memória para assinatura de API de framework. Use o MCP Context7 (`resolve-library-id` → `get-library-docs`) para a versão instalada no projeto.

5. **Antes de abrir o PR**, passe pelo checklist da seção 10 do guia da stack e pelo checklist abaixo.

## Checklist universal

- [ ] O PR tem um único propósito (um tipo: feat, fix, refactor, test, chore, docs) e está dentro do orçamento da label `size:*` (`python3 scripts/pr-size.py`).
- [ ] Fiz só o que os critérios de aceite pedem (YAGNI). Nenhuma abstração "para o futuro", nenhuma mudança de carona.
- [ ] Regra de dependência respeitada: domínio não importa framework, ORM, HTTP nem SDK de terceiros.
- [ ] Cada pattern novo resolve um problema presente e está nomeado no PR com o motivo.
- [ ] Nomes revelam intenção; funções fazem uma coisa; sem código morto ou comentado.
- [ ] Erros tratados explicitamente; nada engolido em `catch` vazio.
- [ ] Configuração por ambiente; nenhum segredo em código, log, teste ou fixture.
- [ ] Testes cobrem os critérios de aceite; lógica de domínio testada sem infraestrutura.
- [ ] Lint, format, typecheck e testes passando localmente.
