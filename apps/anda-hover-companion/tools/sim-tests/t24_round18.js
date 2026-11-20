// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 18 in the browser simulation: the Loupe icon, the new default look (Pocket Plain), Calendar Ribbon and Glass Sheet, the pinned Copy status,
// Enter to copy, text size and spacing, recent and kept ANDAs, One Bar, Today, the tray state, the Appearance Lab, and the attack cases.
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const LAB = SIMURL + '/mockup/appearance-lab.html';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);
const lum = hex => { const c = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255).map(v => v <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4); return .2126 * c[0] + .7152 * c[1] + .0722 * c[2]; };
const contrast = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + .05) / (y + .05); };
(async () => {
  const b = await chromium.launch();
  const errs = [];
  const fresh = async (opts = {}) => {
    const ctx = await b.newContext({ viewport: opts.viewport || { width: 1366, height: 800 }, permissions: ['clipboard-read', 'clipboard-write'] });
    const p = await ctx.newPage();
    p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error') errs.push('console: ' + m.text()); });
    await p.goto(SIM + (opts.all ? '#all' : '')); await p.waitForTimeout(300);
    if (opts.launch !== false) { await p.dblclick('#deskIcon'); await p.waitForTimeout(120); }
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const vis = (p, s) => p.isVisible(s);
  const txt = (p, s) => p.$eval(s, e => e.textContent);
  const openCardFull = async (p, id) => { await p.evaluate(i => openCard(i, { anchor: { x: 520, y: 80 }, forceFull: true }), id); await settle(p, 450); };
  const openSettings = async (p, ...sections) => { await p.evaluate(() => showApp('settings')); await settle(p, 80); for (const s of sections) await p.evaluate(id => { document.getElementById(id).open = true; }, s); await settle(p, 80); };

  // ====================================================== A. the Loupe icon
  let p = await fresh({ launch: false });
  const g = await p.evaluate(() => { const e = document.querySelector('#appIcon .glyph'); const cs = getComputedStyle(e); return { bg: cs.backgroundImage, w: cs.width }; });
  ok(g.bg.startsWith('url("data:image/svg+xml') && g.bg.includes('%233a3f8f') && g.bg.includes('circle'), 'A1 the tray glyph is the Loupe drawing (indigo plate, ring)');
  eq(g.w, '18px', 'A2 it is drawn at 18 px in the tray');
  ok(await p.evaluate(() => getComputedStyle(document.querySelector('#flyIcon .glyph')).width === '28px'), 'A3 and at 28 px in the overflow flyout');
  const alertImg = await p.evaluate(() => { const e = document.querySelector('#appIcon .glyph'); e.classList.add('alert'); const v = getComputedStyle(e).backgroundImage; e.classList.remove('alert'); return v; });
  ok(alertImg.includes('%23f2a93b') && alertImg.includes('r=%2211%22'), 'A4 the alert state adds the small amber dot with a white ring');
  await p.close();

  // ====================================================== B. Pocket Plain is the default and leads with one answer
  p = await fresh();
  eq(await p.evaluate(() => document.documentElement.dataset.dir), 'x', 'B1 the focused edition opens in the default look (X · Graphite Calendar since round 24)'); await p.evaluate(() => applyLook('t'));
  await openCardFull(p, '216482');
  eq([await txt(p, '#c-hero-n'), (await txt(p, '#c-hero-u')).trim()], ['14', 'days later than the goal'], 'B2 a late ANDA says how many days later');
  eq(await txt(p, '#c-hero-s'), 'Goal Oct 3, 2026 · Forecast Oct 17, 2026 · moderate confidence', 'B3 the line under it gives the goal, the forecast and the confidence');
  ok(await p.evaluate(() => getComputedStyle(document.getElementById('c-hero-n')).color === 'rgb(138, 88, 0)'), 'B4 a late number is in the warning colour');
  ok(!(await vis(p, '.clockwrap > .slot.clock')), 'B5 the older goal and forecast rows are not drawn (the same facts are in the hero)');
  ok(await vis(p, '#attnSlot'), 'B6 what needs you is still shown');
  await p.keyboard.press('Escape'); await settle(p, 100);
  await openCardFull(p, '214990');
  eq([await txt(p, '#c-hero-n'), (await txt(p, '#c-hero-u')).trim()], ['9', 'days ahead of the goal'], 'B7 an ANDA ahead of its goal says days ahead');
  ok(await p.evaluate(() => getComputedStyle(document.getElementById('c-hero-n')).color === 'rgb(26, 107, 67)'), 'B8 in the good colour');
  await p.evaluate(() => { DATA['214990'].delta = 0; fill('214990'); });
  eq([await txt(p, '#c-hero-n'), (await txt(p, '#c-hero-u')).trim()], ['On', 'the goal date'], 'B9 a forecast on the goal date says so');
  await p.evaluate(() => { DATA['214990'].delta = 1; fill('214990'); });
  eq((await txt(p, '#c-hero-u')).trim(), 'day later than the goal', 'B10 one day is singular');
  await p.evaluate(() => { DATA['214990'].delta = -9; fill('214990'); });

  // the primary action is pinned and carries its key
  const pin = await p.evaluate(() => { const s = document.querySelector('.chips-slot'), c = document.getElementById('chipNotes'); return { pos: getComputedStyle(s).position, after: getComputedStyle(c, '::after').content, bg: getComputedStyle(c).backgroundColor, order: getComputedStyle(c).order }; });
  eq(pin.pos, 'sticky', 'B11 the action row stays at the bottom of the card while the rest scrolls');
  ok(pin.after.includes('Enter'), 'B12 the main action, Add a note since round 21, shows its key (Enter)');
  ok(/rgba?\(63, 74, 86, 0\.1\)|color\(srgb 0\.24\d* 0\.29\d* 0\.33\d* \/ 0\.1\)/.test(pin.bg), `B13 and is a tinted button, no longer a solid bar (${pin.bg})`);
  const inView = await p.evaluate(() => { const c = document.getElementById('chipNotes').getBoundingClientRect(), k = document.getElementById('card').getBoundingClientRect(); return c.bottom <= k.bottom + 1 && c.top >= k.top; });
  ok(inView, 'B14 the main action is on screen without scrolling');

  // S copies the status line (Enter did until round 21; it now adds a note)
  await p.evaluate(() => document.getElementById('card').focus());
  await p.keyboard.press('s'); await settle(p, 200);
  eq(await p.evaluate(() => navigator.clipboard.readText()), await p.evaluate(() => statusLine('214990')), 'B15 S copies the same status line as the button');
  ok((await txt(p, '#chipCopy')).includes('Copied'), 'B16 and the button says so');
  await p.keyboard.press('Escape'); await settle(p, 100);

  // an older look still shows the older layout
  await p.evaluate(() => { state.allTools = true; applyEdition(); applyLook('k'); }); await openCardFull(p, '216482');
  ok(!(await vis(p, '#c-hero')) && (await vis(p, '.clockwrap > .slot.clock')), 'B17 an older look (K) still shows the goal and forecast rows and no hero');
  ok(await p.evaluate(() => getComputedStyle(document.querySelector('.chips-slot')).position !== 'sticky'), 'B18 and an unpinned action row');
  await p.close();

  // ====================================================== C. Calendar Ribbon
  p = await fresh({ all: true });
  const names = await p.$$eval('input[name=look]', n => n.map(r => r.parentElement.textContent.trim()));
  ok(names.slice(0, 4).join('|') === 'X · Graphite Calendar|V · Glass Sheet|U · Calendar Ribbon|T · Pocket Plain' && names.length === 8, 'C1 Settings lists the four newest looks first (the default leading), 8 in all');
  await p.evaluate(() => applyLook('u')); await openCardFull(p, '216482');
  await p.evaluate(() => toggleExpand()); await settle(p, 150);   // round 34: the ribbon sits under More
  ok(await vis(p, '#c-ribbon-slot'), 'C2 the ribbon is shown in Calendar Ribbon');
  eq(await txt(p, '#c-ribbon-t'), 'Goal Sat Oct 3 (a weekend day); forecast Sat Oct 17 (a weekend day); 14 days apart.', 'C3 a sentence says the same in words');
  const rb = await p.evaluate(() => { const s = document.getElementById('c-ribbon'); return { label: s.getAttribute('aria-label'), text: [...s.querySelectorAll('text')].map(t => t.textContent), poly: s.querySelectorAll('polygon').length, circ: s.querySelectorAll('circle').length, gx: +s.querySelector('polygon').getAttribute('points').split(' ')[0].split(',')[0], fx: +s.querySelector('circle').getAttribute('cx'), rects: s.querySelectorAll('rect').length }; });
  ok(rb.label.startsWith('Calendar ribbon.') && rb.label.includes('14 days apart'), 'C4 the picture has a text equivalent for a screen reader');
  ok(rb.text.includes('Goal') && rb.text.includes('Forecast') && rb.text.includes('Columbus Day'), 'C5 it names the goal, the forecast and the federal holiday');
  ok(rb.poly === 1 && rb.circ === 1 && rb.gx < rb.fx, 'C6 a late forecast sits to the right of the goal');
  ok(rb.rects >= 8, 'C7 weekends are shaded');
  await p.keyboard.press('Escape'); await settle(p, 80);
  await openCardFull(p, '214990');
  eq(await txt(p, '#c-ribbon-t'), 'Goal Sat Aug 15 (a weekend day); forecast Thu Aug 6; 9 days apart.', 'C8 an earlier forecast reads correctly');
  ok(await p.evaluate(() => +document.querySelector('#c-ribbon circle').getAttribute('cx') < +document.querySelector('#c-ribbon polygon').getAttribute('points').split(' ')[0].split(',')[0]), 'C9 and sits left of the goal');
  const lbls = await p.evaluate(() => [...document.querySelectorAll('#c-ribbon text')].map(t => { const r = t.getBoundingClientRect(); return [r.left, r.right]; }));
  let overlap = false; for (let i = 0; i < lbls.length; i++) for (let j = i + 1; j < lbls.length; j++) if (lbls[i][0] < lbls[j][1] - 1 && lbls[j][0] < lbls[i][1] - 1) { const a = lbls[i], c = lbls[j]; if (Math.abs(a[0] - c[0]) < 40 && Math.abs(a[1] - c[1]) < 40) overlap = true; }
  ok(!overlap, 'C10 no two labels sit on top of each other');
  // far apart: more than fourteen weeks falls back to the sentence alone
  const far = await p.evaluate(() => { DATA['214990'].fcD = [2027, 2, 1]; fill('214990'); const r = { n: document.getElementById('c-ribbon').children.length, t: document.getElementById('c-ribbon-t').textContent }; DATA['214990'].fcD = [2026, 8, 6]; fill('214990'); return r; });
  ok(far.n === 0 && /apart/.test(far.t), 'C11 dates more than fourteen weeks apart show the sentence only');
  // across a year end: a goal on Dec 28 and a forecast on Jan 8 of the next year
  await p.evaluate(() => closeCard());
  await settle(p, 200);
  const ye = await p.evaluate(async () => {
    const d = DATA['216482']; const keep = JSON.stringify([d.goalD, d.fcD, d.delta, d.goal, d.fc]);
    d.goalD = [2026, 12, 28]; d.fcD = [2027, 1, 8]; d.delta = 11; d.goal = 'Dec 28, 2026'; d.fc = 'Jan 8, 2027';
    openCard('216482', { anchor: { x: 520, y: 80 }, forceFull: true });
    await new Promise(r => setTimeout(r, 450));
    const s = document.getElementById('c-ribbon');
    const out = { t: document.getElementById('c-ribbon-t').textContent, names: [...s.querySelectorAll('text')].map(t => t.textContent),
      gx: +s.querySelector('polygon').getAttribute('points').split(' ')[0].split(',')[0], fx: +s.querySelector('circle').getAttribute('cx') };
    [d.goalD, d.fcD, d.delta, d.goal, d.fc] = JSON.parse(keep);
    return out;
  });
  eq(ye.t, 'Goal Mon Dec 28; forecast Fri Jan 8; 11 days apart.', 'C13 across a year end the sentence reads correctly');
  ok(ye.names.includes('Christmas Day') && ye.names.includes("New Year's Day") && ye.gx < ye.fx, 'C14 and the ribbon names both holidays, the goal to the left of the forecast');
  await p.evaluate(() => closeCard()); await settle(p, 200);
  await openCardFull(p, '216482');
  await p.evaluate(() => { closeCard(); openCard('216482', { anchor: { x: 520, y: 80 }, mini: true }); }); await settle(p, 350);
  ok(!(await vis(p, '#c-ribbon-slot')), 'C12 a peek (small card) leaves the ribbon out');
  await p.close();

  // ====================================================== D. Glass Sheet
  p = await fresh({ all: true });
  await p.evaluate(() => applyLook('v')); await openCardFull(p, '216482');
  const gl = await p.evaluate(() => { const c = getComputedStyle(document.getElementById('card')); return { bf: c.backdropFilter || c.webkitBackdropFilter, bg: c.backgroundColor }; });
  ok(/blur/.test(gl.bf), 'D1 the card blurs what is behind it');
  const alpha = +(gl.bg.match(/[\d.]+/g)[3] ?? 1);
  ok(alpha >= .86, `D2 and is at least 86% opaque (${alpha})`);
  const worst = Math.round(255 * 0.86), hexW = '#' + [worst, worst, worst].map(v => v.toString(16).padStart(2, '0')).join('');
  ok(contrast('#4a525b', hexW) >= 4.5 && contrast('#3c444d', hexW) >= 7, `D3 the label and body text keep their contrast over a black window (${contrast('#4a525b', hexW).toFixed(2)}:1 and ${contrast('#3c444d', hexW).toFixed(2)}:1)`);
  const sheet = await p.evaluate(() => [...document.styleSheets].flatMap(s => [...s.cssRules]).some(r => r.conditionText && /prefers-reduced-transparency/.test(r.conditionText) && /data-glass/.test(r.cssText)));
  ok(sheet, 'D4 it turns solid when the system asks for reduced transparency');
  await p.close();

  // ====================================================== E. text size and spacing
  p = await fresh();
  await openSettings(p, 'secStart');
  const base = await p.evaluate(() => { const s = document.querySelector('#settings'); return { f: parseFloat(getComputedStyle(document.querySelector('#settings .hint')).fontSize) }; });
  await p.check('input[name=ts][value=large]'); await settle(p, 80);
  eq(await p.evaluate(() => document.documentElement.dataset.ts), 'large', 'E1 Larger text sets the size');
  const big = await p.evaluate(() => parseFloat(getComputedStyle(document.querySelector('#settings .hint')).fontSize));
  ok(Math.abs(big / base.f - 1.15) < .02, `E2 text in this app's windows is 15% larger (${base.f} to ${big})`);
  await p.check('input[name=ts][value=xl]'); await settle(p, 80);
  ok(Math.abs(await p.evaluate(() => parseFloat(getComputedStyle(document.querySelector('#settings .hint')).fontSize)) / base.f - 1.3) < .02, 'E3 Largest text is 30% larger');
  await p.check('input[name=ts][value=standard]');
  ok(await p.evaluate(() => !('ts' in document.documentElement.dataset)), 'E4 Standard removes the setting');
  await p.evaluate(() => closeApp('settings')); await openCardFull(p, '216482');
  const pad0 = await p.evaluate(() => parseFloat(getComputedStyle(document.querySelector('#attnSlot')).paddingTop));
  await p.evaluate(() => { document.querySelector('input[name=density][value=compact]').click(); }); await settle(p, 80);
  const pad1 = await p.evaluate(() => parseFloat(getComputedStyle(document.querySelector('#attnSlot')).paddingTop));
  ok(pad1 < pad0 * .8, `E5 Compact spacing tightens the card (${pad0} to ${pad1})`);
  eq(await p.evaluate(() => document.documentElement.dataset.density), 'compact', 'E6 and records the choice');
  // a card at the largest text still fits the work area and keeps its action row in view
  await p.evaluate(() => { document.querySelector('input[name=ts][value=xl]').click(); }); await settle(p, 120);
  const fit = await p.evaluate(() => { const c = document.getElementById('card').getBoundingClientRect(), k = document.getElementById('chipCopy').getBoundingClientRect(); return { top: c.top >= 0, bottom: c.bottom <= innerHeight - 40, copy: k.bottom <= c.bottom + 1 && k.top >= c.top }; });
  ok(fit.top && fit.bottom && fit.copy, 'E7 at the largest text the card stays on screen with Copy status in view');
  await p.close();

  // ====================================================== F. recent and kept ANDAs
  p = await fresh();
  await openCardFull(p, '216482'); await p.keyboard.press('Escape'); await openCardFull(p, '214990'); await p.keyboard.press('Escape');
  eq(await p.evaluate(() => state.recents), ['214990', '216482'], 'F1 opening a card records it, newest first');
  await p.evaluate(() => { for (let i = 0; i < 12; i++) noteRecent(String(100000 + i)); });
  eq(await p.evaluate(() => state.recents.length), 8, 'F2 only the last eight are kept');
  await p.evaluate(() => { state.recents = ['214990', '216482']; openLookup({ x: 400, y: 200 }); }); await settle(p, 100);
  eq(await p.$$eval('#lookupRecent button', n => n.map(x => x.textContent)), ['214990', '216482'], 'F3 the lookup box lists the recents');
  await p.click('#lookupRecent button >> text=216482'); await settle(p, 400);
  ok(await vis(p, '#card') && (await txt(p, '#c-id')) === 'ANDA 216482', 'F4 pressing one opens its card');
  eq(await p.evaluate(() => state.recents[0]), '216482', 'F5 and moves it to the front');
  // keeping an ANDA
  eq(await txt(p, '#chipPin'), 'Keep in my list', 'F6 the card offers "Keep in my list"');
  await p.click('#chipPin'); await settle(p, 60);
  eq([await txt(p, '#chipPin'), await p.getAttribute('#chipPin', 'aria-pressed'), await p.evaluate(() => state.pins)], ['Remove from my list', 'true', ['216482']], 'F7 keeping it changes the label, the pressed state and the list');
  await p.keyboard.press('Escape'); await p.evaluate(() => openLookup({ x: 400, y: 200 })); await settle(p, 80);
  ok(await p.$eval('#lookupRecent button:first-of-type', e => e.classList.contains('kept')) || (await p.$$eval('#lookupRecent button', n => n.some(x => x.classList.contains('kept')))), 'F8 a kept ANDA is marked in the lookup list');
  eq((await p.$$eval('#lookupRecent button', n => n.map(x => x.textContent)))[0], '216482', 'F9 and listed first');
  await p.evaluate(() => { for (const n of ['100001', '100002', '100003', '100004']) state.pins.push(n); });
  await p.evaluate(() => closeApp('lookup')); await openCardFull(p, '214990');
  await p.click('#chipPin'); await settle(p, 80);
  ok(/holds 5 ANDAs/.test(await p.evaluate(() => document.body.innerText)) && (await p.evaluate(() => state.pins.length)) === 5, 'F10 a sixth is refused with a plain message');
  await p.evaluate(() => { state.pins = ['216482']; });
  // settings: clear and off
  await openSettings(p, 'secStart');
  await p.click('#recentsClear'); await settle(p, 50);
  ok((await p.evaluate(() => state.recents.length)) === 0 && /cleared/.test(await txt(p, '#recentsStatus')), 'F11 Clear recents empties the list and says so');
  ok(await p.evaluate(() => state.pins.length) === 1, 'F12 and leaves the kept ANDAs');
  await p.uncheck('#setRecents'); await p.evaluate(() => { closeApp('settings'); noteRecent('216482'); });
  eq(await p.evaluate(() => state.recents), [], 'F13 with remembering off, nothing is recorded');
  await p.close();

  // ====================================================== G. One Bar
  p = await fresh({ all: true });
  await openSettings(p, 'secLook');
  await p.check('input[name=lkstyle][value=bar]'); await p.evaluate(() => closeApp('settings'));
  await p.keyboard.press('Control+Alt+Space'); await settle(p, 120);
  ok(await p.evaluate(() => document.getElementById('lookup').classList.contains('bar5')) && await vis(p, '#lookup'), 'G1 with One Bar chosen the lookup shortcut opens the bar');
  eq((await p.$$eval('#lkSug .lk-row b', n => n.map(x => x.textContent))), ['Review a draft', 'Ask about a selection'], 'G2 with nothing typed it offers Review and Ask');
  await p.keyboard.type('216482'); await settle(p, 80);
  const rows = await p.$$eval('#lkSug .lk-row', n => n.map(r => [r.querySelector('b').textContent, r.querySelector('small').textContent]));
  eq(rows[0], ['ANDA 216482 · Metformin HCl ER Tablets, 500 mg', '14 days later than the goal · needs you: IR response due in 5 days'], 'G3 a typed number gives its card first, with the answer under it');
  eq(await p.$eval('#lkSug .lk-row', e => e.getAttribute('aria-selected')), 'true', 'G4 and that row is selected');
  await p.keyboard.press('ArrowDown'); await p.keyboard.press('ArrowDown'); await p.keyboard.press('ArrowUp');
  eq(await p.$$eval('#lkSug .lk-row', n => n.map(r => r.getAttribute('aria-selected'))), ['false', 'true', 'false'], 'G5 the arrow keys move the choice');
  await p.keyboard.press('ArrowUp'); await p.keyboard.press('Enter'); await settle(p, 450);
  ok(await vis(p, '#card') && (await txt(p, '#c-id')) === 'ANDA 216482', 'G6 Enter opens the selected row');
  await p.keyboard.press('Escape'); await settle(p, 100);
  // a paragraph typed in the bar goes to Review as text, never to a lookup by itself
  await p.keyboard.press('Control+Alt+Space'); await p.keyboard.type('Quick status: the goal is Oct 3.'); await settle(p, 80);
  eq((await p.$$eval('#lkSug .lk-row b', n => n.map(x => x.textContent)))[0], 'Review what you typed as a draft', 'G7 typed text is offered to Review');
  await p.keyboard.press('Enter'); await settle(p, 300);
  ok(await vis(p, '#draft') && (await p.inputValue('#drText')).startsWith('Quick status'), 'G8 and opens it with what was typed');
  await p.evaluate(() => closeApp('draft'));
  // selected cells make Ask the useful row
  await p.click('[data-pickbtn=TABLE]'); await p.keyboard.press('Control+Alt+Space'); await settle(p, 100);
  ok((await p.$$eval('#lkSug .lk-row b', n => n.map(x => x.textContent))).includes('Ask about the selected cells'), 'G9 selected cells turn the Ask row into "Ask about the selected cells"');
  await p.click('#lkSug .lk-row:last-child'); await settle(p, 200);
  ok(await vis(p, '#ask') && (await p.inputValue('#asText')).startsWith('ANDA\tApplicant'), 'G10 pressing it opens Ask with those cells');
  await p.evaluate(() => closeApp('ask'));
  // an unknown number says so, and an action that asks for a number keeps the plain box
  await p.keyboard.press('Control+Alt+Space'); await p.keyboard.type('999999'); await p.keyboard.press('Enter'); await settle(p, 150);
  ok(/ANDA 999999 is not in your data/.test(await p.evaluate(() => document.body.innerText)), 'G11 an unknown number is said not to be in the records');
  await p.evaluate(() => { askForAnda({ name: 'X', kind: 1, template: '{number}' }, { x: 300, y: 200 }); }); await settle(p, 80);
  ok(!(await p.evaluate(() => document.getElementById('lookup').classList.contains('bar5'))), 'G12 an action that needs a number uses the plain box');
  await p.close();

  // ====================================================== H. Today
  p = await fresh();
  await p.click('#trayMenuBtn'); await settle(p, 80);
  ok(await p.$$eval('#menu button', n => n.some(x => /^Today/.test(x.textContent.trim()))), 'H1 the focused tray menu has Today (round 34: Today is home)');
  await p.keyboard.press('Escape'); await p.close();
  p = await fresh({ all: true });
  await p.click('#trayMenuBtn'); await settle(p, 80);
  const today = p.locator('#menu button', { hasText: /^Today/ });
  ok(await today.count() === 1, 'H2 with all tools on, the tray menu has Today');
  await today.click(); await settle(p, 150);
  ok(await vis(p, '#today'), 'H3 Today opens');
  const grp = await p.$$eval('#tdList .td-grp', n => n.map(x => x.textContent));
  ok(grp[0] === 'NEEDS A LOOK', 'H4 the first group is what needs a look');
  const first = await p.$eval('#tdList .td-it[aria-current=true]', e => e.dataset.id);      // round 33: the row also shows its mark as a word
  ok(['214990', '216482'].includes(first), 'H6 one ANDA is selected and shown on the right');
  await p.fill('#tdSearch', '216482'); await settle(p, 80);
  ok((await txt(p, '#tdMain .meta')).startsWith('ANDA 216482'), 'H7 typing a number selects it');
  eq([await txt(p, '#tdMain .hl b'), (await txt(p, '#tdMain .hl span')).trim()], ['14', 'days later than the goal'], 'H8 the same answer as the card');
  await p.click('#tdMain .pill >> text=Copy status'); await settle(p, 150);
  eq(await p.evaluate(() => navigator.clipboard.readText()), await p.evaluate(() => statusLine('216482')), 'H9 Copy status copies the card\'s status line');
  await p.click('#tdMain .pill >> text=Open the card'); await settle(p, 400);
  ok(await vis(p, '#card'), 'H10 Open the card opens it');
  await p.keyboard.press('Escape'); await p.evaluate(() => openToday()); await settle(p, 100);
  await p.keyboard.press('Escape'); await settle(p, 80);
  ok(!(await vis(p, '#today')), 'H11 Escape closes Today');
  await p.close();

  // ====================================================== I. the tray icon's state
  p = await fresh();
  // round 22: the dot is for what needs the person today, not for "urgent" (the full cases are in suite 31)
  ok(!/attention|urgent/.test(await p.getAttribute('#appIcon', 'aria-label')) && !(await p.evaluate(() => document.querySelector('#appIcon .glyph').classList.contains('alert'))) && await p.evaluate(() => isUrgent('216482')), 'I1 an urgent ANDA alone no longer lights the tray dot, and the icon\'s name does not mention it');
  await p.evaluate(() => setDueToday(true));
  ok(/1 thing needs your attention today/.test(await p.getAttribute('#appIcon', 'aria-label')) && await p.evaluate(() => document.querySelector('#appIcon .glyph').classList.contains('alert')), 'I2 an item due today shows the amber dot and says so');
  await p.evaluate(() => { setDueToday(false); const x = new Date(demoToday()); DATA['216482'].attnDue = [x.getUTCFullYear(), x.getUTCMonth() + 1, x.getUTCDate()]; });
  ok(await p.evaluate(() => document.querySelector('#appIcon .glyph').classList.contains('alert')) === false, 'I3 it does not change by itself: only when the app reads its data');
  await p.evaluate(() => openWatchList?.()); await p.evaluate(() => refreshTrayState()); await p.close();

  // ====================================================== J. backup, restore, reset
  p = await fresh({ all: true });
  const rt = await p.evaluate(() => {
    state.ts = 'large'; state.density = 'compact'; state.lookupStyle = 'bar'; state.recentsOn = false; state.pins = ['216482']; state.recents = ['214990'];
    const json = backupText(), parsed = JSON.parse(json);
    state.ts = 'standard'; state.density = 'comfortable'; state.lookupStyle = 'box'; state.recentsOn = true; state.pins = [];
    const r = importBackup(json)[0];
    const recentsAfterOff = state.recents.slice(), onAfterRestore = state.recentsOn;
    state.recentsOn = true; state.recents = ['214990']; const onJson = JSON.parse(json); onJson.settings.rememberRecents = true; importBackup(JSON.stringify(onJson)); const recentsAfterOn = state.recents.slice();
    const out = { r, ts: state.ts, density: state.density, style: state.lookupStyle, on: onAfterRestore, pins: state.pins, recentsKept: recentsAfterOn, recentsAfterOff, hasRecents: 'recents' in parsed.settings, keys: Object.keys(parsed.settings).filter(k => ['textSize', 'density', 'lookupStyle', 'rememberRecents', 'pins'].includes(k)) };
    const bad = JSON.parse(json); bad.settings.textSize = 'huge'; bad.settings.density = 'tight'; bad.settings.lookupStyle = '<img src=x onerror=window.__pwn=1>'; bad.settings.pins = ['<img src=x onerror=window.__pwn=1>', '12345', '1234567', '216482', '216482', 7, null, '100001', '100002', '100003', '100004', '100005'];
    importBackup(JSON.stringify(bad)); out.badTs = state.ts; out.badDensity = state.density; out.badStyle = state.lookupStyle; out.badPins = state.pins; out.pwn = window.__pwn || 0;
    applySettings({}); out.empty = [state.ts, state.density, state.lookupStyle, state.recentsOn, state.pins];
    return out;
  });
  ok(rt.r && rt.ts === 'large' && rt.density === 'compact' && rt.style === 'bar' && rt.on === false && JSON.stringify(rt.pins) === '["216482"]', 'J1 text size, spacing, lookup style, the recents switch and kept ANDAs travel in a backup');
  ok(!rt.hasRecents && JSON.stringify(rt.recentsKept) === '["214990"]', 'J2 the recents themselves are not in a backup, and a restore with remembering on leaves this PC\'s own');
  ok(JSON.stringify(rt.recentsAfterOff) === '[]', 'J2b a restore that switches remembering off forgets them, as the Settings hint says (round 33)');
  eq([rt.badTs, rt.badDensity, rt.badStyle], ['standard', 'comfortable', 'box'], 'J3 odd values in a hand-edited backup become the defaults');
  eq(rt.badPins, ['216482', '100001', '100002', '100003', '100004'], 'J4 only six-digit numbers are kept, once each, at most five');
  eq(rt.pwn, 0, 'J5 markup in a backup is never run');
  eq(rt.empty, ['standard', 'comfortable', 'box', true, []], 'J6 a backup without these settings gives the defaults');
  await openSettings(p, 'secMaintenance');
  await p.evaluate(() => { state.recents = ['216482']; state.pins = ['214990']; });
  await p.click('#bkReset'); await p.click('#bkReset'); await settle(p, 120);
  eq(await p.evaluate(() => [state.recents, state.pins, state.ts]), [[], [], 'standard'], 'J7 Reset forgets recents and kept ANDAs and restores the display');
  await p.close();

  // ====================================================== K. phones and small screens
  p = await fresh({ viewport: { width: 390, height: 800 }, launch: true });
  await openCardFull(p, '216482'); await settle(p, 300);
  const ph = await p.evaluate(() => { const k = document.getElementById('card').getBoundingClientRect(), c = document.getElementById('chipCopy').getBoundingClientRect(); return { w: k.width <= innerWidth + 1, copy: c.bottom <= k.bottom + 1 && c.top >= k.top, sw: document.documentElement.scrollWidth <= innerWidth + 1, hero: document.getElementById('c-hero').getBoundingClientRect().height > 40 }; });
  ok(ph.w && ph.copy && ph.sw && ph.hero, 'K1 on a phone the card is a sheet with the hero and Copy status in view and no sideways scrolling');
  await p.evaluate(() => { closeCard(); openToday(); }); await settle(p, 200);
  const td = await p.evaluate(() => { const r = document.getElementById('today').getBoundingClientRect(); return r.left >= -1 && r.right <= innerWidth + 1 && document.documentElement.scrollWidth <= innerWidth + 1; });
  ok(td, 'K2 Today fits a phone');
  await p.evaluate(() => { closeApp('today'); state.lookupStyle = 'bar'; openLookup({ x: 20, y: 100 }); }); await settle(p, 150);
  const bar = await p.evaluate(() => { const r = document.getElementById('lookup').getBoundingClientRect(); return r.left >= -1 && r.right <= innerWidth + 1; });
  ok(bar, 'K3 One Bar fits a phone');
  await p.close();

  // ====================================================== L. the Appearance Lab
  p = await fresh({ launch: false });
  await p.goto(LAB); await settle(p, 400);
  eq(await p.$$eval('#gridOffered > .tile .nm', n => n.map(x => x.textContent.replace('DEFAULT', '').trim())), ['X · Graphite Calendar', 'V · Glass Sheet', 'U · Calendar Ribbon', 'T · Pocket Plain', 'K · Graphite Quiet', 'P · Bronze Gauge'], 'L1 the lab shows the six full cards offered, in the order of Settings');
  eq(await p.$$eval('.badge.new', n => n.length), 1, 'L2 and marks one default');
  ok(await p.$eval('#gridOffered .tile[data-id=x] .badge', e => /DEFAULT/.test(e.textContent)), 'L3 which is X');
  ok(await p.$eval('#gridOffered .tile[data-id=u] .ribbon5 svg', e => /Calendar ribbon\./.test(e.getAttribute('aria-label'))), 'L4 the ribbon in the lab has a text equivalent');
  await p.click('[data-rec="214990"]'); await settle(p, 200);
  ok((await p.$$eval('#gridOffered .hero5 .hl b', n => n.map(x => x.textContent))).join() === '9,9,9,9' && await p.$eval('#gridRetired .tile[data-id=w] .hg5 b', e => e.textContent) === '88%' && await p.$eval('#gridOffered .tile[data-id=x] .cd5', e => e.getAttribute('aria-label')) === '30 days to the goal date', 'L5 switching the example ANDA redraws the four, and the gauge of the default');
  await p.close();

  await b.close();
  ok(errs.length === 0, `no page errors (${JSON.stringify(errs.slice(0, 3))})`);
  console.log(`\n${checks - fails}/${checks} passed`);
  process.exit(fails ? 1 : 0);
})();
