// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 16 in the browser simulation: the notes reader (parity with the app's fixture), Notes to my to-do list, My to-do list, its shortcut and
// settings, Settings that arrange themselves, the Getting started card, the third preset agent, 40-100% transparency, and the attack cases.
const { chromium } = require('playwright');
const fs = require('fs');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed#all';
const GOLDEN = JSON.parse(fs.readFileSync(SIMROOT + '/mockup/mytodo-golden.json', 'utf8'));
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);
(async () => {
  const b = await chromium.launch();
  const errs = [];
  const fresh = async (opts = {}) => {
    const ctx = await b.newContext({ viewport: opts.viewport || { width: 1366, height: 800 }, permissions: ['clipboard-read', 'clipboard-write'] });
    const p = await ctx.newPage();
    p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error') errs.push('console: ' + m.text()); });
    if (opts.clock) await p.clock.install({ time: new Date('2026-07-16T10:42:00') });
    await p.goto(URL); await p.waitForTimeout(300);
    return p;
  };
  const settle = (p, ms = 160) => p.waitForTimeout(ms);
  const txt = (p, sel) => p.$eval(sel, e => e.textContent);
  const vis = (p, sel) => p.isVisible(sel);
  const openSettings = async p => { await p.click('#openSettingsBtn'); await settle(p); };
  const openSecs = p => p.$$eval('#settings details.sec', n => n.filter(d => d.open).map(d => d.id.slice(3).toLowerCase()));
  const notesWith = async (p, text, wait = 450) => { await p.evaluate(() => openNotes("")); await p.fill('#ntText', text); await settle(p, wait); };
  const rowsOf = p => p.$$eval('#ntFound .ntrow', n => n.map(r => ({ title: r.querySelector('input[type=text]').value, ticked: r.querySelector('input[type=checkbox]').checked })));
  const listTitles = p => p.$$eval('#clRows .cr .t b', n => n.map(x => x.textContent));
  const alphaOf = c => { const m = c.match(/[\d.]+/g).map(Number); return m.length > 3 ? m[3] : 1; };

  // ====================================================== A. the notes reader equals the app's (shared fixture)
  let p = await fresh();
  const parity = await p.evaluate(g => {
    const [y, m, d] = g.today.split('-').map(Number), today = Date.UTC(y, m - 1, d), iso = t => t == null ? '' : new Date(t).toISOString().slice(0, 10);
    const norm = i => i && ({ title: i.title, category: i.category, due: iso(i.due), high: i.high, repeat: i.repeat, repeatDay: i.repeatDay, number: i.number || '', actionable: i.actionable });
    let bad = [];
    for (const c of g.cases) {
      const got = NP.parse(c.text, today).map(norm), one = norm(NP.parseOne(c.text, today)), due = iso(NP.readDue(c.text, today));
      if (JSON.stringify(got) !== JSON.stringify(c.items) || JSON.stringify(one || null) !== JSON.stringify(c.one || null) || due !== (c.due || '')) bad.push(c.text.slice(0, 50));
    }
    return { n: g.cases.length, bad };
  }, GOLDEN);
  eq(parity.bad, [], `the page's notes reader gives the app's answer for all ${parity.n} cases of the shared fixture`);
  await p.close();

  // ====================================================== B. Notes to my to-do list
  p = await fresh();
  await p.click('[data-pickbtn="NOTES"]'); await p.keyboard.press('Control+Alt+Shift+KeyL'); await settle(p, 500);
  ok(await vis(p, '#notes'), 'selecting the notes in Chat and pressing the to-do shortcut opens Notes to my to-do list');
  ok((await p.$eval('#ntText', e => e.value)).startsWith("Don't forget to confirm the facility inspection date"), 'the selection arrived in the notes box');

  let rows = await rowsOf(p);
  eq(rows.map(r => r.title), ['Confirm the facility inspection date with the team tomorrow ASAP', 'Email the team lead re: the Thursday huddle', 'Submit the IR response for ANDA 216482 by Friday', 'Send the weekly status update to my supervisor every Friday', 'The inspection report was very clear and complete'], 'five suggestions are read from the messy paragraph');
  eq(rows.map(r => r.ticked), [true, true, true, true, false], 'the four tasks are ticked and the remark is not');
  eq(await p.$$eval('#ntFound .ntrow', n => n.map(r => r.querySelectorAll('select')[0].value)), ['Work', 'Work', 'Work', 'Work', 'Work'], 'categories are suggested from the words');
  eq(await p.$$eval('#ntFound .ntrow .wn', n => n.map(x => x.textContent)), ['Due Fri Jul 17', 'Due Thu Jul 16', 'Due Fri Jul 17', 'Every week, starting Fri Jul 17', 'No due date'], 'each suggestion says which day was understood, or that there is none');
  ok(await p.$eval('#ntFound .ntrow:first-child input[type=checkbox]:last-of-type', e => e.checked), '"ASAP" made the first item urgent');
  eq(await txt(p, '#ntAdd'), 'Add the 4 ticked items', 'the button says how many will be added');
  eq(await p.evaluate(() => clStore.items.length), 0, 'nothing is added until the person says so');
  // change one, untick one, add
  await p.fill('#ntFound .ntrow:first-child input[type=text]', 'Confirm the inspection date with the facility team');
  await p.uncheck('#ntFound .ntrow:nth-child(4) > input[type=checkbox]');
  eq(await txt(p, '#ntAdd'), 'Add the 3 ticked items', 'unticking one updates the count');
  await p.click('#ntAdd'); await settle(p);
  eq(await p.evaluate(() => clStore.items.map(i => [i.title, i.category, i.high, i.source, i.number])), [['Confirm the inspection date with the facility team', 0, true, 'Notes', null], ['Email the team lead re: the Thursday huddle', 0, false, 'Notes', null], ['Submit the IR response for ANDA 216482 by Friday', 0, false, 'Notes', '216482']], 'the three ticked items were added, with the change, as "Notes" items, the number kept');
  ok((await txt(p, '#ntStatus')).startsWith('Added 3 items to your to-do list'), 'the window says what was added');
  ok(await vis(p, '#ntUndo'), 'Undo is offered for that batch');
  await p.click('#ntUndo'); await settle(p);
  eq(await p.evaluate(() => clStore.items.length), 0, 'Undo removes the batch');
  ok((await txt(p, '#ntStatus')).includes('Removed 3 items'), 'and says so');
  eq((await rowsOf(p)).length, 5, 'the suggestions are shown again');
  // an unreadable date blocks the whole batch, beside the item
  await p.fill('#ntFound .ntrow:first-child input[type=text]:not([maxlength="200"])', 'sometime soon');
  await p.click('#ntAdd'); await settle(p);
  eq(await p.evaluate(() => clStore.items.length), 0, 'a date that cannot be read blocks the whole batch');
  ok((await txt(p, '#ntStatus')).startsWith('Nothing was added yet. Please fix 1 item'), 'the window says which fix is needed');
  ok(await p.$eval('#ntFound .ntrow:first-child .prob', e => !e.hidden && e.textContent.includes('could not read that date')), 'the problem is shown beside the item');
  await p.fill('#ntFound .ntrow:first-child input[type=text]:not([maxlength="200"])', 'tomorrow');
  await p.click('#ntAdd'); await settle(p);
  eq(await p.evaluate(() => clStore.items.length), 4, 'with the date fixed, everything goes through');
  await p.close();

  // remarks are offered but not ticked; empty and remark-only notes say so
  p = await fresh();
  await notesWith(p, 'The inspection report was very clear and complete.\ncall the applicant');
  rows = await rowsOf(p);
  eq(rows, [{ title: 'The inspection report was very clear and complete', ticked: false }, { title: 'Call the applicant', ticked: true }], 'a remark is offered but not ticked; a task is ticked');
  ok(await p.$eval('#ntFound .ntrow:first-child .nt', e => e.textContent.includes('reads like a note, not a task')), 'the remark says why it is not ticked');
  await notesWith(p, '');
  ok((await txt(p, '#ntFoundNote')).includes('as soon as there are notes') && await p.$eval('#ntAdd', e => e.disabled), 'empty notes: a hint and a disabled Add');
  await notesWith(p, 'Hi Sam,\nThanks,\nRegards');
  ok((await txt(p, '#ntFoundNote')).includes('did not find anything that looks like a task'), 'a greeting alone gives no items and says so');
  await p.close();

  // add at once (the person's own choice in Settings), with Undo
  p = await fresh();
  await p.evaluate(() => { state.clMode = 1; });
  await p.evaluate(() => openNotes("call the applicant tomorrow\nThe inspection report was clear and complete.\nemail the team lead"));
  await settle(p, 500);
  eq(await p.evaluate(() => clStore.items.map(i => i.title)), ['Call the applicant tomorrow', 'Email the team lead'], 'add at once adds the task-like lines and leaves the remark');
  ok((await txt(p, '#ntStatus')).includes('added at once, as you chose in Settings'), 'it says it was added at once');
  await p.click('#ntUndo'); await settle(p);
  eq(await p.evaluate(() => clStore.items.length), 0, 'Undo takes the whole batch back');
  await p.close();

  // a terminal: paused, with a plain notice
  p = await fresh();
  await p.click('#appPick [data-app="term"], #appPick button:has-text("Terminal")').catch(() => {});
  await p.evaluate(() => { focusedWin = "term"; });
  await p.click('#keyList'); await settle(p, 300);
  ok(await vis(p, '#notes') && (await txt(p, '#chipTxt')).includes('Paused in terminals'), 'in a terminal the shortcut is paused with a notice and still opens an empty notes window');
  await p.close();

  // ====================================================== C. My to-do list
  p = await fresh();
  await p.click('#openMyTodoBtn'); await settle(p);
  ok(await vis(p, '#mytodo'), 'the guide button opens My to-do list');
  ok((await txt(p, '#clSummary')).includes('empty') && (await txt(p, '#clSummary')).includes('Ctrl+Alt+Shift+L'), 'empty: it says how to start and names the shortcut');
  await p.fill('#clAddIn', 'submit the expense report on the 1st every month'); await settle(p, 60);
  const preview = await txt(p, '#clPreview');
  ok(preview.startsWith('Will add: ') && preview.includes('Work') && preview.includes('repeats every month'), `typing shows what will be added (${preview})`);
  await p.click('#clAdd'); await settle(p);
  eq(await p.evaluate(() => clStore.items.map(i => [i.title, i.category, i.repeat, i.repeatDay, new Date(i.due).toISOString().slice(0, 10)])), [['Submit the expense report on the 1st every month', 0, 4, 1, '2026-08-01']], 'the item is stored with its category, repeat and first day');
  ok((await txt(p, '#clStatus')).startsWith('Added: '), 'a confirmation is shown');
  await p.fill('#clAddIn', 'submit the expense report on the 1st every month'); await p.click('#clAdd');
  eq(await txt(p, '#clStatus'), 'That is already on your list.', 'the same thing twice is not added twice');
  await p.fill('#clAddIn', '   '); await p.click('#clAdd');
  eq(await txt(p, '#clStatus'), 'Please type what needs doing.', 'empty input explains itself');
  await p.evaluate(() => { const t = clToday(), D = DAY; clAdd('Renew the vehicle registration', 1, t - 2 * D, false, 0, -1, null, 'You'); clAdd('Send the status memo', 0, t, true, 0, -1, null, 'You'); clAdd('Book the dentist appointment', 1, t + 3 * D, false, 0, -1, null, 'You'); });
  await settle(p);
  eq(await p.$$eval('#clRows .gh', n => n.map(x => x.textContent)), ['OVERDUE · 1', 'TODAY · 1', 'COMING UP · NEXT 7 DAYS · 1', 'ANYTIME · 1'], 'items are grouped worst first');
  eq(await txt(p, '#clFocus'), 'Start with: Renew the vehicle registration', 'it says what to start with');
  ok((await txt(p, '#clSummary')).includes('4 open') && (await txt(p, '#clSummary')).includes('1 overdue'), 'the summary counts open and overdue');
  ok(await p.$eval('#clRows .gh.bad', e => e.textContent.startsWith('OVERDUE')), 'the overdue heading is drawn in the warning colour');
  // tick, progress
  await p.click('input[aria-label="Mark finished: Send the status memo"]'); await settle(p);
  ok((await txt(p, '#clSummary')).includes('1 of 2 done today'), 'ticking updates "done today"');
  eq(await p.$eval('#clBarFill', e => e.style.width), '50%', 'the progress bar follows');
  // move, filter
  await p.click('button[aria-label="Move to tomorrow: Book the dentist appointment"]'); await settle(p);
  eq(await p.evaluate(() => new Date(clStore.items.find(i => i.title === 'Book the dentist appointment').due).toISOString().slice(0, 10)), '2026-07-17', 'Tomorrow moves the item to the next day');
  await p.selectOption('#clFilter', 'Work'); await settle(p);
  eq(await listTitles(p), ['Submit the expense report on the 1st every month', 'Send the status memo'], 'the Work filter shows only work (the two work items, the finished one last)');
  await p.selectOption('#clFilter', 'Everything'); await settle(p);
  // edit: unreadable day refused, readable day saved
  await p.click('button[aria-label="Edit: Book the dentist appointment"]'); await settle(p);
  ok(await vis(p, '#clRows .ced'), 'Edit opens an editor under the item');
  await p.fill('#clRows .ced input[aria-label="What needs doing for Book the dentist appointment"]', 'Book the dentist appointment for December');
  await p.fill('#clRows .ced input[aria-label^="When it is due"]', 'not a date');
  ok((await txt(p, '#clRows .ced .wn')).includes('could not read that date'), 'the editor explains an unreadable day as it is typed');
  await p.click('#clRows .ced button:has-text("Save")'); await settle(p);
  ok(await p.evaluate(() => clStore.items.some(i => i.title === 'Book the dentist appointment')) && await vis(p, '#clRows .ced'), 'an unreadable day is refused and nothing is changed');
  await p.fill('#clRows .ced input[aria-label^="When it is due"]', 'friday');
  await p.click('#clRows .ced button:has-text("Save")'); await settle(p);
  eq(await p.evaluate(() => { const i = clStore.items.find(i => i.title === 'Book the dentist appointment for December'); return i && new Date(i.due).toISOString().slice(0, 10); }), '2026-07-17', 'a readable day is saved (Friday is Jul 17)');
  // editing keeps an unchanged day exactly (an overdue date typed back would otherwise roll to next year)
  await p.evaluate(() => { const i = clStore.items.find(x => x.title === 'Renew the vehicle registration'); i.due = Date.UTC(2026, 5, 1); clChanged(); });
  await settle(p);
  await p.click('button[aria-label="Edit: Renew the vehicle registration"]'); await settle(p);
  ok((await p.$eval('#clRows .ced input[aria-label^="When it is due"]', e => e.value)) === 'Mon Jun 1', 'the editor shows the old day as words');
  await p.click('#clRows .ced button:has-text("Save")'); await settle(p);
  eq(await p.evaluate(() => new Date(clStore.items.find(x => x.title === 'Renew the vehicle registration').due).toISOString().slice(0, 10)), '2026-06-01', 'saving without touching the day keeps it exactly (it does not jump to next year)');
  // delete and undo
  await p.click('button[aria-label="Delete: Renew the vehicle registration"]'); await settle(p);
  ok(await p.evaluate(() => !clStore.items.some(i => i.title === 'Renew the vehicle registration')) && await vis(p, '#clUndo'), 'Delete removes it and offers Undo');
  await p.click('#clUndo'); await settle(p);
  ok(await p.evaluate(() => clStore.items.some(i => i.title === 'Renew the vehicle registration')), 'Undo puts it back');
  // roll over, clear finished
  await p.click('#clRoll'); await settle(p);
  eq(await p.evaluate(() => new Date(clStore.items.find(x => x.title === 'Renew the vehicle registration').due).toISOString().slice(0, 10)), '2026-07-16', 'Move overdue to today moves a missed item to today');
  await p.click('#clClear'); await settle(p);
  ok(!(await listTitles(p)).includes('Send the status memo') || await p.evaluate(() => clStore.items.every(i => !i.done || i.repeat !== 0)), 'Clear finished removes finished items that do not repeat');
  // ANDA number opens the card
  await p.evaluate(() => clAdd('Submit the IR response for ANDA 216482', 0, clToday(), false, 0, -1, '216482', 'Notes')); await settle(p);
  await p.click('button[aria-label="Open the card for ANDA 216482"]'); await settle(p, 500);
  ok(await vis(p, '#card'), 'the number button opens the card (in front of the list)');
  await p.close();

  // a repeating item comes back the next day, and a missed one moves on instead of piling up (the day changes while the window is open)
  p = await fresh({ clock: true });
  await p.evaluate(() => { openMyTodo(); clAdd('Check the inbox', 0, clToday(), false, 1, -1, null, 'You'); clAdd('Update the tracking sheet', 0, clToday(), false, 3, 4, null, 'You'); });
  await settle(p);
  await p.click('input[aria-label="Mark finished: Check the inbox"]'); await settle(p);
  ok(await p.evaluate(() => clStore.items.find(i => i.title === 'Check the inbox').done), 'ticked today');
  await p.evaluate(() => { state.pretend = true; });          // midnight passes: the page's date jumps (Oct 1)
  await p.clock.runFor(31000); await settle(p);
  eq(await p.evaluate(() => { const i = clStore.items.find(x => x.title === 'Check the inbox'); return [i.done, new Date(i.due).toISOString().slice(0, 10)]; }), [false, '2026-10-01'], 'the daily item is back, open, due the new day, without the window being reopened');
  eq(await p.evaluate(() => { const i = clStore.items.find(x => x.title === 'Update the tracking sheet'); return [new Date(i.due).toISOString().slice(0, 10), new Date(i.due).getUTCDay()]; }), ['2026-10-01', 4], 'a missed weekly item moves to its next Thursday (not a pile of overdue copies)');
  eq(await p.evaluate(() => clStore.items.length), 2, 'there are still only two items');
  await p.close();

  // ====================================================== D. the shortcut and the tray
  p = await fresh();
  await openSettings(p); await p.click('#adaptExpand');
  eq(await p.$eval('#keyBoxList', e => e.value), 'Ctrl+Alt+Shift+L', 'the to-do shortcut starts as Ctrl+Alt+Shift+L');
  await p.focus('#keyBoxList'); await p.keyboard.press('Control+Alt+KeyK'); await settle(p, 100);
  eq(await p.evaluate(() => state.keys.list), 'Ctrl+Alt+K', 'pressing keys in the box chooses a new shortcut');
  await p.focus('#keyBoxList'); await p.keyboard.press('Control+Alt+KeyA'); await settle(p, 100);
  eq(await p.evaluate(() => state.keys.list), 'Ctrl+Alt+K', 'the keys of Select & Peek are refused');
  ok((await txt(p, '#listKeyStatus')).includes('cannot use the same shortcut'), 'and the reason is shown beside the box');
  await p.focus('#keyBoxList'); await p.keyboard.press('Control+KeyK'); await settle(p, 100);
  eq(await p.evaluate(() => state.keys.list), 'Ctrl+Alt+K', 'a single modifier is refused');
  await p.click('[data-keyclear="list"]'); await settle(p, 100);
  eq(await p.evaluate(() => state.keys.list), '', 'None clears the shortcut');
  ok((await txt(p, '#keyList')).includes('no shortcut') && (await p.$eval('#keyBoxList', e => e.value)) === 'None', 'the button and the box say there is no shortcut');
  await p.keyboard.press('Control+Alt+Shift+KeyL'); await settle(p, 200);
  ok(!(await vis(p, '#notes')), 'with no shortcut, the old keys do nothing');
  await p.click('[data-keyreset="list"]'); await settle(p, 100);
  eq(await p.evaluate(() => state.keys.list), 'Ctrl+Alt+Shift+L', 'Reset brings back Ctrl+Alt+Shift+L');
  // a clash with one of the person's own actions
  await p.evaluate(() => { state.actions[0].hotkey = 'Ctrl+Alt+J'; renderActions(); });
  await p.focus('#keyBoxList'); await p.keyboard.press('Control+Alt+KeyJ'); await settle(p, 100);
  eq(await p.evaluate(() => state.keys.list), 'Ctrl+Alt+Shift+L', 'a shortcut an action already uses is refused');
  // mode radios
  await p.check('input[name=clmode][value="1"]');
  eq(await p.evaluate(() => state.clMode), 1, 'the mode radio changes what the shortcut does');
  // another program already owns it
  await p.click('#setDone');
  await p.check('#swConflictList'); await settle(p, 300);
  ok((await txt(p, '#chipTxt')).includes('already used by another program') && (await txt(p, '#chipTxt')).includes('choose another shortcut for My to-do list'), 'a clash with another program gives a plain notice naming the fix');
  await p.keyboard.press('Control+Alt+Shift+KeyL'); await settle(p, 200);
  ok(!(await vis(p, '#notes')), 'and the shortcut does nothing while another program owns it');
  ok((await p.evaluate(() => hotkeyLine())).includes('Ctrl+Alt+Shift+L ✗ in use'), 'the tray line says it is in use');
  await p.uncheck('#swConflictList');
  await p.evaluate(() => openMenu()); await settle(p, 100);
  const menu = await p.$$eval('#menu button', n => n.map(x => x.textContent));
  ok(menu.some(x => x.startsWith('My to-do list…')) && menu.some(x => x === 'Notes to my to-do list…') && menu.some(x => x.includes('Ctrl+Alt+Shift+L ✓')), 'the tray menu lists My to-do list, Notes to my to-do list and the live shortcut');
  await p.click('#menu >> text=Notes to my to-do list…'); await settle(p);
  ok(await vis(p, '#notes'), 'the tray item opens the notes window');
  await p.close();

  // ====================================================== E. Settings that arrange themselves
  p = await fresh();
  await openSettings(p);
  eq(await openSecs(p), [], 'a new install with nothing to fix opens nothing: every section is collapsed (round 17 removed the "start here" rule)');
  eq(await p.$$eval('#settings .pillnote:not([hidden])', n => n.map(x => x.textContent)), [], 'no section needs a note when everything is in order');
  ok(!(await p.$('#gs')) && !(await p.$('#gsStep1')), 'there is no Getting started card (round 17)');
  ok((await txt(p, '#adaptText')).startsWith('Everything is in order, so every section is collapsed'), 'a line says why everything is collapsed');
  ok(!(await vis(p, '#actList')) && !(await vis(p, '#setLook')), 'collapsed sections show nothing');
  await p.click('#adaptExpand'); eq((await openSecs(p)).length, 8, 'Expand all opens every section');
  await p.click('#adaptCollapse'); eq((await openSecs(p)).length, 0, 'Collapse all closes every section');
  await p.click('#adaptSuggested'); eq(await openSecs(p), [], 'Suggested view goes back to what matters (nothing, when all is well)');
  // jump list opens a collapsed section
  await p.selectOption('#setJump', { label: 'My agents · Alert Hub, Patent Analysis, Triaging Agent and your own' }); await settle(p, 250);
  ok((await openSecs(p)).includes('actions') && await vis(p, '#agList'), 'choosing a section in "Go to" opens it first');
  const jump = await p.$$eval('#setJump option', n => n.map(x => x.textContent));
  ok(jump.includes('Look and feel') && jump.includes('My to-do list') && jump.some(x => x.startsWith('Appearance')), 'the list has the group names and the labels');
  // keyboard: Enter on a heading toggles it
  await p.focus('#secDates > summary'); await p.keyboard.press('Enter'); await settle(p, 100);
  ok((await openSecs(p)).includes('dates'), 'Enter on a heading opens it (keyboard)');
  await p.keyboard.press('Enter'); await settle(p, 100);
  ok(!(await openSecs(p)).includes('dates'), 'and closes it');
  await p.click('#setDone'); await openSettings(p);
  eq(await openSecs(p), [], 'closing and opening again, with nothing wrong, every section starts collapsed');
  ok((await txt(p, '#adaptText')).includes('Everything is in order'), 'and a line says so');
  // something needs a look: another program owns Select & Peek
  await p.click('#setDone'); await p.check('#swConflict'); await settle(p, 300);
  await openSettings(p);
  eq(await openSecs(p), ['start'], 'a shortcut another program owns opens its section');
  ok((await txt(p, '#pillStart')).startsWith('Needs a look: '), 'with a "Needs a look" reason');
  ok(await p.$eval('#pillStart', e => e.classList.contains('attn')), 'drawn as a warning');
  await p.click('#setDone'); await p.uncheck('#swConflict');
  // a connection that is set up but not tested needs a look
  await openSettings(p); await p.click('#adaptExpand'); await p.check('input[name=provider][value=claude]'); await p.click('#setDone'); await openSettings(p);
  ok((await openSecs(p)).includes('connections') && (await txt(p, '#pillConnections')).includes('set up but not tested'), 'an AI connection that was set up but not tested opens its section');
  await p.click('#setDone');
  await p.close();

  // counting: once per visit, learned across visits, shown after three, forgotten on request, not kept when arranging is off
  p = await fresh();
  
  for (let i = 1; i <= 3; i++) {
    await openSettings(p);
    await p.click('#secDates > summary'); await p.click('#secDates > summary'); await p.click('#secDates > summary');        // opening and closing in one visit counts once
    await p.click('#setDone');
    eq(await p.evaluate(() => state.sectionUse.dates), i, `visit ${i}: "Dates" has been opened ${i} time(s)`);
  }
  await openSettings(p);
  eq(await openSecs(p), ['dates'], 'on the fourth visit the section opened often opens by itself');
  eq(await txt(p, '#pillDates'), 'You open this often', 'and says why');
  await p.click('#adaptExpand'); await p.click('#adaptReset'); await settle(p, 60);
  eq(await p.evaluate(() => state.sectionUse), {}, 'Forget what I use most clears the counts');
  await p.uncheck('#adaptCheck'); await settle(p, 60);
  eq(await openSecs(p), [], 'with arranging off, nothing is opened for the person');
  ok((await txt(p, '#adaptText')).includes('stay as you leave them'), 'and the line says so');
  const before = await p.evaluate(() => JSON.stringify(state.sectionUse));
  await p.click('#secLook > summary'); await p.click('#secLook > summary'); await p.focus('#keyBoxPeek'); await p.click('#secDates > summary');
  eq(await p.evaluate(() => JSON.stringify(state.sectionUse)), before, 'with arranging off, no counts are kept from then on');
  await p.close();

  // backups: the preference travels, the counts and the hidden card stay on this PC; Reset forgets them
  p = await fresh();
  await p.evaluate(() => { state.sectionUse = { look: 5 }; state.adaptive = false; state.clMode = 1; });
  const backup = await p.evaluate(() => JSON.parse(backupText()));
  ok(backup.settings.adaptive === false && backup.settings.myTodoMode === 1 && !('sectionUse' in backup.settings) && !('gsDone' in backup.settings) && !('gettingStartedDone' in backup.settings), 'a backup holds the arranging choice and the to-do list mode, but not the counts or the hidden card');
  await p.evaluate(() => { state.adaptive = true; state.clMode = 0; });
  await p.evaluate(b => showImport(JSON.stringify(b)), backup);
  eq(await p.evaluate(() => [state.adaptive, state.clMode, state.sectionUse]), [false, 1, { look: 5 }], 'a restore brings the choices back and leaves the local counts alone');
  await openSettings(p); await p.click('#adaptExpand');
  await p.click('#bkReset'); await p.click('#bkReset'); await settle(p, 100);
  eq(await p.evaluate(() => [state.adaptive, state.clMode, state.sectionUse, state.keys.list]), [true, 0, {}, 'Ctrl+Alt+Shift+L'], 'Reset goes back to a new install, including what the window had learned');
  // hostile restore: markup in the to-do list key, a clashing key, an out-of-range mode
  const hostile = await p.evaluate(() => { const b = JSON.parse(backupText()); b.settings.keys.list = '<img src=x onerror=window.__pwn=1>'; b.settings.myTodoMode = 9; b.settings.adaptive = 'yes'; return b; });
  await p.evaluate(b => showImport(JSON.stringify(b)), hostile);
  eq(await p.evaluate(() => [state.keys.list, state.clMode, state.adaptive, window.__pwn || 0]), ['Ctrl+Alt+Shift+L', 0, true, 0], 'a hand-edited backup is repaired: bad key becomes the default, bad mode and flag are corrected, nothing runs');
  const clash = await p.evaluate(() => { const b = JSON.parse(backupText()); b.settings.keys.list = b.settings.keys.peek; return b; });
  await p.evaluate(b => showImport(JSON.stringify(b)), clash);
  eq(await p.evaluate(() => state.keys.list), '', 'a to-do key that clashes with another shortcut becomes none');
  await p.close();

  // ====================================================== F. the third agent and the transparency range
  p = await fresh();
  await openSettings(p); await p.click('#adaptExpand');
  eq(await p.$$eval('#agList .bx input[aria-label="Agent name"]', n => n.map(x => x.value)), ['Alert Hub', 'Patent Analysis', 'Triaging Agent'], 'three preset agents are listed: Alert Hub, Patent Analysis, Triaging Agent');
  ok(await p.evaluate(() => isPreset(state.agents[1]) && state.agents.length === 3), 'Patent Analysis is a preset (it cannot be removed)');
  ok((await txt(p, '#settings')).includes('Alert Hub, Patent Analysis and Triaging Agent are listed so they can be set up'), 'the hint names all three and says their addresses are not guessed');
  eq(await p.evaluate(() => standInLines('216482', 'Patent Analysis')), ['No patent or exclusivity information is held in this sample data.'], 'the stand-in for Patent Analysis claims nothing it does not have');
  eq(await p.$eval('#setOp', e => [e.min, e.max, e.value]), ['40', '100', '90'], 'the transparency slider runs 40 to 100 and starts at 90 (round 20; 92 before)');
  await p.click('#setDone'); await p.click('[data-pickbtn="216482"]'); await p.keyboard.press('Control+Alt+KeyA'); await settle(p, 700);
  const a92 = alphaOf(await p.$eval('#card', e => getComputedStyle(e).backgroundColor));
  ok(a92 > 0.89 && a92 < 0.91, `the card surface starts about 90% opaque (alpha ${a92})`);
  await p.close();

  // ====================================================== G. attack cases for the notes reader and the to-do list
  p = await fresh();
  const evil = `<img src=x onerror="window.__pwn=1"> call <b>Sam</b> tomorrow\n- "><script>window.__pwn=2</script> email Pat\n[x] {{7*7}} \${7*7} \`ls\` and send the memo`;
  await notesWith(p, evil);
  await p.click('#ntAdd'); await settle(p);
  await p.evaluate(() => openMyTodo()); await settle(p);
  ok(await p.evaluate(() => !window.__pwn && !document.querySelector('#notes img, #mytodo img, #notes script, #mytodo script, #notes b, #mytodo b:not(.t b)')), 'markup in notes is shown as text and never runs or becomes elements');
  ok((await listTitles(p)).some(t => t.includes('<img src=x')) || (await listTitles(p)).length >= 1, 'the items are listed (their text is data)');
  // huge and pathological input is bounded
  const t0 = Date.now();
  await p.evaluate(() => { openNotes("x".repeat(20000)); });
  const capped = await p.$eval('#ntText', e => e.value.length);
  eq(capped, 6000, 'a huge paste is cut to 6,000 characters');
  ok((await txt(p, '#ntHint')).includes('Only the first 6,000 characters were read'), 'and the window says so');
  const slow = await p.evaluate(() => {
    const today = clToday(), t = performance.now();
    NP.parse("a".repeat(5000) + " " + "and ".repeat(1200), today); NP.parse("1/".repeat(2900), today); NP.parse(" ".repeat(5000) + "x", today); NP.parse("by ".repeat(1900), today);
    NP.parse(("call the applicant and email the team lead, ").repeat(180), today); NP.parse("(".repeat(5000), today); NP.parse("\n".repeat(5000) + "call Sam", today);
    return Math.round(performance.now() - t);
  });
  ok(slow < 3000, `pathological notes (long runs, repeated connectors, thousands of lines) are read in ${slow} ms`);
  eq(await p.evaluate(() => NP.parse(("submit the report. ").repeat(900), clToday()).length), 1, 'the same task repeated nine hundred times is offered once');
  eq(await p.evaluate(() => NP.parse(Array.from({ length: 200 }, (_, i) => `call person number ${i}`).join("\n"), clToday()).length), 30, 'at most 30 items are offered, whatever the length');
  // control characters, weird unicode and tiny inputs never throw
  eq(await p.evaluate(() => { const bad = ["", " ", "\u0000\u0001", "ß".repeat(300), "\ud800", "😀 call Sam 😀", "call‮Sam", "1/1/9999 call Sam", "Feb 30 call Sam", "in 99999999 days call Sam"]; return bad.map(t => { try { NP.parse(t, clToday()); NP.parseOne(t, clToday()); return 0; } catch (e) { return 1; } }).reduce((a, c) => a + c, 0); }), 0, 'odd inputs (nulls, lone surrogates, huge numbers, impossible dates) never throw');
  // the item limits
  eq(await p.evaluate(() => { for (let i = 0; i < 520; i++) clAdd('item ' + i, 2, null, false, 0, -1, null, 'You'); return [clStore.items.length, clAdd('one more', 2, null, false, 0, -1, null, 'You').reason]; }), [500, 'The to-do list is full (500 items). Please finish or delete some.'], 'the list holds 500 items and says so when it is full');
  eq(await p.evaluate(() => clAdd('x'.repeat(500), 2, null, false, 0, -1, null, 'You').reason), 'The to-do list is full (500 items). Please finish or delete some.', 'a full list refuses before anything else');
  await p.close();
  p = await fresh();
  eq(await p.evaluate(() => clAdd('x'.repeat(500), 2, null, false, 0, -1, null, 'You').item.title.length), 200, 'a title is cut to 200 characters');
  eq(await p.evaluate(() => clAdd('a\u0000b\tc\nd', 2, null, false, 0, -1, null, 'Evil\u0000<b>source</b>').item.title), 'a b c d', 'control characters in a title become spaces');
  eq(await p.evaluate(() => clAdd('Number test', 2, null, false, 0, -1, '12345; DROP', 'You').item.number), null, 'a number that is not six digits is not kept');
  await p.close();

  // ====================================================== H. phones: nothing scrolls sideways
  for (const [label, w] of [['390 px', 390], ['320 px', 320]]) {
    p = await fresh({ viewport: { width: w, height: 800 } });
    for (const win of ['settings', 'mytodo', 'notes']) {
      await p.evaluate(id => { document.querySelectorAll('.app-win').forEach(e => e.style.display = 'none'); if (id === 'mytodo') { clAdd('A task with a fairly long title that has to wrap across more than one line on a phone', 0, clToday(), true, 3, 4, '216482', 'Notes'); openMyTodo(); } else if (id === 'notes') openNotes("Call the plumber about the leak under the sink tomorrow!! also email Sam"); else { showApp('settings'); document.getElementById('adaptExpand').click(); } }, win);
      await settle(p, 200);
      const over = await p.evaluate(id => { const e = document.getElementById(id); return [document.documentElement.scrollWidth > innerWidth + 1, e.scrollWidth > e.clientWidth + 1]; }, win);
      ok(!over[0] && !over[1], `${label}: ${win} does not scroll sideways`);
    }
    await p.close();
  }

  ok(errs.length === 0, 'no page errors during the whole run: ' + JSON.stringify(errs.slice(0, 4)));
  await b.close();
  console.log(`\n${checks - fails}/${checks} checks passed`);
  process.exit(fails ? 1 : 0);
})();
