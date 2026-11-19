// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 30 in the browser simulation: every change to a list keeps a copy of it; one press brings a copy back, and that can be undone; the
// person chooses how many copies are kept; finished to-do items can be searched in the archive and put back; the person chooses after how
// many days a finished item is archived. Nothing is deleted from a list or from the archive by the app.
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
  const copies = p => p.evaluate(() => Object.fromEntries(BAK_LISTS.map(l => [l.id, (bakStore[l.id] || []).length])));
  const sub = (p, k) => p.evaluate(i => document.querySelectorAll('#bkLists .bk-list')[i].querySelector('.sub').textContent, k);
  const status = p => p.evaluate(() => document.getElementById('bkStatus').textContent);

  // ====================================================== B. A copy after every change, and one press to bring one back
  let p = await fresh();
  await p.evaluate(() => openMenu()); await settle(p, 200);
  eq(await p.evaluate(() => [...document.querySelectorAll('#menu button')].some(x => x.textContent.startsWith('Backups and archive…'))), true, 'B1 the tray menu offers "Backups and archive…"');
  await p.evaluate(() => { closeMenus(); openBackups(); }); await settle(p);
  eq([await p.evaluate(() => document.getElementById('bak').style.display), await copies(p), await p.evaluate(() => [...document.querySelectorAll('#bkLists .nm')].map(x => x.textContent))],
    ['block', { todo: 1, mytodo: 1, notes: 1, prompts: 1, replies: 1 }, ['ANDA to-do list', 'My to-do list', 'Notes on ANDAs', 'Prompt library', 'Quick replies']], 'B2 each of the five lists has a copy from the start, and the window names them');
  eq(await p.evaluate(() => [...document.querySelectorAll('#bkLists .bk-list button')].map(x => x.disabled)), [true, true, true, true, true], 'B3 with one copy, which is the list as it is now, there is nothing to restore');
  await p.evaluate(() => { todoAdd('Send the meeting minutes', '216482', null, null, 'You'); }); await settle(p);
  await p.evaluate(() => { todoAdd('Ask about the facility inspection', null, null, null, 'You'); }); await settle(p);
  eq([(await copies(p)).todo, /^3 copies · newest .+ (EST|EDT)$/.test(await sub(p, 0)), await p.evaluate(() => [...document.querySelectorAll('#bkLists .bk-list')[0].querySelectorAll('option')].map(o => o.textContent.replace(/^.* · /, '')))],
    [3, true, ['2 items (as it is now)', '1 item', '0 items']], 'B4 each change to a list keeps a copy: when it was made, in the time zone of this computer, and how much it holds');
  eq(await p.evaluate(() => { const l = document.querySelectorAll('#bkLists .bk-list')[0], s = l.querySelector('select'), g = l.querySelector('button'); const first = g.disabled; s.selectedIndex = 1; s.dispatchEvent(new Event('change')); return [first, g.disabled]; }), [true, false], 'B5 "Restore this copy" is offered for an earlier copy, never for the list as it is now');
  await p.evaluate(() => document.querySelectorAll('#bkLists .bk-list')[0].querySelector('button').click()); await settle(p);
  eq([await p.evaluate(() => todoStore.items.map(i => i.title)), (await copies(p)).todo, /^ANDA to-do list: restored to the copy of [A-Z][a-z]{2} \d{1,2}, \d{4}, .+ (EST|EDT)\. What was there a moment ago is still among the copies, so this can be undone\.$/.test(await status(p)), await p.evaluate(() => evLog.map(e => e.detail).filter(d => d.includes('restored')))],
    [['Send the meeting minutes'], 4, true, ['a list was restored from a backup']], 'B6 one press brings the copy back, keeps the list as it was a moment ago as a copy, says so, and writes one line to the event log');
  await p.evaluate(() => { const l = document.querySelectorAll('#bkLists .bk-list')[0], s = l.querySelector('select'); s.selectedIndex = 1; s.dispatchEvent(new Event('change')); l.querySelector('button').click(); }); await settle(p);
  eq(await p.evaluate(() => todoStore.items.map(i => i.title)), ['Send the meeting minutes', 'Ask about the facility inspection'], 'B7 and the restore is undone by restoring the copy made a moment before it');
  await p.evaluate(() => { state.replies.push({ name: 'Round 30 reply', text: 'Thank you.' }); state.quickNotes['216482'] = [{ id: 'q9001', text: 'A note for round 30', when: 'Nov 18', t: 1 }]; prompts.push(['Round 30 prompt', 'Be brief.']); clAdd('File the weekly report', 2, null, false, 0, -1, null, 'You'); document.body.click(); }); await settle(p);
  eq(await copies(p), { todo: 5, mytodo: 2, notes: 2, prompts: 2, replies: 2 }, 'B8 the other four lists get a copy when they change, however the change was made');
  await p.evaluate(() => { for (const k of [1, 2, 3, 4]) { const l = document.querySelectorAll('#bkLists .bk-list')[k], s = l.querySelector('select'); s.selectedIndex = 1; s.dispatchEvent(new Event('change')); l.querySelector('button').click(); } }); await settle(p);
  eq(await p.evaluate(() => [clStore.items.length, Object.keys(state.quickNotes).length, prompts.some(x => x[0] === 'Round 30 prompt'), state.replies.some(r => r.name === 'Round 30 reply')]), [0, 0, false, false], 'B9 and each of them can be brought back to an earlier copy');
  eq(await p.evaluate(() => { const before = todoStore.items.length; const r = bakRestore('todo', 12345); return [r, todoStore.items.length === before, document.getElementById('bkStatus').textContent]; }), [false, true, 'That copy is no longer kept. Nothing was changed.'], 'B10 a copy that is no longer kept changes nothing, and says so');

  // ====================================================== N. How many copies are kept
  await p.evaluate(() => { for (let i = 0; i < 14; i++) { todoAdd('Change number ' + i, null, null, null, 'You'); backupSweep(); } }); await settle(p);
  { const before = (await copies(p)).todo;
    await p.focus('#bkKeep'); await p.selectOption('#bkKeep', '10'); await settle(p);
    eq([before, (await copies(p)).todo, await p.evaluate(() => state.backupKeep), await status(p), await p.evaluate(() => [document.getElementById('bkKeepGo').hidden, document.getElementById('bkKeepGo').textContent, document.getElementById('bkKeep').value, document.activeElement.id])],
      [19, 19, 30, 'Keeping 10 would remove 9 older backup copies now, and that cannot be undone. Nothing has been removed. Press “Keep 10 and remove 9 older copies” to go ahead, or choose a larger number.', [false, 'Keep 10 and remove 9 older copies', '10', 'bkKeep']],
      'N1 a smaller number removes nothing by itself: it says how many copies would go, that it cannot be undone, and offers one button to go ahead');
    await p.selectOption('#bkKeep', '30'); await settle(p);
    eq([(await copies(p)).todo, await p.evaluate(() => [state.backupKeep, document.getElementById('bkKeepGo').hidden, document.getElementById('bkKeepGo').textContent])], [19, [30, true, '']], 'N1b choosing the earlier number again withdraws the question, and nothing was removed');
    await p.selectOption('#bkKeep', '10'); await settle(p); await p.evaluate(() => { closeApp('bak'); openBackups(); }); await settle(p);
    eq([(await copies(p)).todo, await p.evaluate(() => [state.backupKeep, document.getElementById('bkKeepGo').hidden, document.getElementById('bkKeep').value])], [19, [30, true, '30']], 'N1c so does closing the window');
    await p.selectOption('#bkKeep', '10'); await settle(p); await p.click('#bkKeepGo'); await settle(p);
    eq([(await copies(p)).todo, await p.evaluate(() => state.backupKeep), await status(p), await p.evaluate(() => [todoStore.items.length, document.getElementById('bkKeepGo').hidden, document.activeElement.id])],
      [10, 10, '9 older backup copies were removed. Your lists and the archive were not changed.', [16, true, 'bkKeep']], 'N1d pressing the button removes them, says how many, leaves the list alone and keeps the keyboard on the box'); }
  await p.evaluate(() => { for (let i = 0; i < 5; i++) { todoAdd('Later change ' + i, null, null, null, 'You'); backupSweep(); } }); await settle(p);
  eq([(await copies(p)).todo, await p.evaluate(() => { const l = bakStore.todo; return l[l.length - 1].text === BAK_LISTS[0].text(); })], [10, true], 'N2 from then on that many copies are kept, and the newest is always the list as it is now');
  await p.selectOption('#bkKeep', '60'); await settle(p);
  eq(await status(p), 'From now on 60 copies of each list are kept. Nothing was removed.', 'N3 choosing a larger number removes nothing');
  eq(await p.evaluate(() => { const D = 86400000, now = Date.UTC(2026, 10, 18, 20), s = []; for (let i = 0; i < 25; i++) s.push(now - i * 60000); for (let i = 1; i <= 40; i++) s.push(now - i * D); const r = backupPlan(s, 10, 'America/New_York'); return [r.keep.length, r.remove.length, r.keep.slice(0, 10).every((x, i) => x === now - i * 60000), r.keep[10] === now - D, r.keep[18] === now - 9 * D]; }),
    [19, 46, true, true, true], 'N4 the rule: the newest copies stay, and the last copy of each of the newest days, so a busy day cannot push out the days before it');

  // ====================================================== A. The archive of finished to-do items
  eq(await p.evaluate(() => [document.getElementById('bkArchive').children.length, document.getElementById('bkCount').textContent]), [0, 'Nothing is archived yet. A finished item is moved here after 30 days, or when you clear the finished items of a list.'], 'A1 with nothing archived the window says so, and says when an item is archived');
  await p.evaluate(() => { const a = todoStore.items.find(i => i.title === 'Send the meeting minutes'), c = todoStore.items.find(i => i.title === 'Ask about the facility inspection'); todoSetDone(a.id, true); todoSetDone(c.id, true); todoClearDone(); bakPaint(); }); await settle(p);
  { const v = await p.evaluate(() => [[...document.querySelectorAll('#bkArchive .bk-arch')].map(r => r.querySelector('.t').innerText.replace(/\n/g, ' | ')), document.getElementById('bkCount').textContent, todoStore.items.some(i => i.title === 'Send the meeting minutes'), [...document.querySelectorAll('#bkArchive button')].map(x => x.textContent)]);
    ok(v[0].length === 2 && v[0].some(t => t.startsWith('ANDA 216482: Send the meeting minutes | ANDA to-do list · archived ') && t.endsWith(' · cleared by the person')) && v[1] === '2 archived items.' && v[2] === false && v[3].every(t => t === 'Put back on the list'), 'A2 finished items that were cleared are in the archive: what, which list, when and why, with a way back'); }
  await p.fill('#bkFind', 'MINUTES'); await settle(p);
  eq(await p.evaluate(() => [document.querySelectorAll('#bkArchive .bk-arch').length, document.getElementById('bkCount').textContent]), [1, '1 of 2 archived items matches.'], 'A3 the archive can be searched, whatever the case of the letters');
  await p.fill('#bkFind', 'no such thing'); await settle(p);
  eq(await p.evaluate(() => [document.querySelectorAll('#bkArchive .bk-arch').length, document.getElementById('bkCount').textContent]), [0, '0 of 2 archived items match.'], 'A4 and says when nothing matches');
  await p.fill('#bkFind', '216482'); await settle(p);
  { const lines = await p.evaluate(() => todoStore.archive.length);
    await p.click('#bkArchive button'); await settle(p);
    const v = await p.evaluate(() => { const i = todoStore.items.find(x => x.title === 'Send the meeting minutes'); return [!!i, i && i.done, i && i.doneOn, todoStore.archive.length, document.querySelectorAll('#bkArchive button').length, document.querySelector('#bkArchive .d').textContent.includes(' · put back '), document.getElementById('bkStatus').textContent, evLog.filter(e => e.detail === 'a to-do item was put back from the archive').length]; });
    eq(v, [true, false, null, lines + 1, 0, true, '“Send the meeting minutes” is back on the ANDA to-do list, as an open item.', 1], 'A5 "Put back on the list" returns the item as an open item; the archive keeps its line and adds one, and the event log says it happened'); }
  eq(await p.evaluate(() => { const id = todoStore.items.find(x => x.title === 'Send the meeting minutes').id; const n = todoStore.items.length; return [archivePutBack('todo', id), todoStore.items.length === n, document.getElementById('bkStatus').textContent]; }), [false, true, 'That item was already put back.'], 'A6 an item cannot be put back twice');
  await p.evaluate(() => { clAdd('<img src=x onerror="window.__ran=1">', 2, null, false, 0, -1, null, 'You'); const i = clStore.items[clStore.items.length - 1]; clSetDone(i.id, true, clToday()); clClearFinished(); document.getElementById('bkFind').value = ''; bakPaint(); }); await settle(p);
  eq(await p.evaluate(() => [document.querySelectorAll('#bkArchive img').length, window.__ran === undefined, [...document.querySelectorAll('#bkArchive .t')].some(t => t.textContent.includes('<img src=x') && t.textContent.includes('My to-do list'))]), [0, true, true], 'A7 a title with markup in it is text, and the other list is archived the same way');

  // ====================================================== D. After how many days a finished item is archived
  eq(await p.evaluate(() => { const DAY = 86400000, d = n => isoDate(demoToday() - n * DAY); todoAdd('Finished ten days ago', null, null, null, 'You'); const i = todoStore.items[todoStore.items.length - 1]; i.done = true; i.doneOn = d(10); todoView(); const kept = todoStore.items.includes(i); return [state.archiveDays, kept]; }), [30, true], 'D1 by default a finished item stays on its list for 30 days');
  await p.selectOption('#bkDays', '7'); await settle(p);
  eq(await p.evaluate(() => { const n = todoStore.archive.length; todoView(); const gone = !todoStore.items.some(i => i.title === 'Finished ten days ago'), a = todoStore.archive[todoStore.archive.length - 1]; return [state.archiveDays, gone, todoStore.archive.length - n, a.why, document.getElementById('bkStatus').textContent]; }),
    [7, true, 1, 'finished more than 7 days ago', 'From now on a finished item is archived after 7 days. Nothing is deleted.'], 'D2 with 7 days chosen it is archived at the next look, with the reason, and nothing is deleted');
  eq(await p.evaluate(() => { const DAY = 86400000, t = clToday(); clAdd('Old finished item', 2, null, false, 0, -1, null, 'You'); const i = clStore.items[clStore.items.length - 1]; i.done = true; i.doneOn = t - 10 * DAY; const n = clStore.archive.length; clRefresh(t); return [clStore.items.includes(i), clStore.archive.length - n, clStore.archive[clStore.archive.length - 1].why]; }), [false, 1, 'finished more than 7 days ago'], 'D3 My to-do list follows the same number');
  eq(await p.evaluate(() => { const t = clToday(); clStore.items = []; for (let k = 0; k < CL_MAX; k++) clStore.items.push({ id: 'f' + k, title: 'Item ' + k, category: 2, due: null, high: false, repeat: 0, repeatDay: -1, done: k === 0, doneOn: k === 0 ? t : null, created: t, number: null, source: 'You' }); const n = clStore.archive.length; const r = clAdd('One more', 2, null, false, 0, -1, null, 'You'); const a = clStore.archive[clStore.archive.length - 1]; return [r.added, clStore.items.length, clStore.archive.length - n, a.why, a.item.id]; }), [true, 500, 1, 'the list was full', 'f0'], 'D4 when a full list makes room, the oldest finished item is archived, not dropped');

  // ====================================================== S. The two choices are settings like any other
  eq(await p.evaluate(() => { const s = JSON.parse(backupText()).settings; return ['backupCopies' in s, 'archiveAfterDays' in s]; }), [false, false], 'S1 the two choices belong to this PC, like the lists: a settings backup does not carry them');
  eq(await p.evaluate(() => { const n = bakStore.todo.length, b = JSON.parse(backupText()); b.settings.backupCopies = 10; b.settings.archiveAfterDays = 365; importBackup(JSON.stringify(b)); const one = [state.backupKeep, state.archiveDays]; applySettings(DEFAULT_SETTINGS); todoAdd('After a restore of settings', null, null, null, 'You'); backupSweep(); return [one, [state.backupKeep, state.archiveDays], bakStore.todo.length >= n]; }), [[60, 7], [60, 7], true], 'S2 so restoring a settings backup, or resetting settings, cannot lower the number and remove copies unasked');
  eq(await p.evaluate(() => [...document.querySelectorAll('#bak button')].filter(x => !x.hidden).map(x => x.textContent).filter(t => /delete|clear|remove|empty/i.test(t))), [], 'S3 the window has no way to delete a list or the archive, and no way to remove a copy that was not asked about first');
  // ====================================================== F. After the review: the keyboard's place, labels, items waiting to be archived
  await p.evaluate(() => { document.getElementById('bkFind').value = ''; todoAdd('Focus check one', null, null, null, 'You'); backupSweep(); todoAdd('Focus check two', null, null, null, 'You'); backupSweep(); bakPaint(); }); await settle(p);
  await p.evaluate(() => { const s = document.querySelector('#bkLists [data-list="todo"] select'); s.selectedIndex = 1; s.dispatchEvent(new Event('change')); });
  await p.click('#bkLists [data-list="todo"] button'); await settle(p);
  eq(await p.evaluate(() => [document.activeElement.tagName, !!document.activeElement.closest('[data-list="todo"]')]), ['SELECT', true], 'F1 after "Restore this copy" the keyboard is on that list' + "'" + 's copies, not on nothing');
  eq(await p.evaluate(() => { const s = document.querySelector('#bkLists [data-list="todo"] select'); s.selectedIndex = 2; s.dispatchEvent(new Event('change')); const chosen = s.value; state.replies.push({ name: 'Made elsewhere', text: 'x' }); backupSweep(); return document.querySelector('#bkLists [data-list="todo"] select').value === chosen; }), true, 'F2 a copy made elsewhere while the window is open does not change which copy is chosen');
  eq(await p.evaluate(() => /^\d+ copies · newest [A-Z][a-z]{2} \d{1,2}, \d{4}, \d{1,2}:\d{2}:\d{2} (AM|PM) (EST|EDT)$/.test(document.querySelector('#bkLists [data-list="todo"] .sub').textContent)), true, 'F3 the time of a copy is shown with its year');
  eq(await p.evaluate(() => { todoStore.items.push({ id: 'unsaved1', title: 'Not yet copied', number: null, note: null, due: null, done: false, doneOn: null, snoozedUntil: null, created: '2026-07-16', source: 'You' }); bakPaint(); const none = ![...document.querySelectorAll('#bkLists [data-list="todo"] option')].some(o => o.textContent.includes('(as it is now)')); backupSweep(); bakPaint(); return [none, document.querySelector('#bkLists [data-list="todo"] option').textContent.includes('(as it is now)')]; }), [true, true], 'F4 "(as it is now)" is said only of a copy that holds what the list holds now');
  eq(await p.evaluate(() => { const year = 365 * 86400000, l = bakStore.todo; l[l.length - 1].t = Date.now() + year; todoAdd('After a clock that was ahead', null, null, null, 'You'); backupSweep(); const now = Date.now(), list = bakStore.todo; return [list.every(v => v.t <= now), list[list.length - 1].text === BAK_LISTS[0].text(), list.map(v => v.t).every((t, i, a) => i === 0 || t > a[i - 1])]; }), [true, true, true], 'F5 a copy stamped by a clock that was ahead is given a moment before now: the newest copy is the list as it is, and none is in the future');
  eq(await p.evaluate(() => { const DAY = 86400000, d = n => isoDate(demoToday() - n * DAY); document.getElementById('bkDays').value = '30'; document.getElementById('bkDays').dispatchEvent(new Event('change')); todoAdd('Finished ten days ago, waiting', '216482', null, null, 'You'); const i = todoStore.items[todoStore.items.length - 1]; i.done = true; i.doneOn = d(10); const shown = todoView().rows.some(r => r.title === i.title); bakPaint(); const row = [...document.querySelectorAll('#bkArchive .bk-arch')].find(r => r.textContent.includes('Finished ten days ago, waiting')); return [shown, todoStore.items.includes(i), !!row && row.querySelector('.d').textContent.includes('still on its list, no longer shown there · archived after 30 days'), !!row && row.querySelector('button').textContent, document.getElementById('bkCount').textContent.includes('1 finished item is still on its list, waiting to be archived.')]; }),
    [false, true, true, 'Open it again', true], 'F6 a finished item that its list no longer shows, and that is not archived yet, is found here: it is never out of sight');
  await p.fill('#bkFind', 'TEN DAYS AGO, WAITING'); await settle(p);
  await p.click('#bkArchive button'); await settle(p);
  eq(await p.evaluate(() => { const i = todoStore.items.find(x => x.title === 'Finished ten days ago, waiting'); return [i.done, i.doneOn, document.getElementById('bkStatus').textContent, document.activeElement.id]; }), [false, null, '“Finished ten days ago, waiting” is open again on the ANDA to-do list.', 'bkFind'], 'F7 "Open it again" opens it on its list, says so, and the keyboard stays in the window');
  eq(await p.evaluate(() => { document.getElementById('bkFind').value = ''; const it = { id: 'dup1', title: 'Archived twice', number: null, done: true, doneOn: '2026-07-01' }; todoStore.archive.push({ archivedOn: '2026-07-10', why: 'cleared by the person', item: it }, { archivedOn: '2026-07-12', why: 'finished more than 30 days ago', item: it }); bakPaint(); const rows = [...document.querySelectorAll('#bkArchive .bk-arch')].filter(r => r.textContent.includes('Archived twice')); const one = rows.length; rows[0].querySelector('button').click(); const after = [...document.querySelectorAll('#bkArchive .bk-arch')].filter(r => r.textContent.includes('Archived twice')); return [one, after.length, after[0].querySelectorAll('button').length, todoStore.items.filter(i => i.id === 'dup1').length]; }), [1, 1, 0, 1], 'F8 an item archived twice is shown once, and putting it back leaves no second button behind');
  eq(await p.evaluate(() => [document.getElementById('bkKeep').getAttribute('aria-label'), document.getElementById('bkDays').getAttribute('aria-label')]), ['How many backup copies of each list are kept', 'After how many days a finished item is archived'], 'F9 the two boxes say what they are for');
  eq(await axe(p, '#bak'), [], 'K1 accessibility scan of the Backups and archive window');
  await p.close();

  // ====================================================== K. Keyboard and phone
  p = await fresh();
  { await p.evaluate(() => openHandoff(null)); await settle(p, 200);
    await p.evaluate(() => { const b = document.querySelector('#handoff button'); b.focus(); openBackups(); }); await settle(p, 250);
    const inside = await p.evaluate(() => document.getElementById('bak').contains(document.activeElement));
    await p.keyboard.press('Escape'); await settle(p);
    eq([inside, await p.evaluate(() => document.getElementById('bak').style.display)], [true, 'none'], 'K2 the window takes the keyboard when it opens, and Escape closes it'); }
  eq(await p.evaluate(() => document.querySelectorAll('#bak .onbar').length), 1, 'K3 the window carries the line that says what is switched on');
  eq(await p.evaluate(() => document.getElementById('settings').textContent.includes('Notices, reminders and the tray dot appear only while the app is running:')), true, 'K6 Settings says that notices and reminders appear only while the app is running');
  await p.close();
  p = await fresh({ width: 390, height: 780 });
  await p.evaluate(() => { for (let i = 0; i < 4; i++) { todoAdd('A long title that has to wrap on a narrow screen, number ' + i, '216482', null, null, 'You'); } const a = todoStore.items[0]; todoSetDone(a.id, true); todoClearDone(); openBackups(); }); await settle(p, 300);
  eq(await p.evaluate(() => { const r = document.getElementById('bak').getBoundingClientRect(); return [r.left >= 0, r.right <= innerWidth + 1, document.documentElement.scrollWidth <= innerWidth + 1, document.getElementById('bak').scrollWidth <= document.getElementById('bak').clientWidth + 1]; }), [true, true, true, true], 'K4 on a phone the window fits the screen and does not scroll sideways');
  eq(await axe(p, '#bak'), [], 'K5 accessibility scan at phone width');
  await p.close();

  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
