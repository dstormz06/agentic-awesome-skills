// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
const OUT = process.argv[2] || TMP;
let fails = 0;
const ok = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(URL);
  const vis = sel => page.$eval(sel, e => getComputedStyle(e).display !== 'none' && e.getBoundingClientRect().width > 0).catch(() => false);
  const inside = sel => page.$eval(sel, e => { const r = e.getBoundingClientRect(); const tb = document.getElementById('taskbar').getBoundingClientRect(); return r.left >= 0 && r.top >= 0 && r.right <= innerWidth && r.bottom <= tb.top + 1; });
  const dir = () => page.evaluate(() => document.documentElement.dataset.dir);
  const openCard = async (n) => { await page.dblclick(`[data-pick="${n}"]`); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(450); };

  ok(await dir() === 'x', 'the default look is X (Graphite Calendar) since round 24; W from round 22, V from round 20, T from round 18, K before that');
  ok(await page.$eval('input[name=look][value=x]', e => e.checked), 'the X radio is the checked one in Settings');
  ok(await page.$$eval('input[name=look]', l => l.length) === 8, 'Settings lists the eight looks offered since round 23');
  ok(await page.$$eval('#setCompact input', l => l.length) === 2, 'two of them are in the compact group');
  ok(await page.$$eval('input[name=autohide]', l => l.length) === 4, 'auto-hide offers Off, 10, 20, 40 s');
  ok(await page.$eval('input[name=autohide][value="10"]', e => e.checked), 'auto-hide defaults to 10 s');

  // each full look: card opens, on screen, shows exactly its own progress form
  const forms = { k:'.p-line', p:'.p-gauge' };   // the two classic-layout looks still offered (round 23)
  await page.evaluate(() => { state.autoHide = 0; });
  for (const [id, sel] of Object.entries(forms)) {
    await page.evaluate(i => applyLook(i), id);
    await openCard('216482');
    ok(await vis('#card') && await inside('#card'), `look ${id.toUpperCase()}: card is open and on screen`);
    ok(await vis('#card ' + sel), `look ${id.toUpperCase()}: shows its progress form (${sel})`);
    const hidden = Object.values(forms).filter(x => x !== sel);
    for (const h of [...new Set(hidden)]) if (h !== sel && !(await vis('#card ' + h)) === false && h !== sel) { ok(false, `look ${id}: ${h} should be hidden`); }
    await page.screenshot({ path: `${OUT}/new-look-${id}.png`, clip: await page.$eval('#card', e => { const r = e.getBoundingClientRect(); return { x: Math.max(0, r.x - 14), y: Math.max(0, r.y - 14), width: r.width + 28, height: Math.min(r.height + 28, 768 - Math.max(0, r.y - 14)) }; }) });
    await page.keyboard.press('Escape');
  }


  // compact boxes
  for (const [id, cls, w] of [['l','strip',340],['o','badge',0]]) {
    await page.evaluate(i => applyLook(i), id);
    await openCard('216482');
    ok(await vis('#compact') && !(await vis('#card')), `look ${id.toUpperCase()}: a compact ${cls} opens instead of the card`);
    const r = await page.$eval('#compact', e => { const b = e.getBoundingClientRect(); return { w: b.width, h: b.height }; });
    ok(r.h < 150 && (w ? Math.abs(r.w - w) < 6 : r.w < 200), `look ${id.toUpperCase()}: ${Math.round(r.w)} x ${Math.round(r.h)} px`);
    ok(await inside('#compact'), `look ${id.toUpperCase()}: compact is fully on screen`);
    ok((await page.textContent('#compact')).includes('+14'), `look ${id.toUpperCase()}: the forecast gap is visible`);
    await page.screenshot({ path: `${OUT}/new-compact-${id}.png`, clip: await page.$eval('#compact', e => { const b = e.getBoundingClientRect(); return { x: b.x - 14, y: b.y - 14, width: b.width + 28, height: b.height + 28 }; }) });
    ok(await page.evaluate(() => document.activeElement !== document.getElementById('compact')), `look ${id.toUpperCase()}: the compact box did not take focus`);
    await page.keyboard.press('Escape');
    ok(!(await vis('#compact')), `look ${id.toUpperCase()}: Esc closes it`);
  }

  // compact over the memo: still readable; click expands to the sibling's full card
  await page.evaluate(() => applyLook('l')); await openCard('216482');
  await page.screenshot({ path: OUT + '/new-compact-over-memo.png' });
  await page.click('#compact .r2');
  ok(!(await vis('#compact')) && await vis('#card'), 'clicking the strip opens the full card');
  ok(await dir() === 'k', 'the expanded card uses the strip\'s sibling look (K)');
  await page.keyboard.press('Escape');
  await page.evaluate(() => applyLook('o')); await openCard('216482');
  await page.click('#compact .x');
  ok(!(await vis('#compact')) && !(await vis('#card')), 'the badge close button closes without expanding');
  // hotkey again on the same number expands
  await openCard('216482');
  ok(await vis('#compact'), 'badge is open');
  await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(400);
  ok(!(await vis('#compact')) && await vis('#card') && await dir() === 'g', 'the hotkey again opens the full card (look G, the badge\'s sibling)');
  await page.keyboard.press('Escape');

  // auto-hide
  await page.evaluate(() => { applyLook('k'); state.autoHide = 1; });
  await openCard('216482'); ok(await vis('#card'), 'auto-hide 1 s: card opens');
  await page.mouse.move(10, 700);
  await page.waitForTimeout(1800); ok(!(await vis('#card')), 'an idle card hides itself');
  await openCard('216482');
  const box = await page.$eval('#card', e => { const r = e.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + 60 }; });
  await page.mouse.move(box.x, box.y); await page.waitForTimeout(2000);
  ok(await vis('#card'), 'hovering the card keeps it open');
  await page.mouse.move(10, 700); await page.waitForTimeout(1800);
  ok(!(await vis('#card')), 'moving away lets it hide again');
  await openCard('216482'); await page.click('#pinBtn'); await page.mouse.move(10, 700); await page.waitForTimeout(2000);
  ok(await vis('#card'), 'a pinned card never hides');
  await page.click('#pinBtn'); await page.keyboard.press('Escape');
  await openCard('216482'); await page.click('#expBtn'); await page.mouse.move(10, 700); await page.waitForTimeout(2000);
  ok(await vis('#card'), 'an expanded card never hides');
  await page.keyboard.press('Escape');
  await page.evaluate(() => { applyLook('l'); }); await openCard('216482'); await page.mouse.move(10, 700); await page.waitForTimeout(1800);
  ok(!(await vis('#compact')), 'an idle compact box hides itself too');
  await page.evaluate(() => { state.autoHide = 0; });
  await openCard('216482'); await page.mouse.move(10, 700); await page.waitForTimeout(1800);
  ok(await vis('#compact'), 'Off: it stays');
  await page.keyboard.press('Escape');

  // phone width still works
  await page.setViewportSize({ width: 390, height: 780 });
  await page.evaluate(() => applyLook('k')); await page.waitForTimeout(200);
  const hs = await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1);
  ok(hs, 'no horizontal scroll at 390 px');

  console.log('errors:', JSON.stringify(errors));
  console.log(fails ? `${fails} FAILED` : 'ALL PASSED');
  await browser.close();
})();
