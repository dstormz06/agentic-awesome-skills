// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium, devices } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const OUT = process.argv[2] || TMP;
let fails = 0; const ok = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
(async () => {
  const browser = await chromium.launch();
  const d = devices['iPhone 13'];
  const ctx = await browser.newContext({ ...d });
  const page = await ctx.newPage();
  const errors = []; page.on('pageerror', e => errors.push(e.message)); page.on('console', m => { if (m.type()==='error') errors.push(m.text()); });
  await page.goto(URL); await page.waitForTimeout(300);
  const vis = sel => page.$eval(sel, e => getComputedStyle(e).display !== 'none' && e.getBoundingClientRect().width > 0).catch(() => false);
  const hscroll = () => page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
  console.log('viewport', JSON.stringify(page.viewportSize()));
  ok((await hscroll()) <= 0, 'no horizontal scroll on load (overflow ' + await hscroll() + 'px)');
  await page.screenshot({ path: OUT + '/m1-load.png' });
  await page.screenshot({ path: OUT + '/m1-full.png', fullPage: true });

  await page.tap('[data-pickbtn="216482"]'); await page.tap('#keyPeek'); await page.waitForTimeout(500);
  ok(await vis('#card'), 'tapping Ctrl+Alt+A opens the card');
  ok(await page.$eval('#card', e => e.classList.contains('sheet')), 'card presents as a bottom sheet on a phone');
  ok(await vis('#scrim'), 'scrim shown behind the sheet');
  ok((await hscroll()) <= 0, 'no horizontal scroll with the sheet open');
  const r = await page.$eval('#card', e => { const b = e.getBoundingClientRect(); return [Math.round(b.left), Math.round(b.right), Math.round(b.bottom), innerWidth, innerHeight]; });
  ok(r[0] === 0 && r[1] === r[3] && r[2] === r[4], 'sheet spans the full width and sits on the bottom edge ' + JSON.stringify(r));
  await page.screenshot({ path: OUT + '/m2-sheet.png' });
  await page.tap('#expBtn'); await page.waitForTimeout(250); await page.screenshot({ path: OUT + '/m3-sheet-expanded.png' });
  await page.tap('#scrim', { position: { x: 20, y: 40 } }); await page.waitForTimeout(250);
  ok(!(await vis('#card')), 'tapping outside dismisses the sheet');

  await page.tap('#trayMenuBtn'); ok(await vis('#menu'), 'Tray menu button opens the menu on touch');
  ok(await page.$eval('#menu', e => { const b = e.getBoundingClientRect(); return b.left >= 0 && b.right <= innerWidth; }), 'menu fits the screen width');
  await page.screenshot({ path: OUT + '/m4-menu.png' });
  await page.tap('#menu >> text=Settings…'); ok(await vis('#settings'), 'Settings opens');
  ok((await hscroll()) <= 0, 'no horizontal scroll with Settings open');
  await page.screenshot({ path: OUT + '/m5-settings.png' });
  await page.tap('#setDone');

  await page.tap('#chev'); ok(await vis('#flyout'), 'chevron opens the overflow on touch');
  ok(await page.$eval('#flyout', e => { const b = e.getBoundingClientRect(); return b.left >= 0 && b.right <= innerWidth && b.bottom <= innerHeight; }), 'flyout fits the screen');
  await page.screenshot({ path: OUT + '/m6-flyout.png' });
  console.log('errors', JSON.stringify(errors)); console.log(fails ? 'FAILED ' + fails : 'ALL PASS');
  await browser.close();
})();
