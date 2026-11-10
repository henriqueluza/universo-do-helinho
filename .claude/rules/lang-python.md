---
paths:
  - "**/*.py"
---

# Python
- Guia completo: `engineering-standards` → references/stacks/fastapi.md.
- Type hints em toda função pública; `mypy`/`pyright` sem erros novos.
- `ruff` (lint + format) e `pytest` antes do PR.
- Pydantic nas fronteiras (request/response/config); domínio em dataclasses/classes puras sem dependência de FastAPI ou SQLAlchemy.
- Nada de `except Exception: pass`; nada de SQL por concatenação de string.
