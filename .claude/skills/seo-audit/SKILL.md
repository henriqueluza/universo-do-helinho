---
name: seo-audit
description: Maximiza o SEO de sites cobrindo o que o Google avalia — Search Essentials (requisitos técnicos, políticas de spam, boas práticas), rastreabilidade e indexação (robots, sitemap, canonical, status HTTP, JS rendering), Core Web Vitals (LCP, INP, CLS) e page experience, mobile-first, HTTPS, dados estruturados (schema.org/JSON-LD e rich results), títulos/meta/headings/links, imagens e vídeo, conteúdo útil e E-E-A-T, internacionalização (hreflang), Open Graph. Use ao criar ou alterar qualquer página pública, rota, layout, head, metadata, sitemap, robots, imagens ou conteúdo de marketing/blog/documentação, e como revisor (seo-reviewer) de PRs que tocam páginas públicas.
---

# SEO — o que o Google avalia

Dois usos: **construir** (ao criar páginas, siga a seção 2 como requisito) e **auditar** (rode o checker + checklist completo em `references/checklist.md`). Páginas internas autenticadas (dashboard, admin) só precisam de `noindex` correto — não gaste SEO nelas.

## 1. Como rodar

```bash
# HTML já renderizado (saída de build, export estático, ou curl da página em dev)
python3 <skill-dir>/scripts/seo_check.py <arquivo.html | pasta-com-html> [--base https://site.com]
```
O checker valida por página: `<title>` (presença, 15–60 chars, único), meta description (50–160, única), `lang`, `viewport`, `canonical`, `robots`, um só `<h1>`, hierarquia de headings, `alt` em imagens, links sem texto, JSON-LD válido (parse + `@type`), Open Graph/Twitter mínimos, `hreflang` recíproco, e na pasta: `robots.txt`, `sitemap.xml` (URLs válidas, ≤ 50k, sem `noindex`), `404` próprio. É estático: performance real exige campo/lab (abaixo).

Para Core Web Vitals, use dado de **laboratório** no PR (Lighthouse via Playwright/Chrome, se disponível) e trate dado de **campo** (CrUX/Search Console) como fonte da verdade depois do deploy. Limiares atuais (confirme em web.dev/vitals quando tiver acesso à web): **LCP ≤ 2,5 s · INP ≤ 200 ms · CLS ≤ 0,1**, no percentil 75, mobile.

## 2. Requisitos ao construir uma página pública

1. **Indexável e rastreável:** HTTP 200, não bloqueada em `robots.txt`, sem `noindex` acidental, conteúdo principal presente no HTML servido (SSR/SSG; se CSR, confirme que o Googlebot renderiza — evite conteúdo só após interação). Listada no `sitemap.xml`. Uma URL canônica por conteúdo (`<link rel="canonical">`, redirects 301 de variantes: `www`, trailing slash, `http`, maiúsculas, parâmetros).
2. **Head completo:** `<title>` único e específico (marca no fim), meta description que convida ao clique, `lang`, `viewport`, OG (`og:title`, `og:description`, `og:image` 1200×630, `og:url`, `og:type`), `twitter:card`. Favicon e `theme-color`.
3. **Conteúdo:** um `<h1>` que responde à intenção de busca; headings em ordem (h2 → h3) descrevendo seções; texto original, útil, com a pessoa/empresa responsável identificável (E-E-A-T: autor, data, "sobre", contato). Sem conteúdo raspado, gerado em escala sem valor, cloaking, texto escondido, keyword stuffing, doorway pages, links comprados — políticas de spam do Google derrubam o site inteiro.
4. **Links:** âncoras descritivas (nunca "clique aqui"), links internos para páginas relacionadas, `rel="nofollow"/"sponsored"/"ugc"` onde aplicável, sem links quebrados, navegação alcançável sem JS complexo.
5. **Imagens e mídia:** `alt` descritivo (vazio só se decorativa), formatos modernos (AVIF/WebP), `width`/`height` ou `aspect-ratio` (CLS), `loading="lazy"` fora da dobra e **nunca** na imagem LCP (`fetchpriority="high"` nela), `srcset`/`sizes`, nome de arquivo descritivo. Vídeo: poster, `VideoObject`, transcrição se relevante.
6. **Dados estruturados** (JSON-LD, schema.org) condizentes com o conteúdo visível: `Organization`/`WebSite` no home, `Article`/`BlogPosting`, `Product` + `Offer`, `FAQPage`, `BreadcrumbList`, `LocalBusiness`, `SoftwareApplication` conforme o caso. Valide com o Rich Results Test quando tiver acesso; dados estruturados que não correspondem à página são spam.
7. **Mobile-first:** mesmo conteúdo em mobile e desktop, alvos de toque ≥ 44px, sem interstitials intrusivos (banner de cookie não pode cobrir o conteúdo principal), fontes legíveis sem zoom.
8. **Performance (CWV):** LCP — imagem/texto principal servido do HTML, pré-carregado, sem lazy; fontes com `font-display: swap` e preload da principal; CSS crítico inline, resto adiado. INP — JS mínimo no caminho de interação, sem long tasks > 50 ms, handlers leves, `content-visibility` em seções longas. CLS — dimensões reservadas para imagens/ads/embeds, fontes com fallback métrico, nada inserido acima do conteúdo após o load.
9. **HTTPS** em tudo, sem mixed content, HSTS. Redirect `http → https`.
10. **Internacional:** `hreflang` recíproco incluindo `x-default`, uma URL por idioma, `lang` correto, sem redirect automático por IP que esconda versões do Googlebot.

## 3. Auditoria (revisão)

Rode o checker, depois percorra `references/checklist.md` (seções: Técnico · Conteúdo · Performance · Dados estruturados · Imagens · Internacional · Spam/Qualidade · Monitoramento). Reporte `[bloqueante|sugestão] página — problema — correção`. **Bloqueante:** página pública sem `title`/`h1`/canonical, `noindex` indevido, bloqueio em robots, conteúdo principal só via JS pós-interação, JSON-LD inválido ou não condizente, imagem LCP lazy, violação de política de spam, CWV de laboratório vermelho na página alterada.

## 4. Depois do deploy

Search Console: enviar sitemap, checar Cobertura/Indexação, relatório de CWV e Page Experience, Rich Results, Links. Monitorar semanalmente; regressão de CWV após deploy vira Issue.
