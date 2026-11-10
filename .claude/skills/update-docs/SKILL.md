---
name: update-docs
description: Decide se um diff exige documentação e o que atualizar (API, modelo de dados, ADR, README/setup, infra/runbooks). Usada pelo subagente docs-writer no fim de toda implementação, no mesmo PR. Não use para mudanças puramente internas (refactor sem efeito externo).
---

# Atualizar documentação

1. Antes de escrever qualquer coisa, confirme: o diff muda contrato de API, schema, arquitetura, infraestrutura, configuração/setup ou comportamento visível? Se não, pare — não há documentação a atualizar.
2. Identifique o documento afetado:
   - Endpoint/API → `docs/api/` (ou a especificação OpenAPI do projeto)
   - Schema/migration → `docs/data-model.md`
   - Decisão arquitetural → **novo** ADR em `docs/architecture/<projeto>/`; nunca edite um ADR antigo (marque-o como "Substituído por ADR-NNNN")
   - Variável de ambiente ou setup local → `README.md` (setup) + `.env.example` sem valores
   - Infraestrutura/deploy → `docs/infra.md`; procedimento operacional ou alerta → `docs/runbooks/<tema>.md`
3. Edite apenas a seção afetada. Não reescreva o documento inteiro.
4. Seja objetivo: o que mudou e por quê, com exemplo quando for API. Não repita o que já estava correto.
5. A documentação vai no **mesmo PR** da mudança, nunca em PR separado (exceto PRs `docs:` puros).
6. Sem CHANGELOG manual: as notas de release saem dos títulos dos PRs em Conventional Commits.
