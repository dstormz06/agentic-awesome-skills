// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href;
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 42 in the browser simulation: the timeline checks (immediate and longer term) over the page's own data, Today's first group, News
// and alerts with its summary for a team lead and the goal dates as calendar text, the limits in Settings, the notices near the clock and
// the backup. Rules and words are held to mockup/round42-golden.json, which the Windows app is also tested against. Fictional data only.
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
const GOLD = JSON.parse(fs.readFileSync(path.join(SIMROOT, 'mockup', 'round42-golden.json'), 'utf8'));
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);

(async () => {
  const b = await chromium.launch(); const errs = [], requests = [];
  const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' });
  const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
  p.on('console', m => { if (m.type() === 'error') errs.push(m.type() + ': ' + m.text()); });
  p.on('request', r => { if (!r.url().startsWith('file:') && !r.url().startsWith('data:')) requests.push(r.url()); });
  await p.goto(SIM); await p.waitForTimeout(300);
  const settle = (ms = 200) => p.waitForTimeout(ms);
  const axe = async sel => {
    if (!AXE_FILE) return ['axe-core not found: set AXE_PATH'];
    await p.addScriptTag({ path: AXE_FILE });
    return p.evaluate(async s => (await axe.run(document.querySelector(s), { runOnly: ['wcag2a', 'wcag2aa'] })).violations.map(v => v.id), sel);
  };
  const show = a => `${a.key} · ${a.number} · ${a.code} · ${a.horizon} · ${a.sev} · ${a.date} · ${a.text}`;

  // ---------- the rules, in the page itself ----------
  const got = await p.evaluate(cases => cases.map(c => timelineAlerts(c.input)), GOLD.cases);
  GOLD.cases.forEach((c, i) => eq(got[i].map(show), c.alerts.map(show), `R${i + 1} the page gives the shared answers: ${c.name}`));
  eq(await p.evaluate(g => [newsFrom(g.paragraphs, g.today), tlCalendar(g.cases[0].input.records, g.today)], GOLD), [GOLD.news, GOLD.calendar], 'R7 division news and the calendar text are the shared ones');

  // ---------- Settings: the limits ----------
  await p.evaluate(() => { showApp('settings'); document.querySelectorAll('#settings details').forEach(d => { d.open = true; }); }); await settle();
  eq(await p.evaluate(() => [...document.querySelectorAll('#tlLimits select')].map(s => [document.querySelector(`label[for="${s.id}"]`).textContent, +s.value, [...s.options].map(o => +o.value)])),
    Object.keys(GOLD.defaults).map(k => [GOLD.labels[k], GOLD.defaults[k], GOLD.choices[k]]), 'S1 each limit has its label, its usual value and only its choices');
  await p.selectOption('#tl_goalClose', '15'); await settle();
  eq(await p.evaluate(() => state.tl.goalClose), 15, 'S2 a limit chosen is used at once');
  eq(await p.evaluate(() => { const t = backupText(); const saved = JSON.parse(t).settings.timeline; state.tl = tlNormalize(null); importBackup(t); const back = state.tl.goalClose;
    const x = JSON.parse(t); delete x.settings.timeline; state.tl = { ...state.tl, goalClose: 5 }; importBackup(JSON.stringify(x)); return [saved.goalClose, back, state.tl.goalClose]; }),
    [15, 15, 5], 'S3 the limits are in a backup and come back; an older backup keeps the ones the person has now');
  eq(await p.evaluate(() => { state.tl = tlNormalize({ goalClose: 12, noForecast: 'x' }); return [state.tl.goalClose, state.tl.noForecast]; }), [10, 30], 'S4 a limit that is not one of its choices is put back to the usual one');
  await p.evaluate(() => { state.tl = tlNormalize(null); syncRound19(); }); await settle();
  eq(await axe('#settings'), [], 'S5 accessibility scan of Settings with the limits');
  await p.evaluate(() => closeApp('settings')); await settle();

  // ---------- Today: most urgent first ----------
  await p.evaluate(() => openToday()); await settle();
  const today = await p.evaluate(() => ({ first: document.querySelector('#tdList > *').textContent, rows: [...document.querySelectorAll('#tdList .td-it[data-grp="urgent"]')].map(r => r.dataset.id),
    want: [...new Set(allAlerts().filter(a => a.number && a.sev !== 'info').map(a => a.number))].slice(0, TL_TODAY_MAX) }));
  eq(today.first, GOLD.said.todayTitle, 'T1 Today opens with what needs a look, most urgent first');
  ok(today.rows.length > 0 && JSON.stringify(today.rows.slice(0, today.want.length)) === JSON.stringify(today.want), `T2 one row per ANDA, in the order of the checks, at most ${8} (${today.rows.join(', ')})`);
  // the owner's choice: one group; the ANDAs round 34's rule marks, that no timeline check names, follow in it
  eq(await p.evaluate(() => [[...document.querySelectorAll('#tdList .td-grp')].some(g => g.textContent === 'NEEDS A LOOK'),
    needLookIds().every(id => document.querySelector(`#tdList .td-it[data-grp="urgent"][data-id="${id}"]`))]), [false, true], 'T2b one group for what needs a look: no second group, and every ANDA the older rule marks is in it');
  eq(await p.evaluate(() => { const keep = allAlerts; allAlerts = () => []; openToday(); const r = [...document.querySelectorAll('#tdList .td-it[data-grp="urgent"]')].map(x => x.dataset.id);
    allAlerts = keep; openToday(); return [r, todayGroups(state.pins, state.recents)[0].ids]; }).then(([a, b]) => a.join() === b.join() && a.length > 0), true, 'T2c with no timeline alert, the older rule\'s ANDAs still show, in its order');
  eq(await axe('#today'), [], 'T3 accessibility scan of Today');
  await p.evaluate(() => closeApp('today')); await settle();

  // ---------- News and alerts ----------
  await p.evaluate(() => openHub()); await settle();
  const hub = await p.evaluate(() => { const w = watchItems(); return {
    now: [...document.querySelectorAll('#hubData .hrow')].map(r => r.textContent), later: [...document.querySelectorAll('#hubLater .hrow')].map(r => r.textContent),
    wantNow: w.filter(i => i.horizon !== 'later').map(i => i.full), wantLater: w.filter(i => i.horizon === 'later').map(i => i.full),
    buttons: [...document.querySelectorAll('#hubData .hrow, #hubLater .hrow')].map(r => r.tagName === 'BUTTON') }; });
  ok(hub.now.length > 0 && JSON.stringify(hub.now) === JSON.stringify(hub.wantNow), `H1 immediate alerts are listed, in order (${hub.now.length})`);
  ok(hub.later.length > 0 && JSON.stringify(hub.later) === JSON.stringify(hub.wantLater), `H2 longer-term alerts are listed apart (${hub.later.length})`);
  ok(hub.buttons.every(Boolean), 'H3 each alert about an ANDA opens its card');
  await p.click('#hubSummary'); await settle(300);
  const sum = await p.evaluate(() => { const list = allAlerts().filter(a => a.sev !== 'info'); const n = list.filter(a => a.horizon === 'now').length;
    return { say: document.getElementById('hubSay').textContent, copied: TL_SAID.copied(n, list.length - n), failed: TL_SAID.copyFailed, text: document.getElementById('hubCalText').value,
      want: tlSummary(list, isoDate(demoToday()), x => DATA[x] && DATA[x].drug) }; });
  ok(sum.say === sum.copied || (sum.say === sum.failed && sum.text === sum.want), `H4 the summary is copied, or shown to copy by hand when the browser refuses (${sum.say})`);
  ok(sum.want.split('\n').every(l => !/Fictional notes|note:/i.test(l)), 'H5 the summary holds numbers, dates and reasons, never the notes');
  await p.click('#hubCal'); await settle();
  const cal = await p.evaluate(() => { const r = Object.keys(DATA).map(id => ({ number: id, goal: isoDate(ymd(...DATA[id].goalD)) })), c = tlCalendar(r, isoDate(demoToday()), nowMs());
    return { text: document.getElementById('hubCalText').value, say: document.getElementById('hubSay').textContent, want: c.text, said: TL_SAID.calendar(c.count) }; });
  eq([cal.text, cal.say], [cal.want.replace(/\r\n/g, '\n'), cal.said], 'H6 the goal dates as calendar text, with what it holds (a text box shows CRLF as LF; the saved file keeps CRLF)');
  ok(cal.text.startsWith('BEGIN:VCALENDAR') && cal.text.trim().endsWith('END:VCALENDAR'), 'H7 the calendar text is a whole calendar file');
  eq(await axe('#hub'), [], 'H8 accessibility scan of News and alerts');
  await p.evaluate(() => closeApp('hub')); await settle();

  // ---------- the notices near the clock ----------
  await p.evaluate(() => { state.wSeen = new Set(); state.wSnoozed = new Set(); state.wDay = ''; state.watchCap = 3; state.watchOn = true; watchCheck(); }); await settle();
  const n1 = await p.evaluate(() => ({ kind: document.getElementById('whKind').textContent, open: document.getElementById('whOpen').textContent,
    snooze: document.getElementById('whSnooze').textContent, close: document.getElementById('whClose').textContent, hidden: document.getElementById('whSnooze').hidden, shown: document.getElementById('whisper').style.display }));
  eq(n1, { kind: 'FROM YOUR DATA · IMMEDIATE', open: 'Open the card', snooze: 'Remind me tomorrow', close: 'Close', hidden: false, shown: 'block' }, 'N1 the first notice is the most urgent, from the data, with the app\'s buttons');
  const key = await p.evaluate(() => wCurrent.key);
  await p.click('#whSnooze'); await settle(800);
  eq(await p.evaluate(k => [state.wSnoozed.has(k), state.wSeen.has(k)], key), [true, false], 'N2 Remind me tomorrow keeps it back for today');
  ok(await p.evaluate(() => document.activeElement === document.body || !document.getElementById('whisper').contains(document.activeElement)), 'N3 a notice never takes the keyboard');
  await p.evaluate(() => { state.watchOn = false; hideWhisper(false); watchCheck(); }); await settle();
  eq(await p.evaluate(() => document.getElementById('watchStatus').textContent), 'Notices are off.', 'N4 switched off, nothing is shown, and it says so');

  // ---------- round 42, hardened: a notice that stays, and why notices wait ----------
  await p.evaluate(() => { state.wSeen = new Set(); state.wSnoozed = new Set(); state.wDay = ''; state.watchCap = 1; state.watchStay = true; state.watchOn = true; watchCheck(); }); await settle();
  await p.waitForTimeout(100);
  eq(await p.evaluate(() => [document.getElementById('whisper').style.display, wTimer === null || document.getElementById('whisper').style.display === 'block']), ['block', true], 'N5 with Keep each notice on, a notice shows');
  await p.evaluate(() => { const t0 = Date.now(); window.__stayCheck = () => document.getElementById('whisper').style.display; }); await p.waitForTimeout(13000);
  eq(await p.evaluate(() => window.__stayCheck()), 'block', 'N6 ... and is still there after twelve seconds, until closed');
  await p.click('#whClose'); await settle(800);
  await p.evaluate(() => openHub()); await settle();
  eq(await p.evaluate(() => [document.getElementById('hubNoticeNote').hidden, document.getElementById('hubNoticeNote').textContent.startsWith("Today's limit of 1 notice is reached.")]), [false, true], 'N7 News and alerts says why notices wait');
  eq(await p.evaluate(() => { const t = backupText(); state.watchStay = false; importBackup(t); return [JSON.parse(t).settings.noticesStay, state.watchStay]; }), [true, true], 'N8 the choice is in a backup and comes back');
  eq(await axe('#hub'), [], 'N9 accessibility scan of News and alerts with the note');
  await p.evaluate(() => { closeApp('hub'); state.watchOn = false; state.watchStay = false; state.noticeNote = null; hideWhisper(false); }); await settle();

  // ---------- what changed ----------
  eq(await p.evaluate(() => [CHANGES.find(c => c.round === 42).items, APP_VERSION]), [GOLD.changes.items, GOLD.version], 'F1 what changed in round 42, and the version');
  eq(requests, [], 'F2 the page asked no server for anything');
  eq(errs, [], 'F3 no error in the page');
  await b.close();
  console.log(`\n${checks - fails} of ${checks} checks pass`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
