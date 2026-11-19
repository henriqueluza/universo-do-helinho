// Só da preview v2: tema, contagem dos números. No projeto real vira wwwroot/js/painel.js (defer).
(() => {
  const root = document.documentElement;
  const calmo = matchMedia('(prefers-reduced-motion: reduce)').matches;

  // Tema com revelação circular a partir do botão (View Transitions API).
  document.querySelectorAll('[data-tema]').forEach((btn) => {
    btn.addEventListener('click', (e) => {
      const proximo = root.dataset.theme === 'dark' ? 'light' : 'dark';
      const aplicar = () => { root.dataset.theme = proximo; try { localStorage.setItem('tema', proximo); } catch (_) {} };
      if (calmo || !document.startViewTransition) return aplicar();
      const x = e.clientX || innerWidth / 2, y = e.clientY || innerHeight / 2;
      const r = Math.hypot(Math.max(x, innerWidth - x), Math.max(y, innerHeight - y));
      root.classList.add('vt-theme');
      const t = document.startViewTransition(aplicar);
      t.ready.then(() => root.animate(
        { clipPath: [`circle(0 at ${x}px ${y}px)`, `circle(${r}px at ${x}px ${y}px)`] },
        { duration: 560, easing: 'cubic-bezier(.19,1,.22,1)', pseudoElement: '::view-transition-new(root)' }));
      t.finished.finally(() => root.classList.remove('vt-theme'));
    });
  });

  // Números contam até o valor (o valor final já está no HTML: sem JS, sem animação, nada quebra).
  const fmt = new Intl.NumberFormat('pt-BR');
  document.querySelectorAll('[data-count]').forEach((el, i) => {
    const alvo = Number(el.dataset.count);
    if (calmo || !Number.isFinite(alvo)) return;
    const dur = 1100, atraso = 200 + i * 40;
    const t0 = performance.now() + atraso;
    el.textContent = fmt.format(0);
    const passo = (t) => {
      const p = Math.min(Math.max((t - t0) / dur, 0), 1);
      const e = 1 - Math.pow(1 - p, 4);
      el.textContent = fmt.format(Math.round(alvo * e));
      if (p < 1) requestAnimationFrame(passo);
    };
    requestAnimationFrame(passo);
  });
})();

// Gaveta do menu no mobile (no projeto real o offcanvas do Bootstrap faz isso).
(() => {
  const btn = document.querySelector('.menu-btn');
  const scrim = document.querySelector('.scrim');
  if (!btn || !scrim) return;
  scrim.hidden = false;
  const fora = [document.querySelector('.main'), document.querySelector('.topbar')];
  const set = (aberto) => {
    document.body.classList.toggle('menu-open', aberto);
    fora.forEach((el) => { if (el) el.inert = aberto; });
    btn.setAttribute('aria-expanded', String(aberto));
    if (aberto) document.querySelector('.rail-close')?.focus();
    else btn.focus();
  };
  btn.addEventListener('click', () => set(!document.body.classList.contains('menu-open')));
  scrim.addEventListener('click', () => set(false));
  document.querySelector('.rail-close')?.addEventListener('click', () => set(false));
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && document.body.classList.contains('menu-open')) set(false); });
})();

// Estados de demonstração da preview (?estado=vazio|carregando|erro).
(() => {
  const estado = new URLSearchParams(location.search).get('estado');
  const $ = (id) => document.getElementById(id);
  if (estado === 'vazio' && $('feed')) { $('feed').hidden = true; $('dashboard-vazio').hidden = false; }
  if (estado === 'carregando' && $('feed')) { $('feed').hidden = true; $('feed-sk').hidden = false; $('feed-status').textContent = 'Atualizando…'; }
  if (estado === 'erro' && $('resumo-erros')) {
    // Mensagem única e genérica: não revela se o erro foi no usuário ou na senha.
    $('resumo-erros').hidden = false;
    document.querySelector('.auth')?.classList.add('erro');
    ['Input_UserName', 'Input_Password'].forEach((id) => {
      $(id).setAttribute('aria-invalid', 'true');
      $(id).setAttribute('aria-describedby', 'resumo-erros');
    });
    $('resumo-erros').focus();
  }
})();

