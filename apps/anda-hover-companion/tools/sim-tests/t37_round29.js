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
  eq([await bars(p), await p.evaluate(() => evLog.map(e => e.type + ': ' + e.detail))], [['Switched on: Claude (AI)'], ['connection: at start: AI off, agents on: 0, Outlook off', 'connection: AI switched on']], 'C3 choosing a connection in Settings changes the line in every window at once and is written to the event log');
  await p.evaluate(() => { document.querySelector('input[name=provider][value=offline]').click(); }); await settle(p);
  eq([await bars(p), await p.evaluate(() => evLog.map(e => e.detail))], [['Offline · Local only'], ['at start: AI off, agents on: 0, Outlook off', 'AI switched on', 'AI switched off']], 'C4 and switching it off again is said and logged as well');
  await p.evaluate(() => { state.agents[0].enabled = true; state.agents[0].name = '<img src=x onerror="window.__ran=1">'; state.outlook.on = true; paintConnBars(); closeApp('settings'); }); await settle(p);
  eq(await bars(p), ['Switched on: <img src=x onerror="window.__ran=1"> (agent) · Outlook (read-only)'], 'C5 an agent and Outlook are named when they are switched on');
  await p.evaluate(() => { openCompare(); }); await settle(p, 200);
  await p.click('#cmp .onbar'); await settle(p, 200);
  { const v = await p.evaluate(() => [document.getElementById('conn').style.display, document.getElementById('cnList').innerText, document.querySelectorAll('#conn img').length, window.__ran === undefined, document.getElementById('cnLine').textContent]);
    ok(v[0] === 'block' && v[1].includes('Sends: Only when you press Run or Send: its prompt, filled in and shown to you.') && v[1].includes('To: no address yet, so nothing can be sent') && v[1].includes('Kept: Its answer is kept in memory until you close the app, and counts as fresh for the minutes you chose. It is never written to a file.')
      && v[1].includes('Outlook (read-only)') && v[1].includes('To: login.microsoftonline.com and graph.microsoft.com') && v[1].includes("Sends: When you sign in: this app's ID and the read-only permission it asks for. After that, only a search for the six-digit number of the open ANDA."), 'C6 a press on the line opens the list: for each connection what it sends, where, and what is kept');
    ok(v[2] === 0 && v[3] && v[4].startsWith('Switched on: <img'), 'C7 a name with markup in it is text: no element is made and nothing runs'); }
  await p.click('#cnCopy'); await settle(p, 200);
  eq(await clip(p) === await p.evaluate(() => connections(connState()).text.join('\n')), true, 'C8 "Copy this list" copies exactly the list');
  eq(await axe(p, '#conn'), [], 'C9 accessibility scan of the Connections window');

  // ====================================================== E. The event log
  { const v = await p.evaluate(() => [document.getElementById('cnLogWrap').hidden, [...document.querySelectorAll('#cnLog tr')].map(r => [...r.children].map(c => c.textContent)), document.getElementById('cnCount').textContent, document.getElementById('cnCsv').disabled]);
    ok(v[0] === false && v[1].length === 5 && v[1][0][1] === 'Connection switched on, off or changed' && v[1][0][2] === 'Outlook switched on' && v[1][3][2] === 'AI switched on' && v[1][4][2] === 'at start: AI off, agents on: 0, Outlook off' && v[2] === '5 events.' && v[3] === false, 'E1 the log lists what happened, newest first, with a count');
    ok(v[1].every(r => /, \d{1,2}:\d{2}:\d{2} (AM|PM) (EST|EDT)$/.test(r[0])), 'E2 each time is shown in the time zone of this computer, with its abbreviation'); }
  await p.click('#cnCsv'); await settle(p, 200);
  { const csv = await clip(p), lines = csv.split('\r\n');
    ok(lines[0] === 'time_utc,type,detail' && lines.length === 7 && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z,connection,"at start: AI off, agents on: 0, Outlook off"$/.test(lines[1]) && lines[2].endsWith(',connection,"AI switched on"') && csv === await p.evaluate(() => eventCsv(evLog)), 'E3 "Copy as CSV" gives the whole log, oldest first, with UTC times'); }
  eq(await p.evaluate(() => { const n = evLog.length; logEvent('sent', 'about ANDA 216482'); logEvent('sent', 'the text <b>of a draft</b>'); logEvent('not-a-kind', 'connection test'); logEvent('sent', 'AI request, 1,234 characters'); logEvent('sent', 'John Smith, 12 Main St'); logEvent('sent', 'host api.example.com'); return evLog.slice(n).map(e => [e.type, e.detail]); }),
    [['sent', "(detail withheld: not one of the log's own sentences)"], ['sent', "(detail withheld: not one of the log's own sentences)"], ['warning', 'connection test'], ['sent', 'AI request, 1,234 characters'], ['sent', "(detail withheld: not one of the log's own sentences)"], ['sent', "(detail withheld: not one of the log's own sentences)"]], 'E4 the log holds only its own sentences: an ANDA number, a text, a name or an address is withheld, and an unknown kind is written as a warning');
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
      ['Nothing is switched on, so nothing leaves this PC.', true, false, false, false], 'E7 with nothing switched on the window says so'); }
  eq(await p.evaluate(() => evLog.map(e => e.type + ': ' + e.detail)), ['connection: at start: AI off, agents on: 0, Outlook off'], 'E9 one line is written at start that says what is already on, so the log never shows "off" without an "on" before it');
  eq(await p.evaluate(() => ['=1+1', '+1', '-1', 'a=1+1-1'].map(d => eventEntry('sent', d, 0).detail === EV_WITHHELD)), [true, true, true, true], 'E8 a detail that begins like a spreadsheet formula, or is not one of the log' + "'" + 's sentences, is never kept');
  eq(await p.evaluate(() => EV_PHRASES.filter(x => !/^[A-Za-z][A-Za-z #,:()-]*$/.test(x))), [], 'E10 every sentence of the log is plain words with a place for a count');
  eq(await p.evaluate(() => { state.agents[0].enabled = true; state.agents[0].name = 'Alert\u202E buH\u200B'; state.agents[0].api.endpoint = 'https://ex\u0430mple.test/ask'; const r = connections(connState()); return [r.line, r.items[0].where]; }),
    ['Switched on: Alert buH (agent)', 'xn--exmple-4nf.test'], 'C10 an invisible character is removed from a name, and a look-alike letter in an address shows in the form the network uses');
  { const n = await p.evaluate(() => { paintConnBars(); return evLog.length; });
    const v = await p.evaluate(() => { const b = document.querySelector('#cmp .onbar'); return [b.textContent, b.getAttribute('aria-label')]; });
    eq(v[1], v[0] + '. Opens the list of connections and the event log.', 'C11 the line is read out as it is shown: what is on, then what a press does');
    eq(await p.evaluate(m => { state.agents[0].enabled = false; state.agents[1].enabled = true; paintConnBars(); state.agents[1].api.endpoint = 'https://other.example.test/ask'; paintConnBars(); return evLog.slice(m).map(e => e.detail); }, n),
      ['agents changed', 'agents changed'], 'C12 another agent, or another address, in place of the one that was on is written to the log, without its name or address'); }
  { const made = await p.evaluate(async () => {
      const ag = state.agents[1]; const n = evLog.length;
      const problem = agentSetupProblem(ag);
      const one = await askAgent(ag, '216482', null, true); state.agentFail = true; const two = await askAgent(ag, '216482', null, true); state.agentFail = false;
      return [problem, one.state, two.state, evLog.slice(n).map(e => e.type + ': ' + e.detail), evLog.some(e => e.detail.includes(ag.name) || e.detail.includes('216482') || e.detail.includes('example'))]; });
    eq(made, [null, 'done', 'failed', ['sent: agent request (simulated)', 'sent: agent request (simulated)', 'send_failed: agent request (simulated): no answer'], false], 'E11 asking an agent is written before it is asked, and again when no answer comes: never which agent, which ANDA or what was asked'); }

  // ====================================================== A. About and what changed
  await p.evaluate(() => { closeApp('conn'); openMenu(); }); await settle(p, 200);
  eq(await p.evaluate(() => ['Connections and event log…', 'About and what changed…'].map(t => [...document.querySelectorAll('#menu button')].some(x => x.textContent.startsWith(t)))), [true, true], 'A1 the tray menu offers both new windows');
  await p.evaluate(() => { closeMenus(); openAbout(); }); await settle(p);
  { const v = await p.evaluate(() => [document.getElementById('abVersion').textContent, [...document.querySelectorAll('#abList .lbl')].map(x => x.textContent), document.querySelectorAll('#abList li').length, document.getElementById('about').innerText.includes('It does not update itself.')]);
    eq(v, ['Prototype, round 33', ['ROUND 33', 'ROUND 32', 'ROUND 31', 'ROUND 30', 'ROUND 29', 'ROUND 28', 'ROUND 27', 'ROUND 26'], await p.evaluate(() => CHANGES.reduce((n, c) => n + c.items.length, 0)), true], 'A2 the page names the version and what changed, newest first, and says the tool does not update itself'); }
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
  await p.dblclick('#cmWeekDel'); await settle(p);
  eq(await p.evaluate(() => [document.getElementById('cmWeekDel').hidden, !!cmpA, evLog.some(e => e.type === 'deleted')]), [false, true, false], 'W4 a double-click is one press: nothing is deleted');
  await p.evaluate(() => { closeApp('cmp'); openCompare('week'); }); await settle(p, 200);
  eq(await p.evaluate(() => document.getElementById('cmWeekDel').textContent), 'Delete the kept copies…', 'W5 closing the window withdraws the question');
  await p.click('#cmWeekDel'); await settle(p, 100); await p.evaluate(() => cmpSetMode('two')); await p.evaluate(() => cmpSetMode('week')); await settle(p, 100);
  eq(await p.evaluate(() => document.getElementById('cmWeekDel').textContent), 'Delete the kept copies…', 'W6 so does changing the view');
  await p.click('#cmWeekDel'); await settle(p, 800); await p.click('#cmWeekDel'); await settle(p);
  eq(await p.evaluate(() => [document.getElementById('cmWeekDel').hidden, !!cmpA, document.getElementById('cmStatus').textContent, evLog.map(e => e.type + ': ' + e.detail)]),
    [true, false, 'The kept copies were deleted. Nothing else was changed.', ['connection: at start: AI off, agents on: 0, Outlook off', 'file: weekly copy of the sheet switched on', 'file: weekly copy of the sheet switched off', 'deleted: kept copies of the sheet']], 'W7 a second press after a pause deletes them, says so, and writes it to the event log');
  await p.click('#cmWeekOn'); await settle(p);
  await p.click('#cmWeekDel'); await settle(p, 800); await p.click('#cmWeekDel'); await settle(p);
  eq(await p.evaluate(() => [document.getElementById('cmWeekOn').checked, state.weekKept, !!cmpA, document.getElementById('cmWeekDel').hidden, document.querySelector('#cmOut .cm-say') ? document.querySelector('#cmOut .cm-say').textContent : null]),
    [true, false, false, true, 'No copy is kept at the moment. Keeping is still switched on, so a new copy is made the next time this opens.'], 'W8 deleting while keeping is switched on leaves no copy behind in the same press, and says when the next one is made');
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
  for (const [id, open] of [['settings', "showApp('settings'); document.querySelectorAll('#settings details').forEach(d => { d.open = true; })"], ['ask', 'openAsk()'], ['cmp', 'openCompare()']]) {
    await p.evaluate(o => { APP_WINS.forEach(closeApp); eval(o); }, open); await settle(p, 250);
    eq(await p.evaluate(i => { const w = document.getElementById(i), r = w.getBoundingClientRect(), b = w.querySelector('.onbar').getBoundingClientRect(); return [w.scrollHeight > w.clientHeight + 40, w.scrollTop, b.bottom <= Math.min(r.bottom, innerHeight) + 1, b.top >= r.top, b.height >= 24]; }, id),
      [true, 0, true, true, true], `K3 on a phone the line stays in sight at the foot of the ${id} window, without scrolling to its end`);
  }
  await p.close();

  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
