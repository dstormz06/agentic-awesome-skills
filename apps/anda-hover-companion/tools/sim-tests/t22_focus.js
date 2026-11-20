// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 17 in the browser simulation: the focused edition (Getting started gone, five Settings groups, solid card, trimmed tray, "Show all tools"),
// Review a draft's extra checks, and Ask about a selection (window, quick checks, stored prompts, own instruction, shortcut, backup, attack cases).
const { chromium } = require('playwright');
const URL = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
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
    await p.goto(URL + (opts.all ? '#all' : '')); await p.waitForTimeout(300);
    if (opts.launch !== false) { await p.dblclick('#deskIcon'); await p.waitForTimeout(150); }
    return p;
  };
  const settle = (p, ms = 160) => p.waitForTimeout(ms);
  const sections = p => p.$$eval('#settings details.sec', n => n.filter(d => !d.hidden).map(d => d.id.slice(3)));
  const trayItems = async p => { await p.click('#trayMenuBtn'); await settle(p, 80); const t = await p.$$eval('#menu button, #menu [role=menuitem]', n => n.map(x => x.textContent.replace(/\s+/g, ' ').trim())); await p.keyboard.press('Escape'); return t; };
  const openAskWith = async (p, text) => { await p.evaluate(t => openAsk(t), text); await settle(p, 60); };
  const result = p => p.inputValue('#asResult');
  const setAll = async (p, on) => { await p.evaluate(() => { document.getElementById('allTools').closest('details').open = true; }); await settle(p, 60); await (on ? p.check('#allTools') : p.uncheck('#allTools')); await settle(p); };
  const TABLE = 'ANDA\tApplicant\tGoal date\tAmount\n216482\tAcme\tOct 3, 2026\t$1,200.50\n214990\tBeta\tAug 15, 2026\t$800\n216482\tAcme\tOct 3, 2026\t$1,200.50\n\tGamma\tNov 2, 2026\t';

  // ====================================================== A. the focused edition is the default
  let p = await fresh();
  eq(await sections(p), ['Look', 'Start', 'Files', 'Actions', 'Connections', 'Maintenance'], 'A1 Settings shows six groups (Look and feel came back in round 19)');
  const body = await p.evaluate(() => document.body.innerText);
  ok(!/Getting started/i.test(body), 'A2 there is no "Getting started" text anywhere');
  ok(await p.evaluate(() => !document.getElementById('steps') && !document.querySelector('[data-step]')), 'A3 the demo "Try it" steps are gone');
  ok(await p.evaluate(() => !state.allTools), 'A4 all tools are off by default');
  for (const id of ['keyList', 'openTodoBtn', 'openMyTodoBtn']) ok(!(await p.isVisible('#' + id)), `A5 ${id} is hidden in the focused edition`);
  ok(await p.isVisible('#keyAsk'), 'A6 the Ask shortcut button is shown');
  await p.evaluate(() => { state.op = 60; applyEdition(); });
  eq(await p.evaluate(() => document.documentElement.style.getPropertyValue('--op')), '60%', 'A7 the transparency a person chose applies in the focused edition (round 19)');
  eq(await p.evaluate(() => document.documentElement.dataset.dir), 'x', 'A8 the default look is used (X · Graphite Calendar since round 24)');
  await p.click('#openSettingsBtn'); await settle(p);
  ok(/Ask: my stored prompts|STORED PROMPTS/i.test(await p.evaluate(() => document.getElementById('secActions').innerText)), 'A9 the Actions group is named for Ask');
  ok(!(await p.isVisible('#agentsBlock')) && !(await p.isVisible('#todoBlock')), 'A10 agents and the ANDA to-do settings are hidden');
  const items = await (async () => { await p.click('#setDone'); return trayItems(p); })();
  ok(items.some(t => /^Lookup ANDA/.test(t)) && items.some(t => /^Review a draft/.test(t)) && items.some(t => /^Ask about a selection/.test(t)) && items.some(t => /^Today/.test(t)) && !items.some(t => /^Watch list/.test(t)), 'A11 the tray has Lookup, Review, Ask and Today (round 34: the watch list opens from Today)');
  ok(!items.some(t => /to-do list|Notes to my/i.test(t)), 'A12 the tray has no to-do items in the focused edition');
  ok(items.some(t => /Ctrl\+Alt\+Shift\+P/.test(t)), 'A13 the tray shows the Ask shortcut');

  // ====================================================== B. "Show all tools" brings the rest back, and keeps the choices
  await p.click('#openSettingsBtn'); await settle(p);
  await setAll(p, true);
  eq((await sections(p)).length, 8, 'B1 all eight groups appear');
  ok(await p.isVisible('#keyList') && await p.isVisible('#openMyTodoBtn'), 'B2 the to-do buttons appear');
  eq(await p.evaluate(() => document.documentElement.style.getPropertyValue('--op')), '60%', 'B3 the transparency chosen earlier is kept');
  await p.click('#setDone');
  const items2 = await trayItems(p);
  ok(items2.some(t => /My to-do list/.test(t)), 'B4 the tray shows the to-do list again');
  await p.click('#openSettingsBtn'); await settle(p); await setAll(p, false);
  eq((await sections(p)).length, 6, 'B5 switching it off returns to six groups');
  await p.close();
  p = await fresh({ all: true });
  ok(await p.evaluate(() => state.allTools), 'B6 #all opens with all tools on (used by the older suites)');
  await p.close();

  // ====================================================== C. the Ask window
  p = await fresh();
  await p.click('#keyAsk'); await settle(p);
  ok(await p.isVisible('#ask'), 'C1 the Ask shortcut opens the window');
  eq(await p.textContent('#asShape'), 'Nothing is selected.', 'C2 an empty selection is said so');
  ok(!(await p.isVisible('#asQuick')) && !(await p.isVisible('#asSuggestBtn')), 'C3 no quick checks and no button for an empty selection');
  await p.keyboard.press('Escape'); await settle(p, 60);
  ok(!(await p.isVisible('#ask')), 'C4 Escape closes it');
  await p.click('[data-pickbtn=TABLE]'); await p.click('#keyAsk'); await settle(p);
  const sel = await p.inputValue('#asText');
  ok(sel.startsWith('ANDA\tApplicant') && sel.includes('\t'), 'C5 the selected cells arrive with their tabs');
  eq(await p.textContent('#asShape'), 'A table of 5 rows and 4 columns.', 'C6 it is described as a table');
  eq(await p.textContent('#asSuggestBtn'), 'Find duplicates', 'C7 the suggested button is Find duplicates');
  ok(!(await p.isVisible('#asResult')), 'C8 nothing has run yet');
  await p.click('#asSuggestBtn'); await settle(p, 60);
  ok(/^Found 1 repeated row/.test(await result(p)), 'C9 Find duplicates reports the repeated row');
  ok(await p.isVisible('#asCopy'), 'C10 Copy the result appears');
  await p.click('#asCopy'); await settle(p, 80);
  ok(/Copied/.test(await p.textContent('#asStatus')), 'C11 copying says so');
  eq(await p.evaluate(() => navigator.clipboard.readText()), await result(p), 'C12 the clipboard holds the result');
  for (const [btn, re] of [['#asDescribe', /headings/], ['#asBlank', /blank/], ['#asTotal', /total/]]) { await p.click(btn); await settle(p, 40); ok(re.test(await result(p)), `C13 ${btn} says something sensible`); }
  await p.fill('#asColA', '2'); await p.fill('#asColB', '9'); await p.click('#asCompare'); await settle(p, 40);
  eq(await result(p), 'Please choose two different columns between 1 and 4.', 'C14 a column out of range is explained');
  await p.fill('#asColB', 'x'); await p.click('#asCompare'); await settle(p, 40);
  eq(await result(p), 'Please type two column numbers, for example 1 and 2.', 'C15 a non-number is explained');
  await p.fill('#asColA', '1'); await p.fill('#asColB', '2'); await p.click('#asCompare'); await settle(p, 40);
  ok(/Only in column 1 \(ANDA\)/.test(await result(p)), 'C16 Compare columns names the columns');

  // typing changes what is offered (after a short pause)
  await p.fill('#asText', 'ANDA 216482'); await settle(p, 420);
  eq(await p.textContent('#asShape'), 'An ANDA number: 216482.', 'C17 one number is recognised after typing');
  eq(await p.textContent('#asSuggestBtn'), 'Look it up', 'C18 the button becomes Look it up');
  ok(!(await p.isVisible('#asQuick')), 'C19 quick checks are hidden for one number');
  await p.click('#asSuggestBtn'); await settle(p, 200);
  ok(await p.isVisible('#card'), 'C20 Look it up opens the card');
  await p.keyboard.press('Escape'); await settle(p, 60);
  await openAskWith(p, 'ANDA 123456');
  await p.click('#asSuggestBtn'); await settle(p, 80);
  ok(/ANDA 123456 is not in your data/.test(await p.evaluate(() => document.body.innerText)), 'C21 an unknown number is said not to be in the records');
  await openAskWith(p, 'Quick status. ANDA 216482 is on track, with a goal date of Oct 10, 2026 and a forecast of Oct 17, 2026.');
  eq(await p.textContent('#asSuggestBtn'), 'Review as a draft', 'C22 a paragraph offers Review as a draft');
  await p.click('#asSuggestBtn'); await settle(p, 200);
  ok(await p.isVisible('#draft') && /Quick status/.test(await p.inputValue('#drText')), 'C23 the draft review opens with the text');
  ok(await p.evaluate(() => +$('draft').style.zIndex > +$('ask').style.zIndex), 'C24 the review window is in front of Ask');
  await p.click('#drDone');
  await p.close();

  // ====================================================== D. stored prompts and an instruction typed here
  p = await fresh();
  await openAskWith(p, TABLE);
  const opts = await p.$$eval('#asPrompt option', n => n.map(o => o.textContent));
  ok(opts.includes('Prepare a status question · fill and show') && opts.includes('Ask my AI for next steps · ask my AI'), 'D1 the picker lists prompts with what each does');
  ok(!opts.some(o => /^Add to my to-do list/.test(o)), 'D2 the to-do action is not offered as a prompt');
  await p.click('#asUse'); await settle(p, 40);
  eq(await p.textContent('#asStatus'), 'Please choose a stored prompt first.', 'D3 pressing Use with nothing chosen explains itself');
  ok(!(await p.isVisible('#actwin')), 'D4 nothing opened');
  await p.selectOption('#asPrompt', '0'); await p.click('#asUse'); await settle(p, 200);
  ok(await p.isVisible('#actwin'), 'D5 the prompt window opens');
  const pr = await p.inputValue('#awPrompt');
  ok(pr.includes('Selected text:') && pr.includes('216482\tAcme'), 'D6 the selection is part of the prompt');
  ok(/\(not in my data\)/.test(pr) && !/Metformin/.test(pr), 'D7 no ANDA is picked as "the" record for a table');
  const st = await p.textContent('#asStatus');
  ok(/Nothing was sent/.test(st) && /A table holds many ANDAs/.test(st), 'D8 the status says nothing was sent and why details are empty');
  ok(await p.evaluate(() => +$('actwin').style.zIndex >= +$('ask').style.zIndex), 'D9 the prompt window is in front of Ask');
  ok(await p.isHidden('#awSend'), 'D10 a fill-only prompt has no Send button');
  await p.click('#awDone').catch(() => {});
  await p.evaluate(() => closeApp('actwin'));
  // the AI prompt: no connection means nothing can be sent
  await p.selectOption('#asPrompt', '1'); await p.click('#asUse'); await settle(p, 200);
  ok(/Nothing was sent/.test(await p.textContent('#awNote')) || /No AI connection/.test(await p.textContent('#awNote')), 'D11 without a connection the AI prompt says nothing was sent');
  await p.evaluate(() => closeApp('actwin'));
  // own instruction
  await p.click('#asFillOwn'); await settle(p, 40);
  eq(await p.textContent('#asStatus'), 'Please type your instruction first.', 'D12 an empty instruction explains itself');
  await p.fill('#asOwn', 'Compare {nonsense} please'); await p.click('#asFillOwn'); await settle(p, 40);
  ok(/is not something Ask can fill in/.test(await p.textContent('#asStatus')), 'D13 an unknown {placeholder} is refused');
  await p.fill('#asOwn', 'list anything inconsistent'); await p.click('#asFillOwn'); await settle(p, 200);
  const own = await p.inputValue('#awPrompt');
  ok(own.startsWith('list anything inconsistent') && own.includes('Selected text:'), 'D14 the instruction is filled in with the selection');
  await p.evaluate(() => closeApp('actwin'));
  await p.fill('#asText', '   '); await p.click('#asAskOwn'); await settle(p, 40);
  eq(await p.textContent('#asStatus'), 'Please select or paste something first.', 'D15 an empty selection is not sent anywhere');
  ok(!(await p.isVisible('#actwin')), 'D16 no window opened for an empty selection');
  // a single ANDA in a paragraph still fills the record (Select & Peek behaviour)
  await p.fill('#asText', 'Please look at ANDA 216482 before Friday.'); await settle(p, 40);
  await p.selectOption('#asPrompt', '0'); await p.click('#asUse'); await settle(p, 200);
  ok(/ANDA 216482 \(Metformin/.test(await p.inputValue('#awPrompt')), 'D17 text about one known ANDA fills its details');
  await p.evaluate(() => closeApp('actwin'));
  // too long
  await p.fill('#asOwn', 'x'.repeat(1000)); await p.evaluate(() => { document.getElementById('asOwn').removeAttribute('maxlength'); document.getElementById('asOwn').value = 'x'.repeat(4100); });
  await p.click('#asFillOwn'); await settle(p, 40);
  eq(await p.textContent('#asStatus'), 'That instruction is too long.', 'D18 an instruction over 4,000 characters is refused');
  await p.close();

  // no stored prompts
  p = await fresh();
  await p.evaluate(() => { state.actions = []; openAsk('hello'); }); await settle(p, 60);
  ok(await p.isDisabled('#asPrompt') && await p.isDisabled('#asUse'), 'D19 with no stored prompts the picker is off');
  ok(/Settings/.test(await p.$eval('#asPrompt', e => e.options[0].textContent)), 'D20 and says where to add one');
  await p.close();

  // ====================================================== E. the shortcut: keyboard, conflicts, Settings, backup
  p = await fresh();
  await p.click('[data-pickbtn=TABLE]');
  await p.keyboard.press('Control+Alt+Shift+KeyP'); await settle(p, 120);
  ok(await p.isVisible('#ask'), 'E1 Ctrl+Alt+Shift+P opens Ask');
  ok(/^ANDA\tApplicant/.test(await p.inputValue('#asText')), 'E2 with the selected cells in it');
  await p.keyboard.press('Escape');
  await p.evaluate(() => { state.taken.ask = true; paintKeys(); });
  await p.click('#keyAsk'); await settle(p, 100);
  ok(!(await p.isVisible('#ask')) && /Another program handled this key/.test(await p.evaluate(() => document.getElementById('keys').innerText)), 'E3 a key owned by another program is said so');
  await p.evaluate(() => { state.taken.ask = false; });
  await p.evaluate(() => { focusedWin = 'term'; }); await p.click('#keyAsk'); await settle(p, 100);
  ok(/Paused in terminals/.test(await p.evaluate(() => document.body.innerText)) && await p.isVisible('#ask'), 'E4 in a terminal the selection is not read; Ask opens empty with a notice');
  eq(await p.inputValue('#asText'), '', 'E5 and holds nothing');
  await p.evaluate(() => { focusedWin = 'mail'; closeApp('ask'); });
  // Settings box: a clash is refused, a free combination is taken, Reset restores the default
  await p.click('#openSettingsBtn'); await settle(p);
  await p.evaluate(() => { document.getElementById('keyBoxAsk').closest('details').open = true; }); await settle(p, 60);
  const box = '#keyBoxAsk';
  eq(await p.inputValue(box), 'Ctrl+Alt+Shift+P', 'E6 Settings shows the default');
  await p.click(box); await p.keyboard.press('Control+Alt+KeyA'); await settle(p, 60);
  ok(/cannot use the same shortcut/.test(await p.textContent('#keyStatus')), 'E7 the same shortcut as Select & Peek is refused');
  eq(await p.inputValue(box), 'Ctrl+Alt+Shift+P', 'E8 and the old one is kept');
  await p.click(box); await p.keyboard.press('Control+Alt+Shift+KeyQ'); await settle(p, 60);
  eq(await p.inputValue(box), 'Ctrl+Alt+Shift+Q', 'E9 a free combination is taken');
  eq(await p.evaluate(() => state.keys.ask), 'Ctrl+Alt+Shift+Q', 'E10 the state follows');
  await p.click(box); await p.keyboard.press('Control+KeyQ'); await settle(p, 60);
  ok(/at least two modifiers/.test(await p.textContent('#keyStatus')), 'E11 one modifier is refused');
  await p.click('[data-keyreset=ask]'); await settle(p, 60);
  eq(await p.inputValue(box), 'Ctrl+Alt+Shift+P', 'E12 Reset restores the default');
  ok(/Ask about a selection: Ctrl\+Alt\+Shift\+P is active/.test(await p.textContent('#keyStatus')), 'E13 the status lists Ask');
  await p.click('#setDone');
  // backup: it travels, an old backup gets the default, a clash becomes none
  const rt = await p.evaluate(() => {
    state.keys = { ...state.keys, ask: 'Ctrl+Alt+Shift+Q' };
    const json = backupText(); const parsed = JSON.parse(json);
    state.keys = { ...state.keys, ask: 'Ctrl+Alt+Shift+P' }; const r1 = importBackup(json)[0], restored = state.keys.ask;
    delete parsed.settings.keys.ask; importBackup(JSON.stringify(parsed)); const old = state.keys.ask;
    parsed.settings.keys.ask = parsed.settings.keys.peek; importBackup(JSON.stringify(parsed)); const clash = state.keys.ask;
    parsed.settings.keys.ask = 'nonsense'; importBackup(JSON.stringify(parsed)); const junk = state.keys.ask;
    parsed.settings.keys.ask = ''; importBackup(JSON.stringify(parsed)); const none = state.keys.ask;
    return { r1, restored, old, clash, junk, none, inBackup: parsed.settings.keys };
  });
  ok(rt.r1 && rt.restored === 'Ctrl+Alt+Shift+Q', 'E14 the Ask shortcut travels with a backup');
  eq(rt.old, 'Ctrl+Alt+Shift+P', 'E15 a backup without it gets the default');
  eq(rt.clash, '', 'E16 a clashing Ask shortcut becomes none');
  eq(rt.junk, 'Ctrl+Alt+Shift+P', 'E17 an unreadable one becomes the default');
  eq(rt.none, '', 'E18 an empty one stays none');
  // a stored action on the same shortcut is refused in the action editor
  const clashMsg = await p.evaluate(() => { state.keys = { ...state.keys, ask: 'Ctrl+Alt+Shift+P' }; return validateAction({ name: 'X', kind: 1, template: 'hi {selection}', hotkey: 'Ctrl+Alt+Shift+P', enabled: true, due: 2, agentId: '', direct: false }, shortcutsInUse(null)); });
  ok(/already used by Ask about a selection/.test(clashMsg || ''), 'E19 a stored action cannot take the Ask shortcut');
  await p.close();

  // ====================================================== F. attack cases and small screens
  p = await fresh();
  await openAskWith(p, '<img src=x onerror="window.__pwn=1"> ANDA 216482\t<b>bold</b>\n"quoted\ttab"\tx');
  await p.click('#asDescribe'); await settle(p, 80);
  ok(await p.evaluate(() => !window.__pwn && !document.querySelector('#ask img, #ask b')), 'F1 markup in a selection is never run or drawn');
  await openAskWith(p, 'a'.repeat(30000));
  ok((await p.inputValue('#asText')).length === 20000, 'F2 a huge selection is cut to 20,000 characters');
  await p.evaluate(() => { const t = $('asText'); t.value = 'x'.repeat(20000); askAnalyse(); });
  eq(await p.textContent('#asShape'), 'Text of 20,000 characters.', 'F3 the length is written with a thousands separator');
  await openAskWith(p, Array.from({ length: 1500 }, (_, i) => `${i}\tv${i % 7}`).join('\n'));
  ok(/1000 rows/.test(await p.textContent('#asShape')), 'F4 a very long table is read up to 1,000 rows');
  await p.click('#asDup'); await settle(p, 60);
  ok((await result(p)).split('\n').length <= 40, 'F5 a long result stays short');
  await p.close();
  for (const [w, h] of [[390, 780], [820, 1000]]) {
    p = await fresh({ viewport: { width: w, height: h } });
    await p.evaluate(t => openAsk(t), TABLE); await settle(p, 120);
    const geo = await p.evaluate(() => { const r = $('ask').getBoundingClientRect(); return { l: r.left, r: r.right, w: innerWidth, sw: document.documentElement.scrollWidth }; });
    ok(geo.l >= -1 && geo.r <= geo.w + 1 && geo.sw <= geo.w + 1, `F6 Ask fits a ${w}px screen without sideways scrolling`);
    await p.close();
  }

  await b.close();
  ok(errs.length === 0, `no page errors (${JSON.stringify(errs.slice(0, 3))})`);
  console.log(`\n${checks - fails}/${checks} passed`);
  process.exit(fails ? 1 : 0);
})();
