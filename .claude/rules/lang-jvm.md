---
paths:
  - "**/*.java"
  - "**/*.kt"
  - "**/*.kts"
---

# Java / Kotlin (Spring)
- Guia completo: `engineering-standards` → references/stacks/spring.md.
- Injeção por construtor; nunca `@Autowired` em campo.
- Entidades JPA não são expostas pela API: use DTOs/records.
- `@Transactional` no caso de uso (application), não no controller.
- Build com testes (`./mvnw verify` ou `./gradlew check`) antes do PR.
