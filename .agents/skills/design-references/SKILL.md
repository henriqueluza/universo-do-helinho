---
name: design-references
description: Usa galerias curadas como referência de design antes de desenhar uma peça web — land-book.com e supahero.io (landing pages e heros), pricingpages.design (páginas de preço), socialproofexamples.com (prova social e depoimentos), reallygoodemails.com (e-mails transacionais e de marketing), deck.gallery (apresentações/pitch decks), posts.design (posts para redes sociais), adfolio.design (anúncios), navbar.gallery (navegação/headers), ogimage.gallery (imagens Open Graph). Use ao começar qualquer landing, hero, pricing, seção de depoimentos, navbar, e-mail, OG image, post, anúncio ou deck, e sempre que faltar referência concreta para uma decisão de layout, copy ou composição. Referência para estudar padrões, nunca para copiar.
---

# Referências de design

Antes de desenhar, **olhe o que os melhores fizeram para o mesmo tipo de peça**. Isso substitui a média da internet (que vira slop) por decisões observadas em trabalho real. Saída: um `references.md` curto com 3–5 referências, o padrão extraído de cada uma e a decisão tomada para o projeto.

## Mapa — qual galeria para qual peça

| Peça | Galeria | O que observar |
|---|---|---|
| Landing page completa, site de produto | **land-book.com** | estrutura de seções (ordem, quantidade), proporção hero/resto, como mostram o produto, ritmo de espaçamento, uma cor de acento |
| Hero (primeira dobra) | **supahero.io** | headline específica vs. vaga, relação headline/sub/CTA, presença do produto real, quanto cabe acima da dobra, tratamento de imagem |
| Página de preços | **pricingpages.design** | número de planos (2–3 vence), plano destacado, toggle mensal/anual, âncora de preço, tabela de comparação, FAQ, CTA por plano, lidar com "Enterprise" |
| Prova social, depoimentos, logos, números | **socialproofexamples.com** | depoimento com nome + cargo + foto + resultado concreto; logos com contexto; números verificáveis; onde na página aparece |
| E-mails (transacional, onboarding, newsletter, cobrança) | **reallygoodemails.com** | hierarquia em uma coluna, um CTA, dark mode, texto alternativo, rodapé com unsubscribe/endereço, tom |
| Apresentação, pitch deck | **deck.gallery** | um ponto por slide, dado grande, narrativa problema→solução→prova, consistência tipográfica |
| Posts para redes sociais | **posts.design** | formato por rede, texto legível em miniatura, série/consistência, uso de marca |
| Anúncios (display, social) | **adfolio.design** | promessa em uma linha, visual do produto, CTA, variações por formato |
| Navbar, header, menu | **navbar.gallery** | número de itens (≤ 6), CTA no header, comportamento no scroll, mobile menu, logo + nome |
| Imagem Open Graph | **ogimage.gallery** | título legível em 1200×630 a 300px de largura, marca, sem texto pequeno, template por tipo de página |

## Processo

1. **Defina a peça e o setor** (ex.: "pricing, SaaS B2B de folha de pagamento"). Se já houver direção do `design-taste-frontend` ou `DESIGN.md`, leia antes — referências servem à direção, não a substituem.
2. **Abra a galeria** da linha correspondente (WebFetch para a listagem; Playwright/Chrome para ver as peças renderizadas, quando disponível). Na nuvem sem acesso ao site, use o que a galeria tem em cache no WebFetch ou declare "referências indisponíveis" no PR e siga com `ui-ux-pro-max`.
3. **Escolha 3–5 exemplos** do mesmo setor ou do mesmo problema (não os mais bonitos — os mais parecidos com o nosso caso). Para cada um, escreva em `docs/design/references-<peça>.md`:
   - URL e nome
   - **Padrão** (uma frase sobre a estrutura/decisão, não sobre o visual): "3 planos, o do meio destacado com borda de acento, toggle anual acima com desconto em %"
   - **O que funciona** para o nosso usuário / **o que não se aplica**
4. **Extraia a decisão**: uma linha por elemento (estrutura, headline, CTA, prova social, imagem). Isso entra no plano da Issue e na seção Design do PR como "Referências: …".
5. **Construa** com `ui-ux-pro-max` (sistema), `impeccable` (craft) e `unslop-design` (checagem final). Rode `unslop-design` também sobre as referências mentalmente: galeria também tem slop; não importe eyebrows, pills e gradientes só porque apareceram lá.

## Regras

- **Padrões, não pixels.** Nunca copie layout, copy, ilustração, paleta ou tipografia de uma referência. O que se leva é a decisão estrutural; a execução é do produto.
- **Setor igual, diferenciação clara.** Se a referência é concorrente direto, anote o que fazer **diferente** — a página precisa ser reconhecível como nossa.
- **Imagens das galerias não entram no repositório** (direitos de terceiros). Só URL + notas.
- **Prova social e preços** seguem `site-compliance`: nada de depoimento inventado, logo sem permissão, preço "a partir de" sem o valor real.
- **Registre no PR** (seção Design → "Referências"): galerias consultadas, 3–5 URLs, padrão adotado. Sem registro, o `ui-reviewer` pede.
