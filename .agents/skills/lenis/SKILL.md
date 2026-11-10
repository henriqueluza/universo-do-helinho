---
name: lenis
description: Smooth scroll com Lenis (darkroom.engineering) — instalação, CSS recomendado, loop raf próprio ou via gsap.ticker, sincronização correta com GSAP ScrollTrigger, lenis/react (ReactLenis, useLenis), lenis/vue, lenis/snap, scroll aninhado (data-lenis-prevent), âncoras, scrollTo, stop/start em modais, prefers-reduced-motion, limitações e troubleshooting. Use quando o usuário pedir "smooth scroll", "scroll suave", "lenis", "scroll tipo Apple/Awwwards", parallax ou WebGL sincronizado ao scroll, ou quando um projeto com GSAP ScrollTrigger precisar de scroll suavizado. Não use para animar um elemento só (gsap-core) nem para scroll-snap puro em CSS.
---

# Lenis — smooth scroll

Fonte: README oficial de `darkroomengineering/lenis` (MIT), pacote npm `lenis` (v1.3.x). Confirme a versão instalada no Context7/npm antes de usar uma opção nova.

## Quando usar / não usar

- **Use** em sites públicos com direção que pede scroll "pesado"/inercial, parallax, pin com ScrollTrigger, ou WebGL sincronizado ao scroll. Lenis roda **sobre o scroll nativo**: `position: sticky`, âncoras, barra de rolagem e acessibilidade continuam funcionando.
- **Não use** em dashboards/apps de trabalho (scroll nativo é o esperado), em páginas com muito conteúdo em iframe (não recebem wheel), ou quando só um efeito leve basta (CSS `scroll-timeline`/`animation-timeline`, zero JS).
- **Acessibilidade:** `respectReducedMotion: true` (padrão) — com `prefers-reduced-motion: reduce`, o lerp vira 1 e `scrollTo` fica instantâneo; mantenha. Nunca `false`.

## Instalação

```bash
npm i lenis
```
```js
import Lenis from 'lenis'
import 'lenis/dist/lenis.css'   // CSS recomendado — obrigatório para autoToggle e evita bugs de overflow
```

## Setup

**Simples (sem GSAP):**
```js
const lenis = new Lenis({ autoRaf: true })
```

**Com GSAP ScrollTrigger — um único driver de frame (padrão da casa):**
```js
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
gsap.registerPlugin(ScrollTrigger)

const lenis = new Lenis()                 // autoRaf: false (padrão)
lenis.on('scroll', ScrollTrigger.update)  // ScrollTrigger lê o scroll suavizado
gsap.ticker.add((time) => lenis.raf(time * 1000))  // ticker em s → Lenis em ms
gsap.ticker.lagSmoothing(0)               // sem compensação de lag, senão desincroniza
```
Nunca dois loops (raf próprio **e** ticker): causa jitter e triggers deslocados. Se ScrollTrigger usa um `scroller` custom, use `ScrollTrigger.scrollerProxy`; para a janela, o código acima basta.

**Raf próprio (sem GSAP, com controle):**
```js
const lenis = new Lenis()
function raf(time) { lenis.raf(time); requestAnimationFrame(raf) }
requestAnimationFrame(raf)
```

**Sem build (landing estática):**
```html
<link rel="stylesheet" href="https://unpkg.com/lenis@1.3.27/dist/lenis.css">
<script src="https://unpkg.com/lenis@1.3.27/dist/lenis.min.js"></script>
<script>new Lenis({ autoRaf: true, autoToggle: true, anchors: true, allowNestedScroll: true, stopInertiaOnNavigate: true })</script>
```

## React (`lenis/react`)

