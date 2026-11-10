# Skills de terceiros incluídas

Copiadas de `~/.agents/skills` em 29/10/2026 para funcionar também em sessões na nuvem (que não carregam skills globais). Atualize rodando `scripts/sync-kit.sh` no repositório ai-software-house.

| Skill | Origem | Licença |
|---|---|---|
| differential-review, fp-check, supply-chain-risk-auditor, agentic-actions-auditor | Trail of Bits (trailofbits/skills) | ver repositório de origem |
| security-review | getsentry/skills | ver `.claude/skills/security-review/LICENSE` |
| owasp-security | instalada localmente (origem registrada no ~/.agents/.skill-lock.json do autor) | ver origem |
| frontend-design | anthropics/skills | ver `.claude/skills/frontend-design/LICENSE.txt` |
| emil-design-eng | emilkowalski/skills | ver repositório de origem |
| web-design-guidelines | vercel-labs/agent-skills | ver repositório de origem |
| impeccable (v4.8.0) | pbakaus/impeccable | ver repositório de origem |
| ui-ux-pro-max | instalada de ~/.agents/skills (origem registrada no ~/.agents/.skill-lock.json do autor) | ver repositório de origem |
| design-taste-frontend (tasteskill v2) | Leonxlnx/taste-skill | ver repositório de origem |
| gsap-core, gsap-timeline, gsap-scrolltrigger, gsap-plugins, gsap-utils, gsap-react, gsap-performance, gsap-frameworks | greensock/gsap-skills (oficial, commit aed9cfd, 08/11/2026) | MIT — `LICENSE` em cada pasta |
| stageshow (v1.1.1) | benjitaylor/skills (commit 299140f, 09/11/2026) | MIT — `.claude/skills/stageshow/LICENSE` + `NOTICE.md` |

Skills **da casa** (autoria própria, não são de terceiros): `lenis` (escrita a partir do README oficial de darkroomengineering/lenis, MIT), `unslop-design`, `seo-audit`, `design-references`, `site-compliance`.

As skills de design pesadas (impeccable, ui-ux-pro-max, design-taste-frontend; 2–4 MB cada) foram copiadas para o kit em 07/11/2026 para que sessões na nuvem (`exec:cloud`) também as usem. Seu uso é obrigatório ao tocar UI — ver `.claude/rules/10-ui-design.md`. Na nuvem, scripts que dependem de binário baixado, Chrome ou Node 22 podem não rodar: a rule define o modo degradado (aplicar o conteúdo de `reference/` manualmente).

**stageshow** (UI de produto ilustrada e demos animadas para páginas de marketing) é uso condicional — ver passo 5a de `.claude/rules/10-ui-design.md`. A licença MIT cobre o código da skill; **não cobre** os ícones de apps em `assets/app-icons.ts` nem as marcas em `kit/brands/` (fontes e licenças por arquivo em `kit/brands/SOURCES.md`), que continuam com os respectivos donos e servem só de referência: não publique esses ícones/marcas em sites de clientes. Os scripts de filmagem precisam de Chrome, Node >= 20.19 e `npm install` na pasta da skill.
