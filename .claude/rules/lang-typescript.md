---
paths:
  - "**/*.ts"
  - "**/*.tsx"
  - "**/*.mts"
---

# TypeScript (NestJS, Next.js, React Native)
- Guia por framework: `engineering-standards` → stacks/nestjs.md, stacks/react-nextjs.md ou stacks/react-native-expo.md (veja `package.json`).
- `strict: true`; proibido `any` sem comentário justificando; prefira `unknown` + narrowing.
- Valide toda entrada externa em runtime (zod/class-validator) — tipos não validam dados.
- Sem segredo em código de cliente: só variáveis explicitamente públicas (`NEXT_PUBLIC_*`, `EXPO_PUBLIC_*`) chegam ao bundle.
- Lint, typecheck (`tsc --noEmit`) e testes antes do PR.
