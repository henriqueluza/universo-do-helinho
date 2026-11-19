// uso: node cap.cjs <saida-dir>   (aplicação em 127.0.0.1:55196; senha em ./pw)
const { chromium } = require('/Users/henriqueluza/.npm/_npx/381cec31605a419a/node_modules/playwright');
const fs = require('fs');
const D = __dirname; const OUT = process.argv[2]; fs.mkdirSync(OUT, { recursive: true }); fs.mkdirSync(OUT + '/html', { recursive: true });
const pw = fs.readFileSync(D + '/pw', 'utf8').trim(); const B = 'http://127.0.0.1:55196';
const EXE = '/Users/henriqueluza/Library/Caches/ms-playwright/chromium_headless_shell-1234/chrome-headless-shell-mac-arm64/chrome-headless-shell';
const fonts = {};
async function shot(p, name, tema) {
  if (tema === 'escuro') await p.evaluate(() => (document.documentElement.dataset.theme = 'dark'));
  await p.waitForTimeout(700);
  await p.screenshot({ path: `${OUT}/${name}.png`, fullPage: true });
  fonts[name] = await p.evaluate(() => { const s = {}; document.querySelectorAll('body *').forEach((e) => { const r = e.getBoundingClientRect(); if (r.width && e.childNodes.length) { const ff = getComputedStyle(e).fontFamily.split(',')[0]; s[ff] = (s[ff] || 0) + 1; } }); return s; });
  if (name.includes('1440-claro')) fs.writeFileSync(`${OUT}/html/${name.replace('-1440-claro', '')}.html`, await p.content());
}
async function login(br, w, h, user) {
  const ctx = await br.newContext({ viewport: { width: w, height: h }, reducedMotion: 'reduce' }); const p = await ctx.newPage();
  await p.goto(B + '/entrar'); await p.fill('#Input_UserName', user); await p.fill('#Input_Password', pw);
  await p.click('button[type=submit]'); await p.waitForLoadState('networkidle'); return { ctx, p };
}
async function first(p, url, sel) { await p.goto(B + url); await p.waitForLoadState('networkidle'); return p.$eval(sel, (a) => a.getAttribute('href')).catch(() => null); }
(async () => {
  const br = await chromium.launch({ executablePath: EXE });
  for (const [w, h, tag] of [[1440, 900, '1440'], [375, 800, '375']]) for (const tema of ['claro', 'escuro']) {
    const ctx = await br.newContext({ viewport: { width: w, height: h }, reducedMotion: 'reduce' }); const p = await ctx.newPage();
    for (const [n, u] of [['entrar', '/entrar'], ['recuperar-senha', '/recuperar-senha'], ['acesso-negado', '/Conta/AcessoNegado']]) { await p.goto(B + u); await p.waitForLoadState('networkidle'); await shot(p, `${n}-${tag}-${tema}`, tema); }
    await p.goto(B + '/entrar'); await p.fill('#Input_UserName', 'x@y.z'); await p.fill('#Input_Password', 'errada'); await p.click('button[type=submit]'); await p.waitForLoadState('networkidle'); await shot(p, `entrar-erro-${tag}-${tema}`, tema);
    await ctx.close();
  }
  for (const [role, user] of [['master', 'master@helinho.example'], ['admin', 'admin.norte@helinho.example']]) for (const [w, h, tag] of [[1440, 900, '1440'], [375, 800, '375']]) for (const tema of ['claro', 'escuro']) {
    const { ctx, p } = await login(br, w, h, user);
    const pages = [['dashboard', '/Dashboard'], ['alunos', '/Alunos'], ['alunos-novo', '/Alunos/Novo'], ['desempenho', '/Desempenho'], ['questoes', '/Questoes'], ['questoes-novo', '/Questoes/Editar'], ['missoes', '/Missoes'], ['eventos', '/Eventos'], ['unidades', '/Unidades'], ['unidades-nova', '/Unidades/Nova'], ['admins', '/Administradores'], ['admins-novo', '/Administradores/Novo']];
    for (const [n, u, s] of [['alunos-editar', '/Alunos', 'a[href*="/Alunos/Editar"]'], ['desempenho-ficha', '/Desempenho', 'a[href*="/Desempenho/Aluno"]'], ['questoes-editar', '/Questoes', 'a[href*="/Questoes/Editar/"]'], ['missoes-editar', '/Missoes', 'a[href*="/Missoes/Editar"]']]) { const h2 = await first(p, u, s); if (h2) pages.push([n, h2]); }
    for (const [n, u] of pages) {
      await p.goto(B + u); await p.waitForLoadState('networkidle'); await shot(p, `${role}-${n}-${tag}-${tema}`, tema);
      if (/novo|nova|editar/.test(n) && !/desempenho|^alunos-editar/.test(n)) {
        const sub = await p.$('#conteudo form[method=post] button[type=submit].btn-primary, form[method=post] .form-actions button[type=submit]');
        if (sub) {
          if (/^(questoes|missoes)-editar/.test(n)) await p.evaluate(() => { const i = document.querySelector('form textarea, form input.input[type=text]'); if (i) i.value = ''; });
          await sub.click().catch(() => {}); await p.waitForLoadState('networkidle').catch(() => {});
          await shot(p, `${role}-${n}-erro-${tag}-${tema}`, tema);
        }
      }
    }
    await ctx.close();
  }
  fs.writeFileSync(OUT + '/fontes.json', JSON.stringify(fonts, null, 1));
  await br.close(); console.log('ok', Object.keys(fonts).length);
})().catch((e) => { console.error(e); process.exit(1); });