// Entrar (só preview): senha vazia mostra o erro genérico; senão girador → "tudo certo" → painel.
// No projeto real quem valida é o servidor; a animação de "entrando" pode ficar (é só o botão ocupado).
(() => {
  const form = document.querySelector('.auth-card form');
  if (!form) return;
  const auth = document.querySelector('.auth');
  const btn = form.querySelector('button[type="submit"]');
  const rotulo = btn.querySelector('.btn-label');
  const resumo = document.getElementById('resumo-erros');
  const campos = ['Input_UserName', 'Input_Password'].map((id) => document.getElementById(id));
  const calmo = matchMedia('(prefers-reduced-motion: reduce)').matches;

  const erro = () => {
    resumo.hidden = false;
    campos.forEach((c) => { c.setAttribute('aria-invalid', 'true'); c.setAttribute('aria-describedby', 'resumo-erros'); });
    auth.classList.remove('erro'); void auth.offsetWidth; auth.classList.add('erro');
    resumo.focus();
  };

  form.addEventListener('submit', (e) => {
    e.preventDefault();
    if (btn.getAttribute('aria-busy') === 'true') return;
    if (!campos[0].value.trim() || !campos[1].value) return erro();
    resumo.hidden = true;
    auth.classList.remove('erro');
    campos.forEach((c) => { c.removeAttribute('aria-invalid'); c.removeAttribute('aria-describedby'); });
    btn.setAttribute('aria-busy', 'true');
    btn.insertAdjacentHTML('afterbegin', '<span class="spin" aria-hidden="true"></span>');
    rotulo.textContent = 'Entrando…';
    setTimeout(() => {
      btn.querySelector('.spin')?.remove();
      btn.insertAdjacentHTML('afterbegin', '<svg class="ok-mark" viewBox="0 0 20 20" aria-hidden="true"><path d="M4 10.5l4 4 8-9"/></svg>');
      rotulo.textContent = 'Tudo certo!';
      auth.classList.add('ok');
      if (!calmo) {
        const r = btn.getBoundingClientRect();
        for (let i = 0; i < 16; i++) {
          const a = (i / 16) * Math.PI * 2, d = 70 + Math.random() * 90;
          const b = document.createElement('i');
          b.className = 'burst'; b.setAttribute('aria-hidden', 'true');
          b.style.cssText = `--bx:${r.left + r.width / 2}px;--by:${r.top + r.height / 2}px;--dx:${Math.cos(a) * d}px;--dy:${Math.sin(a) * d}px;--r:${Math.random() * 360}deg`;
          document.body.append(b);
          setTimeout(() => b.remove(), 1000);
        }
      }
      setTimeout(() => { location.href = form.action; }, calmo ? 150 : 700);
    }, calmo ? 200 : 900);
  });
})();

