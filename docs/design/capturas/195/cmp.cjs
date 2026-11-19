// uso: node cmp.cjs <dirA> <dirB>  -> diferença de pixels por captura
const { chromium } = require('/Users/henriqueluza/.npm/_npx/381cec31605a419a/node_modules/playwright');
const fs = require('fs');
const EXE = '/Users/henriqueluza/Library/Caches/ms-playwright/chromium_headless_shell-1234/chrome-headless-shell-mac-arm64/chrome-headless-shell';
const [A, Bd] = process.argv.slice(2);
(async () => {
  const br = await chromium.launch({ executablePath: EXE }); const p = await br.newPage();
  const files = fs.readdirSync(A).filter((f) => f.endsWith('.png')).sort(); let bad = 0;
  for (const f of files) {
    if (!fs.existsSync(Bd + '/' + f)) { console.log('FALTA', f); bad++; continue; }
    const a = 'data:image/png;base64,' + fs.readFileSync(A + '/' + f).toString('base64'), b = 'data:image/png;base64,' + fs.readFileSync(Bd + '/' + f).toString('base64');
    const r = await p.evaluate(async ([a, b]) => {
      const ld = (s) => new Promise((ok) => { const i = new Image(); i.onload = () => ok(i); i.src = s; });
      const [ia, ib] = await Promise.all([ld(a), ld(b)]);
      if (ia.width !== ib.width || ia.height !== ib.height) return { dim: `${ia.width}x${ia.height} -> ${ib.width}x${ib.height}` };
      const g = (i) => { const c = document.createElement('canvas'); c.width = i.width; c.height = i.height; const x = c.getContext('2d'); x.drawImage(i, 0, 0); return x.getImageData(0, 0, i.width, i.height).data; };
      const da = g(ia), db = g(ib); let n = 0, x0 = 1e9, y0 = 1e9, x1 = -1, y1 = -1;
      for (let k = 0; k < da.length; k += 4) if (Math.abs(da[k] - db[k]) + Math.abs(da[k + 1] - db[k + 1]) + Math.abs(da[k + 2] - db[k + 2]) > 6) { n++; const q = k / 4, x = q % ia.width, y = (q / ia.width) | 0; if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
      return { n, total: da.length / 4, box: n ? [x0, y0, x1, y1] : null };
    }, [a, b]);
    if (r.dim || r.n) { bad++; console.log('DIFF', f, JSON.stringify(r)); }
  }
  console.log(`${files.length} capturas, ${bad} com diferença`); await br.close();
})();
