<!-- Título no formato Conventional Commits: feat(escopo): ..., fix: ..., refactor: ..., test: ..., chore: ..., docs: ... -->

## Escopo

**Este PR faz uma coisa:** <!-- uma frase, sem "e" -->

- Tipo: <!-- feat | fix | refactor | test | chore | docs -->
- Tamanho: <!-- saída de `python3 scripts/pr-size.py` (ex.: 132 linhas · size:s) -->
- Fora deste PR (Issues de continuação): <!-- #N, #M ou "nada" -->

## Issue relacionada

Closes #

## Como revisar

<!-- ordem sugerida de leitura, 2–5 itens. Ex.: 1) domínio Reserva.cs, 2) caso de uso, 3) endpoint, 4) testes -->

## Design

- Nível de arquitetura seguido: <!-- simple | clean | clean+ddd -->
- Patterns introduzidos e motivo: <!-- "nenhum" é uma resposta ótima -->
- Skills de design aplicadas (obrigatório se o PR toca UI — `.claude/rules/10-ui-design.md`): <!-- design-references (galerias + 3–5 URLs + padrão adotado, se peça pública) · ui-ux-pro-max (estilo/paleta/tipografia) · design-taste-frontend (direção) · impeccable · frontend-design · emil-design-eng · gsap-*/lenis (se há motion) · unslop-design (sinais encontrados → corrigidos) · web-design-guidelines; marque as etapas em "modo degradado". Sem UI: "não toca UI" -->
- SEO e compliance (obrigatório se o PR toca página pública): <!-- seo-audit: title/description/canonical/JSON-LD/imagem LCP + saída do seo_check.py · site-compliance: documentos legais, consentimento, formulários, terceiros, a11y + saída do compliance_check.py; texto legal em rascunho → needs-human -->

## Evidências

- [ ] Testes rodados e passando (cole o resumo abaixo)
- [ ] Lint/typecheck sem erros
- [ ] Documentação atualizada (se o diff mudou contrato, schema ou comportamento)
- [ ] Nenhum teste foi removido ou marcado como skip
- [ ] Sem mudanças de carona (formatação, renomeações ou atualizações fora do escopo)

## Segurança

- [ ] Varredura local de segurança rodada no último commit (security-scan.sh, semgrep ou "indisponível — coberto pelo CI")
- [ ] Skills de segurança aplicadas (`owasp-security` ao escrever, `differential-review` ao revisar)
- [ ] Se a Issue é `security:sensitive`: ameaças e mitigações descritas abaixo

<!-- o que foi rodado e o resultado; se security:sensitive, liste as ameaças e como o código as trata -->

## Riscos e observações

<!-- trade-offs, dívida técnica deixada, pontos de atenção para a revisão -->
