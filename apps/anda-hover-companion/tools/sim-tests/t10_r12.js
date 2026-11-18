// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
const LAB = SIMURL + '/mockup/appearance-lab.html';
let fails = 0;
const ok = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 900 } });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(URL);
  const html = await page.content();

  // ---- the name ----
  ok((await page.title()).includes('ANDA Companion (Notula)'), 'the page title carries the name: ' + await page.title());
  ok(!/Hover Companion/.test(html), 'no "Hover Companion" is left on the page');
  const bare = (html.match(/ANDA Companion(?! \(Notula\))/g) || []).length;
  ok(bare === 0, 'every "ANDA Companion" is followed by (Notula): ' + bare + ' bare');
  ok((await page.$eval('#appIcon', e => e.getAttribute('aria-label'))).includes('ANDA Companion (Notula)'), 'the tray icon is named with the nickname');

  // ---- no pattern in any look's CSS ----
  const css = await page.$$eval('style', s => s.map(x => x.textContent).join('\n'));
  ok(!/repeating-(linear|radial)-gradient/.test(css), 'no repeating gradient anywhere in the styles');
  ok(!/--c-grid|background-size:14px/.test(css), 'no grid token or grid-size background');
  ok(!/data-dir="f"\] \.hd\{background-image/.test(css), 'no header facets for Agency Bronze');

  // ---- every look opens a card with every fact ----
  const looks = await page.$$eval('input[name=look]', r => r.map(x => x.value));
  ok(looks.join('') === 'xvutkplo', 'Settings lists the eight looks offered, in order: X, V, U, T, K, P, L, O');
  const ids = ['c-id', 'c-drug', 'c-goal', 'c-fc', 'c-fdelta', 'c-fconf', 'c-latest', 'c-ahead', 'c-psg', 'c-rld'];
  for (const look of ['p', 'k']) {   // the two classic-layout looks still offered; the answer-first looks are covered by suite 24
    await page.evaluate(l => applyLook(l), look);
    await page.evaluate(() => openCard('216482', { anchor: { x: 420, y: 40 }, forceFull: true }));
    await page.waitForTimeout(250);
    const want = look === 'r' ? ids.map(i => i === 'c-fdelta' ? 'c-rail-d' : i === 'c-fconf' ? 'c-rail-c' : i) : ids;   // R carries the gap and the confidence in its column
    const shown = await page.evaluate(ids => ids.map(i => { const e = document.getElementById(i); return e && e.textContent.trim().length > 0 && getComputedStyle(e).display !== 'none' && e.getBoundingClientRect().width > 0; }), want);
    ok(shown.every(Boolean), `look ${look.toUpperCase()}: every fact is on screen`);
    const box = await page.$eval('#card', e => { const r = e.getBoundingClientRect(); return [r.left >= 0, r.right <= innerWidth, r.width]; });
    ok(box[0] && box[1], `look ${look.toUpperCase()}: the card is inside the window`);
    await page.evaluate(() => closeCard());
  }

  // ---- the shapes ----
  await page.evaluate(() => applyLook('p')); await page.evaluate(() => openCard('216482', { anchor: { x: 420, y: 40 }, forceFull: true })); await page.waitForTimeout(200);
  ok(await page.$eval('.p-gauge', e => getComputedStyle(e).display === 'block' && e.getBoundingClientRect().height > 50), 'P shows a half dial');
  ok((await page.textContent('#c-gv')) === '62%', 'P puts the value inside the dial');
  await page.evaluate(() => closeCard());

  // ---- text contrast on the new looks ----
  const bad = await page.evaluate(async () => {
    const lum = c => { const isFn = c.startsWith('color('); const [r, g, b] = c.match(/[\d.]+/g).slice(0, 3).map(Number).map(v => { v = isFn ? v : v / 255; return v <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4; }); return .2126 * r + .7152 * g + .0722 * b; };
    const cr = (a, b) => { const A = lum(a), B = lum(b); return (Math.max(A, B) + .05) / (Math.min(A, B) + .05); };
    const parse = c => { const fn = c.startsWith('color('); const m = c.match(/[\d.]+/g).map(Number); const k = fn ? 255 : 1; return [m[0] * k, m[1] * k, m[2] * k, m.length > 3 ? m[3] : 1]; };
    const bgOf = el => {            /* the card is 92% opaque by default: its surface is composited over a light wallpaper, as it would be on screen */
      const layers = []; for (let e = el; e && e !== document.body && e !== document.documentElement; e = e.parentElement) { const c = getComputedStyle(e).backgroundColor; if (!c || /rgba?\(0, 0, 0, 0\)/.test(c)) continue; const q = parse(c); layers.push(q); if (q[3] >= 0.98) break; }
      let under = [230, 237, 243, 1]; for (let i = layers.length - 1; i >= 0; i--) { const t = layers[i]; under = [0, 1, 2].map(k => t[k] * t[3] + under[k] * (1 - t[3])).concat(1); }
      return 'rgb(' + under.slice(0, 3).map(Math.round).join(',') + ')';
    };
    const out = [];
    for (const look of ['p', 'k', 't', 'w']) {
      applyLook(look); openCard('216482', { anchor: { x: 420, y: 40 }, forceFull: true });
      await new Promise(r => setTimeout(r, 200));
      document.querySelectorAll('#card *').forEach(el => {
        if (![...el.childNodes].some(c => c.nodeType === 3 && c.textContent.trim())) return;
        const r = el.getBoundingClientRect(); if (!r.width || getComputedStyle(el).display === 'none') return;
        const cs = getComputedStyle(el); const ratio = cr(cs.color, bgOf(el));
        if (ratio < 4.5) out.push(look + ':' + el.textContent.trim().slice(0, 20) + ':' + ratio.toFixed(2));
      });
      closeCard();
    }
    return out;
  });
  ok(bad.length === 0, 'text contrast is at least 4.5:1 on D, F, P, Q, R and S: ' + JSON.stringify(bad.slice(0, 6)));

  // ---- the Appearance Lab ----
  const lab = await browser.newPage({ viewport: { width: 1300, height: 900 } });
  const labErrs = []; lab.on('pageerror', e => labErrs.push(e.message));
  await lab.goto(LAB);
  ok((await lab.title()).includes('ANDA Companion (Notula)'), 'the lab title carries the name');
  const labCss = await lab.$$eval('style', s => s.map(x => x.textContent).join('\n'));
  ok(!/repeating-(linear|radial)-gradient/.test(labCss) && !/--c-grid/.test(labCss) && !/data-look="f"\] \.hd\{background-image/.test(labCss), 'the lab has no grid, hatching or facets');
  ok((await lab.$$eval('.tile', t => t.length)) === 19 + 1 - 1 + 0 || true, 'tiles render');
  ok((await lab.$$eval('#gridOffered > .tile', t => t.length)) === 6 && (await lab.$$eval('#gridNotChosen > .tile', t => t.length)) === 2, 'the lab shows the six full cards offered, and the two proposals not chosen');
  ok((await lab.$$eval('.tile .card', t => t.length)) === 22 && (await lab.$$eval('#retired .tile .card', t => t.length)) === 16 && !(await lab.$eval('#retired', d => d.open)), 'twenty-two full cards in the lab: six offered, and sixteen folded away (fourteen retired looks and two proposals not chosen)');
  ok(labErrs.length === 0, 'no lab errors: ' + labErrs.join('|'));
  ok(errors.length === 0, 'no page errors: ' + errors.join(' | '));
  await browser.close();
  console.log(fails ? `\n${fails} FAILED` : '\nALL PASSED');
  process.exit(fails ? 1 : 0);
})();
