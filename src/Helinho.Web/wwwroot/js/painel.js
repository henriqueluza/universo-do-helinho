'use strict';
// Comportamento do painel Recreio (carregado com defer em todas as páginas).
// Só melhora o que o servidor já entrega: sem JS, o conteúdo continua visível e legível.
// Cada módulo sai cedo quando o elemento que espera não existe. Sem HTML montado por texto, sem rede,
// sem manipulador inline: compatível com uma CSP sem 'unsafe-inline'.
(() => {
const raiz = document.documentElement;
const reduz = matchMedia('(prefers-reduced-motion: reduce)');
const calmo = () => reduz.matches;

// Tema claro/escuro. Aplica a preferência guardada e, a partir do botão [data-tema],
// alterna com revelação circular (View Transitions API) quando há movimento.
(() => {
  const lerTema = () => { try { const t = localStorage.getItem('tema'); return t === 'light' || t === 'dark' ? t : null; } catch (_) { return null; } };
  const salvado = lerTema();
  if (salvado) raiz.dataset.theme = salvado;

  const botoes = document.querySelectorAll('[data-tema]');
  const marcar = () => botoes.forEach((b) => b.setAttribute('aria-pressed', String(raiz.dataset.theme === 'dark')));
  marcar();
  botoes.forEach((btn) => {
    btn.addEventListener('click', (e) => {
      const proximo = raiz.dataset.theme === 'dark' ? 'light' : 'dark';
      const aplicar = () => { raiz.dataset.theme = proximo; marcar(); try { localStorage.setItem('tema', proximo); } catch (_) { /* sem armazenamento: vale só nesta página */ } };
      if (calmo() || !document.startViewTransition) return aplicar();
      const x = e.clientX || innerWidth / 2, y = e.clientY || innerHeight / 2;
      const r = Math.hypot(Math.max(x, innerWidth - x), Math.max(y, innerHeight - y));
      raiz.classList.add('vt-theme');
      const t = document.startViewTransition(aplicar);
      t.ready.then(() => raiz.animate(
        { clipPath: [`circle(0 at ${x}px ${y}px)`, `circle(${r}px at ${x}px ${y}px)`] },
        { duration: 560, easing: 'cubic-bezier(.19,1,.22,1)', pseudoElement: '::view-transition-new(root)' }), () => {});
      t.finished.catch(() => {}).finally(() => raiz.classList.remove('vt-theme'));
    });
  });
})();

// Números contam até o valor (o valor final já está no HTML: sem JS ou com movimento reduzido, nada muda).
(() => {
  const alvos = document.querySelectorAll('[data-count]');
  if (!alvos.length || calmo()) return;
  const fmt = new Intl.NumberFormat('pt-BR');
  alvos.forEach((el, i) => {
    const alvo = Number(el.dataset.count);
    if (el.dataset.count.trim() === '' || !Number.isFinite(alvo)) return;
    const dur = 1100, t0 = performance.now() + 200 + i * 40;
    el.textContent = fmt.format(0);
    const passo = (t) => {
      const p = Math.min(Math.max((t - t0) / dur, 0), 1);
      el.textContent = fmt.format(p < 1 ? Math.round(alvo * (1 - Math.pow(1 - p, 4))) : alvo);
      if (p < 1) requestAnimationFrame(passo);
    };
    requestAnimationFrame(passo);
  });
})();

// Gaveta do menu no mobile.
(() => {
  const btn = document.querySelector('.menu-btn');
  const scrim = document.querySelector('.scrim');
  if (!btn || !scrim) return;
  scrim.hidden = false;
  const fora = [document.querySelector('.main'), document.querySelector('.topbar')];
  const aberta = () => document.body.classList.contains('menu-open');
  const set = (abrir, foco = true) => {
    document.body.classList.toggle('menu-open', abrir);
    fora.forEach((el) => { if (el) el.inert = abrir; });
    btn.setAttribute('aria-expanded', String(abrir));
    if (!foco) return;
    if (abrir) document.querySelector('.rail-close')?.focus();
    else btn.focus();
  };
  btn.addEventListener('click', () => set(!aberta()));
  scrim.addEventListener('click', () => set(false));
  document.querySelector('.rail-close')?.addEventListener('click', () => set(false));
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && aberta()) set(false); });
  // Ao crescer a janela o menu vira coluna fixa: a gaveta não pode deixar a página inerte.
  matchMedia('(min-width: 768px)').addEventListener('change', (e) => { if (e.matches && aberta()) set(false, false); });
})();

