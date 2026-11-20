// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 34 in the browser simulation, the three changes the owner chose on Nov 20, 2026:
//   1. the person's own shortcut opens the card directly (Whisper stays a choice; a middle-click still asks first);
//   2. the card shows the answer first, and the rest is one press away (More or E), so it fits a 1366 by 768 laptop;
//   3. a click on the tray icon opens Today, which also holds the watch list (Show all), and the tray menu names Today.
// The rules are read against mockup/round34-golden.json, the answers the Windows app must also give.
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const GOLD = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round34-golden.json'), 'utf8'));
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);

(async () => {
  const b = await chromium.launch(); const errs = [];
  const fresh = async (viewport = { width: 1366, height: 768 }) => {
    const ctx = await b.newContext({ viewport, timezoneId: 'America/New_York', reducedMotion: 'reduce', permissions: ['clipboard-read', 'clipboard-write'] });
    const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error') errs.push(m.type() + ': ' + m.text()); });
    await p.goto(SIM); await p.waitForTimeout(300); return p;
  };
  const settle = (p, ms = 200) => p.waitForTimeout(ms);
  const shown = (p, sel) => p.$eval(sel, e => getComputedStyle(e).display !== 'none' && !e.hidden && e.getClientRects().length > 0).catch(() => false);
  const press = async (p, id, via) => { await p.evaluate(([n, v]) => { APP_WINS.forEach(closeApp); closeCard(); hideChip(); lastSel = { text: 'ANDA ' + n }; hotkey('peek', v); }, [id, via]); await settle(p, 400); };
  const axe = async (p, sel) => {
    if (!AXE_FILE) return ['axe-core not found: set AXE_PATH'];
    await p.addScriptTag({ path: AXE_FILE });
    return p.evaluate(async s => (await axe.run(document.querySelector(s), { runOnly: ['wcag2a', 'wcag2aa'] })).violations.map(v => v.id), sel);
  };

  // ---- G. the shared answers ----
  let p = await fresh();
  eq(await p.evaluate(() => state.mode), GOLD.defaultConsent, 'G1 by default the shortcut opens the card (Full)');
  eq(await p.evaluate(() => CONSENTS.map(c => [c[0], c[1]])), GOLD.consents, 'G2 Settings names the three ways to start, Full marked as the default');
  eq(await p.evaluate(steps => steps.map(s => { state.mode = s.consent; state.escalate = s.escalate; const id = Object.keys(DATA).find(n => isUrgent(n) === s.urgent); return firstStep(id, s.via); }), GOLD.steps),
    GOLD.steps.map(s => s.step), 'G3 how a card starts, for every way to start, urgent or not, by shortcut or middle-click: as the shared answers say');
  eq(await p.evaluate(ns => ns.map(n => watchMoreText(n)), GOLD.watchMore.map(w => w.n)), GOLD.watchMore.map(w => w.text), 'G4 the watch line\'s tail is short: "(+N more)", and nothing when there is one flag');
  eq(await p.evaluate(() => CHANGES.find(c => c.round === 34).items), GOLD.changes.items, 'G5 what changed in round 34 is listed (the newest version is checked by the round 29 answers)');
  await p.close();

  // ---- S. the shortcut opens the answer ----
  p = await fresh();
  eq(await p.evaluate(() => document.querySelector('input[name=consent]:checked').value), 'full', 'S1 Settings shows Full as the choice in use');
  await press(p, '214990', 'keyboard');
  eq([await shown(p, '#card'), await p.evaluate(() => card.classList.contains('mini')), await shown(p, '#chip')], [true, false, false], 'S2 the shortcut on an ANDA that is on track opens the card at once: no invite, no peek');
  await press(p, '216482', 'keyboard');
  eq([await shown(p, '#card'), await p.evaluate(() => card.classList.contains('mini'))], [true, false], 'S3 the same for an ANDA at risk: the full card, not a peek');
  await press(p, '214990', 'middle');
  eq([await shown(p, '#chip'), await shown(p, '#card')], [true, false], 'S4 a middle-click (which the person did not aim at the app) still asks first: the small invite');
  await p.evaluate(() => { const r = document.querySelector('input[name=consent][value=whisper]'); r.checked = true; r.dispatchEvent(new Event('change')); });
  await press(p, '214990', 'keyboard');
  eq([await shown(p, '#chip'), await shown(p, '#card')], [true, false], 'S5 a person who chooses Whisper still gets the invite first');
  await p.close();

  // ---- C. the answer-first card ----
  p = await fresh();
  await press(p, '216482', 'keyboard');
  const MORE = ['#c-ribbon-slot', '.slot.latest', '#aheadSlot', '#psgSlot', '#rldSlot', '.slot.ai', '.kbdbar', '#actHint'];
  eq(await p.evaluate(sels => sels.filter(s => { const e = document.querySelector(s); return e && getComputedStyle(e).display !== 'none'; }), MORE), [], 'C1 collapsed, the calendar, Latest, Ahead, the two reference dates, the brief and the key hints are not drawn');
  ok(/^Goal .+ · Forecast .+/.test(await p.textContent('#c-hero-s')), 'C2a goal and forecast are in the line under the answer (look X)');
  eq(await p.evaluate(() => ['#c-hero', '#c-hero-s', '#watchSlot', '#chipNotes', '#chipCopy', '#chipMore'].filter(s => { const e = document.querySelector(s); return !(e && getComputedStyle(e).display !== 'none' && e.getClientRects().length); })), [], 'C2 the answer is drawn: what it says first, goal, forecast, the first thing worth a look, and the actions with More');
  eq(await p.evaluate(() => [document.getElementById('c-watch-more').textContent, watchMoreText(watchFlags('216482').length - 1)]), ['(+1 more)', '(+1 more)'], 'C3 the watch line says how many more flags wait under More');
  eq(await p.evaluate(() => { const c = document.getElementById('chipNotes'); return [c.innerText.trim(), c.getAttribute('aria-label')]; }), ['Add a note', 'Add a note · 3 notes'], 'C4 the note button reads "Add a note"; the count is still said to a screen reader');
  await p.click('#chipMore'); await settle(p, 300);
  eq([await p.evaluate(() => card.classList.contains('expanded')), await p.evaluate(sels => sels.filter(s => getComputedStyle(document.querySelector(s)).display === 'none'), MORE.filter(s => s !== '.slot.ai' && s !== '#aheadSlot')), await shown(p, '#chipMore'), await p.evaluate(() => document.activeElement.id)],
    [true, [], false, 'expBtn'], 'C5 More opens the rest, hides itself, and hands the keyboard to the expand button so the next press closes it again');
  await p.evaluate(() => card.focus()); await p.keyboard.press('KeyE'); await settle(p, 250);
  eq([await p.evaluate(() => card.classList.contains('expanded')), await shown(p, '#chipMore')], [false, true], 'C6 E folds it back to the answer');
  eq(await axe(p, '#card'), [], 'C7 accessibility scan of the answer-first card');
  // the card fits a 1366 by 768 laptop (728 px above the taskbar) at every text size, and its actions stay in view
  for (const ts of ['m', 'large', 'xl']) {
    await p.evaluate(t => { state.ts = t; applyDisplay(); }, ts);
    await press(p, '216482', 'keyboard');
    const fit = await p.evaluate(() => { const c = card.getBoundingClientRect(), tb = document.getElementById('taskbar').getBoundingClientRect(), k = document.getElementById('chipCopy').getBoundingClientRect();
      return { inside: c.top >= 0 && c.bottom <= tb.top + 1, copy: k.top >= c.top && k.bottom <= c.bottom + 1, scrolls: card.scrollHeight > card.clientHeight + 1, h: Math.round(c.height) }; });
    ok(fit.inside && fit.copy && (ts === 'xl' || !fit.scrolls), `C8 text size ${ts}: the card is ${fit.h} px, inside the screen above the taskbar, Copy status in view${ts === 'xl' ? '' : ', and nothing to scroll'}`);
  }
  // changing the text size with the card open keeps it on screen (it used to grow past the taskbar)
  await p.evaluate(() => { state.ts = 'm'; applyDisplay(); }); await press(p, '216482', 'keyboard');
  await p.evaluate(() => { document.querySelector('input[name=ts][value=xl]').click(); }); await settle(p, 200);
  ok(await p.evaluate(() => { const c = card.getBoundingClientRect(); return c.top >= 0 && c.bottom <= document.getElementById('taskbar').getBoundingClientRect().top + 1; }), 'C9 a larger text size chosen while the card is open keeps the card on screen');
  await p.evaluate(() => { state.ts = 'm'; applyDisplay(); });
  // with an AI connection on, nothing is asked for a part that is not shown: the brief waits for More
  await p.evaluate(() => { state.ai = true; }); await press(p, '216482', 'keyboard');
  eq(await p.evaluate(() => [document.getElementById('c-verify').textContent, document.getElementById('c-ai').textContent, briefWaiting]), ['asked for when you press More', '', '216482'], 'C10 with AI on, the collapsed card asks for no brief, and says when it will');
  await p.click('#chipMore'); await settle(p, 300);
  eq(await p.evaluate(() => [document.getElementById('c-verify').textContent, briefWaiting]), ['sample text · no model is called', null], 'C11 More asks for it');
  // the separate review's case: a brief that waited on a closed card must not be asked for by a later peek
  await p.evaluate(() => closeCard());
  eq(await p.evaluate(() => briefWaiting), null, 'C11a closing the card forgets the brief that waited');
  await press(p, '216482', 'keyboard'); await p.keyboard.press('Escape'); await settle(p);
  await p.evaluate(() => { openCard('216482', { anchor: { x: 600, y: 120 }, mini: true, forceFull: true }); card.focus(); }); await settle(p);
  await p.keyboard.press('KeyE'); await settle(p, 300);
  eq(await p.evaluate(() => [card.classList.contains('mini'), document.getElementById('c-verify').textContent === 'sample text · no model is called', getComputedStyle(document.querySelector('.slot.ai')).display !== 'none']), [true, false, false], 'C11b a peek, even with E pressed, asks for no brief');
  await p.evaluate(() => { closeCard(); state.ai = false; });
  // the expand button says what a press will do, and whether the card is open
  await press(p, '216482', 'keyboard');
  eq(await p.evaluate(() => { const b = document.getElementById('expBtn'); return [b.getAttribute('aria-label'), b.getAttribute('aria-expanded')]; }), ['Expand card', 'false'], 'C11c collapsed: "Expand card", not expanded');
  await p.click('#chipMore'); await settle(p);
  eq(await p.evaluate(() => { const b = document.getElementById('expBtn'); return [document.activeElement === b, b.getAttribute('aria-label'), b.getAttribute('aria-expanded')]; }), [true, 'Collapse card', 'true'], 'C11d after More the keyboard is on "Collapse card", which says the card is open');
  ok((await p.textContent('#setMiddle + *, #settings')).includes('A middle-click always shows the small invite first, even when your shortcut opens the card.'), 'C11e Settings says that a middle-click asks first');
  await p.evaluate(() => closeCard());
  // a peek's "Full brief" shows the brief: the card opens with the rest
  await p.evaluate(() => { const r = document.querySelector('input[name=consent][value=peek]'); r.checked = true; r.dispatchEvent(new Event('change')); });
  await press(p, '214990', 'keyboard'); await p.click('#moreBtn'); await settle(p, 300);
  eq(await p.evaluate(() => [card.classList.contains('mini'), card.classList.contains('expanded'), getComputedStyle(document.querySelector('.slot.ai')).display !== 'none']), [false, true, true], 'C12 a peek\'s Full brief opens the card with the brief in view');
  await p.close();

  // ---- T. Today is home ----
  p = await fresh();
  await p.click('#chev'); await p.click('#pinBtn2'); await settle(p, 150);
  ok((await p.getAttribute('#appIcon', 'aria-label')).includes('Click for Today, right-click or long-press for the menu.'), 'T1 the tray icon says what a click and a right-click do');
  await p.click('#appIcon'); await settle(p, 250);
  eq([await shown(p, '#today'), await p.evaluate(() => document.activeElement.id)], [true, 'tdSearch'], 'T2 a click on the tray icon opens Today, with the caret in its box');
  await p.keyboard.press('Escape'); await settle(p);
  await p.click('#appIcon', { button: 'right' }); await settle(p, 150);
  const items = await p.$$eval('#menu button', n => n.filter(x => x.getClientRects().length).map(x => x.textContent.trim()));
  eq([items.filter(t => /^Today/.test(t)).length, items.filter(t => /^Watch list/.test(t)).length], [1, 0], 'T3 the tray menu (all tools off) names Today once and has no separate Watch list');
  await p.click('#menu >> text=Today…'); await settle(p, 250);
  ok(await shown(p, '#today'), 'T4 Today opens from the menu too');
  const need = await p.evaluate(() => needLookIds().length);
  eq([need > 0, await shown(p, '#tdAll'), await p.textContent('#tdAll')], [true, true, 'Show all, with reasons…'], 'T5 when something needs a look, Today offers Show all');
  eq(await p.evaluate(() => { const all = [...document.querySelectorAll('#today button, #today input')]; return all.indexOf(document.getElementById('tdAll')) > Math.max(...[...document.querySelectorAll('#tdList button')].map(b => all.indexOf(b))); }), true, 'T5a Show all comes after the rows with Tab, as it is drawn');
  await p.click('#tdAll'); await settle(p, 250);
  const top = await p.evaluate(() => { const z = id => parseInt(getComputedStyle(document.getElementById(id)).zIndex, 10) || 0; return [document.getElementById('wlist').contains(document.activeElement), z('wlist') > z('today'), document.querySelectorAll('#wlRows .row').length, document.getElementById('wlNote').textContent]; });
  ok(top[0] && top[1] && top[2] === need && top[3].startsWith(`${need} of `), `T6 Show all opens the watch list above Today, with the keyboard, listing the same ${need} ANDAs with their reasons`);
  await p.keyboard.press('Escape'); await settle(p);
  eq([await shown(p, '#wlist'), await shown(p, '#today'), await p.evaluate(() => document.activeElement.id)], [false, true, 'tdAll'], 'T7 Escape closes the watch list only, and the keyboard goes back to Show all');
  eq(await axe(p, '#today'), [], 'T8 accessibility scan of Today');
  eq(await p.evaluate(() => GUIDE_STEPS.some(s => JSON.stringify(s).includes('Click the tray icon near the clock to open Today'))), true, 'T9 First steps tells the person that a click on the tray icon opens Today');
  await p.close();

  eq(errs, [], 'F1 no error in any page');
  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
