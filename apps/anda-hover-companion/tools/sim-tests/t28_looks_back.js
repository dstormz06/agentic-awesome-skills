// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 19, the owner's two additions: the looks are in Settings again without "Show all tools", and the Go to list is not in capitals.
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const AXE = AXE_FILE;
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
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    await p.goto(SIM + (opts.all ? '#all' : '')); await p.waitForTimeout(300);
    await p.dblclick('#deskIcon'); await p.waitForTimeout(120);
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const openFull = async (p, id) => { await p.evaluate(i => openCard(i, { anchor: { x: 520, y: 60 }, forceFull: true }), id); await settle(p, 450); };
  const openSettings = async p => { await p.evaluate(() => { showApp('settings'); }); await settle(p, 150); };
  const dir = p => p.evaluate(() => document.documentElement.dataset.dir);
  const op = p => p.evaluate(() => document.documentElement.style.getPropertyValue('--op'));

  // ====================================================== L. the looks, without "Show all tools"
  let p = await fresh();
  ok(await p.evaluate(() => !state.allTools), 'L1 the page opens in the focused edition');
  eq(await dir(p), 'x', 'L2 a new install shows the default look X');
  await openSettings(p);
  ok(await p.isVisible('#secLook'), 'L3 Look and feel is a section of the focused Settings');
  eq(await p.$$eval('#settings details.sec', ds => ds.filter(d => getComputedStyle(d).display !== 'none').length), 6, 'L4 six sections are shown');
  await p.evaluate(() => { document.getElementById('secLook').open = true; }); await settle(p);
  const full = await p.$$eval('#setLook label', ls => ls.map(l => l.textContent.trim()));
  const compact = await p.$$eval('#setCompact label', ls => ls.map(l => l.textContent.trim()));
  eq(full.length + compact.length, await p.evaluate(() => LOOKS.length), `L5 every look is offered (${full.length} full cards and ${compact.length} compact boxes)`);
  eq([full.length, compact.length], [6, 2], 'L6 six full cards and two compact boxes since round 23');
  ok(await p.$$eval('#setLook label, #setCompact label', ls => ls.every(l => l.offsetParent !== null)), 'L7 and each one is visible');
  ok(await p.isVisible('#setOp'), 'L8 the transparency slider is there too');

  // choose every look in turn: the page follows, and a card can be opened in it
  const looks = await p.evaluate(() => LOOKS.map(l => ({ id: l[0], tokens: l[2], name: l[1] })));
  let wrong = [];
  for (const l of looks) {
    await p.evaluate(id => { const r = [...document.querySelectorAll('#setLook input, #setCompact input')].find(i => i.value === id); r.click(); }, l.id);
    await settle(p, 40);
    if (await dir(p) !== l.tokens || await p.evaluate(() => state.look) !== l.id) wrong.push(l.id);
  }
  eq(wrong, [], 'L9 choosing any look applies it in the focused edition');
  eq(errs, [], 'L10 no page error while every look was applied');

  // one full-card look and one compact look, by sight of the card
  await p.evaluate(() => { [...document.querySelectorAll('#setLook input')].find(i => i.value === 'k').click(); }); await settle(p);
  await p.evaluate(() => closeApp('settings'));
  await openFull(p, '216482');
  ok(await p.isVisible('#card'), 'L11 with K chosen the card opens');
  eq(await dir(p), await p.evaluate(() => LOOKS.find(l => l[0] === 'k')[2]), 'L12 in the K look');
  await p.keyboard.press('Escape'); await settle(p, 250);

  // transparency applies
  await openSettings(p);
  await p.evaluate(() => { const s = document.getElementById('setOp'); s.value = 60; s.dispatchEvent(new Event('input', { bubbles: true })); s.dispatchEvent(new Event('change', { bubbles: true })); }); await settle(p);
  eq(await op(p), '60%', 'L13 the transparency chosen applies in the focused edition');

  // "Show all tools" on and off keeps the choice
  await p.evaluate(() => { document.getElementById('secMaintenance') && (document.getElementById('secMaintenance').open = true); document.getElementById('allTools').click(); }); await settle(p);
  eq([await p.evaluate(() => state.look), await op(p)], ['k', '60%'], 'L14 turning Show all tools on keeps the look and the transparency');
  await p.evaluate(() => { document.getElementById('allTools').click(); }); await settle(p);
  eq([await p.evaluate(() => state.look), await op(p), await dir(p)], ['k', '60%', await p.evaluate(() => LOOKS.find(l => l[0] === 'k')[2])], 'L15 and turning it off again keeps them too');
  ok(!/appearance/i.test(await p.$eval('label:has(#allTools)', e => e.textContent)), 'L16 the Show all tools label no longer claims the looks');
  await p.evaluate(() => closeApp('settings'));

  // the tray menu offers Appearance in the focused edition
  await p.evaluate(() => openMenu()); await settle(p, 200);
  const menuText = await p.$eval('#menu', e => e.textContent);
  ok(/Appearance/.test(menuText), 'L17 the tray menu has Appearance in the focused edition');
  ok(!/My actions/.test(menuText), 'L18 and still leaves My actions to Show all tools');
  await p.keyboard.press('Escape'); await settle(p);

  // backup and restore carry the look
  const snap = await p.evaluate(() => snapSettings());
  eq([snap.appearance, snap.opacity, snap.allTools], ['k', 60, false], 'L19 the backup holds the look, the transparency and the edition');

  // ====================================================== J. the Go to list
  for (const all of [false, true]) {
    const q = await fresh({ all });
    await openSettings(q);
    const opts = await q.$$eval('#setJump option', o => o.map(x => x.textContent));
    const tag = all ? 'all tools' : 'focused';
    ok(opts.length > 20, `J1 (${tag}) the list has its entries (${opts.length})`);
    eq(opts.filter(t => !/[a-z]/.test(t)), [], `J2 (${tag}) no entry is written in capitals`);
    const allowed = new Set(['ANDA', 'AI', 'API', 'PC', 'RLD', 'PSG', 'FDA', 'CSV', 'US']);
    const shout = []; opts.forEach(t => (t.match(/\b[A-Z]{3,}\b/g) || []).forEach(w => { if (!allowed.has(w)) shout.push(w + ' in ' + t); }));
    eq(shout, [], `J3 (${tag}) the only capital words are abbreviations`);
    ok(opts.every(t => /^[A-Z]/.test(t)), `J4 (${tag}) each entry starts with a capital`);
    ok(opts.every(t => t.split(' · ').every(part => /^[A-Z]/.test(part))), `J5 (${tag}) and so does each part after a dot`);
    ok(opts.includes('Look and feel') && opts.includes('Appearance · Full card') && opts.includes('Appearance · Compact box') && opts.includes('Card transparency'), `J6 (${tag}) the looks can be reached from the list`);
    ok(opts.includes('Select & Peek') && opts.some(t => t === 'Companion Lens · Prompt library') && opts.includes('Outlook · Optional, read-only') && opts.includes('Connection · Which API, if any'), `J7 (${tag}) names and abbreviations keep their capitals`);
    if (all) ok(opts.includes('My agents · Alert Hub, Patent Analysis, Triaging Agent and your own') && opts.includes('My ANDA to-do list · Live, from your items and your data'), 'J8 the agents and ANDA keep theirs');
    else ok(opts.includes('Stored prompts · Used by Ask, or on a shortcut of their own') && !opts.includes('Dates'), 'J8 Ask keeps its capital, and Dates stays with Show all tools');
    // choosing an entry still goes there
    const idx = opts.indexOf('Card transparency');
    await q.selectOption('#setJump', { index: idx }); await settle(q, 300);
    ok(await q.evaluate(() => document.getElementById('secLook').open), `J9 (${tag}) choosing "Card transparency" opens Look and feel`);
    ok(await q.evaluate(() => { const r = document.getElementById('setOp').getBoundingClientRect(), s = document.getElementById('settings').getBoundingClientRect(); return r.top >= s.top && r.bottom <= s.bottom + 2; }), `J10 (${tag}) and brings the slider into view`);
    // the labels on the page are unchanged: only the list was asked for
    ok(await q.$$eval('#settings .lbl', ls => ls.some(l => l.textContent.trim() === 'CARD TRANSPARENCY')), `J11 (${tag}) the small labels on the page are as before`);
    if (!all) {
      await q.addScriptTag({ path: AXE });
      const res = await q.evaluate(async () => (await axe.run(document.getElementById('settings'))).violations.map(v => v.id + ': ' + v.nodes.length));
      eq(res, [], 'J12 accessibility scan of the focused Settings with Look and feel open');
    }
    await q.close();
  }
  eq(await p.evaluate(() => ['CARD TRANSPARENCY', 'URGENT ANDAs', 'DRUGS@FDA · A FILE ON THIS PC', 'WHAT I ASK THE AI', 'Look and feel', '<b>X</b> · Y'].map(sentenceCase)),
     ['Card transparency', 'Urgent ANDAs', 'Drugs@FDA · A file on this PC', 'What I ask the AI', 'Look and feel', '<b>X</b> · Y'], 'J13 the rule itself, on six labels');
  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