```jsx
import { ReactLenis, useLenis } from 'lenis/react'
import 'lenis/dist/lenis.css'

export default function App({ children }) {
  return (<><ReactLenis root />{children}</>)   // root: instância global, scroll no <html>
}
// em qualquer componente:
const lenis = useLenis((l) => { /* a cada scroll */ })
```
Com GSAP: `<ReactLenis root options={{ autoRaf: false }} ref={lenisRef} />` e no `useEffect`: `const update = (t) => lenisRef.current?.lenis?.raf(t * 1000); gsap.ticker.add(update); gsap.ticker.lagSmoothing(0); return () => gsap.ticker.remove(update)`. Next.js App Router: componente `'use client'` no layout raiz. Vue: `lenis/vue` (`<VueLenis root>`, `useLenis`).

## Opções que importam

| Opção | Padrão | Quando mexer |
|---|---|---|
| `lerp` / `duration`+`easing` | `0.1` / `1.2s` | `lerp` 0.08–0.12 é o "peso" usual; `duration` só vale se `lerp` não for definido |
| `anchors` | `false` | `true` (ou `{ offset: -altura-do-header }`) para links `#id` |
| `allowNestedScroll` | `false` | `true` resolve listas/modais roláveis; custa DOM walk por evento — prefira `data-lenis-prevent` no elemento |
| `prevent` | — | `(node) => node.classList.contains('modal')` para exceções por código |
| `autoToggle` | `false` | para/inicia com `overflow: hidden` no wrapper (exige o CSS recomendado) |
| `stopInertiaOnNavigate` | `false` | `true` em SPAs para não "escorregar" após clicar num link interno |
| `syncTouch` | `false` | só se precisa de scroll sincronizado no touch (WebGL); instável em iOS < 16 |
| `infinite` | `false` | scroll infinito; exige `syncTouch` no touch |
| `wrapper`/`content` | `window`/`html` | para suavizar um container interno em vez da página |

## Padrões do dia a dia

- **Modal/drawer aberto:** `lenis.stop()` ao abrir, `lenis.start()` ao fechar; conteúdo rolável dentro com `data-lenis-prevent`.
- **Scroll programático:** `lenis.scrollTo('#precos', { offset: -80, duration: 1 })`; `immediate: true` para pular; `lock: true` para impedir interrupção do usuário.
- **Mudança de rota (SPA):** `lenis.scrollTo(0, { immediate: true })` após navegar; `ScrollTrigger.refresh()` depois que o novo conteúdo montar.
- **Conteúdo que muda de altura:** `autoResize` (ResizeObserver) cobre a maioria; imagens sem dimensão ainda quebram triggers — reserve `width/height` (também ajuda CLS).
- **Scroll aninhado:** `data-lenis-prevent` (todos), `-wheel`, `-touch`, `-vertical`, `-horizontal`.
- **Destruir** no unmount: `lenis.destroy()` (e `gsap.ticker.remove(update)`).
- **Progresso/velocidade** para efeitos: `lenis.progress` (0–1), `lenis.velocity`, `lenis.direction`, eventos `scroll` e `virtual-scroll`.

## Limitações conhecidas

Sem CSS `scroll-snap` (use `lenis/snap`); 60 fps no Safari e 30 fps em modo de baixa energia; não suaviza sobre iframes; `position: fixed` pode atrasar em Safari pré-M1; `syncTouch` instável em iOS < 16.

## Troubleshooting

Não rola → página é rolável sem Lenis? CSS recomendado importado? `autoRaf: true` ou `raf()` sendo chamado? · Triggers do GSAP deslocados → dois loops rodando, ou faltou `lenis.on('scroll', ScrollTrigger.update)` / `lagSmoothing(0)` · Modal não rola → `data-lenis-prevent` · Âncoras mortas → `anchors: true` · Jitter → reduza plugins que leem scroll fora do tick; confirme uma só instância.

## Checklist de PR (seção Design → Motion)

Uma instância · um driver de frame · ScrollTrigger sincronizado · `respectReducedMotion` mantido · modais com stop/start + prevent · âncoras funcionando · destroy no unmount · testado em Safari e touch.
