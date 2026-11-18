// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 22 in the browser simulation: the new default look (W · Graphite Gauge), the thinner taskbar, a tray icon that carries amber
// only when something needs the person today, and the faults the review found. Attack cases are mixed in.
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
    await p.goto(opts.live || opts.at ? FILE : SIM); await p.waitForTimeout(300);
    if (opts.at) await p.clock.runFor(1100);
    await p.dblclick('#deskIcon'); await p.waitForTimeout(120);
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const txt = (p, s) => p.$eval(s, e => e.textContent);
  const openFull = async (p, id) => { await p.evaluate(i => openCard(i, { anchor: { x: 430, y: 60 }, forceFull: true }), id); await settle(p, 450); };
  const openPeek = async (p, id) => { await p.evaluate(i => openCard(i, { anchor: { x: 430, y: 60 }, mini: true }), id); await settle(p, 450); };
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(s ? document.querySelector(s) : document)).violations.map(v => v.id + ': ' + v.nodes.length + ' ' + v.nodes.slice(0, 2).map(n => n.target.join(' ')).join(' | ')), sel); };
  const tray = p => p.evaluate(() => ({ dot: document.querySelector('#appIcon .glyph').classList.contains('alert'), fly: document.querySelector('#flyIcon .glyph').classList.contains('alert'), title: document.getElementById('appIcon').title, aria: document.getElementById('appIcon').getAttribute('aria-label'), why: alertReasons().map(r => r.kind + '|' + r.text) }));
  const pin = async p => { await p.click('#chev'); await p.click('#pinBtn2'); await settle(p, 150); await p.mouse.click(700, 330); await settle(p, 100); };

  // (The section on W · Graphite Gauge as the default was removed in round 24, when X took its place; suite 33 checks the default look.)
  let p;

  // ====================================================== T. the thinner taskbar
  p = await fresh();
  const tb = await p.evaluate(() => { const t = document.getElementById('taskbar').getBoundingClientRect(), inside = s => [...document.querySelectorAll(s)].every(e => { const r = e.getBoundingClientRect(); return r.width === 0 || (r.top >= t.top - .5 && r.bottom <= t.bottom + .5); });
    return { h: Math.round(t.height), bottom: Math.round(t.bottom) === innerHeight, kids: inside('#taskbar button, #taskbar .find, #taskbar .wx, #simClockTime, #simClockDate'), clock: [document.getElementById('simClockTime').getBoundingClientRect().height > 0, document.getElementById('simClockDate').getBoundingClientRect().height > 0], tap: [...document.querySelectorAll('#taskbar button')].filter(e => e.getBoundingClientRect().width > 0).map(e => Math.round(e.getBoundingClientRect().height)) }; });
  eq([tb.h, tb.bottom], [40, true], 'T1 the taskbar is 40 px high (it was 48) and sits at the bottom edge');
  ok(tb.kids && tb.clock[0] && tb.clock[1], 'T2 its buttons, the search box, the weather and both lines of the clock fit inside it');
  ok(tb.tap.every(h => h >= 24), `T3 every taskbar button is still at least 24 px high (${[...new Set(tb.tap)].join(', ')})`);
  await p.click('#appIcon', { button: 'right' }).catch(async () => { await pin(p); await p.click('#appIcon', { button: 'right' }); }); await settle(p, 250);
  ok(await p.evaluate(() => { const m = document.getElementById('menu').getBoundingClientRect(), t = document.getElementById('taskbar').getBoundingClientRect(); return m.height > 0 && m.top >= 0 && m.bottom <= t.top + 1 && m.right <= innerWidth; }), 'T4 the tray menu opens above the taskbar, wholly on screen');
  await p.keyboard.press('Escape'); await settle(p, 150);
  await p.evaluate(() => openCard('216482', { anchor: { x: 900, y: 700 }, forceFull: true })); await settle(p, 450);
  ok(await p.evaluate(() => card.getBoundingClientRect().bottom <= document.getElementById('taskbar').getBoundingClientRect().top + 1), 'T5 a card opened low on the screen stays above the taskbar');
  await p.keyboard.press('Escape'); await settle(p, 250);
  eq(await axe(p, '#taskbar'), [], 'T6 accessibility scan of the taskbar');
  await p.close();
  p = await fresh({ live: true });
  ok(await p.evaluate(() => { const t = document.getElementById('taskbar').getBoundingClientRect(); return ['simClockTime', 'simClockDate'].every(i => { const r = document.getElementById(i).getBoundingClientRect(); return r.top >= t.top && r.bottom <= t.bottom; }) && /^\d{1,2}:\d\d:\d\d [AP]M$/.test(document.getElementById('simClockTime').textContent); }), 'T7 the live clock, to the second, fits the thinner bar');
  await p.close();

  // ====================================================== A. the tray icon: amber only when something needs the person today
  p = await fresh(); await pin(p);
  let t = await tray(p);
  eq([t.dot, t.fly, t.why, t.title, /attention/.test(t.aria)], [false, false, [], 'ANDA Companion (Notula) · OFFLINE · no API connected', false], 'A1 at rest: no dot on the icon (or on its copy behind the ^ arrow), nothing to report, and neither the tooltip nor the spoken label mentions attention');
  eq(await p.evaluate(() => { const bg = s => getComputedStyle(document.querySelector(s)).backgroundImage; const amber = x => /f2a93b/i.test(decodeURIComponent(x)); document.querySelector('#appIcon .glyph').classList.add('alert'); const on = bg('#appIcon .glyph'); document.querySelector('#appIcon .glyph').classList.remove('alert'); return [amber(bg('#appIcon .glyph')), amber(bg('#flyIcon .glyph')), amber(on)]; }), [false, false, true], 'A2 the resting icon has no amber in it at all; amber is drawn only with the dot');
  eq(await p.evaluate(() => [DATA['216482'].attn, DATA['216482'].delta > 0, isUrgent('216482')]), ['IR response due in 5 days — Biopharmaceutics', true, true], 'A3 (the sample ANDA has an open item due in 5 days and a late forecast, which used to light the dot)');
  await p.check('#swDue'); await settle(p, 150); t = await tray(p);
  eq([t.dot, t.fly, t.why, t.title], [true, true, ['due|ANDA 216482: IR response due today — Biopharmaceutics'], 'ANDA Companion (Notula) · OFFLINE · no API connected · 1 thing needs your attention today'], 'A4 an item due today lights the dot, on both copies of the icon, and the tooltip counts it');
  ok(/1 thing needs your attention today\./.test(t.aria), 'A5 a screen reader hears the same');
  ok(!/\d{6}/.test(t.title) && !/\d{6}/.test(t.aria), 'A6 neither names an ANDA: a tooltip can be read over a shoulder');
  await p.click('#appIcon', { button: 'right' }); await settle(p, 250);
  ok((await p.$$eval('#menu *', n => n.map(x => x.textContent))).some(x => x === 'News and alerts… (1 today)'), 'A7 the tray menu says how many, on the line that explains them');
  await p.keyboard.press('Escape'); await settle(p, 150);
  await p.evaluate(() => openHub()); await settle(p, 300);
  eq(await p.$$eval('#hubNow .hrow', n => n.map(x => x.textContent)), ['ANDA 216482: IR response due today — Biopharmaceutics'], 'A8 News and alerts lists the reason first, under “Needs you today”');
  await p.click('#hubNow button'); await settle(p, 600);
  eq(await p.evaluate(() => [current, card.style.display]), ['216482', 'block'], 'A9 pressing the reason opens that ANDA');
  await p.keyboard.press('Escape'); await settle(p, 250);
  await p.uncheck('#swDue'); await settle(p, 150); t = await tray(p);
  eq([t.dot, t.fly, t.why, await p.evaluate(() => JSON.stringify(DATA['216482'].attnDue) === JSON.stringify(shiftYMD ? DATA['216482'].attnDue : null))], [false, false, [], true], 'A10 switching the what-if off clears the dot');
  await p.evaluate(() => openHub()); await settle(p, 250); await p.check('#swDue'); await settle(p, 150);
  eq(await p.$$eval('#hubNow .hrow', n => n.length), 1, 'A11a News and alerts follows a change while it is open');
  await p.uncheck('#swDue'); await settle(p, 150);
  eq(await p.$$eval('#hubNow .hrow', n => n.map(x => x.textContent.slice(0, 55))), ['Nothing needs you today, so the tray icon shows no dot.'], 'A11 and when nothing is left it says so, with the rule in plain words');
  await p.evaluate(() => document.getElementById('hubClose').click()); await settle(p, 150);
  // the edges of "today"
  const day = n => p.evaluate(k => { const x = new Date(demoToday() + k * 864e5); return [x.getUTCFullYear(), x.getUTCMonth() + 1, x.getUTCDate()]; }, n);
  const withData = async (id, patch) => { await p.evaluate(([i, pt]) => { window.__keep = window.__keep || {}; window.__keep[i] = window.__keep[i] || { attnDue: DATA[i].attnDue, goalD: DATA[i].goalD, alerts: DATA[i].alerts }; for (const k of Object.keys(pt)) { if (pt[k] === null) delete DATA[i][k]; else DATA[i][k] = pt[k]; } refreshTrayState(); }, [id, patch]); return tray(p); };
  const restore = () => p.evaluate(() => { for (const i of Object.keys(window.__keep || {})) { for (const k of ['attnDue', 'goalD', 'alerts']) { if (window.__keep[i][k] === undefined) delete DATA[i][k]; else DATA[i][k] = window.__keep[i][k]; } } window.__keep = {}; refreshTrayState(); });
  eq((await withData('216482', { attnDue: await day(1) })).dot, false, 'A12 an item due tomorrow does not light the dot');
  eq((await withData('216482', { attnDue: await day(0) })).dot, true, 'A13 due today does');
  eq((await withData('216482', { attnDue: await day(-1) })).dot, true, 'A14 and so does one day overdue');
  await restore();
  eq((await withData('214990', { goalD: await day(1) })).dot, false, 'A15 a goal date tomorrow does not');
  eq((await withData('214990', { goalD: await day(0) })).why, ['goal|ANDA 214990: the goal date is today'], 'A16 a goal date today does, and says so');
  eq((await withData('214990', { goalD: await day(-3) })).why, ['goal|ANDA 214990: the goal date has passed'], 'A17 a goal date that has passed does');
  await restore();
  await p.check('#swToday'); await settle(p, 150);
  eq((await tray(p)).why, ['goal|ANDA 214990: the goal date has passed', 'due|ANDA 216482: IR response was due 72 days ago — Biopharmaceutics'], 'A18 with the calendar moved forward, what would be overdue by then lights the dot and is named as overdue');
  await p.uncheck('#swToday'); await settle(p, 150);
  // a mark in the person's sheet: a service issue, a safety issue, an immediate risk
  await p.check('#swFlag'); await settle(p, 150); t = await tray(p);
  eq([t.dot, t.why], [true, ['flag|ANDA 214990: Customer service: the applicant has asked twice for a status and has had no answer (made-up sample)']], 'A19 an ANDA the sheet marks as a service issue lights the dot');
  await openFull(p, '214990');
  ok(/^Marked in your sheet: Customer service:/.test(await txt(p, '#c-watch')) && await p.$eval('#c-watch', e => e.classList.contains('bad')), 'A20 its card shows the mark first in the watch row, in the strongest color');
  await p.evaluate(() => { card.classList.add('expanded'); document.getElementById('x-watch-d').open = true; }); await settle(p, 150);
  ok(/Your sheet marks this ANDA as needing you now/.test(await txt(p, '#x-watch')) && /Flag column of the portfolio sheet/.test(await txt(p, '#x-watch')), 'A21 the watch list says why and where it came from');
  eq(await axe(p, '#card'), [], 'A22 accessibility scan of the card with the mark');
  await p.keyboard.press('Escape'); await settle(p, 250);
  await p.uncheck('#swFlag'); await settle(p, 150);
  eq([(await tray(p)).dot, await p.evaluate(() => 'alerts' in DATA['214990'])], [false, false], 'A23 removing the mark clears the dot and leaves the record as it was');
  // hostile and malformed marks
  const evil = '<img src=x onerror="window.__pwned=1"><script>window.__pwned=2</script> ' + 'x'.repeat(400);
  t = await withData('214990', { alerts: [evil] });
  await p.evaluate(() => openHub()); await settle(p, 250); await openFull(p, '214990');
  await p.evaluate(() => { card.classList.add('expanded'); document.getElementById('x-watch-d').open = true; }); await settle(p, 200);
  eq(await p.evaluate(() => [window.__pwned === undefined, document.querySelectorAll('#hub img, #hub script, #card img, #card script').length, document.querySelector('#hubNow .hrow').textContent.includes('<img src=x'), document.getElementById('x-watch').textContent.includes('<img src=x'), alertReasons()[0].text.length <= 136]), [true, 0, true, true, true], 'A24 a mark that carries markup is shown as plain text everywhere, runs nothing, and is cut to a readable length');
  await p.keyboard.press('Escape'); await settle(p, 250); await p.evaluate(() => document.getElementById('hubClose').click());
  for (const [bad, name] of [['a string', 'a string'], [{ a: 1 }, 'an object'], [42, 'a number'], [[], 'an empty list']]) eq((await withData('214990', { alerts: bad })).dot, false, `A25 a mark that is ${name} is ignored without an error`);
  await restore();
  // an urgent message with no reply
  eq((await tray(p)).dot, false, 'A26 with Outlook not connected, mail can never light the dot');
  await p.evaluate(() => { const o = state.outlook; o.on = true; o.client = '12345678-1234-1234-1234-123456789abc'; o.tenant = 'organizations'; o.step = 'out'; paintOutlook(); });
  eq((await tray(p)).dot, false, 'A27 switched on but not signed in: still no dot');
  await p.evaluate(() => { state.outlook.step = 'in'; paintOutlook(); }); t = await tray(p);
  eq([t.dot, t.why], [true, ['mail|ANDA 216482: a message marked urgent has had no reply for 3 days']], 'A28 signed in (rehearsal): a message marked urgent with no reply for 3 days lights it');
  await openFull(p, '216482'); await p.evaluate(() => { card.classList.add('expanded'); document.getElementById('x-mail-d').open = true; }); await settle(p, 200);
  ok(/^URGENT: information request response \(marked urgent · no reply for 3 days\)/.test(await p.$eval('#x-mail', e => e.textContent.trim())), 'A29 that message is first in the card’s mail list, labelled');
  await p.keyboard.press('Escape'); await settle(p, 250);
  const mail = patch => p.evaluate(pt => { window.__m = window.__m || { ...SAMPLE_URGENT_MAIL }; Object.assign(SAMPLE_URGENT_MAIL, window.__m, pt); refreshTrayState(); return document.querySelector('#appIcon .glyph').classList.contains('alert'); }, patch);
  eq([await mail({ daysOld: 1 }), await mail({ daysOld: 2 }), await mail({ daysOld: 2, answered: true }), await mail({ id: '999999' }), await mail({})], [false, true, false, false, true], 'A30 one day is too soon, two days is the line, an answered message and one about an unknown ANDA never count');
  await p.check('#swDue'); await settle(p, 150); t = await tray(p);
  ok(t.why.length === 2 && / · 2 things need your attention today$/.test(t.title) && /2 things need your attention today\./.test(t.aria), 'A31 two reasons are counted as two, in plain grammar');
  await p.click('#appIcon', { button: 'right' }); await settle(p, 250);
  ok((await p.$$eval('#menu *', n => n.map(x => x.textContent))).some(x => x === 'News and alerts… (2 today)'), 'A32 and the menu agrees');
  await p.keyboard.press('Escape'); await settle(p, 150);
  await p.evaluate(() => openHub()); await settle(p, 300);
  eq(await axe(p, '#hub'), [], 'A33 accessibility scan of News and alerts with two reasons');
  await p.evaluate(() => document.getElementById('hubClose').click());
  await p.evaluate(() => { document.getElementById('olOn') && (state.outlook.on = false, state.outlook.step = 'out', paintOutlook()); }); await p.uncheck('#swDue'); await settle(p, 150);
  eq((await tray(p)).dot, false, 'A34 switching Outlook off clears the mail reason');
  // the tooltip is written in one place: the connection and the count never overwrite each other
  await p.check('#swAi'); await p.check('#swDue'); await settle(p, 150);
  eq((await tray(p)).title, 'ANDA Companion (Notula) · SIMULATED CONNECTION · sample text, not a real API · 1 thing needs your attention today', 'A35 the tooltip carries the connection and the count together');
  await p.uncheck('#swAi'); await settle(p, 100);
  eq((await tray(p)).title, 'ANDA Companion (Notula) · OFFLINE · no API connected · 1 thing needs your attention today', 'A36 a change of connection keeps the count');
  await p.uncheck('#swDue'); await settle(p, 100);
  eq((await tray(p)).title, 'ANDA Companion (Notula) · OFFLINE · no API connected', 'A37 and clearing the count keeps the connection');
  eq(await axe(p), [], 'A38 accessibility scan of the whole desktop');
  await p.close();

  // "today" changes at midnight while the page is open
  p = await fresh({ at: '2026-11-12T23:59:40-05:00' }); await pin(p);
  await p.evaluate(() => { const x = new Date(demoToday() + 864e5); DATA['216482'].attnDue = [x.getUTCFullYear(), x.getUTCMonth() + 1, x.getUTCDate()]; refreshTrayState(); });
  eq([(await tray(p)).dot, await p.evaluate(() => DATA['216482'].attn)], [false, 'IR response due in 1 day — Biopharmaceutics'], 'A39 twenty seconds before midnight an item due tomorrow shows no dot');
  await p.clock.runFor(30000); await settle(p, 100);
  t = await tray(p);
  eq([t.dot, t.why, await txt(p, '#simClockDate')], [true, ['due|ANDA 216482: IR response due today — Biopharmaceutics'], '11/13/2026'], 'A40 after midnight it is due today, and the dot appears without anything being pressed');
  await p.close();

  // a what-if is never a switch with no visible effect: with the icon behind the ^ arrow, a notice says where the dot is
  p = await fresh();
  await p.check('#swDue'); await settle(p, 250);
  const note = await p.evaluate(() => { const c = document.getElementById('chip'), r = c.getBoundingClientRect(); return { text: document.getElementById('chipTxt').textContent, on: r.width > 0 && r.left >= 0 && r.right <= innerWidth && r.bottom <= document.getElementById('taskbar').getBoundingClientRect().top, pinned: state.pinned }; });
  eq([note.pinned, note.on, note.text], [false, true, 'The tray icon now shows its dot. The icon is behind the ^ arrow near the clock: open the arrow to see it, or pin the icon to the taskbar.'], 'A41 with the icon behind the ^ arrow, switching the what-if on says where the dot is');
  await p.click('#chev'); await settle(p, 200);
  ok(await p.evaluate(() => document.querySelector('#flyIcon .glyph').classList.contains('alert') && getComputedStyle(document.getElementById('flyIcon')).display !== 'none'), 'A42 and behind the arrow the icon does show it');
  await p.click('#pinBtn2'); await settle(p, 200); await p.uncheck('#swDue'); await p.check('#swFlag'); await settle(p, 250);
  eq(await txt(p, '#chipTxt'), 'The tray icon now shows its dot. Right-click it, then News and alerts, to see why.', 'A43 with the icon on the taskbar, the notice says how to see why');
  await p.close();

  // ====================================================== R. what the review found
  p = await fresh(); await openFull(p, '216482');
  ok(/0\.97\)$/.test(await p.$eval('.chips-slot', e => getComputedStyle(e).backgroundColor)) || /color-mix|0\.97/.test(await p.$eval('.chips-slot', e => getComputedStyle(e).backgroundColor)), 'R1 the action area is nearly solid, so text scrolling under it does not show through');
  await p.close();
  const lab = await (await b.newContext({ viewport: { width: 1366, height: 900 } })).newPage(); lab.on('pageerror', e => errs.push('lab: ' + e.message));
  await lab.goto(LAB); await lab.waitForSelector('.tile[data-id="w"]', { state: 'attached' });      /* W is folded away since round 24: it is on the page, not in view */
  const L = await lab.evaluate(() => ({ first: document.querySelector('#gridOffered .tile').dataset.id, badges: [...document.querySelectorAll('.badge.new')].filter(x => x.textContent === 'DEFAULT').map(x => x.closest('.tile').dataset.id), full: document.querySelectorAll('.tile article.card').length, lede: document.querySelector('.lede').textContent, firstChip: document.querySelector('.tile[data-id="w"] .chip').textContent, gauge: !!document.querySelector('.tile[data-id="w"] .hg5'), vGauge: !!document.querySelector('.tile[data-id="v"] .hg5') }));
  eq([L.first, L.badges, L.full, L.gauge, L.vGauge, L.firstChip], ['x', ['x'], 22, true, false, 'Add a note · 3 notes'], 'R2 the Appearance Lab shows one default first (X since round 24), keeps W with its gauge, and shows today’s main action');
  ok(/^Eight looks are offered.*six full cards and two compact boxes.*X \(Graphite Calendar\) is the default.*The sixteen looks that are no longer offered are folded at the end/.test(L.lede), 'R3 and its introduction counts them correctly');
  eq(await (async () => { await lab.addScriptTag({ path: AXE }); return lab.evaluate(async () => (await window.axe.run(document.querySelector('.tile[data-id="x"]'))).violations.map(v => v.id)); })(), [], 'R4 accessibility scan of the default tile in the Lab');
  await lab.close();
  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