// Entrar: a cena acompanha o mouse (só com mouse e sem redução de movimento).
(() => {
  const cena = document.querySelector('.auth-scene');
  if (!cena || matchMedia('(prefers-reduced-motion: reduce)').matches || !matchMedia('(pointer: fine)').matches) return;
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

// Painel: novidades chegam no "Acabou de acontecer" (só na preview; no real vem do servidor).
(() => {
  const feed = document.getElementById('feed');
  if (!feed || new URLSearchParams(location.search).get('estado')) return;
  const novas = [
    ['MF', 'amb', 'Maria Fernandes', 'respondeu 5 perguntas do Dia da Água', 'Ambiental', 'flora'],
    ['TS', 'con', 'Theo Santos', 'acertou 10 de 10 com o Byte', 'Conhecimento', 'byte'],
    ['LP', 'esp', 'Lara Pires', 'completou a missão Treino do Max · 40 XP', 'Esportivo', 'max'],
  ];
  const status = document.getElementById('feed-status');
  let i = 0;
  const tick = () => {
    if (i >= novas.length || document.hidden) return;
    const [ini, k, nome, acao, tema, img] = novas[i++];
    const agora = new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
    const li = document.createElement('li');
    li.className = 'novo';
    li.innerHTML = `<span class="av" style="background:var(--q-${k}-bg);color:var(--q-${k}-ink)">${ini}</span><div><b>${nome}</b><small>${acao}</small></div><span class="tag tag-type ${k}"><img src="img/${img}.webp" alt="">${tema}</span><time>${agora}</time>`;
    const ultimo = feed.lastElementChild;
    ultimo.classList.add('sai');
    setTimeout(() => ultimo.remove(), 260);
    feed.prepend(li);
    if (status) status.textContent = 'Atualizado agora';
  };
  setInterval(tick, 8000);
})();

// Painel: gráficos. A fonte dos números é sempre HTML que o servidor já manda
// (tabela ou data-spark): sem JS, a tabela continua lá e nada some.
(() => {
  const calmo = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const NS = 'http://www.w3.org/2000/svg';
  const num = (t) => Number(String(t).replace(/\./g, '').replace(',', '.'));
  const fmt = new Intl.NumberFormat('pt-BR');
  const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  // Dica flutuante única (mouse, toque e teclado).
  const tip = document.createElement('div');
  tip.className = 'tip'; tip.setAttribute('aria-hidden', 'true');
  document.body.append(tip);
  // linhas: [titulo, [amostra, rotulo, valor], ...] — tudo vira texto (sem innerHTML)
  const el = (tag, cls, txt) => { const n = document.createElement(tag); if (cls) n.className = cls; if (txt != null) n.textContent = txt; return n; };
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
  document.querySelectorAll('[data-tip]').forEach((alvo) => {
    const [t, ...resto] = alvo.dataset.tip.split('|');
    const on = () => { const r = alvo.getBoundingClientRect(); mostrar([t, ...resto.map((x) => [null, x])], r.left + r.width / 2, r.top); };
    alvo.addEventListener('pointerenter', on); alvo.addEventListener('pointerleave', esconder);
  });

  // Seções abaixo da dobra: entram (e animam os gráficos) quando aparecem.
  const main = document.querySelector('.main');
  if (main && 'IntersectionObserver' in window && !calmo) {
    const io = new IntersectionObserver((es) => es.forEach((e) => { if (e.isIntersecting) { e.target.classList.add('vis'); io.unobserve(e.target); } }), { rootMargin: '0px 0px -12% 0px' });
    [...main.children].forEach((el, i) => { if (i >= 2 && el.getBoundingClientRect().top > innerHeight * .9) { el.classList.add('reveal'); io.observe(el); } });
  }

  // Minigráfico dos indicadores.
  document.querySelectorAll('svg.spark[data-spark]').forEach((svg) => {
    const v = svg.dataset.spark.split(',').map(Number);
    const mn = Math.min(...v), mx = Math.max(...v), W = 100, H = 40;
    const pts = v.map((y, i) => [i / (v.length - 1) * W, H - 4 - ((y - mn) / (mx - mn || 1)) * (H - 10)]);
    const d = 'M' + pts.map((p) => p.map((n) => n.toFixed(1)).join(' ')).join(' L');
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`); svg.setAttribute('preserveAspectRatio', 'none');
    svg.innerHTML = `<g><path class="s-area" d="${d} L${W} ${H} L0 ${H}Z"/><path class="s-line" d="${d}"/></g>`;
  });

  // Gráfico de linha: lê a tabela, desenha no tamanho do contêiner.
  document.querySelectorAll('.linechart[data-fonte]').forEach((box) => {
    const tab = document.getElementById(box.dataset.fonte);
    if (!tab) return;
    const series = [...tab.tBodies[0].rows].map((tr) => ({ rot: tr.cells[0].textContent, curto: tr.cells[0].dataset.curto, a: num(tr.cells[1].textContent), b: num(tr.cells[2].textContent) }));
    const nomes = [...tab.tHead.rows[0].cells].map((c) => c.textContent);
    let idx = series.length - 1, geo = null;

    const desenhar = () => {
      const W = box.clientWidth, H = box.clientHeight, m = { l: 44, r: 14, t: 12, b: 30 };
      const max = Math.max(...series.flatMap((s) => [s.a, s.b]));
      const passo = max > 600 ? 200 : 100, topo = Math.ceil(max / passo) * passo;
      const x = (i) => m.l + (i / (series.length - 1)) * (W - m.l - m.r);
      const y = (v) => m.t + (1 - v / topo) * (H - m.t - m.b);
      const linha = (k) => series.map((s, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)} ${y(s[k]).toFixed(1)}`).join(' ');
      let grid = '', eixo = '';
      for (let v = 0; v <= topo; v += passo) {
        grid += `<line x1="${m.l}" x2="${W - m.r}" y1="${y(v)}" y2="${y(v)}"${v === 0 ? ' class="base"' : ''}/>`;
        eixo += `<text x="${m.l - 10}" y="${y(v) + 4}" text-anchor="end">${fmt.format(v)}</text>`;
      }
      const cada = W < 480 ? 14 : 7;
      series.forEach((s, i) => { if ((series.length - 1 - i) % cada === 0) eixo += `<text x="${x(i)}" y="${H - 8}" text-anchor="${i === series.length - 1 ? 'end' : 'middle'}">${i === series.length - 1 ? 'hoje' : esc(s.curto)}</text>`; });
      const ult = series.length - 1;
      box.innerHTML = `<svg width="${W}" height="${H}" aria-hidden="true">
        <g class="lc-grid">${grid}</g><g class="lc-axis">${eixo}</g>
        <g class="lc-series ref"><path class="lc-ref" d="${linha('b')}"/></g>
        <g class="lc-series"><path class="lc-area" d="${linha('a')} L${x(ult)} ${y(0)} L${x(0)} ${y(0)}Z"/><path class="lc-line" d="${linha('a')}"/></g>
        <circle class="lc-today" r="5" cx="${x(ult)}" cy="${y(series[ult].a)}"/>
        <line class="lc-hair" y1="${m.t}" y2="${y(0)}"/><circle class="lc-dot r" r="5"/><circle class="lc-dot d" r="5"/>
      </svg>`;
      geo = { x, y, m, W };
      if (box.dataset.visto) box.dataset.desenhado = '';
    };
    const focar = (i, mostrarDica = true) => {
      idx = Math.max(0, Math.min(series.length - 1, i));
      const s = series[idx], X = geo.x(idx);
      const svg = box.firstElementChild;
      svg.querySelector('.lc-hair').setAttribute('x1', X); svg.querySelector('.lc-hair').setAttribute('x2', X);
      const [r, d] = svg.querySelectorAll('.lc-dot');
      r.setAttribute('cx', X); r.setAttribute('cy', geo.y(s.b)); d.setAttribute('cx', X); d.setAttribute('cy', geo.y(s.a));
      box.classList.add('hover');
      if (!mostrarDica) return;
      const bb = box.getBoundingClientRect();
      const dif = Math.round((s.a - s.b) / s.b * 100);
      mostrar([s.rot, ['sw-data', nomes[1], fmt.format(s.a)], ['sw-ref', nomes[2], fmt.format(s.b)], [null, 'Diferença', `${dif > 0 ? '+' : ''}${dif}%`]], bb.left + X, bb.top + geo.m.t, X > geo.W * .55 ? 'esq' : 'dir');
    };
    const sair = () => { box.classList.remove('hover'); esconder(); };
    box.addEventListener('pointermove', (e) => {
      const bb = box.getBoundingClientRect();
      const t = (e.clientX - bb.left - geo.m.l) / (geo.W - geo.m.l - geo.m.r);
      focar(Math.round(t * (series.length - 1)));
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
