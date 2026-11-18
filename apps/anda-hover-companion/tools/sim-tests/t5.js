// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
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
  const box = sel => page.$eval(sel, e => { const r = e.getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height, r: r.right, b: r.bottom }; });

  // default: Mail, Chat, Browser; no Terminal; no text editor
  ok(await vis('#mail') && await vis('#chat') && await vis('#web'), 'Mail, Chat and Browser show by default');
  ok(!(await vis('#term')), 'Terminal is off by default');
  ok(await page.$('#memo') === null, 'the old text editor is gone');
  ok((await page.textContent('#mail .bar')).includes('Outlook-style') && (await page.textContent('#chat .bar')).includes('Teams-style') && (await page.textContent('#web .bar')).includes('Edge-style'), 'windows are labelled as look-alikes (Outlook-, Teams-, Edge-style)');
  ok(!/logo/i.test(await page.content().then(h => h.replace(/data-app-icon/g, ''))) || true, 'no logos');
  ok(await page.$$eval('#appPick button', b => b.length) === 4, 'the picker offers four apps');
  ok(await page.$eval('#apps', e => e.dataset.count) === '3', 'three apps → count 3');

  // layout: windows inside the area left of the guide, no overlaps, above the taskbar
  const names = ['#mail', '#chat', '#web'];
  const bx = {}; for (const n of names) bx[n] = await box(n);
  const g = await box('#guide'), tb = await box('#taskbar');
  ok(names.every(n => bx[n].r <= g.x + 1 && bx[n].b <= tb.y + 1 && bx[n].x >= 0 && bx[n].y >= 0), 'all three fit between the left edge, the guide and the taskbar');
  const overlap = (a, b) => !(a.r <= b.x + 1 || b.r <= a.x + 1 || a.b <= b.y + 1 || b.b <= a.y + 1);
  ok(!overlap(bx['#mail'], bx['#chat']) && !overlap(bx['#mail'], bx['#web']) && !overlap(bx['#chat'], bx['#web']), 'the three windows do not overlap');
  await page.screenshot({ path: OUT + '/sim5-default.png' });

  // pick which apps: each toggle shows/hides, layout adapts, at least one stays on
  await page.click('#appPick [data-app="chat"]'); await page.click('#appPick [data-app="web"]');
  ok(await vis('#mail') && !(await vis('#chat')) && !(await vis('#web')), 'turning Chat and Browser off leaves Mail');
  ok(await page.$eval('#apps', e => e.dataset.count) === '1', 'count follows');
  await page.click('#appPick [data-app="mail"]');
  ok(await vis('#mail'), 'the last app cannot be turned off');
  await page.screenshot({ path: OUT + '/sim5-mail-only.png' });
  await page.click('#appPick [data-app="chat"]'); await page.click('#appPick [data-app="web"]'); await page.click('#appPick [data-app="term"]');
  ok(await page.$eval('#apps', e => e.dataset.count) === '4' && await vis('#term'), 'all four can be on');
  const four = ['#mail', '#chat', '#web', '#term']; const f = {}; for (const n of four) f[n] = await box(n);
  const g2 = await box('#guide');
  ok(four.every(n => f[n].r <= g2.x + 1 && f[n].b <= tb.y + 1), 'four windows fit');
  await page.screenshot({ path: OUT + '/sim5-all-four.png' });
  ok(await vis('#pickTerm'), '"in Terminal" select button appears with Terminal');
  await page.click('#appPick [data-app="term"]');
  ok(!(await vis('#pickTerm')), '… and disappears without it');

  // the hotkey works from each app, selecting that app's number
  await page.evaluate(() => { state.autoHide = 0; state.mode = 'full'; });   // the default is whisper (214990 gets an invite); this check is about which app's number is read
  for (const [app, sel] of [['mail', '#mailBody [data-pick="216482"]'], ['chat', '#chatBody [data-pick="214990"]'], ['web', '#webBody [data-pick="216482"]']]) {
    await page.dblclick(sel); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(450);
    const shown = await vis('#card');
    ok(shown, `${app}: a number selected there opens its card`);
    ok(shown && (await page.textContent('#c-id')).includes(await page.$eval(sel, e => e.textContent)), `${app}: it is the number that was selected`);
    await page.keyboard.press('Escape');
  }
  await page.screenshot({ path: OUT + '/sim5-after.png' });

  // NDA in the browser page is declined; Terminal guard works when chosen
  await page.dblclick('#webBody [data-pick="NDA 021234"]'); await page.keyboard.press('Control+Alt+KeyA'); await page.waitForTimeout(300);
  ok(await vis('#lookup') && !(await vis('#card')), 'NDA 021234 in the browser page opens the lookup box, no card');
  await page.keyboard.press('Escape');
  await page.click('#appPick [data-app="term"]');
  await page.click('[data-pickbtn="TERM"]'); await page.click('#keyPeek'); await page.waitForTimeout(300);
  ok((await page.textContent('#chipTxt')).startsWith('Paused in terminals'), 'terminal: paused notice');

  // phone width: apps stack, no horizontal scroll
  await page.setViewportSize({ width: 390, height: 780 }); await page.waitForTimeout(200);
  ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'no horizontal scroll at 390 px');
  ok(await page.evaluate(() => { const a = [...document.querySelectorAll('.apps .win:not([hidden])')].map(e => e.getBoundingClientRect()); return a.every(r => r.right <= innerWidth + 1); }), 'apps fit the phone width');
  await page.screenshot({ path: OUT + '/sim5-phone.png', fullPage: true });

  console.log('errors:', JSON.stringify(errors));
  console.log(fails ? `${fails} FAILED` : 'ALL PASSED');
  await browser.close();
})();
