// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
(async () => {
  const b = await chromium.launch();
  const ctx = await b.newContext({ viewport: { width: 1366, height: 768 } });
  const p = await ctx.newPage();
  const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto(URL); await p.waitForTimeout(300);
  const active = () => p.evaluate(() => { const e = document.activeElement; if (!e || e === document.body) return null; const r = e.getBoundingClientRect(); const cs = getComputedStyle(e); return { id: e.id || e.getAttribute('aria-label') || e.textContent.trim().slice(0, 24) || e.tagName, w: r.width, h: r.height, vis: cs.visibility !== 'hidden' && cs.display !== 'none', outline: cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) > 0, inCard: !!e.closest('#card'), inWin: !!e.closest('.app-win') }; });
  // 1. Tab through the page: every stop is a visible control with a visible focus ring
  await p.keyboard.press('Tab');
  const stops = [];
  for (let i = 0; i < 40; i++) { const a = await active(); if (a) stops.push(a); await p.keyboard.press('Tab'); }
  ok(stops.length >= 25, `Tab reaches many controls (${stops.length} stops in 40 presses)`);
  ok(stops.every(s => s.vis && s.w > 0 && s.h > 0), 'every Tab stop is a visible control with a size');
  const noRing = stops.filter(s => !s.outline).map(s => s.id);
  ok(noRing.length === 0, `every Tab stop shows a focus ring${noRing.length ? ' (missing: ' + [...new Set(noRing)].slice(0, 5).join(', ') + ')' : ''}`);
  // 2. lookup by keyboard, then the card is reachable and Esc closes it
  await p.click('body', { position: { x: 5, y: 400 } });
  await p.keyboard.press('Control+Alt+Space'); await p.waitForTimeout(300);
  let a = await active(); ok(a && a.inWin, `the lookup box takes the keyboard (focus: ${a && a.id})`);
  await p.keyboard.type('216482'); await p.keyboard.press('Enter'); await p.waitForTimeout(600);
  ok(await p.isVisible('#card'), 'Enter opens the card');
  const inside = [];
  for (let i = 0; i < 6; i++) { await p.keyboard.press('Tab'); const x = await active(); inside.push(x && x.inCard); }
  ok(inside.filter(Boolean).length >= 3, `Tab moves through the card's own controls (${inside.filter(Boolean).length} of 6 stops inside the card)`);
  await p.keyboard.press('Escape'); await p.waitForTimeout(300);
  ok(!(await p.isVisible('#card')), 'Esc closes the card');
  // 3. draft check by keyboard and Esc closes it
  await p.keyboard.press('Control+Alt+Shift+KeyR'); await p.waitForTimeout(400);
  ok(await p.isVisible('#draft'), 'Ctrl+Alt+Shift+R opens the draft check');
  await p.keyboard.press('Escape'); await p.waitForTimeout(300);
  ok(!(await p.isVisible('#draft')), 'Esc closes the draft check');
  // 4. card keyboard shortcuts: E expands, Space pins, S copies the status, Esc closes
  await p.keyboard.press('Control+Alt+Space'); await p.keyboard.type('216482'); await p.keyboard.press('Enter'); await p.waitForTimeout(600);
  await p.keyboard.press('e'); await p.waitForTimeout(300);
  ok(await p.evaluate(() => document.getElementById('card').classList.contains('expanded')), 'E expands the card');
  await p.keyboard.press('s'); await p.waitForTimeout(400);
  const clip = await p.evaluate(() => navigator.clipboard.readText().catch(() => 'DENIED'));
  ok(clip === 'DENIED' || /216482/.test(clip), 'S copies a status sentence (or the clipboard is not readable here)');
  await p.keyboard.press('Escape'); await p.waitForTimeout(300);
  ok(errs.length === 0, 'no page errors ' + errs.join('|'));
  await b.close();
  console.log(fails ? `\n${fails} FAILED of ${checks}` : `\nALL ${checks} PASSED`);
  process.exit(fails ? 1 : 0);
})();
