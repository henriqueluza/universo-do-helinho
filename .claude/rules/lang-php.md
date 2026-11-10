---
paths:
  - "**/*.php"
---

# PHP (Laravel)
- Guia completo: `engineering-standards` → references/stacks/laravel.md.
- `declare(strict_types=1);` e tipos em parâmetros/retornos.
- Validação em Form Requests; autorização em Policies; controllers finos.
- Nada de query com concatenação; mass assignment protegido (`$fillable`).
- `pint`, `phpstan`/`larastan` e `php artisan test` antes do PR.
