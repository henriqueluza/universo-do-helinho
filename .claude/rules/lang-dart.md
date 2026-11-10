---
paths:
  - "**/*.dart"
---

# Dart / Flutter
- Guia completo: `engineering-standards` → references/stacks/flutter.md.
- Widgets sem regra de negócio nem chamada HTTP direta; lógica em controllers/notifiers/blocs e casos de uso.
- Modelos imutáveis; `const` constructors onde possível.
- `dart format`, `flutter analyze` sem warnings novos e `flutter test` antes do PR.
