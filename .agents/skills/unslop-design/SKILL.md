---
name: unslop-design
description: Remove a "cara de IA" de sites e interfaces. Audita e corrige os sinais clássicos de UI gerada (tags/pills, itálico em excesso, Instrument Serif, kerning negativo, dividers demais, bordas em todo container, gradientes de fundo, eyebrows, numeração de seção, ALL CAPS, mono fora de lugar, squircles, badges de enchimento, texto redundante) e aplica as correções (mostrar o produto real, ampliar a feature principal, agrupar por espaçamento, headline específica, CTA em destaque, uma cor de acento, ícones consistentes, raio interno = externo − padding, contraste, tabular nums, estados de loading, feedback animado). Use ao construir ou revisar qualquer página pública, landing, dashboard ou componente, sempre que algo pareça "genérico", "template", "feito por IA", ou como passo obrigatório do protocolo de design antes do PR.
---

# Unslop — tirar a cara de IA

Objetivo: a página parecer **desenhada por alguém para este produto**, não montada por um modelo a partir da média da internet. Trabalhe em dois passes: **detectar** (checklist A, com o scanner) e **corrigir** (checklist B). Nunca "compense" um sinal adicionando outro ornamento; a direção é sempre remover, especificar e hierarquizar.

## Como rodar

1. Scanner estático (heurístico, sem dependências):
   ```bash
   python3 <skill-dir>/scripts/scan.py <pasta-do-front>   # ex.: src/, app/, components/
   ```
   Ele aponta arquivo:linha para os sinais detectáveis por código (itálico, kerning negativo, Instrument Serif, uppercase, gradientes, mono, dividers, bordas, opacidades, squircles, pontos em título, eyebrows, numeração). O que ele **não** vê (diagramas detalhados demais, texto redundante, produto não mostrado, headline vaga) é com você, lendo a página renderizada.
2. Se houver Playwright/Chrome, capture a página em desktop e mobile e leia-a como um visitante: o que é este produto? Em 3 segundos dá para saber? Se não, é slop de conteúdo, não de estilo.
3. Aplique as correções da lista B. Rode o scanner de novo. Registre no PR (seção Design) os sinais encontrados e o que foi feito.

## A. Sinais de IA — detectar e remover

| Sinal | Por que denuncia | O que fazer |
|---|---|---|
| **Tags & pills** em todo card/hero ("New", "AI-powered", "Beta") | Enchimento visual sem informação | Apague as que não mudam uma decisão do usuário. Sobrou uma? Vira texto normal. |
| **Itálico em excesso** / palavras aleatórias em itálico no headline | Ênfase sem critério | Desitalicize. Ênfase é hierarquia (tamanho, peso, cor), não itálico espalhado. |
| **Instrument Serif** (e primas: serifas display "elegantes" default) | Tipografia-assinatura de IA em 2025–26 | Troque por uma família escolhida para o produto (`ui-ux-pro-max` → typography; `design-taste-frontend` → type). Serif só se a direção pedir. |
| **Kerning negativo** (`letter-spacing: -0.0Xem` em tudo) | Tique de "premium" copiado | Tracking negativo só em display grande (≥ 48px) e leve (≤ −0.02em). Corpo e UI: 0. |
| **UI detalhada demais** (mock de dashboard com 40 widgets, gráficos fake) | Ruído; ninguém lê | Mostre **uma** tela real com **um** dado que importa. |
| **Dividers em excesso** (`<hr>`, `border-bottom` entre tudo) | Separação por linha em vez de por espaço | Remova; agrupe por espaçamento (escala 8px: 8/16/24/40/64). |
| **Shades & opacidades** (`text-white/60`, `/40`, `/30` por todo lado; 7 cinzas) | Paleta sem decisão | Máximo 3 tons de texto (primário, secundário, desabilitado) com contraste AA real. Sem opacidade em texto sobre imagem. |
| **Containers com borda** em todo bloco (`border border-white/10 rounded-xl`) | Caixa dentro de caixa | Borda só quando o fundo não separa. Prefira fundo ou espaço. |
| **Diagramas detalhados demais** (arquitetura com 30 nós no hero) | Decoração disfarçada de explicação | Um diagrama por página, ≤ 7 nós, ou um screenshot do produto. |
| **Texto verboso/redundante** ("Seamlessly unlock powerful insights…") | Copy genérica | Headline específica (ver B); cada frase afirma algo verificável. Corte 40%. |
| **Code breadcrumbs** (`~/app/page.tsx` como decoração, `$ npm i` fake) | Estética dev sem função | Só código real que o usuário vai executar/ler. |
| **Numeração de seção** ("01 — Features", "02 — Pricing") | Template de portfólio | Remova; títulos específicos bastam. |
| **Eyebrows** (rótulo pequeno uppercase acima de todo título) | Padrão de template | Remova em todos; mantenha no máximo um, se carrega informação. |
| **Dithering**, **ASCII art**, texturas de ruído | Moda "retro-tech" default | Substitua por imagem/ilustração do produto ou nada. |
| **Squircles** / `rounded-[2rem]` em tudo | Mesmo raio para tudo | Raio pela escala do elemento: botão 6–8, card 12–16, modal 16–24; **raio interno = raio externo − padding**. |
| **Mono em excesso** (`font-mono` em labels, datas, preços) | Mono como "tech" | Mono só para código, IDs, hashes, dados tabulares. |
| **ALL CAPS em subtítulos** | Grita e reduz legibilidade | Subtítulo em caixa normal; uppercase só em labels ≤ 12px com tracking +0.05em. |
| **Gradient backgrounds** (mesh, radial roxo/azul, "aurora") | O fundo mais gerado da web | Fundo sólido; cor via acento, imagem ou produto. |
| **Ponto final em títulos.** | Tique de copy "editorial" | Remova de títulos e subtítulos. Mantenha só em frases completas de corpo. |
| **Badges de enchimento** ("Trusted by 10k+", logos genéricos, selos inventados) | Prova social falsa ou vazia | Apague; prova social só com nome/número real e verificável (ver `site-compliance`). |
| **Labels redundantes** ("Email address" acima de input com placeholder "Email address"; "Image of…") | Repetição | Um só: label visível **ou** placeholder; alt descreve, não repete legenda. |

