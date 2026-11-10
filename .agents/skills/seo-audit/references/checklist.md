# Checklist SEO — critérios avaliados pelo Google

Marque cada item como OK · FALHA · N/A, com a página e a correção.

## Técnico (rastreamento e indexação)
- [ ] Toda página pública responde 200; erros com status correto (404/410, nunca "soft 404" com 200)
- [ ] `robots.txt` existe, não bloqueia CSS/JS/imagens nem páginas que devem indexar; aponta para o sitemap
- [ ] `sitemap.xml` válido, só URLs canônicas 200, ≤ 50.000 URLs / 50 MB, `lastmod` real; índice de sitemaps se houver vários
- [ ] `<link rel="canonical">` absoluto em toda página, apontando para si mesma ou para a versão preferida; sem canonical para página `noindex` ou redirecionada
- [ ] Redirects 301 (não 302 permanente, não cadeias > 2, não loops): http→https, www/non-www, barra final, maiúsculas
- [ ] `noindex` só onde intencional (busca interna, filtros, área logada, obrigado, staging); staging protegido por auth, não por robots
- [ ] Conteúdo principal e links de navegação presentes no HTML inicial (SSR/SSG) ou renderizáveis pelo Googlebot sem interação; sem `#!`; estados via History API com URLs únicas
- [ ] Paginação com URLs próprias e links `<a href>` rastreáveis (não só botão JS / scroll infinito)
- [ ] Parâmetros de URL não geram duplicatas indexáveis (canonical ou `noindex`)
- [ ] URLs curtas, minúsculas, com hífens, descritivas, estáveis
- [ ] HTTPS válido, sem mixed content, HSTS; certificado cobre todos os hosts
- [ ] Sem interstitials intrusivos em mobile; banner de cookie não cobre o conteúdo principal
- [ ] Página 404 própria, útil, retornando 404
- [ ] Sem iframes com conteúdo essencial; sem Flash/plug-ins
- [ ] Favicon válido (≥ 48×48, múltiplo de 48), `theme-color`

## Conteúdo e on-page
- [ ] `<title>` único por página, 15–60 caracteres, palavra-chave principal no início, marca no fim
- [ ] Meta description única, 50–160 caracteres, descreve o valor e convida ao clique (Google pode reescrever; ainda assim)
- [ ] Exatamente um `<h1>`, alinhado à intenção de busca; h2/h3 em hierarquia, descritivos
- [ ] Conteúdo original, completo para a intenção, atualizado (data visível e `dateModified` quando mudar)
- [ ] E-E-A-T: autor identificável com bio, página "Sobre", contato, políticas; fontes citadas; experiência demonstrada
- [ ] Linguagem do público; sem keyword stuffing; sinônimos e perguntas relacionadas cobertas
- [ ] Âncoras descritivas; links internos para e a partir de páginas relacionadas; nenhuma página órfã
- [ ] Links externos relevantes; `rel="sponsored"` em pagos, `rel="ugc"` em gerados por usuário
- [ ] Sem links quebrados (internos e externos)
- [ ] Breadcrumbs visíveis + `BreadcrumbList`
- [ ] Open Graph e Twitter Card completos; `og:image` 1200×630 específica da página

## Performance e page experience (Core Web Vitals)
- [ ] LCP ≤ 2,5 s (p75 mobile): elemento LCP no HTML inicial, `fetchpriority="high"`, sem `loading="lazy"`, preload de fonte/hero, TTFB < 800 ms, CDN, cache
- [ ] INP ≤ 200 ms: JS no caminho crítico mínimo, sem long tasks > 50 ms, hidratação parcial/islands, handlers que cedem a thread (`scheduler.yield`/`setTimeout 0`), sem bibliotecas pesadas para coisa pequena
- [ ] CLS ≤ 0,1: `width`/`height` ou `aspect-ratio` em imagens/vídeos/iframes/ads, espaço reservado para conteúdo dinâmico, fontes com `size-adjust`/fallback métrico, nada inserido acima do viewport após load, animações só com `transform`/`opacity`
- [ ] Imagens AVIF/WebP, `srcset`/`sizes`, comprimidas, dimensionadas para o viewport
- [ ] Fontes: ≤ 2 famílias, subset, `font-display: swap`, self-hosted, preload só da principal
- [ ] CSS crítico inline, restante adiado; JS `defer`/`module`; terceiros carregados tarde ou em worker (Partytown) e auditados (`supply-chain-risk-auditor`)
- [ ] Cache HTTP (immutable para assets com hash), compressão Brotli, HTTP/2 ou 3
- [ ] Mobile: mesmo conteúdo que desktop, alvos ≥ 44px, texto ≥ 16px, sem overflow horizontal
- [ ] Lighthouse (lab) na página alterada: Performance, Acessibilidade, Best Practices e SEO sem itens vermelhos; registrar os números no PR

## Dados estruturados (schema.org, JSON-LD)
- [ ] JSON-LD válido (parse), com `@context` e `@type`, em `<script type="application/ld+json">`
- [ ] Tipo condizente com a página: `Organization`/`WebSite` (home, com `logo`, `sameAs`), `Article`/`BlogPosting` (`headline`, `author`, `datePublished`, `image`), `Product` (`offers` com `price`, `priceCurrency`, `availability`; `aggregateRating` só com avaliações reais), `FAQPage` (só perguntas visíveis), `BreadcrumbList`, `LocalBusiness` (endereço, horário, telefone), `SoftwareApplication`, `Event`, `JobPosting`, `VideoObject`
- [ ] Todo dado marcado está visível na página; nenhum dado inventado (viola política de dados estruturados)
- [ ] Propriedades obrigatórias e recomendadas do rich result desejado preenchidas; validado no Rich Results Test quando possível
- [ ] Sem marcação em páginas `noindex`

## Imagens e mídia
- [ ] `alt` descritivo e específico (vazio `alt=""` só para decorativas); sem "imagem de"
- [ ] Nome de arquivo descritivo; imagens hospedadas em URL estável; incluídas no sitemap de imagens se relevante
- [ ] Vídeo com `poster`, `VideoObject`, página própria se é o conteúdo principal, transcrição/legendas

## Internacional
- [ ] `hreflang` recíproco em todas as versões + `x-default`, com códigos ISO corretos (`pt-BR`, `en`)
- [ ] `lang` do `<html>` coerente; uma URL por idioma/região (subpasta ou subdomínio); sem redirecionamento automático por IP que esconda versões
- [ ] Conteúdo traduzido (não máquina crua) e localizado (moeda, formato de data)

## Spam e qualidade (políticas do Google — violação derruba o domínio)
- [ ] Sem cloaking, texto/links ocultos, doorway pages, redirects enganosos
- [ ] Sem conteúdo gerado em escala sem valor, raspado ou "spun"; sem abuso de reputação de site (conteúdo de terceiros parasitando o domínio); sem abuso de domínio expirado
- [ ] Sem links comprados/trocados sem `rel="sponsored"/"nofollow"`; sem keyword stuffing
- [ ] Sem malware, phishing, downloads enganosos; formulários e CTAs honestos (ver `site-compliance`)
- [ ] Avaliações, selos e números verificáveis (ver `site-compliance`)

## Monitoramento (pós-deploy)
- [ ] Search Console verificado; sitemap enviado; propriedade de domínio
- [ ] Relatórios de Indexação, Core Web Vitals, Page Experience, Rich Results, Links e Segurança sem erros novos
- [ ] Alertas para regressão de CWV e páginas 5xx/404 após deploy
- [ ] Revisão trimestral de conteúdo desatualizado e canibalização de palavras-chave