// Entrar: a cena acompanha o mouse (só com mouse e sem redução de movimento).
(() => {
  const cena = document.querySelector('.auth-scene');
  if (!cena || calmo() || !matchMedia('(pointer: fine)').matches) return;
  let raf = 0;
  cena.addEventListener('pointermove', (e) => {
    cancelAnimationFrame(raf);
    raf = requestAnimationFrame(() => {
      const r = cena.getBoundingClientRect();
      cena.style.setProperty('--mx', (((e.clientX - r.left) / r.width) * 2 - 1).toFixed(3));
      cena.style.setProperty('--my', (((e.clientY - r.top) / r.height) * 2 - 1).toFixed(3));
    });
  });
  cena.addEventListener('pointerleave', () => { cena.style.setProperty('--mx', 0); cena.style.setProperty('--my', 0); });
})();

// Seções abaixo da dobra entram quando aparecem. A classe que esconde (.reveal, sob .js) só
// existe porque este script a aplica: sem JS, ou com IntersectionObserver ausente, nada fica oculto.
(() => {
  const main = document.querySelector('.main');
  if (!main || !('IntersectionObserver' in window) || calmo()) return;
  const io = new IntersectionObserver((es) => es.forEach((e) => { if (e.isIntersecting) { e.target.classList.add('vis'); io.unobserve(e.target); } }), { rootMargin: '0px 0px -12% 0px' });
  const pendentes = [...main.children].filter((el, i) => i >= 2 && el.getBoundingClientRect().top > innerHeight * .9);
  if (!pendentes.length) return;
  raiz.classList.add('js');
  pendentes.forEach((el) => { el.classList.add('reveal'); io.observe(el); });
  // Saltos de rolagem (End, âncora) pulam seções sem cruzar o observador: o que ficou acima da tela é revelado.
  addEventListener('scroll', () => pendentes.forEach((el) => { if (el.getBoundingClientRect().bottom < 0) { el.classList.add('vis'); io.unobserve(el); } }), { passive: true });
  // Impressão e "reduzir movimento" ligado depois do carregamento: tudo visível.
  const mostrarTudo = () => pendentes.forEach((el) => el.classList.add('vis'));
  addEventListener('beforeprint', mostrarTudo);
  reduz.addEventListener('change', (e) => { if (e.matches) mostrarTudo(); });
})();

