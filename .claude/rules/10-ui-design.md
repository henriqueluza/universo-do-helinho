---
paths:
  - "**/*.tsx"
  - "**/*.jsx"
  - "**/*.vue"
  - "**/*.svelte"
  - "**/*.astro"
  - "**/*.html"
  - "**/*.css"
  - "**/*.scss"
  - "**/*.razor"
  - "**/*.blade.php"
  - "**/*.dart"
  - "**/*.mjml"
  - "**/components/**"
  - "**/screens/**"
  - "**/pages/**"
  - "**/views/**"
  - "**/widgets/**"
  - "**/ui/**"
  - "**/emails/**"
  - "**/tailwind.config.*"
  - "**/DESIGN.md"
  - "**/sitemap*"
  - "**/robots.txt"
---

# Design de interface (obrigatório ao tocar UI)

Qualquer arquivo de UI que você crie ou altere — tela, página, componente, estilo, tema, animação, e-mail — passa pelo **protocolo de design** abaixo. Ele vale em qualquer executor (`exec:orca` e `exec:cloud`): todas as skills estão em `.claude/skills/` deste repositório. Pular uma etapa sem registrar é achado **bloqueante** no `ui-reviewer`.

## Protocolo

0. **Referências — `design-references`** (só para peças públicas: landing, hero, pricing, prova social, navbar, e-mail, OG image, post, anúncio, deck). Olhe 3–5 exemplos na galeria certa, extraia o padrão (não o visual) e escreva `docs/design/references-<peça>.md`. Dashboards e telas internas pulam este passo.
1. **Sistema visual — `ui-ux-pro-max`.** Antes de escrever a primeira linha de UI, rode a busca da skill para o domínio do produto (estilo, paleta, tipografia, guidelines de UX e o guia da stack). Se o projeto já tem `DESIGN.md`/tokens, eles mandam: use a skill para completar lacunas, nunca para reinventar o que já existe.
2. **Direção e anti-slop — `design-taste-frontend`.** Página pública: sorteie e escolha uma direção com `draw.mjs`. Tela de app (dashboard, admin, formulário, lista, configurações): `draw.mjs --app`. Siga a direção escolhida; releia-a antes de cada tela.
3. **Craft e acabamento — `impeccable`.** Rode `impeccable context` uma vez por sessão (carrega PRODUCT.md/DESIGN.md e o brief da superfície). Construa seguindo as diretivas; antes do PR, rode o fluxo de polish/audit da skill sobre as telas alteradas.
4. **Princípios durante a implementação:** `frontend-design` (identidade visual própria, nada de template) e `emil-design-eng` (componentes, animação, detalhes invisíveis).
5. **Motion:** qualquer animação além de transição CSS simples usa as skills oficiais `gsap-core` / `gsap-timeline` / `gsap-scrolltrigger` / `gsap-react` (ou `gsap-frameworks`) / `gsap-performance`; smooth scroll e parallax usam `lenis` (um único driver de frame, sincronizado com ScrollTrigger). `prefers-reduced-motion` sempre respeitado.
   - **5a. Ilustração ou demo de produto — `stageshow`** (condicional: só em **páginas de marketing** — hero, feature card, bento, hero de docs — que precisem de uma UI de produto ilustrada, parada ou animada com cursor). Parta do exemplo gold mais próximo em `examples/`, siga o workflow do `SKILL.md` (plano → look → história → tela → coreografia → rehearse) e entregue com `REHEARSAL.md` ao lado do componente. **Não se aplica a telas reais** (dashboard, login, formulários, admin): essas seguem os passos 1–4. Ícones de apps e marcas em `assets/` e `kit/brands/` são só referência e **não** são cobertos pela licença MIT (`NOTICE.md`): use os ícones e a marca do próprio produto.
6. **Checagem final de slop — `unslop-design`.** Rode `scripts/scan.py` na pasta alterada, leia a página renderizada e aplique as correções (produto real, feature principal ampliada, headline específica, um CTA, uma cor de acento, sem pills/eyebrows/gradientes/dividers/bordas em tudo). Zero achado sem justificativa.
7. **Auto-revisão antes do PR — `web-design-guidelines`** sobre todos os arquivos de UI alterados: acessibilidade (contraste, foco, labels, teclado, alvos de toque), estados (loading, vazio, erro, desabilitado), responsividade.
8. **Páginas públicas também passam por `seo-audit`** (head completo, canonical, h1 único, JSON-LD condizente, imagem LCP sem lazy, `scripts/seo_check.py` no HTML renderizado) **e por `site-compliance`** (documentos legais linkados, consentimento de cookies que bloqueia terceiros, formulários opt-in, dados da empresa, alt/contraste/teclado, `scripts/compliance_check.py`). Primeiro deploy em produção exige `site-compliance` completo (checklist ★ sem falha).

## Modo degradado (nuvem sem binário, Chrome ou Node 22)

Se um script de skill não rodar no ambiente (download do binário do `impeccable` bloqueado, `inspect.mjs` sem Chrome, Node antigo, galerias de referência inacessíveis), **não pule a skill**: leia o `SKILL.md` e os arquivos em `reference/`/`references/` dela e aplique o conteúdo manualmente. Os scripts em Python puro (`ui-ux-pro-max`, `unslop-design`, `seo-audit`, `site-compliance`) sempre rodam. Os scripts de filmagem do `stageshow` (`film.mjs --gate`) precisam de Chrome, Node >= 20.19 e `npm install` na pasta da skill; sem eles, construa a partir do exemplo gold e de `references/`, e anote no PR que o rehearsal **não foi filmado**.

## Registro no PR

Na seção **Design** do template, preencha "Skills de design aplicadas" com cada skill, a direção escolhida (`design-taste-frontend`), estilo/paleta/tipografia (`ui-ux-pro-max`), referências consultadas (`design-references`), resultado do `unslop-design` (sinais encontrados → corrigidos), o exemplo gold de partida e o veredito do `--gate` (`stageshow`, se usada), e para páginas públicas o resumo de `seo-audit` e `site-compliance`. Marque as etapas que rodaram em modo degradado. Sem esse registro o `ui-reviewer` reprova; `seo-reviewer` e `compliance-reviewer` cobram os seus.

## Fora do protocolo

Mudança que não altera como algo **parece, se move ou é usado** (renomear prop, extrair hook sem UI, corrigir tipo) não precisa do protocolo — diga isso em uma linha na seção Design.