## B. Correções que dão identidade

- **Use um design system.** Tokens de cor, tipo, espaçamento e raio num lugar (`DESIGN.md`, `tokens.css`, tema Tailwind). Nenhum valor mágico no componente.
- **Mostre o produto de verdade.** Screenshot ou embed funcional no hero, não ilustração abstrata. Se o produto é feio, conserte o produto.
- **Amplie a feature principal.** Uma seção grande para a coisa que vende; o resto menor. Três colunas iguais = ninguém decidiu o que importa.
- **Agrupe por espaçamento**, não por linha ou caixa. Proximidade antes de borda.
- **Headlines específicas.** "Feche a folha de pagamento em 4 minutos" em vez de "Simplifique sua gestão". Teste: a frase serviria para um concorrente? Então é vaga.
- **Destaque o CTA.** Um CTA primário por tela, com a cor de acento; o secundário é link ou ghost. Nunca dois botões sólidos lado a lado.
- **Uma cor de acento.** Escolhida para o produto, usada em CTA, foco, links e pouco mais. Neutros fazem o resto.
- **Ícones consistentes.** Um conjunto (mesmo stroke, mesmo grid), mesmo tamanho dentro do mesmo contexto. Zero emoji como ícone.
- **Remova embelezamentos:** glows, blur decorativo, `backdrop-blur` sem conteúdo atrás, sombras coloridas, bordas gradiente, partículas.
- **Raio interno = raio externo − padding.** Cards aninhados, imagens dentro de cards, botões dentro de inputs.
- **Contraste de texto:** AA (4.5:1 corpo, 3:1 ≥ 24px). Nada de cinza sobre cinza "elegante".
- **Headline quebrando certo:** `text-wrap: balance` em títulos, `max-width` em `ch` (45–75 corpo, ≤ 20 display). Sem órfãos de uma palavra.
- **Alinhe pelo olho, não pelo pixel.** Ícones e texto, caps e baseline, formas ópticas. Ajuste 1–2px quando o grid mentir.
- **Tabular nums** (`font-variant-numeric: tabular-nums`) em tabelas, preços, contadores, timers.
- **Estados de loading** reais (skeleton com a forma do conteúdo, não spinner genérico) e vazios com ação.
- **Anime feedback útil**, não decoração: confirmação de ação, transição de estado, foco. 150–250ms, `prefers-reduced-motion` respeitado. (Motion com GSAP: skills `gsap-*`; scroll: `lenis`.)

## Saída esperada

Lista `[sinal] arquivo:linha — correção aplicada`, mais uma frase: o que a página comunica agora em 3 segundos. Se restou algum sinal com motivo (ex.: pill que é filtro funcional), diga qual e por quê.
