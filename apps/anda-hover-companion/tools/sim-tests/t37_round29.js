// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 29 in the browser simulation: every window says what is switched on, or "Offline · Local only"; a list of the connections with
// what each sends, where and what is kept; an event log that is only added to and holds times, kinds and counts; a page that says what
// changed; kept copies of the sheet deleted only when the person asks and confirms.
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);
(async () => {
  const b = await chromium.launch(); const errs = [];
  const fresh = async (viewport) => {
    const ctx = await b.newContext({ viewport: viewport || { width: 1366, height: 768 }, permissions: ['clipboard-read', 'clipboard-write'], timezoneId: 'America/New_York', reducedMotion: 'reduce' });
    const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    await p.goto(SIM); await p.waitForTimeout(300); return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE_FILE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(document.querySelector(s))).violations.map(v => v.id + ': ' + v.nodes.length), sel); };
  const clip = p => p.evaluate(() => navigator.clipboard.readText());
  const bars = p => p.evaluate(() => [...new Set([...document.querySelectorAll('.onbar')].map(x => x.textContent))]);

  // ====================================================== C. What is switched on, in every window
  let p = await fresh();
  eq(await p.evaluate(() => APP_WINS.filter(id => document.querySelectorAll('#' + id + ' .onbar').length !== 1)), [], 'C1 every window carries one line that says what is switched on');
  eq(await bars(p), ['Offline · Local only'], 'C2 on a new page every one of them reads "Offline · Local only"');
  await p.evaluate(() => { showApp('settings'); document.querySelector('input[name=provider][value=claude]').click(); }); await settle(p);
  eq([await bars(p), await p.evaluate(() => evLog.map(e => e.type + ': ' + e.detail))], [['Switched on: Claude (AI)'], ['connection: AI switched on']], 'C3 choosing a connection in Settings changes the line in every window at once and is written to the event log');
  await p.evaluate(() => { document.querySelector('input[name=provider][value=offline]').click(); }); await settle(p);
  eq([await bars(p), await p.evaluate(() => evLog.map(e => e.detail))], [['Offline · Local only'], ['AI switched on', 'AI switched off']], 'C4 and switching it off again is said and logged as well');
  await p.evaluate(() => { state.agents[0].enabled = true; state.agents[0].name = '<img src=x onerror="window.__ran=1">'; state.outlook.on = true; paintConnBars(); closeApp('settings'); }); await settle(p);
  eq(await bars(p), ['Switched on: <img src=x onerror="window.__ran=1"> (agent) · Outlook (read-only)'], 'C5 an agent and Outlook are named when they are switched on');
  await p.evaluate(() => { openCompare(); }); await settle(p, 200);
  await p.click('#cmp .onbar'); await settle(p, 200);
  { const v = await p.evaluate(() => [document.getElementById('conn').style.display, document.getElementById('cnList').innerText, document.querySelectorAll('#conn img').length, window.__ran === undefined, document.getElementById('cnLine').textContent]);
    ok(v[0] === 'block' && v[1].includes('Sends: Only when you press Run or Send: its prompt, filled in and shown to you.') && v[1].includes('To: no address yet, so nothing can be sent') && v[1].includes('Kept: Its answer is kept in memory for the minutes you chose, never in a file.')
      && v[1].includes('Outlook (read-only)') && v[1].includes('To: graph.microsoft.com') && v[1].includes('Sends: Only a search for the six-digit number of the open ANDA.'), 'C6 a press on the line opens the list: for each connection what it sends, where, and what is kept');
    ok(v[2] === 0 && v[3] && v[4].startsWith('Switched on: <img'), 'C7 a name with markup in it is text: no element is made and nothing runs'); }
  await p.click('#cnCopy'); await settle(p, 200);
  eq(await clip(p) === await p.evaluate(() => connections(connState()).text.join('\n')), true, 'C8 "Copy this list" copies exactly the list');
  eq(await axe(p, '#conn'), [], 'C9 accessibility scan of the Connections window');

  // ====================================================== E. The event log
  { const v = await p.evaluate(() => [document.getElementById('cnLogWrap').hidden, [...document.querySelectorAll('#cnLog tr')].map(r => [...r.children].map(c => c.textContent)), document.getElementById('cnCount').textContent, document.getElementById('cnCsv').disabled]);
    ok(v[0] === false && v[1].length === 4 && v[1][0][1] === 'Connection switched on or off' && v[1][0][2] === 'Outlook switched on' && v[1][3][2] === 'AI switched on' && v[2] === '4 events.' && v[3] === false, 'E1 the log lists what happened, newest first, with a count');
    ok(v[1].every(r => /, \d{1,2}:\d{2}:\d{2} (AM|PM) (EST|EDT)$/.test(r[0])), 'E2 each time is shown in the time zone of this computer, with its abbreviation'); }
  await p.click('#cnCsv'); await settle(p, 200);
  { const csv = await clip(p), lines = csv.split('\r\n');
    ok(lines[0] === 'time_utc,type,detail' && lines.length === 6 && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z,connection,"AI switched on"$/.test(lines[1]) && csv === await p.evaluate(() => eventCsv(evLog)), 'E3 "Copy as CSV" gives the whole log, oldest first, with UTC times'); }
  eq(await p.evaluate(() => { const n = evLog.length; logEvent('sent', 'about ANDA 216482'); logEvent('sent', 'the text <b>of a draft</b>'); logEvent('not-a-kind', 'x'); logEvent('sent', 'AI request, 1,234 characters'); return evLog.slice(n).map(e => [e.type, e.detail]); }),
    [['sent', '(detail withheld: not in the allowed form)'], ['sent', '(detail withheld: not in the allowed form)'], ['warning', 'x'], ['sent', 'AI request, 1,234 characters']], 'E4 the log cannot hold an ANDA number or text: such a detail is withheld, and an unknown kind is written as a warning');
  eq(await p.evaluate(() => [...document.querySelectorAll('#conn button')].map(x => x.textContent).filter(t => /clear|delete|remove|reset/i.test(t))), [], 'E5 the log is only added to: the window has no way to clear or change it');
  await p.evaluate(() => { closeApp('conn'); closeApp('cmp'); document.getElementById('swAi').click(); }); await settle(p);
  { await p.evaluate(() => runCustomAction({ name: 'Ask', kind: 2, template: 'Summarize: {selection}', enabled: true, hotkey: '', due: 2, agentId: '', direct: false }, 'A short text.', { x: 300, y: 300 }, true)); await settle(p, 300);
    const before = await p.evaluate(() => evLog.length);
    await p.click('#awSend'); await settle(p, 1500);
    const last = await p.evaluate(n => evLog.slice(n).map(e => [e.type, e.detail]), before);
    ok(last.length === 1 && last[0][0] === 'sent' && /^AI request \(simulated\), [0-9,]+ characters$/.test(last[0][1]), 'E6 pressing Send writes one line: that something was sent and how long it was, never the text'); }
  await p.close();
  p = await fresh();
  { await p.evaluate(() => openConn()); await settle(p);
    eq(await p.evaluate(() => [document.getElementById('cnList').textContent, document.getElementById('cnLogEmpty').hidden, document.getElementById('cnLogWrap').hidden, document.getElementById('cnCsv').disabled, document.getElementById('cnSave').disabled]),
      ['Nothing is switched on, so nothing leaves this PC.', false, true, true, true], 'E7 with nothing switched on and nothing logged, the window says so and offers nothing to export'); }

  // ====================================================== A. About and what changed
  await p.evaluate(() => { closeApp('conn'); openMenu(); }); await settle(p, 200);
  eq(await p.evaluate(() => ['Connections and event log…', 'About and what changed…'].map(t => [...document.querySelectorAll('#menu button')].some(x => x.textContent.startsWith(t)))), [true, true], 'A1 the tray menu offers both new windows');
  await p.evaluate(() => { closeMenus(); openAbout(); }); await settle(p);
  { const v = await p.evaluate(() => [document.getElementById('abVersion').textContent, [...document.querySelectorAll('#abList .lbl')].map(x => x.textContent), document.querySelectorAll('#abList li').length, document.getElementById('about').innerText.includes('It does not update itself.')]);
    eq(v, ['Prototype, round 29', ['ROUND 29', 'ROUND 28', 'ROUND 27', 'ROUND 26'], await p.evaluate(() => CHANGES.reduce((n, c) => n + c.items.length, 0)), true], 'A2 the page names the version and what changed, newest first, and says the tool does not update itself'); }
  await p.click('#abCopy'); await settle(p, 200);
  eq(await clip(p) === await p.evaluate(() => changesText().join('\n')), true, 'A3 the page can be copied');
  eq(await axe(p, '#about'), [], 'A4 accessibility scan of the About window');
  await p.close();

  // ====================================================== W. Kept copies of the sheet are deleted only by the person
  p = await fresh();
  await p.evaluate(() => openCompare('week')); await settle(p, 200);
  eq(await p.evaluate(() => [document.getElementById('cmWeekDel').hidden, !!cmpA]), [true, false], 'W1 with nothing kept there is nothing to delete');
  await p.click('#cmWeekOn'); await settle(p);
  await p.click('#cmWeekOn'); await settle(p);
  eq(await p.evaluate(() => [document.getElementById('cmWeekOn').checked, document.getElementById('cmWeekDel').hidden, !!cmpA && cmpA.name, document.getElementById('cmStatus').textContent]), [false, false, 'My sheet, copy kept Nov 10', 'No new copies are kept. The copies that exist stay until you delete them.'], 'W2 switching the weekly copy off keeps the copies that exist, and they can still be compared');
  await p.click('#cmWeekDel'); await settle(p);
  eq(await p.evaluate(() => [document.getElementById('cmWeekDel').textContent, !!cmpA]), ['Click again to delete', true], 'W3 the first press on "Delete the kept copies…" only asks');
  await p.click('#cmWeekDel'); await settle(p);
  eq(await p.evaluate(() => [document.getElementById('cmWeekDel').hidden, !!cmpA, document.getElementById('cmStatus').textContent, evLog.map(e => e.type + ': ' + e.detail)]),
    [true, false, 'The kept copies were deleted. Nothing else was changed.', ['file: weekly copy of the sheet switched on', 'file: weekly copy of the sheet switched off', 'deleted: kept copies of the sheet']], 'W4 the second press deletes them, says so, and writes it to the event log');
  await p.close();

  // ====================================================== K. Keyboard and phone
  p = await fresh();
  { await p.evaluate(() => openHandoff(null)); await settle(p, 200);
    let reached = false; for (let i = 0; i < 30 && !reached; i++) { await p.keyboard.press('Tab'); reached = await p.evaluate(() => document.activeElement.classList.contains('onbar') && document.getElementById('handoff').contains(document.activeElement)); }
    await p.keyboard.press('Enter'); await settle(p, 200);
    const open = await p.evaluate(() => [document.getElementById('conn').style.display, document.getElementById('conn').contains(document.activeElement)]);
    await p.keyboard.press('Escape'); await settle(p);
    const back = await p.evaluate(() => [document.getElementById('conn').style.display, document.activeElement.classList.contains('onbar') && document.getElementById('handoff').contains(document.activeElement)]);
    eq([reached, open, back], [true, ['block', true], ['none', true]], 'K1 the line is reached with Tab, Enter opens the list, and Escape closes it and gives the keyboard back to the line'); }
  await p.close();
  p = await fresh({ width: 390, height: 780 });
  for (const [id, open] of [['conn', 'openConn()'], ['about', 'openAbout()']]) {
    await p.evaluate(o => { APP_WINS.forEach(closeApp); eval(o); }, open); await settle(p, 250);
    eq(await p.evaluate(i => { const r = document.getElementById(i).getBoundingClientRect(); return [r.left >= 0, r.right <= innerWidth + 1, document.documentElement.scrollWidth <= innerWidth + 1]; }, id), [true, true, true], `K2 on a phone the ${id === 'conn' ? 'Connections' : 'About'} window fits the screen`);
  }
  await p.close();

  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
