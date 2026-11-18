// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 23 in the browser simulation: eight looks are offered; the tray icon shows the number when two or more things need the person;
// the person chooses when the icon is counted again; the Loupe has no amber; the Appearance Lab shows three proposals with a
// "days until the goal date" indicator. Attack cases are mixed in.
const { chromium } = require('playwright');
const FILE = SIMURL + '/mockup/windows-simulation.html';
const LAB = SIMURL + '/mockup/appearance-lab.html';
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
    await p.goto(opts.at ? FILE : SIM); await p.waitForTimeout(300);
    if (opts.at) await p.clock.runFor(1100);
    await p.dblclick('#deskIcon'); await p.waitForTimeout(120);
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const txt = (p, s) => p.$eval(s, e => e.textContent);
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(s ? document.querySelector(s) : document)).violations.map(v => v.id + ': ' + v.nodes.length + ' ' + v.nodes.slice(0, 2).map(n => n.target.join(' ')).join(' | ')), sel); };
  const pin = async p => { await p.click('#chev'); await p.click('#pinBtn2'); await settle(p, 150); await p.mouse.click(700, 330); await settle(p, 100); };
  const tray = p => p.evaluate(() => { const g = document.querySelector('#appIcon .glyph'), f = document.querySelector('#flyIcon .glyph'), svg = e => decodeURIComponent(getComputedStyle(e).backgroundImage);
    const num = s => { const m = /<text[^>]*>([^<]*)<\/text>/.exec(s); return m ? m[1] : ''; };
    return { count: g.dataset.count, fly: f.dataset.count, n: num(svg(g)), flyN: num(svg(f)), amber: /f2a93b/i.test(svg(g)), dotClass: g.classList.contains('alert'), title: document.getElementById('appIcon').title, aria: document.getElementById('appIcon').getAttribute('aria-label') }; });
  const setAlerts = (p, n) => p.evaluate(k => { if (k) DATA['214990'].alerts = Array.from({ length: k }, (_, i) => 'Mark ' + (i + 1)); else delete DATA['214990'].alerts; refreshTrayState(); }, n);

  // ====================================================== E. eight looks
  let p = await fresh();
  eq(await p.evaluate(() => [LOOKS.map(l => l[0]).join(''), LOOKS.map(l => l[1]), ALL_LOOKS.length]), ['xvutkplo', ['X · Graphite Calendar', 'V · Glass Sheet', 'U · Calendar Ribbon', 'T · Pocket Plain', 'K · Graphite Quiet', 'P · Bronze Gauge', 'L · Graphite Strip', 'O · Teal Badge'], 24], 'E1 eight looks are offered, in this order; the other sixteen are defined and not listed');
  await p.click('#openSettingsBtn'); await settle(p, 250);
  eq(await p.evaluate(() => [[...document.querySelectorAll('#setLook input')].map(r => r.value).join(''), [...document.querySelectorAll('#setCompact input')].map(r => r.value).join(''), [...document.querySelectorAll('input[name=look]:checked')].map(r => r.value)]), ['xvutkp', 'lo', ['x']], 'E2 Settings lists six full cards and two compact boxes, with the default chosen');
  await p.keyboard.press('Escape'); await settle(p, 200);
  await pin(p); await p.click('#appIcon', { button: 'right' }); await settle(p, 250);
  await p.evaluate(() => [...document.querySelectorAll('#menu *')].find(x => x.textContent === 'Appearance' || /^Appearance/.test(x.textContent) && x.children.length === 0)?.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true })));
  const menuLooks = await p.evaluate(() => LOOKS.map(l => l[1]).filter(n => [...document.querySelectorAll('#menu *, .pop *')].some(x => x.textContent === n)).length + '/' + ALL_LOOKS.filter(l => !LOOKS.includes(l)).map(l => l[1]).filter(n => [...document.querySelectorAll('#menu *, .pop *')].some(x => x.textContent === n)).length);
  ok(/\/0$/.test(menuLooks), `E3 the tray menu names no retired look (${menuLooks})`);
  await p.keyboard.press('Escape'); await settle(p, 150);
  for (const id of ['x', 'v', 'u', 't', 'k', 'p']) {
    await p.evaluate(l => { applyLook(l); openCard('216482', { anchor: { x: 430, y: 60 }, forceFull: true }); }, id); await settle(p, 400);
    ok(await p.evaluate(l => state.look === l && card.style.display === 'block' && card.getBoundingClientRect().bottom <= innerHeight + 1 && /216482/.test(document.getElementById('c-id').textContent), id), `E4 the full card ${id.toUpperCase()} opens and fits`);
    await p.keyboard.press('Escape'); await settle(p, 200);
  }
  for (const id of ['l', 'o']) {
    await p.evaluate(l => { applyLook(l); openCard('216482', { anchor: { x: 430, y: 60 } }); }, id); await settle(p, 400);
    ok(await p.evaluate(() => { const c = document.getElementById('compact'); return c.style.display !== 'none' && c.getBoundingClientRect().width > 0; }), `E5 the compact box ${id.toUpperCase()} opens`);
    await p.evaluate(() => closeCompact()); await settle(p, 150);
  }
  await p.evaluate(() => applyLook('x'));
  eq(await p.evaluate(() => { const out = []; for (const id of ['w', 'a', 'f', 'q', 'm', 'n', 'zz', '', null, '<img src=x onerror=alert(1)>']) { applyLook(id); out.push(state.look); } return [...new Set(out)]; }), ['x'], 'E6 a retired or unknown look changes nothing, and raises no error');
  eq(await p.evaluate(() => { const before = snapSettings(); applySettings({ ...before, appearance: 'f' }); const a = state.look; applySettings({ ...before, appearance: 'k' }); const k = state.look; applySettings(before); return [a, k, state.look]; }), ['x', 'k', 'x'], 'E7 a backup that names a retired look falls back to the default; one that names an offered look is kept');
  eq(await axe(p), [], 'E8 accessibility scan of the desktop');
  await p.close();

  // ====================================================== N. a dot for one, the number for more
  p = await fresh(); await pin(p);
  let t = await tray(p);
  eq([t.count, t.fly, t.n, t.amber, t.dotClass], ['', '', '', false, false], 'N1 nothing needs the person: no badge and no amber');
  await p.check('#swDue'); await settle(p, 200); t = await tray(p);
  eq([t.count, t.fly, t.n, t.amber, t.dotClass], ['dot', 'dot', '', true, true], 'N2 one thing: a dot, with no number in it');
  await p.check('#swFlag'); await settle(p, 200); t = await tray(p);
  eq([t.count, t.fly, t.n, t.flyN, t.amber, t.title], ['2', '2', '2', '2', true, 'ANDA Companion (Notula) · OFFLINE · no API connected · 2 things need your attention today'], 'N3 two things: the number 2, on both copies of the icon, and the tooltip says the same');
  await setAlerts(p, 2); t = await tray(p); eq([t.count, t.n], ['3', '3'], 'N4 three things: the number 3');
  await setAlerts(p, 8); t = await tray(p); eq([t.count, t.n], ['9', '9'], 'N5 nine things: 9');
  await setAlerts(p, 9); t = await tray(p); eq([t.count, t.n, /10 things need your attention today/.test(t.aria)], ['9+', '9+', true], 'N6 ten things: 9+, and the spoken label gives the true count');
  await setAlerts(p, 400); t = await tray(p); eq([t.count, t.n], ['9+', '9+'], 'N7 four hundred: still 9+');
  await setAlerts(p, 0); await p.uncheck('#swFlag'); await settle(p, 150); t = await tray(p);
  eq([t.count, t.n, t.dotClass], ['dot', '', true], 'N8 back to one thing: the dot returns');
  await p.uncheck('#swDue'); await settle(p, 150); t = await tray(p);
  eq([t.count, t.n, t.amber, t.dotClass], ['', '', false, false], 'N9 and with nothing left the icon is plain again');
  eq(await p.evaluate(() => [0, 1, 2, 9, 10, -4].map(trayBadge)), ['', 'dot', '2', '9', '9+', ''], 'N10 the rule in one place: nothing, a dot, the number, 9+');
  ok(await p.evaluate(() => { const svg = decodeURIComponent(trayGlyph(7, false)); return /fill="#1d2329">7<\/text>/.test(svg) && /fill="#f2a93b"/.test(svg) && !/<script|onerror/i.test(svg); }), 'N11 the number is dark on amber (readable), and the picture holds nothing but shapes and the digit');
  await p.check('#swDue'); await p.check('#swFlag'); await settle(p, 200);
  await p.click('#appIcon', { button: 'right' }); await settle(p, 250);
  ok((await p.$$eval('#menu *', n => n.map(x => x.textContent))).some(x => x === 'News and alerts… (2 today)'), 'N12 the tray menu gives the same number');
  await p.keyboard.press('Escape'); await settle(p, 150);
  eq(await axe(p, '#taskbar'), [], 'N13 accessibility scan of the taskbar with a number on the icon');
  await p.close();
  p = await fresh();
  eq(await p.evaluate(() => { const amber = e => /f2a93b/i.test(decodeURIComponent(getComputedStyle(e).backgroundImage)); return [amber(document.querySelector('#appIcon .glyph')), amber(document.querySelector('#flyIcon .glyph')), [...document.querySelectorAll('.icon .glyph, #deskIcon .glyph, .glyph')].filter(amber).length]; }), [false, false, 0], 'N14 at rest no copy of the Loupe on the page carries amber, large or small');
  await p.close();

  // ====================================================== C. when the icon is counted again
  p = await fresh();
  await p.click('#openSettingsBtn'); await settle(p, 250);
  eq(await p.evaluate(() => [[...document.querySelectorAll('input[name=trec]')].map(r => r.closest('label').textContent.trim()), [...document.querySelectorAll('input[name=trec]:checked')].map(r => r.value), state.trayRecount]),
    [['Only when I use the app', 'Also once a day, just after midnight', 'Also every hour'], ['daily'], 'daily'], 'C1 Settings offers three choices, and starts at once a day just after midnight');
  ok(await p.evaluate(() => { const r = document.querySelector('input[name=trec]'); const sec = r.closest('details, section, .sec'); return getComputedStyle(r.closest('label')).display !== 'none' && r.closest('[role=radiogroup]').getAttribute('aria-label') === 'When the tray icon is counted again'; }), 'C2 the group has a name a screen reader can say');
  await p.evaluate(() => { const r = document.querySelector('input[name=trec][value=hourly]'); r.checked = true; r.dispatchEvent(new Event('change', { bubbles: true })); });
  eq(await p.evaluate(() => [state.trayRecount, snapSettings().trayRecount]), ['hourly', 'hourly'], 'C3 a choice is kept and is part of a backup');
  eq(await p.evaluate(() => { const b = snapSettings(); const out = []; for (const v of ['use', 'daily', 'hourly', 'always', '', 7, null, undefined, '<b>']) { applySettings({ ...b, trayRecount: v }); out.push(state.trayRecount); } applySettings(b); return out; }), ['use', 'daily', 'hourly', 'daily', 'daily', 'daily', 'daily', 'daily', 'daily'], 'C4 a backup with a value that is not one of the three falls back to once a day');
  eq(await axe(p, '#settings'), [], 'C5 accessibility scan of Settings');
  await p.close();
  const midnight = async mode => {
    const q = await fresh({ at: '2026-11-12T23:59:40-05:00' }); await pin(q);
    await q.evaluate(m => { state.trayRecount = m; const x = new Date(demoToday() + 864e5); DATA['216482'].attnDue = [x.getUTCFullYear(), x.getUTCMonth() + 1, x.getUTCDate()]; refreshTrayState(); }, mode);
    const before = (await tray(q)).count; await q.clock.runFor(30000); await settle(q, 100);
    const after = (await tray(q)).count;
    await q.evaluate(() => openCard('214990', { anchor: { x: 430, y: 60 }, forceFull: true })); await settle(q, 400);
    const used = (await tray(q)).count; await q.close(); return [before, after, used];
  };
  eq(await midnight('daily'), ['', 'dot', 'dot'], 'C6 once a day: an item due tomorrow shows after midnight, with nothing pressed');
  eq(await midnight('hourly'), ['', 'dot', 'dot'], 'C7 every hour: the same at midnight');
  eq(await midnight('use'), ['', '', 'dot'], 'C8 only when I use the app: nothing changes at midnight, and opening a card counts');
  p = await fresh({ at: '2026-11-12T10:59:40-05:00' }); await pin(p);
  const hourly = async mode => { await p.evaluate(m => { state.trayRecount = m; refreshTrayState(); DATA['214990'].alerts = ['A mark added to the sheet at 10:59']; }, mode); const b0 = (await tray(p)).count; await p.clock.runFor(30000); await settle(p, 100); const a0 = (await tray(p)).count; await p.evaluate(() => { delete DATA['214990'].alerts; refreshTrayState(); }); return [b0, a0]; };
  eq(await hourly('hourly'), ['', 'dot'], 'C9 every hour: a change in the sheet shows at the next hour');
  await p.clock.runFor(3600000 - 30000 - 20000); await settle(p, 50);
  eq(await hourly('daily'), ['', ''], 'C10 once a day: the same change waits (here it would show when the app is next used)');
  await p.close();

  // ====================================================== L. the Appearance Lab: three proposals, eight offered, fifteen folded away
  const lab = await (await b.newContext({ viewport: { width: 1366, height: 900 } })).newPage(); lab.on('pageerror', e => errs.push('lab: ' + e.message)); lab.on('console', m => { if (m.type() === 'error') errs.push('lab: ' + m.text()); });
  await lab.goto(LAB); await lab.waitForSelector('.tile[data-id="x"]');
  eq(await lab.evaluate(() => [[...document.querySelectorAll('#gridNotChosen > .tile')].map(t => t.dataset.id).join(''), [...document.querySelectorAll('#gridOffered > .tile')].map(t => t.dataset.id).join(''), [...document.querySelectorAll('#gridCompact > .tile')].map(t => t.dataset.id).join(''), document.querySelectorAll('#gridRetired > .tile').length, document.querySelectorAll('#gridRetiredCompact > .tile').length, document.getElementById('retired').open]),
    ['yz', 'xvutkp', 'lo', 14, 2, false], 'L1 the six full cards (X first) and two compact boxes offered; sixteen retired looks and the two proposals not chosen folded away');
  eq([await lab.$$eval('#gridOffered .tile[data-id=x] .badge', n => n.map(x => x.textContent)), await lab.$$eval('#gridNotChosen .badge', n => n.map(x => x.textContent))], [['DEFAULT'], ['NOT CHOSEN', 'NOT CHOSEN']], 'L2 X is marked as the default, and the other two proposals as not chosen');
  ok(await lab.evaluate(() => [...document.querySelectorAll('.card[data-prop]')].length === 3 && [...document.querySelectorAll('.card[data-prop]')].every(c => c.querySelector('.ribbon5 svg[role=img]') && /^Calendar ribbon\. Goal Sat Oct 3 \(a weekend day\); forecast Sat Oct 17 \(a weekend day\); 14 days apart\.$/.test(c.querySelector('.ribbon5 svg').getAttribute('aria-label')) && c.querySelectorAll('.chip').length === 4 && c.querySelector('.chip').textContent === 'Add a note · 3 notes')), 'L3 each keeps the calendar from U, in a picture and in words, and the same actions at the bottom');
  const states = [['rec', 'calm', '79 days to the goal date'], ['21', 'near', '21 days to the goal date'], ['5', 'soon', '5 days to the goal date'], ['0', 'today', 'The goal date is today'], ['-3', 'passed', 'The goal date passed 3 days ago']];
  for (const [d, lv, words] of states) {
    await lab.click(`[data-days="${d}"]`);
    const r = await lab.evaluate(() => ({ x: [document.querySelector('.card[data-prop=x] .cd5').className, document.querySelector('.card[data-prop=x] .cd5').getAttribute('aria-label'), document.querySelector('.card[data-prop=x] .cd5').textContent.replace(/\s+/g, ' ').trim()],
      y: [document.querySelector('.card[data-prop=y] .cl5').className, document.querySelector('.card[data-prop=y] .cl5 svg').getAttribute('aria-label'), document.querySelector('.card[data-prop=y] .cl5 .rt').textContent.replace(/\s+/g, ' ').trim()],
      z: [document.querySelector('.card[data-prop=z] .hero5').className, document.querySelector('.card[data-prop=z] .hero5 .hl').textContent.replace(/\s+/g, ' ').trim()],
      wide: [...document.querySelectorAll('.card[data-prop=x]')].some(c => c.scrollWidth > c.clientWidth + 1) }));
    ok(r.x[0].includes('lv-' + lv) && r.x[1] === words && r.y[0].includes('lv-' + lv) && r.y[1].endsWith(words + '.') && r.z[0].includes('lv-' + lv) && !r.wide, `L4 (${d}) all three proposals are at the “${lv}” level and say “${words}” (X: ${r.x[2]} · Y: ${r.y[2]} · Z: ${r.z[1]})`);
    await lab.addScriptTag({ path: AXE }).catch(() => {});
    eq(await lab.evaluate(async () => (await window.axe.run(document.querySelector('#gridOffered .tile[data-id=x]'))).violations.map(v => v.id + ' ' + v.nodes.length + ' ' + v.nodes[0].target.join(' '))), [], `L5 (${d}) accessibility scan of the default tile, contrast included`);
  }
  await lab.click('[data-days="rec"]'); await lab.click('[data-rec="214990"]');
  eq(await lab.evaluate(() => [document.querySelector('.card[data-prop=x] .cd5').className.includes('lv-near'), document.querySelector('.card[data-prop=x] .cd5').getAttribute('aria-label'), document.querySelector('.card[data-prop=z] .fpill').textContent, document.querySelector('.card[data-prop=z] .fpill').className]), [true, '30 days to the goal date', 'Forecast 9 days earlier', 'fpill good'], 'L6 the on-track example, 30 days out, is at the amber level and says the forecast is earlier');
  eq(await lab.evaluate(() => [[31, 30, 8, 7, 1, 0, -1].map(levelOf), [1, 0, -1].map(cdWords)]), [['calm', 'near', 'near', 'soon', 'soon', 'today', 'passed'], ['1 day to the goal date', 'The goal date is today', 'The goal date passed 1 day ago']], 'L7 the levels change at 30 days, at 7 days, on the day and after it; one day is singular');
  ok(await lab.evaluate(() => { document.getElementById('retired').open = true; return [...document.querySelectorAll('#gridRetired .tile .nm')].map(x => x.textContent).join('|') === ['W · Graphite Gauge', 'A · Quiet Fluent', 'B · Docket Ledger', 'C · Signal', 'D · Draft', 'E · Instrument', 'F · Agency Bronze', 'G · Harbor Teal', 'H · Sage Clinical', 'I · Indigo Dossier', 'J · Cobalt Timeline', 'Q · Bronze Folder', 'R · Bronze Rail', 'S · Bronze Tag'].join('|'); }), 'L8 the retired looks can still be opened and seen, by name');
  for (const w of [390, 768, 1920]) { await lab.setViewportSize({ width: w, height: 900 }); await lab.waitForTimeout(100); ok(await lab.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `L9 the Lab does not scroll sideways at ${w} px`); }
  await lab.close();
  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
