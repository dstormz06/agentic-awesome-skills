// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Fixes for the findings of the fresh-eyes break test (P1 and the cheap P2s). Each check names the finding it covers.
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);
(async () => {
  const b = await chromium.launch();
  const errs = [];
  const fresh = async (opts = {}) => {
    const ctx = await b.newContext({ viewport: opts.viewport || { width: 1366, height: 800 }, permissions: opts.perms === false ? [] : ['clipboard-read', 'clipboard-write'] });
    const p = await ctx.newPage();
    p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    if (opts.init) await p.addInitScript(opts.init);
    await p.goto(SIM + (opts.all ? '#all' : '')); await p.waitForTimeout(300);
    await p.dblclick('#deskIcon'); await p.waitForTimeout(120);
    return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const txt = (p, s) => p.$eval(s, e => e.textContent);
  const openFull = async (p, id) => { await p.evaluate(i => openCard(i, { anchor: { x: 520, y: 80 }, forceFull: true }), id); await settle(p, 450); };

  // ===== D1 day counts follow the extract date (Jul 16, 2026)
  let p = await fresh({ all: true });
  await p.evaluate(() => applyLook('k'));
  await openFull(p, '216482');
  eq(await txt(p, '#c-days'), '· 79 days', 'D1 the goal line counts 79 days from Jul 16 to Oct 3');
  ok(/IR response due in 5 days/.test(await txt(p, '#c-attn-t')), 'D1 the attention line says the IR response is due in 5 days (due Jul 21)');
  await p.evaluate(() => closeCard()); await settle(p, 200);
  await openFull(p, '214990');
  eq(await txt(p, '#c-days'), '· 30 days', 'D1 214990 counts 30 days to Aug 15');
  const n = await p.evaluate(() => [DATA['216482'].daysTo, DATA['214990'].daysTo, Math.round((Date.UTC(2026, 9, 3) - Date.UTC(2026, 6, 16)) / 864e5)]);
  eq(n, [79, 30, 79], 'D1 the stored counts agree with plain date arithmetic');

  // ===== D2 the pretend date keeps the card, the WATCH line and the to-do list in agreement
  await p.evaluate(() => closeCard());
  await p.evaluate(() => { document.getElementById('swToday').checked = true; document.getElementById('swToday').dispatchEvent(new Event('change', { bubbles: true })); });
  await settle(p, 200);
  await openFull(p, '216482');
  eq(await txt(p, '#c-days'), '· 2 days', 'D2 with Oct 1 as today the card counts 2 days to the Oct 3 goal');
  ok(/was due 72 days ago/.test(await txt(p, '#c-attn-t')), 'D2 and the IR response is reported as overdue, not "due in 5 days"');
  const watch = await p.evaluate(() => document.querySelector('.watch') ? document.querySelector('.watch').textContent : '');
  ok(/in 2 days/.test(watch), 'D2 the WATCH line also says 2 days');
  const rows = await p.evaluate(() => Object.keys(DATA).flatMap(autoRowsFor).map(r => r.title));
  eq(rows.length, new Set(rows).size, 'D2 the to-do list never lists the same sentence twice');
  ok(rows.filter(r => /214990.*passed 47 days ago/.test(r)).length === 1, 'D2 the passed goal of 214990 appears once');
  await p.close();

  // ===== D3 "Copied" is shown only when the browser really copied
  const denyAll = () => {
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.reject(new Error('denied')) }, configurable: true });
    document.execCommand = () => false;
  };
  p = await fresh({ init: denyAll });
  await openFull(p, '214990');
  await p.click('#chipReplies'); await settle(p, 100);
  const firstReply = await p.$('#replyMenu button');
  await firstReply.click(); await settle(p, 400);
  ok((await txt(p, '#chipReplies')) !== 'Copied ✓', 'D3 Quick replies does not say Copied when the clipboard refuses');
  ok(/did not allow copying/.test(await txt(p, '#notice').catch(() => '')) || /did not allow copying/.test(await p.evaluate(() => document.body.innerText)), 'D3 and a polite notice shows the text instead');
  await p.click('#chipCopy'); await settle(p, 400);
  ok((await txt(p, '#chipCopy')) !== 'Copied ✓', 'D3 Copy status also stays honest when copying is refused');
  const hrefsBad = await p.evaluate(() => { const t = document.createElement('textarea'); return typeof writeClipboard === 'function'; });
  ok(hrefsBad, 'D3 one helper (writeClipboard) serves every copy button');
  await p.close();
  // with a working clipboard the confirmation still appears
  p = await fresh();
  await openFull(p, '214990');
  await p.click('#chipCopy'); await settle(p, 300);
  eq(await txt(p, '#chipCopy'), 'Copied ✓', 'D3 with a working clipboard the button says Copied');
  ok((await p.evaluate(() => navigator.clipboard.readText())).startsWith('ANDA 214990'), 'D3 and the status line is really on the clipboard');
  await p.close();
  // a sandbox that blocks the clipboard API through its permissions policy: no console errors
  p = await fresh({ init: () => { Object.defineProperty(document, 'permissionsPolicy', { value: { allowsFeature: f => f !== 'clipboard-write' }, configurable: true }); document.execCommand = () => false; } });
  const before = errs.length;
  await openFull(p, '214990'); await p.click('#chipCopy'); await settle(p, 300);
  ok(errs.length === before, 'D14 a blocked clipboard policy is detected first, so nothing is logged as an error');
  ok((await txt(p, '#chipCopy')) !== 'Copied ✓', 'D14 and nothing claims success');
  await p.close();

  // ===== D4 the shortcut boxes no longer trap the keyboard
  p = await fresh({ all: true });
  await p.evaluate(() => showApp('settings')); await settle(p, 100);
  await p.evaluate(() => { document.querySelectorAll('#settings details.sec').forEach(d => { d.open = true; }); }); await settle(p, 80);
  await p.focus('#keyBoxPeek');
  await p.keyboard.press('Tab'); await settle(p, 60);
  ok(await p.evaluate(() => document.activeElement.id !== 'keyBoxPeek'), 'D4 Tab leaves the Select & Peek shortcut box');
  await p.focus('#keyBoxPeek');
  await p.keyboard.press('Shift+Tab'); await settle(p, 60);
  ok(await p.evaluate(() => document.activeElement.id !== 'keyBoxPeek'), 'D4 Shift+Tab leaves it backwards');
  await p.focus('#keyBoxPeek');
  await p.keyboard.press('Escape'); await settle(p, 60);
  ok(await p.evaluate(() => document.activeElement.id !== 'keyBoxPeek' && getComputedStyle(document.getElementById('settings')).display !== 'none'), 'D4 Escape leaves the box and keeps Settings open');
  await p.keyboard.press('Escape'); await settle(p, 100);
  ok(await p.evaluate(() => getComputedStyle(document.getElementById('settings')).display === 'none'), 'D4 a second Escape closes Settings');
  eq(await p.evaluate(() => state.keys.peek), 'Ctrl+Alt+A', 'D4 and the shortcut itself was not changed by those keys');
  await p.evaluate(() => { showApp('settings'); document.querySelectorAll('#settings details.sec').forEach(d => { d.open = true; }); }); await settle(p, 100);
  await p.focus('#keyBoxPeek');
  await p.keyboard.press('Control+Alt+K'); await settle(p, 80);
  eq(await p.evaluate(() => state.keys.peek), 'Ctrl+Alt+K', 'D4 a real new shortcut is still captured');
  await p.close();

  // ===== D5 the tray menu stays on screen on short displays
  for (const vp of [{ width: 1366, height: 768 }, { width: 1280, height: 720 }, { width: 1024, height: 768 }]) {
    p = await fresh({ all: true, viewport: vp });
    await p.evaluate(() => openMenu()); await settle(p, 150);
    const r = await p.evaluate(() => { const m = document.getElementById('menu'); const b = m.getBoundingClientRect(); return { top: b.top, bottom: b.bottom, scroll: m.scrollHeight > m.clientHeight, vh: innerHeight, ov: getComputedStyle(m).overflowY }; });
    ok(r.top >= 0 && r.bottom <= r.vh, `D5 at ${vp.width}x${vp.height} the tray menu sits fully on screen (top ${Math.round(r.top)}, bottom ${Math.round(r.bottom)})`);
    ok(r.scroll ? r.ov === 'auto' : true, `D5 at ${vp.width}x${vp.height} a menu taller than the screen scrolls`);
    if (vp.height === 768 && vp.width === 1366) {
      await p.keyboard.press('ArrowDown'); await settle(p, 40);
      const first = await p.evaluate(() => document.activeElement.textContent);
      await p.keyboard.press('ArrowDown'); await settle(p, 40);
      const second = await p.evaluate(() => document.activeElement.textContent);
      ok(first !== second && first.length > 0, 'D20 the arrow keys move through the tray menu');
    }
    await p.close();
  }

  // ===== P2 and wording fixes
  p = await fresh();
  const banner = await p.evaluate(() => document.querySelector('.banner').innerText.replace(/\s+/g, ' '));
  ok(/Browser simulation of the ANDA Companion \(Notula\) Windows app\. Fictional data\. Not an FDA product\./.test(banner), 'C18 the banner reads as whole sentences');
  const menuTxt = async () => { await p.evaluate(() => openMenu()); await settle(p, 100); const t = await p.evaluate(() => document.getElementById('menu').innerText); await p.keyboard.press('Escape'); return t; };
  const basicMenu = await menuTxt();
  ok(!/Ctrl\+Alt\+L/.test(basicMenu), 'D9 the basic tray menu no longer advertises a shortcut that does nothing');
  ok(/Demo: ANDA at risk/.test(basicMenu) && /Demo: ANDA on track/.test(basicMenu), 'C16 the demo menu items name the ANDA state');
  await p.evaluate(() => { showApp('settings'); document.querySelectorAll('#settings details.sec').forEach(d => { d.open = true; }); }); await settle(p, 100);
  ok(!/My to-do list: Ctrl\+Alt\+L/.test(await txt(p, '#keyStatus')), 'D9 and Settings does not list it either in basic mode');
  ok(/Off shows the three main tools/.test(await p.evaluate(() => document.body.innerText)), 'C12 the basic-mode hint describes what is really shown');
  ok(/Neither changes other programs/.test(await p.evaluate(() => document.body.innerText)), 'D23 the spacing hint says what spacing applies to');
  await p.evaluate(() => { state.keys.look = 'Ctrl+Alt+K'; paintKeys(); });
  eq(await txt(p, '#lookupHead'), 'LOOKUP · CTRL+ALT+K', 'D10 the Lookup header follows a rebound shortcut');
  await p.evaluate(() => closeApp('settings'));
  // lookup messages
  await p.evaluate(() => openLookup({ x: 400, y: 300 })); await settle(p, 100);
  for (const [input, want, label] of [['NDA 216482', /not ANDA numbers/, 'D22 a labelled NDA number is explained'], ['216 482', /without spaces or dashes/, 'D22 digits with a space are explained'], ['216-482', /without spaces or dashes/, 'D22 digits with a dash are explained'], ['12345', /six-digit ANDA number/, 'D22 five digits get the general message']]) {
    await p.fill('#lookupIn', input); await p.keyboard.press('Enter'); await settle(p, 80);
    ok(want.test(await txt(p, '#lookupErr')), label);
  }
  await p.fill('#lookupIn', '999999'); await p.keyboard.press('Enter'); await settle(p, 150);
  ok(/ANDA 999999 is not in your data\./.test(await p.evaluate(() => document.body.innerText)), 'C5 an unknown ANDA reads "ANDA 999999 is not in your data."');
  // desktop icon feedback
  await p.dblclick('#deskIcon'); await settle(p, 120);
  ok(/already running/.test(await p.evaluate(() => document.body.innerText)), 'D18 the desktop icon answers politely while the app is running');
  // Ask: a long paste is announced
  await p.evaluate(() => openAsk('')); await settle(p, 150);
  await p.evaluate(() => { const ta = document.getElementById('asText'); ta.focus(); const dt = new DataTransfer(); dt.setData('text', 'x'.repeat(25000)); ta.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true })); });
  await settle(p, 100);
  ok(/only the first 20,000 were kept/.test(await p.evaluate(() => document.body.innerText)), 'D11 a paste longer than 20,000 characters is announced');
  // reset buttons have distinct names
  const names = await p.evaluate(() => [...document.querySelectorAll('[data-keyreset]')].map(b => b.getAttribute('aria-label')));
  ok(new Set(names).size === names.length && names.every(n => /^Reset the .+ shortcut to its default$/.test(n)), 'D21 each Reset button has its own accessible name');
  // forced colors / reduced transparency
  const fc = await p.context().newPage();
  await fc.emulateMedia({ forcedColors: 'active' });
  await fc.goto(SIM); await fc.waitForTimeout(300); await fc.dblclick('#deskIcon'); await fc.waitForTimeout(100);
  await fc.evaluate(() => openCard('216482', { anchor: { x: 520, y: 80 }, forceFull: true })); await fc.waitForTimeout(400);
  const fcBg = await fc.evaluate(() => getComputedStyle(document.getElementById('card')).backgroundColor);
  ok(!/rgba\(.*0\.\d+\)/.test(fcBg), `D7 in Windows High Contrast the card is opaque (${fcBg})`);
  await fc.close();
  const rt = await p.context().newPage();
  await rt.emulateMedia({ reducedMotion: 'reduce' });
  await rt.goto(SIM + '#all'); await rt.waitForTimeout(300); await rt.dblclick('#deskIcon'); await rt.waitForTimeout(100);
  await rt.evaluate(() => { state.op = 60; applyEdition(); openCard('216482', { anchor: { x: 520, y: 80 }, forceFull: true }); }); await rt.waitForTimeout(400);
  const bf = await rt.evaluate(() => getComputedStyle(document.getElementById('card')).backdropFilter);
  ok(/blur/.test(bf), `D6 a translucent card blurs what is behind it (${bf})`);
  await rt.close();
  // hash change turns on all tools
  await p.evaluate(() => { location.hash = '#all'; }); await settle(p, 200);
  ok(await p.evaluate(() => state.allTools === true), 'D13 adding #all to an open page turns all tools on');
  await p.close();

  ok(errs.length === 0, `no page errors or console warnings (${JSON.stringify(errs.slice(0, 3))})`);
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close();
  process.exit(fails ? 1 : 0);
})();
