// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 35 in the browser simulation: the tray menu of seven choices (Lookup, Today, Review a draft, Ask, More, Settings, Help) and Exit;
// with all tools on, Lists comes before More. Groups open in place under their name. The order is held to mockup/round35-golden.json,
// which the Windows app's menu is also tested against. (The type scale of round 35 is the app's; its test reads the app's windows.)
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const GOLD = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round35-golden.json'), 'utf8'));
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);

(async () => {
  const b = await chromium.launch(); const errs = [];
  const fresh = async (viewport = { width: 1366, height: 768 }, hash = '') => {
    const ctx = await b.newContext({ viewport, timezoneId: 'America/New_York', reducedMotion: 'reduce' });
    const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error') errs.push(m.type() + ': ' + m.text()); });
    await p.goto(SIM + hash); await p.waitForTimeout(300);
    await p.click('#chev'); await p.click('#pinBtn2'); await p.waitForTimeout(100);
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const shown = (p, sel) => p.$eval(sel, e => getComputedStyle(e).display !== 'none' && !e.hidden && e.getClientRects().length > 0).catch(() => false);
  const topChoices = p => p.$$eval('#menu > button:not(:disabled)', n => n.map(x => x.querySelector('span').textContent));
  const visibleChoices = p => p.$$eval('#menu button:not(:disabled)', n => n.filter(x => x.offsetParent !== null).map(x => x.querySelector('span').textContent));
  const want = edition => GOLD[edition].map(e => typeof e === 'string' ? e : e.label);
  const axe = async (p, sel) => {
    if (!AXE_FILE) return ['axe-core not found: set AXE_PATH'];
    await p.addScriptTag({ path: AXE_FILE });
    return p.evaluate(async s => (await axe.run(document.querySelector(s), { runOnly: ['wcag2a', 'wcag2aa'] })).violations.map(v => v.id), sel);
  };

  // ---- M. the focused menu ----
  let p = await fresh();
  await p.click('#appIcon', { button: 'right' }); await settle(p);
  eq(await topChoices(p), want('focused'), 'M1 the focused menu offers the shared answers\' choices, in their order');
  eq((await topChoices(p)).filter(t => t !== 'Exit').length, 7, 'M2 seven choices, and Exit');
  eq(await visibleChoices(p), want('focused'), 'M3 at first every group is closed: only the seven choices and Exit can be seen');
  const more = p.locator('#menu > button[data-group="More"]');
  eq(await more.getAttribute('aria-expanded'), 'false', 'M4 More says it is closed');
  await more.click(); await settle(p);
  const moreItems = GOLD.focused.find(e => e.label === 'More').items;
  eq([await more.getAttribute('aria-expanded'), await shown(p, '#menu'), (await visibleChoices(p)).filter(t => moreItems.includes(t))], ['true', true, moreItems], 'M5 More opens in place, says so, and shows what it holds; the menu stays open');
  // the arrow keys move through what can be seen, and skip a closed group's choices
  await more.click(); await settle(p);
  await p.locator('#menu > button').first().focus();
  const seen = [];
  for (let i = 0; i < 9; i++) { seen.push(await p.evaluate(() => document.activeElement.querySelector('span').textContent)); await p.keyboard.press('ArrowDown'); }
  eq(seen.slice(0, 8), want('focused'), 'M6 the arrow keys go through the seven choices and Exit, never into a closed group');
  eq(await axe(p, '#menu'), [], 'M7 accessibility scan of the menu');
  // added after the review: Right opens a group and goes in, Left closes it and goes back, the glyph is not read, Escape returns to the icon
  await more.focus(); await p.keyboard.press('ArrowRight'); await settle(p);
  eq([await more.getAttribute('aria-expanded'), await p.evaluate(() => document.activeElement.querySelector('span').textContent), await more.getAttribute('aria-haspopup'), await p.evaluate(() => document.querySelector('#menu > button[data-group="More"] .k').getAttribute('aria-hidden'))],
    ['true', moreItems[0], 'true', 'true'], 'M7a Right arrow opens More and goes to its first choice; the arrow glyph is hidden from screen readers');
  await p.keyboard.press('ArrowLeft'); await settle(p);
  eq([await more.getAttribute('aria-expanded'), await p.evaluate(() => document.activeElement.dataset.group)], ['false', 'More'], 'M7b Left arrow closes More and goes back to its name');
  await more.click(); await p.locator('#menu button[data-group="Appearance"]').click(); await p.locator('#menu button[data-group="Auto-hide"]').click(); await settle(p);
  eq([await p.$$eval('#menu [aria-checked]', n => [...new Set(n.map(x => x.getAttribute('role')))]), await axe(p, '#menu')], [['menuitemradio'], []], 'M7c with More, Appearance and Auto-hide open, the checked choices are radios and the scan finds nothing');
  await p.locator('#menu button[data-group="Appearance"]').focus(); await p.keyboard.press('ArrowLeft'); await settle(p);
  eq([await p.locator('#menu button[data-group="Appearance"]').getAttribute('aria-expanded'), await more.getAttribute('aria-expanded')], ['false', 'true'], 'M7d Left arrow on an open group inside More closes that group, not More');
  await p.keyboard.press('Escape'); await settle(p);
  eq([await shown(p, '#menu'), await p.evaluate(() => document.activeElement.id)], [false, 'appIcon'], 'M7e Escape closes the menu and the keyboard goes back to the tray icon');
  await p.click('#appIcon', { button: 'right' }); await settle(p);
  // a choice in a group does what it says
  await more.click(); await p.click('#menu >> text=Connections and event log…'); await settle(p, 250);
  ok(await shown(p, '#conn') && !(await shown(p, '#menu')), 'M8 a choice inside More opens its window and closes the menu');
  await p.keyboard.press('Escape'); await settle(p);
  await p.click('#appIcon', { button: 'right' }); await p.click('#menu > button[data-group="Help"]'); await p.click('#menu >> text=About and what changed…'); await settle(p, 250);
  ok(await shown(p, '#about'), 'M9 Help holds About and what changed');
  await p.keyboard.press('Escape'); await settle(p);
  // the menu fits the screen with More open
  await p.click('#appIcon', { button: 'right' }); await p.click('#menu > button[data-group="More"]'); await settle(p);
  ok(await p.evaluate(() => { const m = document.getElementById('menu'), r = m.getBoundingClientRect(), tb = document.getElementById('taskbar').getBoundingClientRect(); return r.top >= 0 && (r.bottom <= tb.top + 1 || m.scrollHeight > m.clientHeight); }), 'M10 with More open the menu stays on the screen (it scrolls if it must)');
  await p.keyboard.press('Escape');
  // the icon in the overflow does what the pinned one does
  await p.evaluate(() => { APP_WINS.forEach(closeApp); }); await p.click('#chev'); await settle(p);
  await p.close();
  // the icon not pinned: it is in the overflow, and a click on it there opens Today (no script makes it visible)
  { const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' });
    p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message)); await p.goto(SIM); await p.waitForTimeout(300);
    await p.click('#chev'); await settle(p);
    const there = await shown(p, '#flyIcon');
    if (there) { await p.click('#flyIcon'); await settle(p, 250); }
    ok(there && await shown(p, '#today'), 'M11 the icon in the overflow opens Today too'); await p.close(); }

  // ---- L. with all tools on ----
  p = await fresh({ width: 1366, height: 768 }, '#all');
  await p.click('#appIcon', { button: 'right' }); await settle(p);
  eq(await topChoices(p), want('all'), 'L1 with all tools, Lists comes before More, as the shared answers say');
  await p.click('#menu > button[data-group="Lists"]'); await settle(p);
  eq((await visibleChoices(p)).filter(t => GOLD.all.find(e => e.label === 'Lists').items.includes(t)), GOLD.all.find(e => e.label === 'Lists').items, 'L2 Lists holds the two to-do lists, the notes and my usage');
  await p.click('#menu >> text=Notes to my to-do list…'); await settle(p, 250);
  ok(await shown(p, '#notes'), 'L3 a choice inside Lists opens its window');
  await p.close();

  // ---- P. on a phone ----
  p = await fresh({ width: 390, height: 844 });
  await p.click('#appIcon', { button: 'right' }); await settle(p);
  await p.click('#menu > button[data-group="More"]'); await settle(p);
  ok(await p.evaluate(() => document.documentElement.scrollWidth <= innerWidth && document.getElementById('menu').getBoundingClientRect().right <= innerWidth + 1), 'P1 on a phone the menu with More open does not run off the side');
  await p.close();

  // ---- G. the guide and the shared answers ----
  p = await fresh();
  ok(await p.evaluate(() => GUIDE_STEPS.some(s => JSON.stringify(s).includes('Right-click it for the menu: Lookup, Today, Review a draft, Ask, Settings, Help, and More for the rest'))), 'G1 First steps names the menu as it now is');
  eq(await p.evaluate(() => CHANGES.find(c => c.round === 35).items), GOLD.changes.items, 'G2 what changed in round 35 is listed');
  const v29 = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round29-golden.json'), 'utf8')).version;
  eq(await p.evaluate(() => APP_VERSION), v29, 'G3 the version is the one in round 29\'s shared answers, which the Windows app is held to');
  await p.close();

  eq(errs, [], 'F1 no error in any page');
  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
