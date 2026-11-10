# Engenharia (sempre ativo)

- Antes de implementar ou revisar código, use a skill `engineering-standards` e o guia da stack. Ela define Clean Architecture, SOLID, patterns e testes da casa.
- O nível de arquitetura (`simple` | `clean` | `clean+ddd`) vem do ADR do projeto. Siga-o; nunca mude de nível sem ADR novo.
- Domínio não depende de framework, ORM, HTTP ou SDK externo. Regra de negócio não fica em controller/tela.
- Pattern só para problema presente, citado no PR com o motivo. Sem abstração "para o futuro".
- PR pequeno e de propósito único: um tipo (feat/fix/refactor/test/chore/docs), dentro do orçamento da label `size:*` (xs ≤50, s ≤150, m ≤400 linhas). Passou disso, pare e fatie (`engineering-standards` → small-prs.md). Sem mudanças de carona.
- Toda UI (tela, página, componente, estilo, e-mail) segue o protocolo de design de `rules/10-ui-design.md`: `design-references` (peças públicas) → `ui-ux-pro-max` → `design-taste-frontend` → `impeccable` → `frontend-design` + `emil-design-eng` → motion com `gsap-*`/`lenis` → `unslop-design` → `web-design-guidelines`; páginas públicas também passam por `seo-audit` e `site-compliance`. Vale na nuvem e no Mac; registre as skills aplicadas na seção Design do PR.
- Todo critério de aceite tem teste. Não remova nem pule teste para o CI passar.
- API de biblioteca: confirme no Context7 para a versão instalada, não confie na memória.
- Nunca escreva segredo em código, log, teste ou commit. Config por variável de ambiente.
