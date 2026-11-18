// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 24 in the browser simulation: X · Graphite Calendar, the owner's choice of three proposals, is the default. Beside the answer it
// shows the days to the goal date in a style that follows the distance; under the answer it draws the calendar. Attack cases are mixed in.
const { chromium } = require('playwright');
const FILE = SIMURL + '/mockup/windows-simulation.html';
const SIM = FILE + '?clock=fixed';
const AXE = AXE_FILE;
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);
(async () => {
  const b = await chromium.launch();
  const errs = [];
  const fresh = async (opts = {}) => {
    const ctx = await b.newContext({ viewport: opts.viewport || { width: 1366, height: 768 }, permissions: ['clipboard-read', 'clipboard-write'], timezoneId: 'America/New_York', reducedMotion: 'reduce' });
    const p = await ctx.newPage();
    p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    if (opts.at) await p.clock.install({ time: new Date(opts.at) });
    await p.goto(opts.at || opts.live ? FILE : SIM); await p.waitForTimeout(300);
    if (opts.at) await p.clock.runFor(1100);
    await p.dblclick('#deskIcon'); await p.waitForTimeout(120);
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const txt = (p, s) => p.$eval(s, e => e.textContent);
  const openFull = async (p, id) => { await p.evaluate(i => openCard(i, { anchor: { x: 430, y: 60 }, forceFull: true }), id); await settle(p, 450); };
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(s ? document.querySelector(s) : document)).violations.map(v => v.id + ': ' + v.nodes.length + ' ' + v.nodes.slice(0, 2).map(n => n.target.join(' ')).join(' | ')), sel); };
  const count = p => p.evaluate(() => { const c = document.getElementById('c-cd'), s = getComputedStyle(c); return { cls: c.className, aria: c.getAttribute('aria-label'), fig: document.getElementById('c-cd-n').textContent, cap: document.getElementById('c-cd-c').textContent, shown: s.display !== 'none', bg: s.backgroundColor, ink: s.color, weight: getComputedStyle(document.getElementById('c-cd-n')).fontWeight }; });
  const goalIn = (p, id, n) => p.evaluate(([i, k]) => { const x = new Date(demoToday() + k * 864e5); window.__g = window.__g || {}; window.__g[i] = window.__g[i] || DATA[i].goalD; DATA[i].goalD = [x.getUTCFullYear(), x.getUTCMonth() + 1, x.getUTCDate()]; fill(i); }, [id, n]);
  const goalBack = p => p.evaluate(() => { for (const i of Object.keys(window.__g || {})) DATA[i].goalD = window.__g[i]; window.__g = {}; refreshTrayState(); });

  // ====================================================== X. the default look
  let p = await fresh();
  eq(await p.evaluate(() => [state.look, document.documentElement.dataset.dir, document.documentElement.dataset.fam, document.documentElement.hasAttribute('data-glass'), LOOKS.length, LOOKS[0][1], LOOKS.some(l => l[0] === 'w'), ALL_LOOKS.some(l => l[0] === 'w'), state.op]),
    ['x', 'x', 'pocket', true, 8, 'X · Graphite Calendar', false, true, 90], 'X1 the page opens in X · Graphite Calendar, a glass look of the answer-first family, at 90%; W is defined and no longer offered');
  await p.click('#openSettingsBtn'); await settle(p, 250);
  eq(await p.evaluate(() => { const r = [...document.querySelectorAll('input[name=look]')]; return [r.length, r[0].value, r.filter(x => x.checked).map(x => x.value), r[0].closest('label').textContent.trim()]; }), [8, 'x', ['x'], 'X · Graphite Calendar'], 'X2 Settings lists it first and chosen');
  await p.keyboard.press('Escape'); await settle(p, 200);
  await openFull(p, '216482');
  let c = await count(p);
  eq([c.shown, c.cls, c.aria, c.fig, c.cap], [true, 'cd5 lv-calm', '79 days to the goal date', '79', 'DAYS TO GOAL'], 'X3 beside the answer: 79 days to the goal date, in a picture and in words');
  const geo = await p.evaluate(() => { const r = e => e.getBoundingClientRect(), cd = r(document.getElementById('c-cd')), hero = r(document.getElementById('c-hero')), hl = r(document.querySelector('#c-hero .hl')), hs = r(document.getElementById('c-hero-s')), rb = r(document.getElementById('c-ribbon-slot')), cs = getComputedStyle(document.documentElement);
    return { inHero: cd.left >= hero.left && cd.right <= hero.right + .5 && cd.top >= hero.top - .5 && cd.bottom <= hero.bottom + .5, clear: hl.right <= cd.left + .5 && hs.right <= cd.left + .5, oneLine: hl.height < 44, w: Math.round(cd.width), ribbon: getComputedStyle(document.getElementById('c-ribbon-slot')).display !== 'none' && rb.top >= hero.bottom - 1, gauge: getComputedStyle(document.getElementById('c-hgauge')).display, accent: cs.getPropertyValue('--c-accent').trim(), said: document.getElementById('c-ribbon').getAttribute('aria-label'), hero: document.getElementById('c-hero').innerText.replace(/\s+/g, ' ') }; });
  ok(geo.inHero && geo.clear && geo.oneLine && geo.w === 96, `X4 the count sits inside the answer area, 96 px wide, no text runs under it, and the answer stays on one line`);
  eq([geo.ribbon, geo.said, geo.gauge, geo.accent], [true, 'Calendar ribbon. Goal Sat Oct 3 (a weekend day); forecast Sat Oct 17 (a weekend day); 14 days apart.', 'none', '#56626e'], 'X5 the calendar from U is under the answer, with its sentence; the dial of W is gone; the colors are the graphite ones');
  ok(/^14 days later than the goal /.test(geo.hero), 'X6 the first thing the card says is still the forecast against the goal');
  eq(await p.$$eval('.chips .chip', n => n.filter(x => !x.hidden).map(x => x.textContent)), ['Add a note · 3 notes', 'Copy status', 'Quick replies ▾', 'Keep in my list', 'Sister ANDA · 1', 'Open ANDA page ↗'], 'X7 the same actions stay at the bottom');
  ok(await p.evaluate(() => { const k = card.getBoundingClientRect(); return getComputedStyle(document.querySelector('.chips-slot')).position === 'sticky' && k.bottom <= innerHeight + 1 && [...document.querySelectorAll('.chips .chip')].filter(x => !x.hidden).every(c => { const r = c.getBoundingClientRect(); return r.top >= k.top && r.bottom <= k.bottom + 1; }); }), 'X8 the card fits the screen with the calendar in it, and every action is on screen');
  await p.keyboard.press('Enter'); await settle(p, 300);
  eq(await p.evaluate(() => [document.activeElement.id, document.getElementById('x-notes-d').open]), ['qnText', true], 'X9 Enter still goes straight to a new note');
  await p.keyboard.press('Escape'); await settle(p, 120); await p.keyboard.press('Escape'); await settle(p, 250);
  await openFull(p, '216482');
  eq(await axe(p, '#card'), [], 'X10 accessibility scan of the default card');
  // the five levels, at their edges
  const want = [[400, 'calm', '400 days to the goal date', '400', 'DAYS TO GOAL'], [31, 'calm', '31 days to the goal date', '31', 'DAYS TO GOAL'], [30, 'near', '30 days to the goal date', '30', 'DAYS TO GOAL'], [8, 'near', '8 days to the goal date', '8', 'DAYS TO GOAL'],
    [7, 'soon', '7 days to the goal date', '7', 'DAYS TO GOAL'], [1, 'soon', '1 day to the goal date', '1', 'DAY TO GOAL'], [0, 'today', 'The goal date is today', 'Today', 'IS THE GOAL DATE'], [-1, 'passed', 'The goal date passed 1 day ago', '1', 'DAY PAST GOAL'], [-3, 'passed', 'The goal date passed 3 days ago', '3', 'DAYS PAST GOAL'], [-4000, 'passed', 'The goal date passed 4000 days ago', '4000', 'DAYS PAST GOAL']];
  const seen = {};
  for (const [n, lv, words, fig, cap] of want) {
    await goalIn(p, '216482', n); await settle(p, 60); c = await count(p);
    eq([c.cls, c.aria, c.fig, c.cap], ['cd5 lv-' + lv, words, fig, cap], `X11 (${n}) the count is at the “${lv}” level and says “${words}”`);
    ok(await p.evaluate(() => { const cd = document.getElementById('c-cd').getBoundingClientRect(), k = card.getBoundingClientRect(), hl = document.querySelector('#c-hero .hl').getBoundingClientRect(); return cd.right <= k.right && hl.right <= cd.left + .5 && [...document.querySelectorAll('#c-cd b, #c-cd span')].every(e => e.scrollWidth <= e.clientWidth + 1); }), `X12 (${n}) nothing is cut and nothing collides`);
    if (!seen[lv]) { seen[lv] = [c.bg, c.ink, c.weight]; eq(await axe(p, '#c-hero'), [], `X13 (${lv}) accessibility scan of the answer area, contrast included`); }
  }
  ok(new Set(Object.values(seen).map(v => v.join('|'))).size === 5 && seen.calm[0] === 'rgba(0, 0, 0, 0)' && seen.near[0] === 'rgba(0, 0, 0, 0)' && seen.today[0] === 'rgb(138, 88, 0)' && seen.passed[0] === 'rgb(163, 54, 54)' && +seen.soon[2] > +seen.near[2], `X14 the five levels differ in ground, ink or weight, not in color alone (${JSON.stringify(seen)})`);
  await goalBack(p); await p.keyboard.press('Escape'); await settle(p, 250);
  await openFull(p, '214990'); c = await count(p);
  eq([c.cls, c.aria], ['cd5 lv-near', '30 days to the goal date'], 'X15 the sister ANDA, 30 days out, is at the amber level');
  await p.keyboard.press('Escape'); await settle(p, 250);
  await p.evaluate(() => openCard('216482', { anchor: { x: 430, y: 60 }, mini: true })); await settle(p, 450);
  ok(await p.evaluate(() => { const cd = document.getElementById('c-cd').getBoundingClientRect(), k = card.getBoundingClientRect(), hl = document.querySelector('#c-hero .hl').getBoundingClientRect(); return card.classList.contains('mini') && cd.width > 0 && cd.right <= k.right && cd.bottom <= k.bottom && hl.right <= cd.left + 1 && getComputedStyle(document.getElementById('c-ribbon-slot')).display === 'none'; }), 'X16 the small card keeps the count and leaves the calendar out');
  eq(await axe(p, '#card'), [], 'X17 accessibility scan of the small card');
  await p.keyboard.press('Escape'); await settle(p, 250);
  await p.evaluate(() => { state.ts = 'l'; state.density = 'roomy'; applyDisplay(); }); await openFull(p, '216482');
  ok(await p.evaluate(() => { const cd = document.getElementById('c-cd').getBoundingClientRect(), hl = document.querySelector('#c-hero .hl').getBoundingClientRect(), hs = document.getElementById('c-hero-s').getBoundingClientRect(), k = card.getBoundingClientRect(); return hl.right <= cd.left + 1 && hs.right <= cd.left + 1 && cd.right <= k.right && k.bottom <= innerHeight + 1 && [...document.querySelectorAll('#c-cd b, #c-cd span')].every(e => e.scrollWidth <= e.clientWidth + 1); }), 'X18 with the largest text and widest spacing nothing collides and the card still fits');
  await p.keyboard.press('Escape'); await settle(p, 250); await p.evaluate(() => { state.ts = 'm'; state.density = 'cozy'; applyDisplay(); });
  // W gave its place to X
  eq(await p.evaluate(() => { applyLook('w'); const a = state.look; const b0 = snapSettings(); applySettings({ ...b0, appearance: 'w' }); const c0 = state.look; applySettings(b0); return [a, c0]; }), ['x', 'x'], 'X19 W can no longer be chosen, and a backup that names it opens in X');
  for (const id of ['v', 'u', 't', 'k', 'p']) { await p.evaluate(l => applyLook(l), id); await openFull(p, '216482'); ok(await p.evaluate(() => getComputedStyle(document.getElementById('c-cd')).display === 'none'), `X20 look ${id.toUpperCase()} has no count beside the answer: it is as it was`); await p.keyboard.press('Escape'); await settle(p, 200); }
  await p.close();
  for (const [w, hgt] of [[1920, 1080], [1280, 720], [1024, 768], [390, 844]]) {
    const q = await fresh({ viewport: { width: w, height: hgt } });
    await openFull(q, '216482');
    eq(await q.evaluate(() => { const k = card.getBoundingClientRect(), cd = document.getElementById('c-cd').getBoundingClientRect(), rb = document.getElementById('c-ribbon').getBoundingClientRect(); return [document.documentElement.scrollWidth <= innerWidth, k.top >= -1 && k.bottom <= innerHeight + 1, cd.width > 0 && cd.right <= k.right + .5, rb.width > 0 && rb.right <= k.right + .5]; }), [true, true, true, true], `X21 at ${w} by ${hgt} the page does not scroll sideways, the card fits, and the count and the calendar are inside it`);
    await q.close(); }
  // the live clock: real dates, long holiday names, and midnight
  p = await fresh({ at: '2026-11-12T23:59:40-05:00' });
  await p.evaluate(() => { state.autoHide = 0; });     // keep the card open across the wait
  await openFull(p, '216482');
  const before = (await count(p)).aria;
  ok(await p.evaluate(() => { const svg = document.getElementById('c-ribbon'); return [...svg.querySelectorAll('text')].every(t => { const bb = t.getBBox(); return bb.x >= -0.5 && bb.x + bb.width <= 300.5; }) && [...svg.querySelectorAll('text')].some(t => /Martin Luther King/.test(t.textContent)); }), 'X22 a long holiday name on the calendar (Birthday of Martin Luther King, Jr.) is not cut at the edge');
  await p.clock.runFor(30000); await settle(p, 150);
  const after = (await count(p)).aria;
  eq([before, after], ['79 days to the goal date', '78 days to the goal date'], 'X23 after midnight the open card counts one day fewer, with nothing pressed');
  await p.close();
  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
