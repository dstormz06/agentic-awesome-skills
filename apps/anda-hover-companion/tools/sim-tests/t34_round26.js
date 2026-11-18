// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 26 in the browser simulation: Compare two files (two versions of one report, by fixed rules), the extra buttons in Ask about a selection
// (four reading prompts, a calendar invitation, a note on an ANDA the selection names) and My usage. Attack cases are mixed in.
const { chromium } = require('playwright');
const fs = require('fs');
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
    const ctx = await b.newContext({ viewport: opts.viewport || { width: 1366, height: 768 }, permissions: ['clipboard-read', 'clipboard-write'], timezoneId: 'America/New_York', reducedMotion: 'reduce', acceptDownloads: true });
    const p = await ctx.newPage();
    p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    await p.goto(SIM); await p.waitForTimeout(300);
    await p.dblclick('#deskIcon'); await p.waitForTimeout(120);
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const txt = (p, s) => p.$eval(s, e => e.textContent);
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(s ? document.querySelector(s) : document)).violations.map(v => v.id + ': ' + v.nodes.length), sel); };
  const clip = p => p.evaluate(() => navigator.clipboard.readText());
  const download = async (p, act) => { const [d] = await Promise.all([p.waitForEvent('download'), act()]); return { name: d.suggestedFilename(), text: fs.readFileSync(await d.path(), 'utf8') }; };
  const csv = (name, text) => ({ name, mimeType: 'text/csv', buffer: Buffer.from(text, 'utf8') });

  // ====================================================== C. Compare two files
  let p = await fresh();
  await p.evaluate(() => openMenu()); await settle(p, 200);
  eq(await p.evaluate(() => { const items = [...document.querySelectorAll('#menu button, #menu [role=menuitem]')].map(e => e.textContent.trim()); return [items.some(t => t.startsWith('Compare two files…')), items.some(t => t.startsWith('My usage…'))]; }), [true, false], 'C1 the tray menu offers "Compare two files…"; "My usage…" is there only when all tools are shown');
  await p.evaluate(() => { closeMenus(); openCompare(); }); await settle(p, 200);
  eq(await p.evaluate(() => [getComputedStyle(document.getElementById('cmp')).display, ['cmCopy', 'cmCopyAll', 'cmSave', 'cmAi'].map(i => document.getElementById(i).disabled), document.getElementById('cmKey').disabled, document.getElementById('cmNameA').textContent]),
    ['block', [true, true, true, true], true, 'No file yet.'], 'C2 it opens empty: nothing to copy yet, and it says no file is in');
  eq(await axe(p, '#cmp'), [], 'C3 accessibility scan of the empty window');
  await p.click('#cmSample'); await settle(p, 200);
  const sum = await p.$$eval('#cmSummary p', ps => ps.map(x => x.textContent));
  eq(sum.slice(0, 2), ['Portfolio, Nov 10 has 6 rows; Portfolio, Nov 17 has 6 rows. Rows were matched by “ANDA” (it names every row once in both files).', '2 rows are the same, 3 changed (6 cells), 1 is new in Portfolio, Nov 17, and 1 is no longer there.'], 'C4 the sample files give a summary in plain sentences');
  eq(await p.evaluate(() => cmpSummary(cmpResult, cmpA.name, cmpB.name)), sum, 'C5 what is shown is exactly what the rule gives');
  eq(await p.$$eval('#cmp .cm-legend span', s => s.map(x => x.textContent)), ['Same 2', 'Changed 3', 'New 1', 'No longer there 1'], 'C6 the bar is named part by part with its numbers, not by color alone');
  eq(await p.$eval('#cmp .cm-bar', e => [e.getAttribute('role'), e.getAttribute('aria-label'), e.querySelectorAll('rect').length]), ['img', 'Same: 2, Changed: 3, New: 1, No longer there: 1', 4], 'C7 and it reads as a picture with a label');
  eq(await p.$$eval('#cmp .cm-col', r => r.map(x => [x.querySelector('.nm').textContent, x.querySelector('.ct').textContent])), [['Forecast date', '2'], ['Status', '2'], ['Open items', '2']], 'C8 changes by column');
  eq(await p.$$eval('#cmp .cm-table tbody tr', r => r.map(x => [...x.children].map(c => c.textContent).join(' | '))).then(r => [r.length, r[0], r[2]]), [6, '216482 | Forecast date | 10/17/2026 | 10/31/2026 | 14 days later', '216482 | Open items | 2 | 1 | −1'], 'C9 the table lists each changed cell with the size of the change');
  eq(await p.$$eval('#cmp .cm-keys', k => k.map(x => x.textContent)), ['220017', '219120'], 'C10 new rows and rows that are gone are named');
  ok(!(await p.evaluate(() => cmpReport(cmpResult, cmpA.name, cmpB.name).join('\n'))).includes('Goal date:'), 'C11 "10/3/2026" and "Oct 3, 2026" are the same day, and "1,000" equals "1000.0": neither is reported');
  eq(await axe(p, '#cmp'), [], 'C12 accessibility scan with a result');
  eq(await p.$$eval('#cmKey option', o => o.map(x => x.textContent)), ['Suggested for you', 'ANDA', 'Product', 'Applicant', 'Goal date', 'Forecast date', 'Status', 'Open items'], 'C13 only columns both files have can match rows');
  await p.selectOption('#cmKey', 'Product'); await settle(p, 150);
  eq([await txt(p, '#cmStatus'), await p.evaluate(() => cmpResult.key), (await p.$$eval('#cmSummary p', ps => ps[0].textContent)).includes('“Product” (chosen by you)')], ['Rows are now matched by “Product”.', 'Product', true], 'C14 the person can choose the column, and the summary says it was their choice');
  await p.selectOption('#cmKey', ''); await settle(p, 150);
  eq([await txt(p, '#cmStatus'), await p.evaluate(() => cmpResult.key)], ['The suggested column is used again.', 'ANDA'], 'C15 and go back to the suggestion');
  await p.click('#cmCopy'); await settle(p, 200);
  eq([await txt(p, '#cmStatus'), (await clip(p)) === sum.join('\n')], ['Summary copied.', true], 'C16 Copy the summary copies exactly the summary');
  await p.click('#cmCopyAll'); await settle(p, 200);
  ok((await clip(p)).includes('CHANGED\n216482: Forecast date: 10/17/2026 → 10/31/2026 (14 days later)') && (await txt(p, '#cmStatus')) === 'Full report copied.', 'C17 Copy the full report adds every change');
  const rep = await download(p, () => p.click('#cmSave'));
  ok(rep.name === 'comparison-report.html' && rep.text.includes('<h1>Comparison: Portfolio, Nov 10 and Portfolio, Nov 17</h1>') && rep.text.includes('No AI was used and nothing was sent.') && !/<script|https?:\/\//i.test(rep.text), 'C18 Save a report gives one page with no script and no outside address');
  await p.click('#cmAi'); await settle(p, 300);
  const prev = await p.evaluate(() => ({ shown: getComputedStyle(document.getElementById('actwin')).display, prompt: document.getElementById('awPrompt').value, result: document.getElementById('awResult').hidden, status: document.getElementById('cmStatus').textContent }));
  ok(prev.shown === 'block' && prev.prompt.startsWith('Write an executive summary of the differences') && prev.prompt.includes('216482: Forecast date: 10/17/2026 → 10/31/2026') && !prev.prompt.includes('Sertraline') && prev.result === true, 'C19 the narrative opens the usual preview with the summary and the changes in it, not the files; nothing has been sent');
  ok(prev.status.startsWith('A window with exactly what would be sent is open. Nothing has been sent.'), 'C20 and the window says so');
  await p.close();

  // files from disk, and what is not a table
  p = await fresh(); await p.evaluate(() => openCompare()); await settle(p, 150);
  await p.setInputFiles('#cmFileA', csv('last week.csv', 'ANDA;Status;Note\r\n216482;"Under review; second cycle";a\r\n214990;On track;b\r\n'));
  await settle(p, 200);
  eq([await txt(p, '#cmNameA'), await txt(p, '#cmStatus'), await p.evaluate(() => cmpResult)], ['last week.csv: 2 rows, 3 columns', 'last week.csv was read on this PC. Nothing was sent.', null], 'C21 a file with semicolons and quotes is read; with one file in, nothing is compared yet');
  await p.setInputFiles('#cmFileB', csv('this week.csv', '﻿ANDA,Status,Note\n216482,"<img src=x onerror=window.__x=1>",a\n214990,On track,"=HYPERLINK(""http://x"")"\n300001,New,c\n'));
  await settle(p, 250);
  eq(await p.evaluate(() => [cmpResult.key, cmpResult.changedRows, cmpResult.added, document.querySelector('#cmOut img'), window.__x === undefined, [...document.querySelectorAll('#cmp .cm-table td')].some(t => t.textContent === '<img src=x onerror=window.__x=1>')]),
    ['ANDA', 2, ['300001'], null, true, true], 'C22 markup in a cell is shown as text, builds nothing and runs nothing');
  const rep2 = await download(p, () => p.click('#cmSave'));
  ok(rep2.text.includes('&lt;img src=x onerror=window.__x=1&gt;') && !rep2.text.includes('<img'), 'C23 and it is escaped in the saved report');
  await p.setInputFiles('#cmFileA', { name: 'notes.docx', mimeType: 'application/octet-stream', buffer: Buffer.from('PK\u0003\u0004 not a table') }); await settle(p, 200);
  eq([await txt(p, '#cmStatus'), await txt(p, '#cmNameA')], ['notes.docx is not a kind of file this page reads. It reads CSV tables and plain text (.txt, .md); the Windows app also reads Excel workbooks (.xlsx) and Word documents (.docx).', 'last week.csv: 2 rows, 3 columns'], 'C24 a file of another kind is refused in words, and the earlier file stays');
  await p.setInputFiles('#cmFileA', { name: 'huge.csv', mimeType: 'text/csv', buffer: Buffer.alloc(5 * 1024 * 1024 + 10, 'a') }); await settle(p, 300);
  eq(await txt(p, '#cmStatus'), 'huge.csv is larger than 5 MB, so it was not read.', 'C25 a file over 5 MB is not read');
  await p.setInputFiles('#cmFileA', csv('empty.csv', '\n\n  \n')); await settle(p, 200);
  eq(await txt(p, '#cmStatus'), 'empty.csv could not be read as a table.', 'C26 an empty file is said plainly');
  await p.close();

  // every look, and a phone
  p = await fresh(); await p.evaluate(() => { openCompare(); document.getElementById('cmSample').click(); }); await settle(p, 200);
  for (const look of ['v', 'u', 't', 'k', 'p']) { await p.evaluate(l => applyLook(l), look); await settle(p, 120); eq(await axe(p, '#cmp'), [], `C27 accessibility scan in look ${look.toUpperCase()}`); }
  await p.close();
  p = await fresh({ viewport: { width: 390, height: 780 } }); await p.evaluate(() => { openCompare(); document.getElementById('cmSample').click(); }); await settle(p, 250);
  eq(await p.evaluate(() => { const r = document.getElementById('cmp').getBoundingClientRect(); return [r.left >= 0, r.right <= innerWidth + 1, document.documentElement.scrollWidth <= innerWidth + 1]; }), [true, true, true], 'C28 on a phone the window fits the screen and nothing scrolls sideways');
  await p.close();

  // ====================================================== A. Ask about a selection: the extras
  p = await fresh();
  await p.evaluate(() => openAsk('ANDA 216482: call the applicant about the IR response on Nov 20, then file the memo.')); await settle(p, 400);
  eq(await p.evaluate(() => [document.getElementById('asMore').hidden, document.getElementById('asIcsHint').textContent, [...document.querySelectorAll('#asMore button')].map(x => x.textContent)]),
    [false, 'A date was found: Friday, Nov 20, 2026. The invitation would read “ANDA 216482: call the applicant about the IR response on Nov 20, then file the…”.', ['Save a calendar invitation', 'Make a meeting page for ANDA 216482', 'Add to the notes of ANDA 216482']], 'A1 a selection with a date and a known ANDA offers an invitation and a note');
  eq(await axe(p, '#ask'), [], 'A2 accessibility scan of Ask with the new rows');
  const before = await p.evaluate(() => (state.quickNotes['216482'] || []).length);
  await p.click('#asMore [data-note-for="216482"]'); await settle(p, 200);
  eq([await txt(p, '#asStatus'), await p.evaluate(() => (state.quickNotes['216482'] || []).length), await p.evaluate(() => notesFor('216482')[0].text)], ['Added to your notes on ANDA 216482. Open the card and press N to see it.', before + 1, 'ANDA 216482: call the applicant about the IR response on Nov 20, then file the memo.'], 'A3 the note is added to that ANDA and the window says where to find it');
  const inv = await download(p, () => p.click('#asIcs'));
  ok(inv.name === 'invitation.ics' && inv.text.startsWith('BEGIN:VCALENDAR\r\n') && inv.text.includes('DTSTART;VALUE=DATE:20261120\r\n') && inv.text.includes('DTEND;VALUE=DATE:20261121\r\n') && /UID:[a-z0-9]+-[a-z0-9]+@notula\r\n/.test(inv.text) && /DTSTAMP:\d{8}T\d{6}Z\r\n/.test(inv.text) && inv.text.endsWith('END:VCALENDAR\r\n'), 'A4 the invitation is a standard calendar file for that day, all day');
  ok(inv.text.split('\r\n').every(l => l.length <= 61) && inv.text.includes('Nov 20\\, then'), 'A5 its lines are folded and its commas escaped');
  eq(await txt(p, '#asStatus'), 'A download of invitation.ics was tried: an all-day event for Friday, Nov 20, 2026. Open the file to add it to your calendar. If nothing was saved, this page is not allowed to save files where it is shown; the Windows app saves the file itself. Nothing was sent, and nothing was added for you.', 'A6 and the window says what was and was not done');
  for (const [text, hidden, why] of [['Version 3/4 of the plan, ratio 10/12, for ANDA 999999.', true, 'a fraction is not a date, and an unknown number has no notes'], ['Lunch on Dec 3.', false, 'a date alone offers the invitation'], ['ANDA 214990 is fine.', false, 'an ANDA alone offers the note'], ['', true, 'nothing selected offers nothing']]) {
    await p.fill('#asText', text); await p.evaluate(() => askAnalyse()); await settle(p, 120);
    eq(await p.evaluate(() => document.getElementById('asMore').hidden), hidden, `A7 ${why}`);
  }
  await p.fill('#asText', 'ANDA 216482 ' + 'x'.repeat(700)); await p.evaluate(() => askAnalyse()); await settle(p, 120);
  await p.click('#asMore [data-note-for="216482"]'); await settle(p, 150);
  eq([await txt(p, '#asStatus'), await p.evaluate(() => notesFor('216482')[0].text.length)], ['Added to your notes on ANDA 216482. Only the first 500 characters were kept. Open the card and press N to see it.', 500], 'A8 a long selection is cut to a note\'s length, and the window says so');
  await p.fill('#asText', 'ANDA 216482 <img src=x onerror=window.__y=1> on Dec 3, 2026'); await p.evaluate(() => askAnalyse()); await settle(p, 120);
  eq(await p.evaluate(() => [document.querySelector('#asMore img'), window.__y === undefined, document.getElementById('asIcsHint').textContent.includes('<img src=x onerror=window.__y=1>')]), [null, true, true], 'A9 markup in a selection is only ever text here too');
  eq(await p.$$eval('#asRead0,#asRead1,#asRead2,#asRead3', bs => bs.map(x => x.textContent)), ['Give me the TL;DR', 'What else should I know?', 'Onboard me on this topic', 'Why does it matter?'], 'A10 four reading prompts are offered');
  await p.fill('#asText', 'The applicant asked to move the meeting to Friday.'); await p.evaluate(() => askAnalyse()); await settle(p, 120);
  for (let i = 0; i < 4; i++) {
    await p.evaluate(k => { closeApp('actwin'); document.getElementById('asRead' + k).click(); }, i); await settle(p, 250);
    const s = await p.evaluate(() => ({ shown: getComputedStyle(document.getElementById('actwin')).display, prompt: document.getElementById('awPrompt').value, result: document.getElementById('awResult').hidden, status: document.getElementById('asStatus').textContent }));
    ok(s.shown === 'block' && s.prompt.includes('The applicant asked to move the meeting to Friday.') && s.result === true && s.status === 'A window with exactly what would be sent is open. Nothing has been sent.', `A11 reading prompt ${i + 1} is filled in and shown first; nothing is sent`);
  }
  await p.evaluate(() => { closeApp('actwin'); document.getElementById('asText').value = ''; document.getElementById('asRead0').click(); }); await settle(p, 150);
  eq(await txt(p, '#asStatus'), 'Please select or paste something first.', 'A12 with nothing selected a reading prompt asks for a selection');
  await p.close();

  // ====================================================== U. My usage
  p = await fresh();
  await p.evaluate(() => openUsage()); await settle(p, 150);
  eq(await p.evaluate(() => [document.getElementById('usOn').checked, document.getElementById('usOut').value, document.getElementById('usCopy').disabled]), [false, 'Counting is off. Nothing is being counted.', true], 'U1 counting is off until the person switches it on');
  await p.evaluate(() => { openAsk('x'); openCompare(); document.getElementById('cmSample').click(); openUsage(); }); await settle(p, 200);
  eq(await p.evaluate(() => [Object.keys(usage.days).length, document.getElementById('usOut').value]), [0, 'Counting is off. Nothing is being counted.'], 'U2 while it is off, using the tools counts nothing');
  eq(await axe(p, '#usage'), [], 'U3 accessibility scan of My usage');
  await p.check('#usOn'); await settle(p, 100);
  await p.evaluate(() => { openAsk('ANDA 216482 on Dec 3, 2026'); openAsk('again'); openCompare(); document.getElementById('cmSample').click(); document.getElementById('asText').value = 'Hello there.'; askAnalyse(); document.getElementById('asRead0').click(); closeApp('actwin'); openUsage(); }); await settle(p, 250);
  eq(await p.evaluate(() => document.getElementById('usOut').value.split('\n')), ['My usage: last 7 days (last 30 days)', 'Selections asked about: 2 (2)', 'File comparisons: 1 (1)', 'Reading prompts used: 1 (1)', 'Counts only. No ANDA number, name or text is ever recorded. Kept on this PC, and shared only if you copy it.'], 'U4 once on, each use adds one, and only counts are kept');
  ok(await p.evaluate(() => !JSON.stringify(usage).includes('216482') && !JSON.stringify(usage).includes('Hello')), 'U5 nothing that was selected is in what is kept');
  await p.click('#usCopy'); await settle(p, 200);
  eq([await txt(p, '#usStatus'), (await clip(p)).startsWith('My usage: last 7 days (last 30 days)\nSelections asked about: 2 (2)')], ['Summary copied.', true], 'U6 the person copies the summary if they choose to');
  await p.click('#usClear'); await settle(p, 100);
  eq(await p.evaluate(() => [Object.keys(usage.days).length, document.getElementById('usStatus').textContent]), [0, 'The counts were cleared.'], 'U7 and can clear the counts');
  eq(await p.evaluate(() => { usageRecord('ANDA 216482 is late'); usageRecord('<script>'); return Object.keys(usage.days).length; }), 0, 'U8 only the fixed list of events can ever be counted');
  await p.close();

  // ====================================================== S. The security review
  p = await fresh(); await p.evaluate(() => openCompare()); await settle(p, 150);
  await p.setInputFiles('#cmFileA', csv('=cmd.csv', 'Id,Value\n=1+1,x\n+2,y\n@gone,z\n-5,k\n'));
  await settle(p, 200);
  await p.setInputFiles('#cmFileB', csv('later.csv', 'Id,Value\n=1+1,X\n+2,"=SUM(A1)"\n-new,n\n-5,k\n'));
  await settle(p, 250);
  await p.click('#cmCopyAll'); await settle(p, 200);
  const pasted = (await clip(p)).split('\n');
  ok(pasted.every(l => !/^[=+\-@\t\r]/.test(l)) && pasted.includes("'=1+1: Value: x → X") && pasted.includes("'+2: Value: y → =SUM(A1)") && pasted.includes("'-new") && pasted.includes("'@gone") && pasted[0].startsWith("'=cmd.csv has 4 rows"), 'S1 a copied report never starts a line with =, +, - or @: such a line gets an apostrophe, so a spreadsheet reads it as text');
  await p.click('#cmCopy'); await settle(p, 200);
  ok((await clip(p)).split('\n').every(l => !/^[=+\-@\t\r]/.test(l)), 'S2 the same holds for the copied summary');
  ok(await p.evaluate(() => [...document.querySelectorAll('#cmp .cm-table th[scope=row]')].some(t => t.textContent === '=1+1')), 'S3 on the screen the value is shown exactly as it is in the file');
  eq(await p.evaluate(() => { const r = cmpReport(cmpTables(CMP_SAMPLE_A, CMP_SAMPLE_B, null), 'a', 'b'); return JSON.stringify(cmpForPaste(r)) === JSON.stringify(r); }), true, 'S4 an ordinary report is copied unchanged');
  const rep3 = await download(p, () => p.click('#cmSave'));
  ok(rep3.text.includes(`<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">`) && !/<script|<img|<svg| href=|https?:\/\//i.test(rep3.text), 'S5 the saved report carries a policy that lets it load and run nothing');
  eq(await p.evaluate(() => {
    const A = { name:'A', headers:['Id', 'H' + 'h'.repeat(300)], rows:[['1', 'a'.repeat(5000) + '1'], ['K' + 'k'.repeat(900), 'same'], ['K' + 'k'.repeat(900) + '-gone', 'x']] };
    const B = { name:'B', headers:['Id', 'H' + 'h'.repeat(300)], rows:[['1', 'a'.repeat(5000) + '2'], ['K' + 'k'.repeat(900), 'changed'], ['K' + 'k'.repeat(900) + '-new', 'x']] };
    const r = cmpTables(A, B, null);
    return [r.changedRows, r.addedCount, r.removedCount, r.changes.every(c => c.row.length <= 200 && c.cells.every(x => x.from.length <= 200 && x.to.length <= 200)), r.added[0].length, r.removed[0].endsWith('…'), Math.max(...cmpReport(r, 'A', 'B').map(l => l.length)) < 1200, cmpShow('ab😀cd', 4), cmpShow('ab😀cd', 5)];
  }), [2, 1, 1, true, 200, true, true, 'ab…', 'ab😀…'], 'S6 a very long value is shown cut, never through the middle of a character, and is still matched and compared whole');
  const evil = await p.evaluate(() => {
    const c = String.fromCharCode;
    const text = 'Review on Dec 1, 2026\r\nATTENDEE:mailto:someone@example.invalid' + c(0x2028) + 'BEGIN:VALARM' + c(0x2029) + 'ACTION:PROCEDURE' + c(0x85) + 'END:VEVENT\rORGANIZER:x\nX-BAD;PARAM=1:2' + c(0, 7, 27) + '[31m' + c(127, 11, 12);
    const made = icsFor(text, Date.UTC(2026, 10, 18), '20261118T120000Z', 't@notula'), lines = made.ics.split('\r\n'), logical = [];
    for(const l of lines){ if(l.startsWith(' ') && logical.length) logical[logical.length - 1] += l.slice(1); else logical.push(l); }
    const allowed = ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:', 'CALSCALE:GREGORIAN', 'METHOD:PUBLISH', 'BEGIN:VEVENT', 'UID:t@notula', 'DTSTAMP:20261118T120000Z', 'DTSTART;VALUE=DATE:20261201', 'DTEND;VALUE=DATE:20261202', 'SUMMARY:', 'DESCRIPTION:', 'TRANSP:TRANSPARENT', 'END:VEVENT', 'END:VCALENDAR'];
    const rest = made.ics.split('\r\n').join('');
    return [logical.filter(l => l !== '').every(l => allowed.some(a => l.startsWith(a))), logical.filter(l => l === 'BEGIN:VEVENT').length, logical.filter(l => l === 'END:VEVENT').length,
      [...rest].some(ch => { const n = ch.charCodeAt(0); return (n < 32 && n !== 9) || n === 127 || n === 0x85 || n === 0x2028 || n === 0x2029; }), lines.every(l => l.length <= 61),
      logical.find(l => l.startsWith('DESCRIPTION:')).includes('\\nATTENDEE:mailto:someone@example.invalid\\nBEGIN:VALARM\\nACTION:PROCEDURE\\nEND:VEVENT\\nORGANIZER:x\\nX-BAD\\;PARAM=1:2[31m')];
  });
  eq(evil, [true, 1, 1, false, true, true], 'S7 a selection cannot add a line of its own to a calendar file: line breaks of every kind become text, control characters are removed');
  eq(await p.evaluate(() => [typeof cmpA.name, cmpA.name.length <= 80, CMP_FILE_MAX, document.querySelector('#cmFileA').getAttribute('accept')]), ['string', true, 5242880, '.csv,.tsv,.txt,.md,text/csv,text/plain'], 'S8 the page reads only small text tables that the person chose');
  // the design review (accessibility): graphics stand 3 to 1 against the window, and controls are large enough to press
  for (const look of ['x', 'v', 'u', 't', 'k', 'p']) {
    await p.evaluate(l => applyLook(l), look); await settle(p, 120);
    const low = await p.evaluate(() => {
      const lum = c => { const v = c.map(x => { x /= 255; return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); }); return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2]; };
      const parse = t => { const m = t.match(/rgba?\(([^)]+)\)/); if (!m) return null; const a = m[1].split(',').map(x => parseFloat(x)); return { rgb: a.slice(0, 3), a: a.length > 3 ? a[3] : 1 }; };
      const bgOf = el => { const stack = []; for (let e = el; e; e = e.parentElement) { const c = parse(getComputedStyle(e).backgroundColor); if (c && c.a > 0) { stack.push(c); if (c.a >= 1) break; } } let base = [255, 255, 255]; for (const c of stack.reverse()) base = base.map((v, i) => v * (1 - c.a) + c.rgb[i] * c.a); return base; };
      const ratio = (a, b) => { const l1 = lum(a), l2 = lum(b); return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05); };
      const win = document.getElementById('cmp'), bg = bgOf(win.querySelector('#cmOut')), out = []; let seen = 0;
      for (const el of win.querySelectorAll('svg.cm-bar rect, .cm-legend i, .cm-col .fl')) {
        const cs = getComputedStyle(el), outline = /(^| )s( |$)/.test(el.getAttribute('class') || ''), f = parse(outline ? (el.tagName === 'rect' ? cs.stroke : cs.borderTopColor) : el.tagName === 'rect' ? cs.fill : cs.backgroundColor), op = parseFloat(cs.opacity) * f.a, eff = f.rgb.map((v, i) => v * op + bg[i] * (1 - op)), r = ratio(eff, bg); seen++;
        if (r < 3) out.push(`${el.getAttribute('class')}: ${r.toFixed(2)}`);
      }
      const same = win.querySelector('svg.cm-bar rect.s'), sw = same ? parseFloat(getComputedStyle(same).strokeWidth) : 0, sf = same ? parse(getComputedStyle(same).fill) : null;
      if (!same || sw < 2 || !sf || sf.a !== 0) out.push('"Same" is not an outline');      /* an outline beside filled parts: the parts differ by more than color */
      return seen >= 9 ? out : ['only ' + seen + ' chart parts were found'];
    });
    eq(low, [], `S9 every part of the charts stands at least 3 to 1 against the window in look ${look.toUpperCase()}`);
  }
  eq(await p.evaluate(() => [...document.querySelectorAll('#cmp button, #cmp select, #cmp input, #cmp textarea')].map(e => [e.id, Math.round((e.type === 'checkbox' ? e.closest('label') : e).getBoundingClientRect().height)]).filter(x => x[1] > 0 && x[1] < 24)), [], 'S10 every control in the window is at least 24 pixels tall, the two file choosers included (a checkbox is measured with its label, which is the target)');
  await p.close();

  // the page says what it tried, never that a file was saved: inside another page that forbids downloads (as a hosted copy is shown), nothing is saved
  const TRIED = 'A download of the report was tried (comparison-report.html). If nothing was saved, this page is not allowed to save files where it is shown: please use “Copy the full report” instead. Nothing was sent.';
  p = await fresh(); await p.evaluate(() => { openCompare(); document.getElementById('cmSample').click(); }); await settle(p, 200);
  await download(p, () => p.click('#cmSave'));
  eq(await txt(p, '#cmStatus'), TRIED, 'S11 after Save a report the window says a download was tried and what to do if nothing was saved');
  await p.close();
  fs.writeFileSync(TMP + '/frame_nodl.html', `<!doctype html><body style="margin:0"><iframe id="f" src="${SIM}" sandbox="allow-scripts allow-same-origin" width="1300" height="760" style="border:0;display:block"></iframe>`);
  { const ctx = await b.newContext({ viewport: { width: 1320, height: 780 }, acceptDownloads: true, reducedMotion: 'reduce' }); const page = await ctx.newPage(); let got = 0; page.on('download', () => got++);
    page.on('pageerror', e => errs.push(e.message));
    await page.goto(require('url').pathToFileURL(TMP + '/frame_nodl.html').href); await page.waitForTimeout(700);
    const f = page.frames().find(x => x !== page.mainFrame());
    await f.evaluate(() => { openCompare(); document.getElementById('cmSample').click(); }); await page.waitForTimeout(250);
    await f.click('#cmSave'); await page.waitForTimeout(900);
    eq([got, await f.$eval('#cmStatus', e => e.textContent)], [0, TRIED], 'S12 shown inside a page that forbids downloads, no file is saved and the window does not claim one was');
    await f.evaluate(() => { closeApp('cmp'); openAsk('Call the applicant on Dec 3, 2026.'); }); await page.waitForTimeout(250);
    await f.click('#asIcs'); await page.waitForTimeout(900);
    eq([got, (await f.$eval('#asStatus', e => e.textContent)).startsWith('A download of invitation.ics was tried: an all-day event for Thursday, Dec 3, 2026.')], [0, true], 'S13 the same for the calendar invitation');
    await ctx.close(); }

  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
