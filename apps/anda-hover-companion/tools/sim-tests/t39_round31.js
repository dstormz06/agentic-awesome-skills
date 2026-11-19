// Paths are found from this file's place in the repository, so the suite runs from any folder. See README.md in this folder.
const SIMROOT = require('path').resolve(__dirname, '..', '..'), SIMURL = require('url').pathToFileURL(SIMROOT).href, TMP = require('os').tmpdir();
const AXE_FILE = process.env.AXE_PATH || (() => { try { return require.resolve('axe-core/axe.min.js'); } catch (e) { return ''; } })();
// Round 31 in the browser simulation: the tool writes every time in a time zone the person can choose, with the zone's abbreviation and
// the right daylight time; a clock in Settings and "minutes ago" on the card are brought up to date once a minute; every piece of data
// says where it is from and as of when, and is marked STALE once it is older than the days the person chose.
const { chromium } = require('playwright');
const SIM = SIMURL + '/mockup/windows-simulation.html?clock=fixed';
let fails = 0, checks = 0;
const ok = (c, m) => { checks++; console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) fails++; };
const eq = (a, b, m) => ok(JSON.stringify(a) === JSON.stringify(b), `${m}${JSON.stringify(a) === JSON.stringify(b) ? '' : ` (got ${JSON.stringify(a)}, wanted ${JSON.stringify(b)})`}`);
(async () => {
  const b = await chromium.launch(); const errs = [];
  const fresh = async (viewport, tz) => {
    const ctx = await b.newContext({ viewport: viewport || { width: 1366, height: 768 }, permissions: ['clipboard-read', 'clipboard-write'], timezoneId: tz || 'America/New_York', reducedMotion: 'reduce' });
    const p = await ctx.newPage(); p.on('pageerror', e => errs.push(e.message));
    p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errs.push(m.type() + ': ' + m.text()); });
    await p.goto(SIM); await p.waitForTimeout(300); return p;
  };
  const settle = (p, ms = 150) => p.waitForTimeout(ms);
  const axe = async (p, sel) => { await p.addScriptTag({ path: AXE_FILE }).catch(() => {}); return p.evaluate(async s => (await window.axe.run(document.querySelector(s))).violations.map(v => v.id + ': ' + v.nodes.length), sel); };
  const now = p => p.evaluate(() => document.getElementById('setNow').textContent);
  const line = p => p.evaluate(() => [document.getElementById('c-fresh').textContent, document.getElementById('c-fresh').classList.contains('stale')]);
  const hint = p => p.evaluate(() => document.getElementById('setTimeHint').textContent);

  // ====================================================== T. The time zone, and the clock in Settings
  let p = await fresh();
  await p.evaluate(() => { showApp('settings'); document.getElementById('secLook').open = true; }); await settle(p);
  eq(await now(p), 'Now: Thursday, Jul 16, 2026, 10:42 AM EDT', 'T1 Settings, Look and feel, shows the time now, with the day, the year and the abbreviation of the time zone');
  eq(await p.evaluate(() => [[...document.querySelectorAll('#setTz option')].map(o => o.textContent), document.getElementById('setTz').value, [...document.querySelectorAll('#setStale option')].map(o => o.textContent), document.getElementById('setStale').value]),
    [["Automatic: this computer's time zone", 'Eastern (America/New York)', 'Central (America/Chicago)', 'Mountain (America/Denver)', 'Arizona (no daylight time) (America/Phoenix)', 'Pacific (America/Los Angeles)', 'Alaska (America/Anchorage)', 'Hawaii (Pacific/Honolulu)', 'Atlantic (Puerto Rico) (America/Puerto Rico)', 'UTC (UTC)'], '', ['1 day', '3 days', '7 days', '14 days', '30 days'], '7'],
    'T2 the time zone is the computer' + "'" + 's unless another is chosen, and data is stale after 7 days unless another number is chosen');
  await p.selectOption('#setTz', 'America/Los_Angeles'); await settle(p);
  eq([await now(p), await hint(p), await p.evaluate(() => state.tz)], ['Now: Thursday, Jul 16, 2026, 7:42 AM PDT', 'Times are now written in Pacific time (PDT). No stored time was changed.', 'America/Los_Angeles'], 'T3 choosing a zone changes how the time is written, at once, and says that no stored time was changed');
  await p.evaluate(() => { logEvent('sent', 'connection test'); openConn(); }); await settle(p);
  eq(await p.evaluate(() => { const cell = document.querySelector('#cnLog tr td').textContent, stored = evLog[evLog.length - 1].t; return [/^[A-Z][a-z]{2} \d{1,2}, \d{4}, \d{1,2}:\d{2}:\d{2} (AM|PM) (PDT|PST)$/.test(cell), /Z$/.test(stored)]; }), [true, true], 'T4 the event log is written in the chosen zone, and stored in UTC as before');
  await p.evaluate(() => { closeApp('conn'); openBackups(); }); await settle(p);
  eq(await p.evaluate(() => /(PDT|PST)$/.test(document.querySelector('#bkLists .sub').textContent)), true, 'T5 so are the times of the backup copies');
  await p.evaluate(() => { closeApp('bak'); showApp('settings'); document.getElementById('secLook').open = true; }); await settle(p);
  for (const [zone, want] of [['Pacific/Honolulu', '4:42 AM HST'], ['America/Phoenix', '7:42 AM MST'], ['America/Anchorage', '6:42 AM AKDT'], ['America/Puerto_Rico', '10:42 AM AST'], ['UTC', '2:42 PM UTC'], ['America/Chicago', '9:42 AM CDT']]) {
    await p.selectOption('#setTz', zone); await settle(p, 60);
    eq(await now(p), 'Now: Thursday, Jul 16, 2026, ' + want, `T6 ${zone}: the same moment, written in that zone`);
  }
  await p.selectOption('#setTz', ''); await settle(p);
  eq([await now(p), await hint(p)], ['Now: Thursday, Jul 16, 2026, 10:42 AM EDT', "Times are now written in this computer's time zone (America/New York, EDT). No stored time was changed."], 'T7 "Automatic" goes back to the computer' + "'" + 's own zone and names it');
  // The night the clocks go back, and the night they go forward: the abbreviation follows by itself.
  eq(await p.evaluate(() => { const at = s => { window.__now = Date.parse(s); timeTick(); return document.getElementById('setNow').textContent; }; const r = [at('2026-11-01T05:30:00Z'), at('2026-11-01T06:30:00Z'), at('2026-03-08T06:59:00Z'), at('2026-03-08T07:00:00Z')]; delete window.__now; timeTick(); return r; }),
    ['Now: Sunday, Nov 1, 2026, 1:30 AM EDT', 'Now: Sunday, Nov 1, 2026, 1:30 AM EST', 'Now: Sunday, Mar 8, 2026, 1:59 AM EST', 'Now: Sunday, Mar 8, 2026, 3:00 AM EDT'], 'T8 the hour that comes twice in November is written once as EDT and once as EST, and in March 1:59 AM EST is followed by 3:00 AM EDT');
  eq(await p.evaluate(() => [document.getElementById('setNow').getAttribute('aria-live'), document.getElementById('setNow').getAttribute('role'), document.getElementById('setTimeHint').getAttribute('role')]), [null, null, 'status'], 'T9 the clock is not read out each minute; what a choice changed is said once');

  // ====================================================== F. Where data is from, as of when, and when it is stale
  await p.evaluate(() => { closeApp('settings'); openCard('216482', { anchor: { x: 900, y: 300 }, forceFull: true }); }); await settle(p, 400);
  eq(await line(p), ['Fictional demo data · extract #412 · as of Jul 16, 2026, 7:12 AM EDT (3 hours ago)', false], 'F1 the card says where its data is from, as of when in the chosen zone, and how long ago');
  eq(await p.evaluate(() => { window.__now = FIXED_NOW + 31 * 60000; timeTick(); const a = document.getElementById('c-fresh').textContent; window.__now = FIXED_NOW + 26 * 3600000; timeTick(); const b = document.getElementById('c-fresh').textContent; delete window.__now; timeTick(); return [a.slice(-13), /\((1 day ago|just now|\d+ hours? ago)\)$/.test(b)]; }), ['(4 hours ago)', true], 'F2 "hours ago" is brought up to date by the once-a-minute refresh while the card is open');
  const card = async () => { await p.evaluate(() => { APP_WINS.forEach(closeApp); openCard('216482', { anchor: { x: 900, y: 300 }, forceFull: true }); }); await settle(p, 400); return line(p); };
  const choose = async (sel, value) => { await p.evaluate(() => { showApp('settings'); document.getElementById('secLook').open = true; }); await p.selectOption(sel, value); await settle(p); };
  await p.evaluate(() => document.getElementById('swOld').click()); await settle(p);
  eq(await card(), ['Fictional demo data · extract #412 · as of Jul 7, 2026, 7:12 AM EDT (9 days ago) · STALE: older than 7 days', true], 'F3 data older than the days chosen is marked STALE, in words and by its look');
  await choose('#setStale', '14');
  eq([await hint(p), await card()], ['Data older than 14 days is now marked STALE.', ['Fictional demo data · extract #412 · as of Jul 7, 2026, 7:12 AM EDT (9 days ago)', false]], 'F4 the number of days is the person' + "'" + 's choice and takes effect at once');
  await choose('#setStale', '1');
  eq((await card())[0].slice(-26), ' · STALE: older than 1 day', 'F5 one day is written as "1 day"');
  await choose('#setTz', 'UTC');
  eq((await card())[0], 'Fictional demo data · extract #412 · as of Jul 7, 2026, 11:12 AM UTC (9 days ago) · STALE: older than 1 day', 'F6 the line follows the chosen zone: the same moment, written in UTC');
  await choose('#setTz', ''); await choose('#setStale', '7'); await p.evaluate(() => document.getElementById('swOld').click()); await settle(p);
  await p.evaluate(() => { state.data.portfolio = null; showApp('settings'); document.getElementById('dataPortfolioPick').click(); }); await settle(p);
  eq(await card(), ['YOUR FILE (simulated) · my-portfolio.xlsx · as of Jul 16, 2026, 10:42 AM EDT (just now)', false], 'F7 a file of the person' + "'" + 's own is named as the source, as of the moment it was read');
  eq(await p.evaluate(() => { const f = freshLine('X', null, 5, 7, 'UTC'), g = freshLine('X', Date.UTC(2026, 6, 16, 15), Date.UTC(2026, 6, 16, 14), 7, 'UTC'); return [f.text, f.stale, g.text, g.stale]; }), ['X · time not known', false, "X · as of Jul 16, 2026, 3:00 PM UTC (later than now: please check this computer's clock)", false], 'F8 a time that is not known is said to be not known, and a time later than now points at the clock: neither is called fresh or stale by guess');

  // ====================================================== S. Settings
  eq(await p.evaluate(() => { state.tz = 'America/Denver'; state.staleDays = 30; const b = JSON.parse(backupText()); const carried = [b.settings.timeZone, b.settings.staleAfterDays]; b.settings.timeZone = 'Mars/Olympus'; b.settings.staleAfterDays = 5; importBackup(JSON.stringify(b)); return [carried, [state.tz, state.staleDays], [document.getElementById('setTz').value, document.getElementById('setStale').value]]; }),
    [['America/Denver', 30], ['', 7], ['', '7']], 'S1 the zone and the days are settings: a backup carries them, and a value that is not one of the choices becomes the usual one');
  await p.evaluate(() => { showApp('settings'); document.getElementById('secLook').open = true; }); await settle(p);
  eq(await axe(p, '#secLook'), [], 'K1 accessibility scan of the part of Settings that holds the time');
  eq(await p.evaluate(() => [document.querySelector('label[for=setTz]').textContent, document.querySelector('label[for=setStale]').textContent]), ['Show times in', 'Mark my data as stale when it is older than'], 'K2 each of the two boxes has a label of its own');
  await p.evaluate(() => { state.tz = ''; document.getElementById('openTodoBtn').click(); }); await settle(p, 200);
  const foot = async () => (await p.textContent('#tdFooter')).split('. ')[0];
  ok(/^Updated 10:4\d:\d\d AM EDT$/.test(await foot()), 'O1 the to-do list says when it was last brought up to date, with the zone: ' + await foot());
  await p.evaluate(() => { state.tz = 'UTC'; tdReload(true); }); await settle(p);
  ok(/^Updated 2:4\d:\d\d PM UTC$/.test(await foot()), 'O2 and follows the zone chosen: ' + await foot());
  eq(await p.evaluate(() => { state.tz = ''; return [timeOfDay(Date.UTC(2026, 10, 18, 20, 12, 9), 'America/New_York', false), timeOfDay(Date.UTC(2026, 10, 18, 20, 12, 9), 'America/Los_Angeles', true), timeOfDay(NaN, 'UTC', true)]; }),
    ['3:12 PM EST', '12:12:09 PM PST', 'time not known'], 'O3 a time of day alone carries the abbreviation as well (an agent' + "'" + 's "Asked at")');
  ok((await p.textContent('#secLook')).includes('Which day is “today”, for due dates and day counts, stays this computer' + "'" + 's day.'), 'O4 Settings says plainly what the zone does not change');
  await p.close();

  // ====================================================== Z. Another computer, and a phone
  p = await fresh(null, 'Asia/Kolkata');
  await p.evaluate(() => { showApp('settings'); document.getElementById('secLook').open = true; }); await settle(p);
  eq(await now(p), 'Now: Thursday, Jul 16, 2026, 8:12 PM UTC+5:30', 'Z1 on a computer in a zone that is not one of those offered, the time is written with its difference from UTC, which is always right');
  await p.selectOption('#setTz', 'America/New_York'); await settle(p);
  eq(await now(p), 'Now: Thursday, Jul 16, 2026, 10:42 AM EDT', 'Z2 and Eastern can still be chosen there');
  await p.close();
  p = await fresh({ width: 390, height: 780 });
  await p.evaluate(() => { showApp('settings'); document.getElementById('secLook').open = true; }); await settle(p, 250);
  eq(await p.evaluate(() => { const w = document.getElementById('settings'); return [document.documentElement.scrollWidth <= innerWidth + 1, w.scrollWidth <= w.clientWidth + 1, document.getElementById('setTz').getBoundingClientRect().right <= innerWidth]; }), [true, true, true], 'K3 on a phone the Time part fits the screen and does not scroll sideways');
  await p.close();

  // ====================================================== R. What the separate review found
  p = await fresh();
  const flagsOf = () => p.evaluate(() => watchFlags('216482').filter(f => f.code === 'stale-data').map(f => [f.sev, f.text]));
  eq(await flagsOf(), [], 'R1 fresh data is not flagged');
  await p.evaluate(() => document.getElementById('swOld').click()); await settle(p);
  eq(await flagsOf(), [['warn', 'This data is 9 days old (as of Jul 7).']], 'R2 stale data is flagged by the watch rules, in the words of the Windows app');
  eq(await p.evaluate(() => [dataLine().stale, Object.keys(DATA).every(id => watchFlags(id).some(f => f.code === 'stale-data')), needLookIds().length === Object.keys(DATA).length]), [true, true, true], 'R3 the card' + "'" + 's line and the watch rules call the same data stale, for every ANDA, and the list of ANDAs that need a look follows');
  eq(await p.evaluate(() => { state.staleDays = 14; const a = watchFlags('216482').some(f => f.code === 'stale-data'); state.staleDays = 7; return a; }), false, 'R4 with 14 days chosen the same data is not flagged');
  await p.evaluate(() => { APP_WINS.forEach(closeApp); openHandoff(null); }); await settle(p, 250);
  eq((await p.inputValue('#hoOut')).split('\n').slice(0, 3), ['Hand-off: 2 ANDAs, made on Jul 16, 2026', 'From my own data and notes on this PC. It is not the system of record.', 'Data: Fictional demo data · extract #412 · as of Jul 7, 2026, 7:12 AM EDT (9 days ago) · STALE: older than 7 days'],
    'R5 the hand-off page says when it was made, and where its data is from, as of when, and that it is STALE');
  ok(/Needs a look: .*This data is 9 days old \(as of Jul 7\)/.test(await p.inputValue('#hoOut')), 'R6 and each ANDA on it says so as well');
  await p.evaluate(() => { APP_WINS.forEach(closeApp); state.form = 'strip'; showCompact('216482', { x: 700, y: 300 }); }); await settle(p, 200);
  eq(await p.evaluate(() => { const c = document.getElementById('compact'), m = c.querySelector('.stalemark'); return [m && m.textContent, c.title.split('\n').some(l => l === dataLine().text), c.scrollWidth <= c.clientWidth + 1]; }), ['STALE: older than 7 days', true, true],
    'R7 a compact box writes STALE on itself, and carries the whole data line as its tooltip');
  await p.evaluate(() => { document.getElementById('swOld').click(); }); await settle(p);
  eq(await p.evaluate(() => { timeTick(); const c = document.getElementById('compact'); return [!!c.querySelector('.stalemark'), c.title.includes('(3 hours ago)')]; }), [false, true], 'R8 and loses the word when the data is fresh again');
  await p.evaluate(() => closeCompact()); await settle(p);
  // an open card: its notes follow a change of zone at once
  await p.evaluate(() => { openCard('216482', { anchor: { x: 900, y: 300 }, forceFull: true }); addQuickNote('216482', 'A note for the zone test'); fill('216482'); }); await settle(p, 300);
  const who = () => p.evaluate(() => notesFor('216482').filter(n => n.mine).map(n => n.who)[0]);
  ok(/^You · Jul 16, 2026, 10:4\d AM EDT$/.test(await who()), 'R9 a note says when it was written, in the zone in use: ' + await who());
  await p.evaluate(() => { showApp('settings'); document.getElementById('secLook').open = true; }); await p.selectOption('#setTz', 'UTC'); await settle(p);
  ok(/^You · Jul 16, 2026, 2:4\d PM UTC$/.test(await who()) && /Jul 16, 2026, 2:4\d PM UTC/.test(await p.evaluate(() => document.getElementById('x-notes').textContent)), 'R10 and the open card shows it in the new zone without being opened again');
  await p.selectOption('#setTz', ''); await settle(p);
  // backups count their days by the computer's calendar, so a change of zone never removes a copy
  eq(await p.evaluate(() => { const day = 86400000, base = Date.UTC(2026, 10, 18, 4, 30); bakStore.todo = Array.from({ length: 40 }, (_, i) => ({ t: base - i * day / 2, text: '{"items":[]}' })); const a = bakWouldRemove(10); state.tz = 'Pacific/Honolulu'; const b = bakWouldRemove(10); state.tz = 'UTC'; const c = bakWouldRemove(10); state.tz = ''; return [a === b, b === c]; }), [true, true],
    'R11 choosing another zone does not change which backup copies stay');
  // a note with a moment that cannot be a date; a note written while a day is pretended
  eq(await p.evaluate(() => { state.quickNotes['216482'].push({ id: 'q9', text: 'Odd', when: 'Jul 1', t: 5, at: 9e15 }); let threw = false; try { fill('216482'); } catch (e) { threw = true; } const w = notesFor('216482').find(n => n.id === 'q9').who; state.quickNotes['216482'].pop(); return [threw, w]; }), [false, 'You · Jul 1'],
    'R12 a stored note whose moment cannot be a date does not break the card: it keeps the words it was saved with');
  eq(await p.evaluate(() => { document.getElementById('swToday').click(); addQuickNote('216482', 'Written while pretending'); const w = notesFor('216482').find(n => n.text === 'Written while pretending').who; document.getElementById('swToday').click(); return w.slice(0, 18); }), 'You · Oct 1, 2026,', 'R13 a note written while a day is pretended carries that day, as everything else then does');
  // a zone this computer does not know
  eq(await p.evaluate(() => { const real = zoneKnown; window.zoneKnown = z => z !== 'America/Chicago' && real(z); state.tz = 'America/Chicago'; const r = [toolTz(), zoneSaid('America/Chicago', simTz(), FIXED_NOW)]; state.tz = ''; window.zoneKnown = real; return r; }),
    ['America/New_York', "This computer has no data for Central time, so times are written in this computer's time zone (America/New York, EDT). No stored time was changed."], 'R14 a zone this computer has no data for is not used and is said plainly');
  eq(await p.evaluate(() => [timeLabel(Date.UTC(2026, 6, 16, 14, 42), 'Mars/Olympus', false), timeLabel(1e20, 'UTC', true), timeLabel(Date.UTC(1967, 6, 4, 18), 'America/Phoenix', false)]), ['Jul 16, 2026, 2:42 PM UTC', 'time not known', 'Jul 4, 1967, 12:00 PM UTC-6'],
    'R15 a zone that is not known is written as UTC and says UTC; a number that is no date is "time not known"; a year a zone kept other hours gets its difference from UTC, not a wrong abbreviation');
  // the sample data is read at 7:12 on the wall clock, also on the morning the clocks went back
  eq(await p.evaluate(() => { window.__now = Date.UTC(2026, 10, 1, 15, 0); const a = timeLabel(dataAsOf(), 'America/New_York', false); window.__now = Date.UTC(2026, 2, 8, 15, 0); const b = timeLabel(dataAsOf(), 'America/New_York', false); delete window.__now; return [a, b]; }), ['Nov 1, 2026, 7:12 AM EST', 'Mar 8, 2026, 7:12 AM EDT'], 'R16 7:12 that morning, on both mornings the clocks change');
  await p.close();
  // the minute timer itself: nobody calls it but the clock
  { const ctx = await b.newContext({ viewport: { width: 1366, height: 768 }, timezoneId: 'America/New_York', reducedMotion: 'reduce' }); const q = await ctx.newPage(); q.on('pageerror', e => errs.push(e.message));
    await q.clock.install({ time: new Date('2026-07-16T14:42:20Z') }); await q.goto(SIM); await q.clock.runFor(400);
    await q.evaluate(() => { state.autoHide = 0; showApp('settings'); document.getElementById('secLook').open = true; openCard('216482', { anchor: { x: 900, y: 300 }, forceFull: true }); });      // the card must stay open for a minute: auto-hide off await q.clock.runFor(300);
    const seen = () => q.evaluate(() => [document.getElementById('setNow').textContent, document.getElementById('c-fresh').textContent.replace(/^.*\(/, '(')]);
    const first = await seen();
    await q.evaluate(() => { window.__now = FIXED_NOW + 60000; }); await q.clock.runFor(30000);
    const early = await seen();
    await q.clock.runFor(12000);                 // the minute of the clock begins at :00, forty seconds after the page was opened
    const second = await seen();
    eq([first, early, second], [['Now: Thursday, Jul 16, 2026, 10:42 AM EDT', '(3 hours ago)'], ['Now: Thursday, Jul 16, 2026, 10:42 AM EDT', '(3 hours ago)'], ['Now: Thursday, Jul 16, 2026, 10:43 AM EDT', '(3 hours ago)']],
      'R17 the clock in Settings moves by itself as a minute of the clock begins, not a minute after the page was opened');
    await q.evaluate(() => { window.__now = FIXED_NOW + 31 * 60000; }); await q.clock.runFor(61000);
    eq(await seen(), ['Now: Thursday, Jul 16, 2026, 11:13 AM EDT', '(4 hours ago)'], 'R18 and the open card' + "'" + 's line with it');
    await q.close(); }

  eq(errs, [], 'Z no page error or warning in the whole run');
  console.log(`\n${checks - fails}/${checks} passed`);
  await b.close(); process.exit(fails ? 1 : 0);
})();