// Gráficos e dicas. A fonte dos números é sempre HTML que o servidor já manda (tabela ou
// data-spark): sem JS, a tabela continua lá e nada some. Todo SVG é montado por DOM (createElementNS).
(() => {
  const graficos = document.querySelectorAll('.linechart[data-fonte]');
  const sparks = document.querySelectorAll('svg.spark[data-spark]');
  const dicas = document.querySelectorAll('[data-tip]');
  if (!graficos.length && !sparks.length && !dicas.length) return;

  const NS = 'http://www.w3.org/2000/svg';
  const num = (t) => Number(String(t).replace(/\./g, '').replace(',', '.'));
  const fmt = new Intl.NumberFormat('pt-BR');
  const el = (tag, cls, txt) => { const n = document.createElement(tag); if (cls) n.className = cls; if (txt != null) n.textContent = txt; return n; };
  const sv = (tag, attrs = {}, txt) => {
    const n = document.createElementNS(NS, tag);
    for (const k in attrs) n.setAttribute(k, attrs[k]);
    if (txt != null) n.textContent = txt;
    return n;
  };

  // Dica flutuante única (mouse, toque e teclado). Conteúdo sempre como texto.
  const tip = el('div', 'tip');
  tip.setAttribute('aria-hidden', 'true');
  document.body.append(tip);
  // linhas: [titulo, [amostra, rotulo, valor], ...]
  const mostrar = (linhas, x, y, lado) => {
    tip.replaceChildren(el('b', null, linhas[0]), ...linhas.slice(1).map(([sw, rot, val]) => {
      const sp = el('span'); if (sw) sp.append(el('i', 'sw ' + sw)); sp.append(rot); if (val != null) sp.append(el('b', null, val)); return sp;
    }));
    tip.classList.add('on');
    const r = tip.getBoundingClientRect();
    // lado: ao lado do ponto (gráfico de linha, sem cobrir a série); sem lado: acima do alvo
    let L = lado ? (lado === 'dir' ? x + 16 : x - r.width - 16) : x - r.width / 2;
    L = Math.min(Math.max(8, L), innerWidth - r.width - 8);
    let T = lado ? y : y - r.height - 12; if (T < 8) T = y + 18;
    tip.style.translate = `${L}px ${T}px`;
  };
  const esconder = () => tip.classList.remove('on');
  addEventListener('scroll', esconder, { passive: true });
  addEventListener('keydown', (e) => { if (e.key === 'Escape') esconder(); });

  // [data-tip="Título|linha|linha"]: mouse e foco do teclado.
  dicas.forEach((alvo) => {
    const [t, ...resto] = alvo.dataset.tip.split('|');
    const on = () => { const r = alvo.getBoundingClientRect(); mostrar([t, ...resto.map((x) => [null, x])], r.left + r.width / 2, r.top); };
    alvo.addEventListener('pointerenter', on); alvo.addEventListener('pointerleave', esconder);
    alvo.addEventListener('focus', on); alvo.addEventListener('blur', esconder);
  });

  // Minigráfico dos indicadores.
  sparks.forEach((svg) => {
    const v = svg.dataset.spark.split(',').map(Number);
    if (v.length < 2 || v.some((n) => !Number.isFinite(n))) return;
    const mn = Math.min(...v), mx = Math.max(...v), W = 100, H = 40;
    const pts = v.map((y, i) => [i / (v.length - 1) * W, H - 4 - ((y - mn) / (mx - mn || 1)) * (H - 10)]);
    const d = 'M' + pts.map((p) => p.map((n) => n.toFixed(1)).join(' ')).join(' L');
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`); svg.setAttribute('preserveAspectRatio', 'none');
    const g = sv('g');
    g.append(sv('path', { class: 's-area', d: `${d} L${W} ${H} L0 ${H}Z` }), sv('path', { class: 's-line', d }));
    svg.replaceChildren(g);
  });

  // Gráfico de linha: lê a tabela, desenha no tamanho do contêiner.
  graficos.forEach((box) => {
    const tab = document.getElementById(box.dataset.fonte);
    if (!tab || !tab.tBodies[0] || !tab.tHead) return;
    const series = [...tab.tBodies[0].rows].filter((tr) => tr.cells.length >= 3).map((tr) => ({ rot: tr.cells[0].textContent, curto: tr.cells[0].dataset.curto || tr.cells[0].textContent, a: num(tr.cells[1].textContent), b: num(tr.cells[2].textContent) }));
    if (series.length < 2 || series.some((s) => !Number.isFinite(s.a) || !Number.isFinite(s.b))) return;
    const nomes = [...tab.tHead.rows[0].cells].map((c) => c.textContent);
    let idx = series.length - 1, geo = null;

    const desenhar = () => {
      const W = box.clientWidth, H = box.clientHeight, m = { l: 44, r: 14, t: 12, b: 30 };
      if (!W || !H) return;
      const max = Math.max(...series.flatMap((s) => [s.a, s.b]), 1);
      const passo = max > 600 ? 200 : 100, topo = Math.ceil(max / passo) * passo;
      const x = (i) => m.l + (i / (series.length - 1)) * (W - m.l - m.r);
      const y = (v) => m.t + (1 - v / topo) * (H - m.t - m.b);
      const linha = (k) => series.map((s, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)} ${y(s[k]).toFixed(1)}`).join(' ');
      const grid = sv('g', { class: 'lc-grid' }), eixo = sv('g', { class: 'lc-axis' });
      for (let v = 0; v <= topo; v += passo) {
        grid.append(sv('line', { x1: m.l, x2: W - m.r, y1: y(v), y2: y(v), ...(v === 0 ? { class: 'base' } : {}) }));
        eixo.append(sv('text', { x: m.l - 10, y: y(v) + 4, 'text-anchor': 'end' }, fmt.format(v)));
      }
      const ult = series.length - 1, cada = W < 480 ? 14 : 7;
      series.forEach((s, i) => { if ((ult - i) % cada === 0) eixo.append(sv('text', { x: x(i), y: H - 8, 'text-anchor': i === ult ? 'end' : 'middle' }, i === ult ? 'hoje' : s.curto)); });
      const ref = sv('g', { class: 'lc-series ref' }), dado = sv('g', { class: 'lc-series' });
      ref.append(sv('path', { class: 'lc-ref', d: linha('b') }));
      dado.append(sv('path', { class: 'lc-area', d: `${linha('a')} L${x(ult)} ${y(0)} L${x(0)} ${y(0)}Z` }), sv('path', { class: 'lc-line', d: linha('a') }));
      const svg = sv('svg', { width: W, height: H, 'aria-hidden': 'true' });
      svg.append(grid, eixo, ref, dado,
        sv('circle', { class: 'lc-today', r: 5, cx: x(ult), cy: y(series[ult].a) }),
        sv('line', { class: 'lc-hair', y1: m.t, y2: y(0) }),
        sv('circle', { class: 'lc-dot r', r: 5 }), sv('circle', { class: 'lc-dot d', r: 5 }));
      box.replaceChildren(svg);
      geo = { x, y, m, W };
      if (box.dataset.visto) box.dataset.desenhado = '';
      if (box.classList.contains('hover')) focar(idx, false);
    };
    const focar = (i, mostrarDica = true) => {
      if (!geo) return;
      idx = Math.max(0, Math.min(series.length - 1, i));
      const s = series[idx], X = geo.x(idx);
      const svg = box.firstElementChild;
      svg.querySelector('.lc-hair').setAttribute('x1', X); svg.querySelector('.lc-hair').setAttribute('x2', X);
      const [r, d] = svg.querySelectorAll('.lc-dot');
      r.setAttribute('cx', X); r.setAttribute('cy', geo.y(s.b)); d.setAttribute('cx', X); d.setAttribute('cy', geo.y(s.a));
      box.classList.add('hover');
      if (!mostrarDica) return;
      const bb = box.getBoundingClientRect();
      const dif = s.b ? Math.round((s.a - s.b) / s.b * 100) : 0;
      mostrar([s.rot, ['sw-data', nomes[1], fmt.format(s.a)], ['sw-ref', nomes[2], fmt.format(s.b)], [null, 'Diferença', `${dif > 0 ? '+' : ''}${dif}%`]], bb.left + X, bb.top + geo.m.t, X > geo.W * .55 ? 'esq' : 'dir');
    };
    const sair = () => { box.classList.remove('hover'); esconder(); };
    box.addEventListener('pointermove', (e) => {
      if (!geo) return;
      const bb = box.getBoundingClientRect();
      focar(Math.round(((e.clientX - bb.left - geo.m.l) / (geo.W - geo.m.l - geo.m.r)) * (series.length - 1)));
    });
    box.addEventListener('pointerleave', sair);
    box.addEventListener('blur', sair);
    box.addEventListener('focus', () => focar(idx));
    box.addEventListener('keydown', (e) => {
      const k = { ArrowLeft: -1, ArrowRight: 1, Home: -999, End: 999 }[e.key];
      if (k === undefined) return;
      e.preventDefault(); focar(idx + k);
    });
    desenhar();
    setTimeout(() => { box.dataset.visto = '1'; }, 2200);
    let w = box.clientWidth;
    new ResizeObserver(() => { if (box.clientWidth !== w) { w = box.clientWidth; desenhar(); } }).observe(box);
  });
})();
})();
